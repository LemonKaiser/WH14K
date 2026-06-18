using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Server.Clothing.Systems;
using Content.Server.Hands.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared._WH40K.Cinematic;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Cinematic;

public sealed partial class WH40KCinematicSystem
{
    private const float NpcDirectMoveRange = 0.20f;
    private const float NpcPathMoveRange = 0.35f;
    private const float NpcRecordingSampleIntervalSeconds = 0.20f;
    private const float NpcRecordingMovementThreshold = 0.35f;
    private const float NpcRecordingRotationThresholdDegrees = 12f;
    private const float NpcRecordingInteractionAnchorRadius = 0.45f;
    private const float NpcDefaultActionTimeoutSeconds = 8f;
    private const float NpcTrackEntryRuntimeIdSalt = 7919f;

    [Dependency] private  HTNSystem _htn = default!;
    [Dependency] private  NPCSteeringSystem _steering = default!;
    [Dependency] private  HandsSystem _hands = default!;
    [Dependency] private  InventorySystem _inventory = default!;
    [Dependency] private  OutfitSystem _outfit = default!;
    [Dependency] private  EntityLookupSystem _lookup = default!;
    [Dependency] private  SharedInteractionSystem _interaction = default!;
    [Dependency] private  SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private  SharedCombatModeSystem _combatMode = default!;

    private readonly Dictionary<NetUserId, NpcRecordingSession> _activeNpcRecordingSessions = new();
    private readonly Dictionary<NetUserId, NpcRecordingSession> _completedNpcRecordingSessions = new();

    private void InitializeNpcTrackFeatures()
    {
        SubscribeLocalEvent<EntitySpokeEvent>(OnRecordedEntitySpoke);
        SubscribeLocalEvent<DidEquipEvent>(OnRecordedDidEquip);
        SubscribeLocalEvent<DidUnequipEvent>(OnRecordedDidUnequip);
        SubscribeLocalEvent<TransformComponent, MeleeAttackEvent>(OnRecordedMeleeAttack);
        SubscribeLocalEvent<UserInteractUsingEvent>(OnRecordedUserInteractUsing);
        SubscribeLocalEvent<UserActivateInWorldEvent>(OnRecordedUserActivateInWorld);
    }

    private void UpdateNpcTrackRecording(float frameTime)
    {
        if (_activeNpcRecordingSessions.Count == 0)
            return;

        foreach (var session in _activeNpcRecordingSessions.Values.ToArray())
        {
            if (!session.IsActive || session.IsPaused)
                continue;

            if (!TryGetNpcRecordingActor(session, out var actor))
                continue;

            if (_timing.CurTime < session.NextMovementSampleAt)
                continue;

            session.NextMovementSampleAt = _timing.CurTime + TimeSpan.FromSeconds(NpcRecordingSampleIntervalSeconds);
            CaptureMovementSample(session, actor.Entity);
        }
    }

    private void CleanupNpcTrackRuntime(ActiveCinematicRun run)
    {
        foreach (var session in _activeNpcRecordingSessions.Values
                     .Where(session => session.RunSerial == run.RunSerial)
                     .ToArray())
        {
            FinalizeNpcRecordingSession(session, keepAsCompleted: true);
        }

        foreach (var actor in run.NpcActors.Values.ToArray())
        {
            CleanupNpcActorRuntime(actor);
        }

        run.NpcActors.Clear();
    }

    public bool TryStartNpcRecording(
        IConsoleShell shell,
        int runSerial,
        string npcId,
        string trackId,
        string segmentId,
        out string message)
    {
        message = string.Empty;

        if (shell.Player is not { AttachedEntity: { Valid: true } attached } player)
        {
            message = "NPC recording requires an in-game admin player session.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(segmentId))
        {
            message = "trackId and segmentId must not be empty.";
            return false;
        }

        if (!_mind.TryGetMind(attached, out var mindId, out _))
        {
            message = "The current admin-controlled entity has no mind to visit the NPC with.";
            return false;
        }

        if (!TryFindRun(runSerial, out var run))
        {
            message = $"No cinematic run with id '{runSerial}' is active.";
            return false;
        }

        if (!TryGetNpcActor(run, npcId, out var actor, out message))
            return false;

        if (_activeNpcRecordingSessions.TryGetValue(player.UserId, out var existing))
            FinalizeNpcRecordingSession(existing, keepAsCompleted: true);

        EnsureScriptControl(actor);

        var session = new NpcRecordingSession(
            player.UserId,
            run.RunSerial,
            run.Prototype.ID,
            actor.NpcId,
            actor.Entity,
            trackId.Trim(),
            segmentId.Trim(),
            mindId,
            _timing.CurTime);

        var xform = Transform(actor.Entity);
        session.LastSamplePosition = _xform.GetWorldPosition(xform);
        session.LastSampleRotationDegrees = (float) xform.LocalRotation.Degrees;
        session.NextMovementSampleAt = _timing.CurTime + TimeSpan.FromSeconds(NpcRecordingSampleIntervalSeconds);

        _mind.Visit(mindId, actor.Entity);
        _activeNpcRecordingSessions[player.UserId] = session;
        _completedNpcRecordingSessions.Remove(player.UserId);
        message =
            $"Started NPC recording for run {run.RunSerial}, npcId '{actor.NpcId}', track '{session.TrackId}', segment '{session.SegmentId}'.";
        return true;
    }

    public bool TryPauseNpcRecording(IConsoleShell shell, out string message)
    {
        message = string.Empty;
        if (!TryGetNpcRecordingSession(shell, requireCompleted: false, out var session, out message))
            return false;

        if (session.IsPaused)
        {
            message = "NPC recording is already paused.";
            return false;
        }

        session.IsPaused = true;
        session.PausedAt = _timing.CurTime;
        message = $"Paused NPC recording for npcId '{session.NpcId}'.";
        return true;
    }

    public bool TryResumeNpcRecording(IConsoleShell shell, out string message)
    {
        message = string.Empty;
        if (!TryGetNpcRecordingSession(shell, requireCompleted: false, out var session, out message))
            return false;

        if (!session.IsPaused)
        {
            message = "NPC recording is not paused.";
            return false;
        }

        session.IsPaused = false;
        if (session.PausedAt != null)
            session.PausedDuration += _timing.CurTime - session.PausedAt.Value;

        session.PausedAt = null;
        session.NextMovementSampleAt = _timing.CurTime + TimeSpan.FromSeconds(NpcRecordingSampleIntervalSeconds);
        message = $"Resumed NPC recording for npcId '{session.NpcId}'.";
        return true;
    }

    public bool TryStopNpcRecording(IConsoleShell shell, out string message)
    {
        message = string.Empty;
        if (!TryGetNpcRecordingSession(shell, requireCompleted: false, out var session, out message))
            return false;

        FinalizeNpcRecordingSession(session, keepAsCompleted: true);
        message =
            $"Stopped NPC recording for npcId '{session.NpcId}'. Recorded {session.Entries.Count} entry/entries into track '{session.TrackId}' segment '{session.SegmentId}'.";
        return true;
    }

    public bool TryExportNpcRecording(IConsoleShell shell, string? relativePath, out string message)
    {
        message = string.Empty;
        if (!TryGetNpcRecordingSession(shell, requireCompleted: true, out var session, out message))
            return false;

        var targetPath = ResolveNpcRecordingExportPath(relativePath, session);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, BuildNpcRecordingYaml(session), Encoding.UTF8);
        message = $"Exported NPC recording track '{session.TrackId}' to '{targetPath}'.";
        return true;
    }

    public IReadOnlyList<string> GetKnownNpcAnchorIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var query = EntityQueryEnumerator<WH40KCinematicNpcAnchorComponent>();
        while (query.MoveNext(out _, out var anchor))
        {
            if (!string.IsNullOrWhiteSpace(anchor.AnchorId))
                ids.Add(anchor.AnchorId);
        }

        return ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private bool TryExecuteNpcScriptAction(
        ActiveCinematicRun run,
        WH40KCinematicStepDefinition step,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;

        switch (action.Type)
        {
            case WH40KCinematicActionType.SpawnNpc:
                return TrySpawnNpc(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.BindExistingEntityAsNpc:
                return TryBindExistingEntityAsNpc(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.DespawnNpc:
                return TryDespawnNpc(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.NpcSpeak:
                return TryNpcSpeak(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.NpcEmote:
                return TryNpcEmote(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.NpcFaceDirection:
                return TryNpcFaceDirection(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.NpcMoveByOffset:
                return TryNpcMoveByOffset(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.NpcMoveToAnchor:
                return TryNpcMoveToAnchor(run, action, actionLabel, directMove: true, out failureReason);

            case WH40KCinematicActionType.NpcPathToAnchor:
                return TryNpcMoveToAnchor(run, action, actionLabel, directMove: false, out failureReason);

            case WH40KCinematicActionType.NpcPathThroughAnchors:
                return TryNpcPathThroughAnchors(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.NpcAttackDirection:
                return TryNpcAttackDirection(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.NpcUseEntity:
                return TryNpcUseEntity(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.NpcEquipPrototype:
                return TryNpcEquipPrototype(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.NpcUnequipSlot:
                return TryNpcUnequipSlot(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.NpcSetHTNEnabled:
                return TryNpcSetHtnEnabled(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.NpcReleaseScriptControl:
                return TryNpcReleaseScriptControl(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.NpcWait:
                return TryNpcWait(run, action, actionLabel, out failureReason);

            case WH40KCinematicActionType.PlayActorTrack:
                return TryPlayActorTrack(run, step, action, actionLabel, out failureReason);

            default:
                failureReason = $"Unsupported NPC scripting action '{action.Type}'.";
                return false;
        }
    }

    private bool TrySpawnNpc(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(action.NpcId))
        {
            failureReason = $"{actionLabel} requires npcId.";
            return false;
        }

        if (run.NpcActors.TryGetValue(action.NpcId.Trim(), out var existing) &&
            existing.Entity.Valid &&
            !Deleted(existing.Entity))
        {
            if (action.ReuseExistingEntity)
                return true;

            failureReason = $"{actionLabel} attempted to spawn duplicate npcId '{action.NpcId}'.";
            return false;
        }

        var anchors = ResolveNpcAnchors(run, action.AnchorId, action.ContextId);
        if (anchors.Count == 0)
        {
            if (action.OptionalAnchor)
            {
                Log.Warning($"Skipping optional npc spawn anchor '{action.AnchorId}' for {actionLabel}.");
                return true;
            }

            failureReason = $"Missing required npc anchor '{action.AnchorId}' for {actionLabel}.";
            return false;
        }

        var anchorEntity = anchors[0];
        var anchor = Comp<WH40KCinematicNpcAnchorComponent>(anchorEntity);
        var prototypeId = action.Prototype ?? anchor.DefaultPrototype;
        if (prototypeId == null)
        {
            failureReason = $"{actionLabel} requires prototype or anchor defaultPrototype.";
            return false;
        }

        var entity = Spawn(prototypeId.Value, Transform(anchorEntity).Coordinates);
        _xform.SetLocalRotation(entity, Angle.FromDegrees(anchor.RotationDegrees));

        var startingGearId = action.StartingGearId ?? anchor.DefaultStartingGear;
        if (startingGearId != null)
            _outfit.SetOutfit(entity, startingGearId.Value);

        ApplyNpcFactionOverride(entity, action.NpcFactionId?.ToString() ?? anchor.DefaultFactionId);

        RegisterNpcActor(run, action.NpcId.Trim(), entity, spawnedByCinematic: true, startingGearId);
        TrackEntitiesInSet(run, action.EntitySetId, new[] { entity });
        return true;
    }

    private bool TryBindExistingEntityAsNpc(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(action.NpcId))
        {
            failureReason = $"{actionLabel} requires npcId.";
            return false;
        }

        var npcId = action.NpcId.Trim();
        if (run.NpcActors.TryGetValue(npcId, out var existing) &&
            existing.Entity.Valid &&
            !Deleted(existing.Entity) &&
            action.ReuseExistingEntity)
        {
            return true;
        }

        if (!TryFindExistingEntityForNpc(run, action, out var entity))
        {
            if (action.OptionalAnchor)
            {
                Log.Warning($"Skipping optional bindExistingEntityAsNpc for {actionLabel} because no matching entity was found.");
                return true;
            }

            failureReason = $"{actionLabel} could not find an entity to bind as npcId '{npcId}'.";
            return false;
        }

        RegisterNpcActor(run, npcId, entity!.Value, spawnedByCinematic: false, action.StartingGearId);
        return true;
    }

    private bool TryDespawnNpc(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out var actorFailure))
        {
            Log.Warning($"{actionLabel}: {actorFailure}");
            return true;
        }

        CleanupNpcActorRuntime(actor);
        run.NpcActors.Remove(actor.NpcId);
        return true;
    }

    private bool TryNpcSpeak(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        var message = ResolveOptionalLocalizedString(action.Message, action.MessageLoc, action.LocArgs, action.ResolveLocArgValues);
        if (string.IsNullOrWhiteSpace(message))
        {
            failureReason = $"{actionLabel} requires message or messageLoc.";
            return false;
        }

        _chat.TrySendInGameICMessage(actor.Entity, message, InGameICChatType.Speak, ChatTransmitRange.Normal, ignoreActionBlocker: true);
        return true;
    }

    private bool TryNpcEmote(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        var emote = action.Message ?? action.Text;
        if (string.IsNullOrWhiteSpace(emote))
        {
            failureReason = $"{actionLabel} requires message/text containing the emote id.";
            return false;
        }

        if (!_chat.TryEmoteWithChat(actor.Entity, emote.Trim(), ignoreActionBlocker: true, forceEmote: true))
            Log.Warning($"{actionLabel} could not play emote '{emote}' for npcId '{actor.NpcId}'.");

        return true;
    }

    private bool TryNpcFaceDirection(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        if (!TryResolveFacingDirection(run, actor.Entity, action, out var direction))
        {
            failureReason = $"{actionLabel} requires facingDirection, targetNpcId, or anchorId.";
            return false;
        }

        EnsureScriptControl(actor);
        _xform.SetLocalRotation(actor.Entity, direction.ToAngle());
        return true;
    }

    private bool TryNpcMoveByOffset(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        if (action.Offset == null)
        {
            failureReason = $"{actionLabel} requires offset.";
            return false;
        }

        EnsureScriptControl(actor);
        var xform = Transform(actor.Entity);
        var mapCoords = _xform.ToMapCoordinates(xform.Coordinates);
        var target = new MapCoordinates(mapCoords.Position + action.Offset.Value, mapCoords.MapId);
        var runtime = new NpcMoveActionRuntime(
            action.Id,
            run.CurrentStepIndex,
            run.CurrentStep.Id,
            actionLabel,
            actor,
            _xform.ToCoordinates(target),
            directMove: true,
            action.Blocking,
            NpcDirectMoveRange,
            ResolveNpcActionTimeout(run, action),
            action.AllowRecoveryTeleport);
        runtime.Start(this);
        run.ActiveActions.Add(runtime);
        return true;
    }

    private bool TryNpcMoveToAnchor(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        bool directMove,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        if (!TryResolveNpcAnchorCoordinates(run, action.AnchorId, action.ContextId, out var targetCoordinates))
        {
            if (action.OptionalAnchor)
            {
                Log.Warning($"Skipping optional NPC anchor '{action.AnchorId}' for {actionLabel}.");
                return true;
            }

            failureReason = $"{actionLabel} could not resolve npc anchor '{action.AnchorId}'.";
            return false;
        }

        EnsureScriptControl(actor);
        var runtime = new NpcMoveActionRuntime(
            action.Id,
            run.CurrentStepIndex,
            run.CurrentStep.Id,
            actionLabel,
            actor,
            targetCoordinates,
            directMove,
            action.Blocking,
            directMove ? NpcDirectMoveRange : NpcPathMoveRange,
            ResolveNpcActionTimeout(run, action),
            action.AllowRecoveryTeleport);
        runtime.Start(this);
        run.ActiveActions.Add(runtime);
        return true;
    }

    private bool TryNpcPathThroughAnchors(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        if (action.AnchorIds.Count == 0)
        {
            failureReason = $"{actionLabel} requires anchorIds.";
            return false;
        }

        var targets = new Queue<EntityCoordinates>();
        foreach (var anchorId in action.AnchorIds)
        {
            if (!TryResolveNpcAnchorCoordinates(run, anchorId, action.ContextId, out var coords))
            {
                if (action.OptionalAnchor)
                    continue;

                failureReason = $"{actionLabel} could not resolve npc anchor '{anchorId}'.";
                return false;
            }

            targets.Enqueue(coords);
        }

        if (targets.Count == 0)
            return true;

        EnsureScriptControl(actor);
        var runtime = new NpcPathThroughAnchorsRuntime(
            action.Id,
            run.CurrentStepIndex,
            run.CurrentStep.Id,
            actionLabel,
            actor,
            targets,
            action.Blocking,
            ResolveNpcActionTimeout(run, action),
            action.AllowRecoveryTeleport);
        runtime.Start(this);
        run.ActiveActions.Add(runtime);
        return true;
    }

    private bool TryNpcAttackDirection(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        EnsureScriptControl(actor);
        _combatMode.SetInCombatMode(actor.Entity, true);
        if (!_melee.TryGetWeapon(actor.Entity, out var weaponUid, out var weapon))
        {
            Log.Warning($"{actionLabel}: npcId '{actor.NpcId}' has no usable melee weapon.");
            return true;
        }

        var target = TryResolveNpcCombatTarget(run, actor.Entity, action);
        if (target != null)
        {
            _melee.AttemptLightAttack(actor.Entity, weaponUid, weapon, target.Value);
            return true;
        }

        if (!TryResolveFacingDirection(run, actor.Entity, action, out var direction))
            direction = Transform(actor.Entity).LocalRotation.ToWorldVec();

        var missTarget = Transform(actor.Entity).Coordinates.Offset(direction * Math.Max(0.8f, action.SearchRadius));
        _melee.AttemptLightAttackMiss(actor.Entity, weaponUid, weapon, missTarget);
        return true;
    }

    private bool TryNpcUseEntity(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        if (!TryResolveNpcTargetEntity(run, actor.Entity, action, out var target))
        {
            Log.Warning($"{actionLabel}: no valid target entity resolved.");
            return true;
        }

        var resolvedTarget = target!.Value;

        EnsureScriptControl(actor);
        if (_hands.TryGetActiveItem(actor.Entity, out var held))
        {
            _interaction.InteractUsing(actor.Entity, held.Value, resolvedTarget, Transform(resolvedTarget).Coordinates, checkCanInteract: false, checkCanUse: false);
            return true;
        }

        _interaction.InteractionActivate(actor.Entity, resolvedTarget, checkCanInteract: false, checkAccess: false, checkUseDelay: false);
        return true;
    }

    private bool TryNpcEquipPrototype(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        if (action.Prototype == null)
        {
            failureReason = $"{actionLabel} requires prototype.";
            return false;
        }

        EnsureScriptControl(actor);
        var item = action.ReuseExistingEntity
            ? FindNearbyPrototype(action.Prototype.Value, _xform.GetMapCoordinates(actor.Entity), action.SearchRadius)
            : null;

        var spawnedForEquip = false;
        if (item == null)
        {
            item = Spawn(action.Prototype.Value, Transform(actor.Entity).Coordinates);
            spawnedForEquip = true;
        }

        var equipped = !string.IsNullOrWhiteSpace(action.Slot)
            ? _inventory.TryEquip(actor.Entity, item.Value, action.Slot!, force: true)
            : _hands.TryPickupAnyHand(actor.Entity, item.Value, checkActionBlocker: false);

        if (!equipped)
        {
            if (spawnedForEquip)
                QueueDel(item.Value);

            Log.Warning($"{actionLabel}: npcId '{actor.NpcId}' could not equip '{action.Prototype.Value}'.");
        }

        return true;
    }

    private bool TryNpcUnequipSlot(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        if (string.IsNullOrWhiteSpace(action.Slot))
        {
            failureReason = $"{actionLabel} requires slot.";
            return false;
        }

        EnsureScriptControl(actor);
        if (!_inventory.TryUnequip(actor.Entity, action.Slot!, out var removed, force: true))
        {
            Log.Warning($"{actionLabel}: npcId '{actor.NpcId}' has nothing removable in slot '{action.Slot}'.");
            return true;
        }

        _hands.TryPickupAnyHand(actor.Entity, removed.Value, checkActionBlocker: false);
        return true;
    }

    private bool TryNpcSetHtnEnabled(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        if (action.HtnEnabled == null)
        {
            failureReason = $"{actionLabel} requires htnEnabled.";
            return false;
        }

        if (!action.HtnEnabled.Value)
        {
            EnsureScriptControl(actor);
            return true;
        }

        ReleaseScriptControl(actor);
        if (TryComp<HTNComponent>(actor.Entity, out var htn))
        {
            _htn.SetHTNEnabled((actor.Entity, htn), true);
            _htn.Replan(htn);
        }

        return true;
    }

    private bool TryNpcReleaseScriptControl(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        ReleaseScriptControl(actor);
        return true;
    }

    private bool TryNpcWait(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        var duration = action.DurationSeconds ?? 0f;
        if (duration <= 0f)
        {
            failureReason = $"{actionLabel} requires duration > 0.";
            return false;
        }

        run.ActiveActions.Add(new NpcWaitActionRuntime(
            action.Id,
            run.CurrentStepIndex,
            run.CurrentStep.Id,
            actionLabel,
            action.Blocking,
            duration,
            _timing.CurTime));
        return true;
    }

    private bool TryPlayActorTrack(
        ActiveCinematicRun run,
        WH40KCinematicStepDefinition step,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryGetNpcActor(run, action.NpcId, out var actor, out failureReason))
            return false;

        if (action.TrackId == null || !_prototypes.TryIndex(action.TrackId.Value, out var track))
        {
            failureReason = $"{actionLabel} requires a valid trackId.";
            return false;
        }

        if (track.Segments.Count == 0)
        {
            failureReason = $"{actionLabel} references actor track '{track.ID}' with no segments.";
            return false;
        }

        var segmentId = string.IsNullOrWhiteSpace(action.TrackSegmentId) ? track.Segments[0].Id : action.TrackSegmentId.Trim();
        var segment = track.Segments.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, segmentId, StringComparison.OrdinalIgnoreCase));

        if (segment == null)
        {
            failureReason = $"{actionLabel} references missing actor track segment '{segmentId}'.";
            return false;
        }

        var restoreWarning = string.Empty;
        if (action.RestoreActorState &&
            !TryRestoreActorBaseline(actor, action, out restoreWarning) &&
            !action.IgnoreStateMismatch)
        {
            failureReason = $"{actionLabel} could not restore actor baseline: {restoreWarning}";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(restoreWarning))
            Log.Warning($"{actionLabel}: {restoreWarning}");

        var runtime = new ActorTrackActionRuntime(
            action.Id,
            run.CurrentStepIndex,
            run.CurrentStep.Id,
            actionLabel,
            action.Blocking,
            run,
            step,
            actor.NpcId,
            track.ID,
            segment);
        run.ActiveActions.Add(runtime);
        return true;
    }

    private void RegisterNpcActor(
        ActiveCinematicRun run,
        string npcId,
        EntityUid entity,
        bool spawnedByCinematic,
        ProtoId<StartingGearPrototype>? startingGearId)
    {
        if (run.NpcActors.TryGetValue(npcId, out var existing))
            CleanupNpcActorRuntime(existing);

        var baseline = CaptureNpcActorBaseline(entity, startingGearId);
        run.NpcActors[npcId] = new NpcActorRuntime(npcId, entity, spawnedByCinematic, baseline);
    }

    private void CleanupNpcActorRuntime(NpcActorRuntime actor)
    {
        ReleaseScriptControl(actor);
        if (actor.SpawnedByCinematic && actor.Entity.Valid && !Deleted(actor.Entity))
            QueueDel(actor.Entity);
    }

    private bool TryGetNpcActor(
        ActiveCinematicRun run,
        string? npcId,
        out NpcActorRuntime actor,
        out string failureReason)
    {
        actor = default!;
        failureReason = string.Empty;

        if (string.IsNullOrWhiteSpace(npcId))
        {
            failureReason = "npcId must not be empty.";
            return false;
        }

        if (!run.NpcActors.TryGetValue(npcId.Trim(), out var foundActor) || foundActor == null)
        {
            failureReason = $"Run {run.RunSerial} does not have npcId '{npcId}'.";
            return false;
        }

        actor = foundActor;

        if (!actor.Entity.Valid || Deleted(actor.Entity))
        {
            run.NpcActors.Remove(actor.NpcId);
            failureReason = $"npcId '{actor.NpcId}' no longer has a live entity.";
            return false;
        }

        return true;
    }

    private void EnsureScriptControl(NpcActorRuntime actor)
    {
        if (actor.ScriptControlActive)
            return;

        if (TryComp<HTNComponent>(actor.Entity, out var htn))
        {
            actor.HadHTN = true;
            actor.OriginalHTNEnabled = htn.Enabled;
            _htn.SetHTNEnabled((actor.Entity, htn), false);
        }

        _steering.Unregister(actor.Entity);
        actor.ScriptControlActive = true;
    }

    private void ReleaseScriptControl(NpcActorRuntime actor)
    {
        if (!actor.ScriptControlActive)
            return;

        _steering.Unregister(actor.Entity);
        if (actor.HadHTN &&
            actor.Entity.Valid &&
            !Deleted(actor.Entity) &&
            TryComp<HTNComponent>(actor.Entity, out var htn))
        {
            _htn.SetHTNEnabled((actor.Entity, htn), actor.OriginalHTNEnabled);
            if (actor.OriginalHTNEnabled)
                _htn.Replan(htn);
        }

        actor.ScriptControlActive = false;
    }

    private bool TryRestoreActorBaseline(
        NpcActorRuntime actor,
        WH40KCinematicActionDefinition action,
        out string warning)
    {
        warning = string.Empty;
        if (!actor.Entity.Valid || Deleted(actor.Entity))
        {
            warning = $"npcId '{actor.NpcId}' no longer has a live entity.";
            return false;
        }

        if (actor.Baseline.StartingGearId != null)
            _outfit.SetOutfit(actor.Entity, actor.Baseline.StartingGearId.Value);

        if (TryComp<DamageableComponent>(actor.Entity, out var damageable))
            _damageable.SetDamage((actor.Entity, damageable), actor.Baseline.Damage);

        if (action.AllowRecoveryTeleport)
        {
            _xform.SetCoordinates(actor.Entity, actor.Baseline.Coordinates);
            _xform.SetLocalRotation(actor.Entity, actor.Baseline.Rotation);
            _xform.AttachToGridOrMap(actor.Entity);
        }
        else if (_xform.ToMapCoordinates(Transform(actor.Entity).Coordinates).MapId != _xform.GetMapId(actor.Baseline.Coordinates))
        {
            warning = $"npcId '{actor.NpcId}' drifted to another map and allowRecoveryTeleport=false.";
            return false;
        }

        if (actor.Baseline.Factions.Count > 0 || HasComp<NpcFactionMemberComponent>(actor.Entity))
        {
            _npcFaction.ClearFactions(actor.Entity);
            if (actor.Baseline.Factions.Count > 0)
                _npcFaction.AddFactions(actor.Entity, actor.Baseline.Factions);
        }

        return true;
    }

    private NpcActorBaseline CaptureNpcActorBaseline(EntityUid entity, ProtoId<StartingGearPrototype>? startingGearId)
    {
        var xform = Transform(entity);
#pragma warning disable CS0618
        var damage = TryComp<DamageableComponent>(entity, out var damageable)
            ? _damageable.GetAllDamage((entity, damageable))
            : new DamageSpecifier();
#pragma warning restore CS0618
        var factions = new HashSet<ProtoId<NpcFactionPrototype>>();
        if (TryComp<NpcFactionMemberComponent>(entity, out var faction))
            factions.UnionWith(faction.Factions);

        return new NpcActorBaseline(
            xform.Coordinates,
            xform.LocalRotation,
            damage,
            factions,
            startingGearId);
    }

    private void ApplyNpcFactionOverride(EntityUid entity, string? factionId)
    {
        if (string.IsNullOrWhiteSpace(factionId))
            return;

        _npcFaction.ClearFactions(entity);
        _npcFaction.AddFaction(entity, factionId.Trim());
    }

    private bool TryResolveNpcAnchorCoordinates(
        ActiveCinematicRun run,
        string? anchorId,
        string? contextId,
        out EntityCoordinates coordinates)
    {
        coordinates = default;
        var anchors = ResolveNpcAnchors(run, anchorId, contextId);
        if (anchors.Count == 0)
            return false;

        coordinates = Transform(anchors[0]).Coordinates;
        return true;
    }

    private List<EntityUid> ResolveNpcAnchors(ActiveCinematicRun? run, string? anchorId, string? explicitContextId)
    {
        var result = ResolveNpcAnchorsInternal(run, anchorId, explicitContextId, respectContext: true);
        if (result.Count > 0 || !ShouldFallbackToAnyContext(run, explicitContextId))
            return result;

        return ResolveNpcAnchorsInternal(run, anchorId, explicitContextId, respectContext: false);
    }

    private List<EntityUid> ResolveNpcAnchorsInternal(
        ActiveCinematicRun? run,
        string? anchorId,
        string? explicitContextId,
        bool respectContext)
    {
        var result = new List<EntityUid>();
        if (string.IsNullOrWhiteSpace(anchorId))
            return result;

        var query = AllEntityQuery<WH40KCinematicNpcAnchorComponent>();
        while (query.MoveNext(out var uid, out var anchor))
        {
            if (respectContext && run != null && !DoesEntityMatchContext(uid, run, explicitContextId))
                continue;

            if (string.Equals(anchor.AnchorId, anchorId, StringComparison.OrdinalIgnoreCase))
                result.Add(uid);
        }

        return result;
    }

    private bool TryFindExistingEntityForNpc(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        out EntityUid? entity)
    {
        entity = null;
        if (!string.IsNullOrWhiteSpace(action.AnchorId))
        {
            foreach (var anchor in ResolveNpcAnchors(run, action.AnchorId, action.ContextId))
            {
                var mapCoords = _xform.ToMapCoordinates(Transform(anchor).Coordinates);
                foreach (var candidate in _lookup.GetEntitiesInRange<TransformComponent>(mapCoords, action.SearchRadius))
                {
                    if (MatchesPrototype(candidate.Owner, action.Prototype))
                    {
                        entity = candidate.Owner;
                        return true;
                    }
                }
            }
        }

        if (action.Prototype != null && run.TriggerUserId != null &&
            _players.TryGetSessionById(run.TriggerUserId.Value, out var session) &&
            session.AttachedEntity is { Valid: true } attached &&
            !Deleted(attached))
        {
            var mapCoords = _xform.GetMapCoordinates(attached);
            foreach (var candidate in _lookup.GetEntitiesInRange<TransformComponent>(mapCoords, action.SearchRadius))
            {
                if (MatchesPrototype(candidate.Owner, action.Prototype))
                {
                    entity = candidate.Owner;
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryResolveNpcTargetEntity(
        ActiveCinematicRun run,
        EntityUid actorEntity,
        WH40KCinematicActionDefinition action,
        out EntityUid? target)
    {
        target = null;

        if (!string.IsNullOrWhiteSpace(action.TargetNpcId) &&
            TryGetNpcActor(run, action.TargetNpcId, out var targetActor, out _))
        {
            target = targetActor.Entity;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(action.AnchorId))
        {
            foreach (var anchor in ResolveSpawnAnchors(run, action.AnchorId, action.ContextId))
            {
                if (TryFindNearbyTarget(anchor, actorEntity, action, out target))
                    return true;
            }

            foreach (var anchor in ResolveSoundAnchors(run, action.AnchorId, action.ContextId))
            {
                if (TryFindNearbyTarget(anchor, actorEntity, action, out target))
                    return true;
            }

            foreach (var anchor in ResolveNpcAnchors(run, action.AnchorId, action.ContextId))
            {
                if (TryFindNearbyTarget(anchor, actorEntity, action, out target))
                    return true;
            }
        }

        var coords = _xform.GetMapCoordinates(actorEntity);
        foreach (var candidate in _lookup.GetEntitiesInRange<TransformComponent>(coords, action.SearchRadius))
        {
            if (candidate.Owner == actorEntity || Deleted(candidate.Owner))
                continue;

            if (!MatchesPrototype(candidate.Owner, action.Prototype))
                continue;

            target = candidate.Owner;
            return true;
        }

        return false;
    }

    private bool TryFindNearbyTarget(
        EntityUid searchOrigin,
        EntityUid actorEntity,
        WH40KCinematicActionDefinition action,
        out EntityUid? target)
    {
        target = null;
        var coords = _xform.GetMapCoordinates(searchOrigin);
        foreach (var candidate in _lookup.GetEntitiesInRange<TransformComponent>(coords, action.SearchRadius))
        {
            if (candidate.Owner == actorEntity || Deleted(candidate.Owner))
                continue;

            if (!MatchesPrototype(candidate.Owner, action.Prototype))
                continue;

            target = candidate.Owner;
            return true;
        }

        return false;
    }

    private EntityUid? TryResolveNpcCombatTarget(
        ActiveCinematicRun run,
        EntityUid actorEntity,
        WH40KCinematicActionDefinition action)
    {
        if (TryResolveNpcTargetEntity(run, actorEntity, action, out var explicitTarget))
            return explicitTarget;

        if (!TryResolveFacingDirection(run, actorEntity, action, out var facing))
            return null;

        var origin = _xform.GetMapCoordinates(actorEntity);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var candidate in _lookup.GetEntitiesInRange<TransformComponent>(origin, Math.Max(1.5f, action.SearchRadius)))
        {
            if (candidate.Owner == actorEntity || Deleted(candidate.Owner) || !HasComp<DamageableComponent>(candidate.Owner))
                continue;

            var delta = _xform.GetWorldPosition(candidate.Comp) - origin.Position;
            var distance = delta.Length();
            if (distance <= 0.001f)
                continue;

            var direction = Vector2.Normalize(delta);
            if (Vector2.Dot(direction, facing) < 0.55f || distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = candidate.Owner;
        }

        return best;
    }

    private bool TryResolveFacingDirection(
        ActiveCinematicRun run,
        EntityUid actorEntity,
        WH40KCinematicActionDefinition action,
        out Vector2 direction)
    {
        direction = Vector2.Zero;
        if (action.FacingDirection is { } explicitDirection && explicitDirection.LengthSquared() > 0.0001f)
        {
            direction = Vector2.Normalize(explicitDirection);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(action.TargetNpcId) &&
            TryGetNpcActor(run, action.TargetNpcId, out var targetActor, out _))
        {
            var actorPos = _xform.GetWorldPosition(actorEntity);
            var targetPos = _xform.GetWorldPosition(targetActor.Entity);
            var delta = targetPos - actorPos;
            if (delta.LengthSquared() <= 0.0001f)
                return false;

            direction = Vector2.Normalize(delta);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(action.AnchorId) &&
            TryResolveNpcAnchorCoordinates(run, action.AnchorId, action.ContextId, out var anchorCoords))
        {
            var actorPos = _xform.GetWorldPosition(actorEntity);
            var anchorPos = _xform.ToMapCoordinates(anchorCoords).Position;
            var delta = anchorPos - actorPos;
            if (delta.LengthSquared() <= 0.0001f)
                return false;

            direction = Vector2.Normalize(delta);
            return true;
        }

        return false;
    }

    private bool MatchesPrototype(EntityUid uid, EntProtoId? prototype)
    {
        if (prototype == null)
            return true;

        return TryComp(uid, out MetaDataComponent? meta) &&
               string.Equals(meta.EntityPrototype?.ID, prototype.Value, StringComparison.OrdinalIgnoreCase);
    }

    private EntityUid? FindNearbyPrototype(EntProtoId prototype, MapCoordinates origin, float radius)
    {
        foreach (var candidate in _lookup.GetEntitiesInRange<TransformComponent>(origin, radius))
        {
            if (MatchesPrototype(candidate.Owner, prototype))
                return candidate.Owner;
        }

        return null;
    }

    private TimeSpan ResolveNpcActionTimeout(ActiveCinematicRun run, WH40KCinematicActionDefinition action)
    {
        var seconds = action.DurationSeconds ?? run.Prototype.DefaultWaitTimeoutSeconds ?? NpcDefaultActionTimeoutSeconds;
        return TimeSpan.FromSeconds(Math.Max(0.25f, seconds));
    }

    private bool TryGetNpcRecordingSession(
        IConsoleShell shell,
        bool requireCompleted,
        out NpcRecordingSession session,
        out string message)
    {
        session = default!;
        message = string.Empty;

        if (shell.Player == null)
        {
            message = "NPC recording commands require an in-game admin player session.";
            return false;
        }

        var userId = shell.Player.UserId;
        if (!requireCompleted && _activeNpcRecordingSessions.TryGetValue(userId, out var activeSession) && activeSession != null)
        {
            session = activeSession;
            return true;
        }

        if (requireCompleted && _completedNpcRecordingSessions.TryGetValue(userId, out var completedSession) && completedSession != null)
        {
            session = completedSession;
            return true;
        }

        message = requireCompleted
            ? "No completed NPC recording was found for this admin session."
            : "No active NPC recording was found for this admin session.";
        return false;
    }

    private bool TryGetNpcRecordingActor(NpcRecordingSession session, out NpcActorRuntime actor)
    {
        actor = default!;
        if (!TryFindRun(session.RunSerial, out var run))
            return false;

        return TryGetNpcActor(run, session.NpcId, out actor, out _);
    }

    private void FinalizeNpcRecordingSession(NpcRecordingSession session, bool keepAsCompleted)
    {
        if (!_activeNpcRecordingSessions.Remove(session.UserId))
            _completedNpcRecordingSessions.Remove(session.UserId);

        session.IsActive = false;
        if (session.PausedAt != null)
        {
            session.PausedDuration += _timing.CurTime - session.PausedAt.Value;
            session.PausedAt = null;
        }

        if (session.ControllerMindId != null)
            _mind.UnVisit(session.ControllerMindId.Value);

        if (TryGetNpcRecordingActor(session, out var actor))
            ReleaseScriptControl(actor);

        CaptureMovementSample(session, session.RecordedEntity);

        if (keepAsCompleted)
            _completedNpcRecordingSessions[session.UserId] = session;
    }

    private void CaptureMovementSample(NpcRecordingSession session, EntityUid entity)
    {
        if (!entity.IsValid() || Deleted(entity))
            return;

        var xform = Transform(entity);
        var position = _xform.GetWorldPosition(xform);
        var rotationDegrees = (float) xform.LocalRotation.Degrees;
        var delta = position - session.LastSamplePosition;
        var deltaRotation = Math.Abs(Angle.ShortestDistance(
            Angle.FromDegrees(session.LastSampleRotationDegrees),
            Angle.FromDegrees(rotationDegrees)).Degrees);

        if (delta.Length() >= NpcRecordingMovementThreshold)
        {
            var action = new WH40KCinematicActionDefinition
            {
                Type = WH40KCinematicActionType.NpcMoveByOffset,
                NpcId = session.NpcId,
                Offset = delta,
            };
            session.Entries.Add(new RecordedTrackEntry(GetRecordingSeconds(session), false, action));
            session.LastSamplePosition = position;
        }

        if (deltaRotation >= NpcRecordingRotationThresholdDegrees)
        {
            var facing = xform.LocalRotation.ToWorldVec();
            var action = new WH40KCinematicActionDefinition
            {
                Type = WH40KCinematicActionType.NpcFaceDirection,
                NpcId = session.NpcId,
                FacingDirection = facing,
            };
            session.Entries.Add(new RecordedTrackEntry(GetRecordingSeconds(session), false, action));
            session.LastSampleRotationDegrees = rotationDegrees;
        }
    }

    private void OnRecordedEntitySpoke(EntitySpokeEvent ev)
    {
        if (!TryFindRecordingSession(ev.Source, out var session))
            return;

        session.Entries.Add(new RecordedTrackEntry(GetRecordingSeconds(session), false, new WH40KCinematicActionDefinition
        {
            Type = WH40KCinematicActionType.NpcSpeak,
            NpcId = session.NpcId,
            Message = ev.Message,
        }));
    }

    private void OnRecordedDidEquip(DidEquipEvent ev)
    {
        if (!TryFindRecordingSession(ev.EquipTarget, out var session))
            return;

        if (!TryComp(ev.Equipment, out MetaDataComponent? meta) || meta.EntityPrototype == null)
            return;

        session.Entries.Add(new RecordedTrackEntry(GetRecordingSeconds(session), false, new WH40KCinematicActionDefinition
        {
            Type = WH40KCinematicActionType.NpcEquipPrototype,
            NpcId = session.NpcId,
            Prototype = meta.EntityPrototype.ID,
            Slot = ev.Slot,
            ReuseExistingEntity = false,
        }));
    }

    private void OnRecordedDidUnequip(DidUnequipEvent ev)
    {
        if (!TryFindRecordingSession(ev.EquipTarget, out var session))
            return;

        session.Entries.Add(new RecordedTrackEntry(GetRecordingSeconds(session), false, new WH40KCinematicActionDefinition
        {
            Type = WH40KCinematicActionType.NpcUnequipSlot,
            NpcId = session.NpcId,
            Slot = ev.Slot,
        }));
    }

    private void OnRecordedMeleeAttack(Entity<TransformComponent> ent, ref MeleeAttackEvent ev)
    {
        if (!TryFindRecordingSession(ent.Owner, out var session))
            return;

        var facing = ent.Comp.LocalRotation.ToWorldVec();
        session.Entries.Add(new RecordedTrackEntry(GetRecordingSeconds(session), false, new WH40KCinematicActionDefinition
        {
            Type = WH40KCinematicActionType.NpcAttackDirection,
            NpcId = session.NpcId,
            FacingDirection = facing,
        }));
    }

    private void OnRecordedUserInteractUsing(UserInteractUsingEvent ev)
    {
        if (!TryFindRecordingSession(ev.User, out var session))
            return;

        var action = BuildRecordedUseAction(session, ev.Target);
        session.Entries.Add(new RecordedTrackEntry(GetRecordingSeconds(session), false, action));
    }

    private void OnRecordedUserActivateInWorld(UserActivateInWorldEvent ev)
    {
        if (!TryFindRecordingSession(ev.User, out var session))
            return;

        var action = BuildRecordedUseAction(session, ev.Target);
        session.Entries.Add(new RecordedTrackEntry(GetRecordingSeconds(session), false, action));
    }

    private bool TryFindRecordingSession(EntityUid entity, out NpcRecordingSession session)
    {
        session = default!;
        foreach (var active in _activeNpcRecordingSessions.Values)
        {
            if (!active.IsActive || active.IsPaused || active.RecordedEntity != entity)
                continue;

            session = active;
            return true;
        }

        return false;
    }

    private WH40KCinematicActionDefinition BuildRecordedUseAction(NpcRecordingSession session, EntityUid target)
    {
        var action = new WH40KCinematicActionDefinition
        {
            Type = WH40KCinematicActionType.NpcUseEntity,
            NpcId = session.NpcId,
            SearchRadius = 1.5f,
        };

        if (TryFindRun(session.RunSerial, out var run))
        {
            foreach (var actor in run.NpcActors.Values)
            {
                if (actor.Entity != target)
                    continue;

                action.TargetNpcId = actor.NpcId;
                return action;
            }
        }

        if (TryFindNearestAnchorId(target, out var anchorId))
        {
            action.AnchorId = anchorId;
            return action;
        }

        if (TryComp(target, out MetaDataComponent? meta) && meta.EntityPrototype != null)
            action.Prototype = meta.EntityPrototype.ID;

        return action;
    }

    private bool TryFindNearestAnchorId(EntityUid target, out string anchorId)
    {
        anchorId = string.Empty;
        var targetMap = _xform.GetMapCoordinates(target);
        var bestDistance = float.MaxValue;
        string? bestAnchorId = null;

        var actionQuery = EntityQueryEnumerator<WH40KCinematicActionAnchorComponent, TransformComponent>();
        while (actionQuery.MoveNext(out _, out var anchor, out var xform))
        {
            TryConsiderNearestAnchor(anchor.AnchorId, xform.Coordinates, targetMap, ref bestDistance, ref bestAnchorId);
        }

        var spawnQuery = EntityQueryEnumerator<WH40KCinematicSpawnAnchorComponent, TransformComponent>();
        while (spawnQuery.MoveNext(out _, out var anchor, out var xform))
        {
            TryConsiderNearestAnchor(anchor.AnchorId, xform.Coordinates, targetMap, ref bestDistance, ref bestAnchorId);
        }

        var soundQuery = EntityQueryEnumerator<WH40KCinematicSoundAnchorComponent, TransformComponent>();
        while (soundQuery.MoveNext(out _, out var anchor, out var xform))
        {
            TryConsiderNearestAnchor(anchor.AnchorId, xform.Coordinates, targetMap, ref bestDistance, ref bestAnchorId);
        }

        var npcQuery = EntityQueryEnumerator<WH40KCinematicNpcAnchorComponent, TransformComponent>();
        while (npcQuery.MoveNext(out _, out var anchor, out var xform))
        {
            TryConsiderNearestAnchor(anchor.AnchorId, xform.Coordinates, targetMap, ref bestDistance, ref bestAnchorId);
        }

        anchorId = bestAnchorId ?? string.Empty;
        return !string.IsNullOrWhiteSpace(bestAnchorId);
    }

    private void TryConsiderNearestAnchor(
        string candidateAnchorId,
        EntityCoordinates coords,
        MapCoordinates targetMap,
        ref float bestDistance,
        ref string? bestAnchorId)
    {
        var candidateMap = _xform.ToMapCoordinates(coords);
        if (candidateMap.MapId != targetMap.MapId)
            return;

        var distance = (candidateMap.Position - targetMap.Position).Length();
        if (distance > NpcRecordingInteractionAnchorRadius || distance >= bestDistance)
            return;

        bestDistance = distance;
        bestAnchorId = candidateAnchorId;
    }

    private float GetRecordingSeconds(NpcRecordingSession session)
    {
        var now = session.IsPaused && session.PausedAt != null ? session.PausedAt.Value : _timing.CurTime;
        return Math.Max(0f, (float) (now - session.StartedAt - session.PausedDuration).TotalSeconds);
    }

    private string ResolveNpcRecordingExportPath(string? relativePath, NpcRecordingSession session)
    {
        var defaultName = $"{session.TrackId}_{session.SegmentId}.yml";
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Path.Combine(
                Directory.GetCurrentDirectory(),
                "Resources",
                "Prototypes",
                "_WH40K",
                "Cinematics",
                "Tracks",
                defaultName);
        }

        return Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(Directory.GetCurrentDirectory(), relativePath);
    }

    private string BuildNpcRecordingYaml(NpcRecordingSession session)
    {
        var builder = new StringBuilder();
        builder.AppendLine("- type: wh40kCinematicActorTrack");
        builder.AppendLine($"  id: {session.TrackId}");
        builder.AppendLine("  segments:");
        builder.AppendLine($"  - id: {session.SegmentId}");
        builder.AppendLine("    entries:");

        foreach (var entry in session.Entries.OrderBy(entry => entry.AtSeconds))
        {
            builder.AppendLine($"    - at: {FormatFloat(entry.AtSeconds)}");
            if (entry.WaitForCompletion)
                builder.AppendLine("      waitForCompletion: true");

            builder.AppendLine("      action:");
            AppendTrackActionYaml(builder, entry.Action, "        ");
        }

        return builder.ToString();
    }

    private void AppendTrackActionYaml(StringBuilder builder, WH40KCinematicActionDefinition action, string indent)
    {
        builder.AppendLine($"{indent}type: {action.Type}");

        if (!string.IsNullOrWhiteSpace(action.NpcId))
            builder.AppendLine($"{indent}npcId: {action.NpcId}");

        if (!string.IsNullOrWhiteSpace(action.TargetNpcId))
            builder.AppendLine($"{indent}targetNpcId: {action.TargetNpcId}");

        if (!string.IsNullOrWhiteSpace(action.AnchorId))
            builder.AppendLine($"{indent}anchorId: {action.AnchorId}");

        if (!string.IsNullOrWhiteSpace(action.Slot))
            builder.AppendLine($"{indent}slot: {action.Slot}");

        if (!string.IsNullOrWhiteSpace(action.Message))
            builder.AppendLine($"{indent}message: \"{EscapeYaml(action.Message)}\"");

        if (action.Prototype != null)
            builder.AppendLine($"{indent}prototype: {action.Prototype.Value}");

        if (action.Offset != null)
            builder.AppendLine($"{indent}offset: {FormatVector(action.Offset.Value)}");

        if (action.FacingDirection != null)
            builder.AppendLine($"{indent}facingDirection: {FormatVector(action.FacingDirection.Value)}");

        if (action.SearchRadius > 0f && action.SearchRadius != 1.25f)
            builder.AppendLine($"{indent}searchRadius: {FormatFloat(action.SearchRadius)}");

        if (action.ReuseExistingEntity)
            builder.AppendLine($"{indent}reuseExistingEntity: true");
    }

    private static string FormatVector(Vector2 vector)
    {
        return $"{FormatFloat(vector.X)}, {FormatFloat(vector.Y)}";
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string EscapeYaml(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private sealed class NpcActorRuntime
    {
        public string NpcId { get; }
        public EntityUid Entity { get; set; }
        public bool SpawnedByCinematic { get; }
        public NpcActorBaseline Baseline { get; }
        public bool ScriptControlActive;
        public bool HadHTN;
        public bool OriginalHTNEnabled;

        public NpcActorRuntime(string npcId, EntityUid entity, bool spawnedByCinematic, NpcActorBaseline baseline)
        {
            NpcId = npcId;
            Entity = entity;
            SpawnedByCinematic = spawnedByCinematic;
            Baseline = baseline;
        }
    }

    private readonly record struct NpcActorBaseline(
        EntityCoordinates Coordinates,
        Angle Rotation,
        DamageSpecifier Damage,
        HashSet<ProtoId<NpcFactionPrototype>> Factions,
        ProtoId<StartingGearPrototype>? StartingGearId);

    private sealed class NpcMoveActionRuntime : ActiveActionRuntime
    {
        private readonly NpcActorRuntime _actor;
        private readonly EntityCoordinates _targetCoordinates;
        private readonly bool _directMove;
        private readonly float _range;
        private readonly TimeSpan _timeoutDuration;
        private readonly bool _allowRecoveryTeleport;
        private bool _started;
        private bool _complete;
        private TimeSpan _timeoutAt;

        public NpcMoveActionRuntime(
            string? runtimeId,
            int stepIndex,
            string stepId,
            string actionLabel,
            NpcActorRuntime actor,
            EntityCoordinates targetCoordinates,
            bool directMove,
            bool blocking,
            float range,
            TimeSpan timeout,
            bool allowRecoveryTeleport)
            : base(runtimeId, stepIndex, stepId, actionLabel, blocking)
        {
            _actor = actor;
            _targetCoordinates = targetCoordinates;
            _directMove = directMove;
            _range = range;
            _timeoutDuration = timeout;
            _allowRecoveryTeleport = allowRecoveryTeleport;
        }

        public void Start(WH40KCinematicSystem system)
        {
            if (_started || !_actor.Entity.Valid || system.Deleted(_actor.Entity))
                return;

            var steering = system._steering.Register(_actor.Entity, _targetCoordinates);
            steering.Range = _range;
            _timeoutAt = system._timing.CurTime + _timeoutDuration;
            _started = true;
        }

        public override void Tick(WH40KCinematicSystem system)
        {
            if (_complete || !_actor.Entity.Valid || system.Deleted(_actor.Entity))
            {
                _complete = true;
                return;
            }

            if (!_started)
                Start(system);

            if (system.TryComp<NPCSteeringComponent>(_actor.Entity, out var steering) &&
                steering.Status == SteeringStatus.InRange)
            {
                _complete = true;
                return;
            }

            if (system._timing.CurTime < _timeoutAt)
                return;

            if (_allowRecoveryTeleport)
            {
                system._xform.SetCoordinates(_actor.Entity, _targetCoordinates);
                system._xform.AttachToGridOrMap(_actor.Entity);
            }
            else
            {
                system.Log.Warning($"{ActionLabel}: npc movement timed out before reaching the target.");
            }

            _complete = true;
        }

        public override bool IsComplete(WH40KCinematicSystem system)
        {
            return _complete;
        }

        public override void Cleanup(WH40KCinematicSystem system)
        {
            system._steering.Unregister(_actor.Entity);
        }
    }

    private sealed class NpcPathThroughAnchorsRuntime : ActiveActionRuntime
    {
        private readonly NpcActorRuntime _actor;
        private readonly Queue<EntityCoordinates> _targets;
        private readonly TimeSpan _timeoutDuration;
        private readonly bool _allowRecoveryTeleport;
        private bool _complete;
        private EntityCoordinates? _activeTarget;
        private TimeSpan _timeoutAt;

        public NpcPathThroughAnchorsRuntime(
            string? runtimeId,
            int stepIndex,
            string stepId,
            string actionLabel,
            NpcActorRuntime actor,
            Queue<EntityCoordinates> targets,
            bool blocking,
            TimeSpan timeout,
            bool allowRecoveryTeleport)
            : base(runtimeId, stepIndex, stepId, actionLabel, blocking)
        {
            _actor = actor;
            _targets = targets;
            _timeoutDuration = timeout;
            _allowRecoveryTeleport = allowRecoveryTeleport;
        }

        public void Start(WH40KCinematicSystem system)
        {
            Advance(system);
        }

        public override void Tick(WH40KCinematicSystem system)
        {
            if (_complete || !_actor.Entity.Valid || system.Deleted(_actor.Entity))
            {
                _complete = true;
                return;
            }

            if (_activeTarget == null)
                Start(system);

            if (system.TryComp<NPCSteeringComponent>(_actor.Entity, out var steering) &&
                steering.Status == SteeringStatus.InRange)
            {
                Advance(system);
                return;
            }

            if (system._timing.CurTime < _timeoutAt)
                return;

            if (_allowRecoveryTeleport && _activeTarget != null)
            {
                system._xform.SetCoordinates(_actor.Entity, _activeTarget.Value);
                system._xform.AttachToGridOrMap(_actor.Entity);
            }
            else
            {
                system.Log.Warning($"{ActionLabel}: npc path-through-anchors timed out.");
            }

            _complete = true;
        }

        public override bool IsComplete(WH40KCinematicSystem system)
        {
            return _complete;
        }

        public override void Cleanup(WH40KCinematicSystem system)
        {
            system._steering.Unregister(_actor.Entity);
        }

        private void Advance(WH40KCinematicSystem system)
        {
            if (_targets.Count == 0)
            {
                _complete = true;
                return;
            }

            _activeTarget = _targets.Dequeue();
            var steering = system._steering.Register(_actor.Entity, _activeTarget.Value);
            steering.Range = NpcPathMoveRange;
            _timeoutAt = system._timing.CurTime + _timeoutDuration;
        }
    }

    private sealed class NpcWaitActionRuntime : ActiveActionRuntime
    {
        private readonly TimeSpan _endsAt;

        public NpcWaitActionRuntime(
            string? runtimeId,
            int stepIndex,
            string stepId,
            string actionLabel,
            bool blocking,
            float durationSeconds,
            TimeSpan startedAt)
            : base(runtimeId, stepIndex, stepId, actionLabel, blocking)
        {
            _endsAt = startedAt + TimeSpan.FromSeconds(durationSeconds);
        }

        public override bool IsComplete(WH40KCinematicSystem system)
        {
            return system._timing.CurTime >= _endsAt;
        }
    }

    private sealed class ActorTrackActionRuntime : ActiveActionRuntime
    {
        private readonly ActiveCinematicRun _run;
        private readonly WH40KCinematicStepDefinition _step;
        private readonly string _defaultNpcId;
        private readonly string _trackId;
        private readonly WH40KCinematicActorTrackSegmentDefinition _segment;
        private readonly TimeSpan _startedAt;
        private int _nextEntryIndex;
        private ActiveActionRuntime? _waitingOn;
        private bool _complete;

        public ActorTrackActionRuntime(
            string? runtimeId,
            int stepIndex,
            string stepId,
            string actionLabel,
            bool blocking,
            ActiveCinematicRun run,
            WH40KCinematicStepDefinition step,
            string defaultNpcId,
            string trackId,
            WH40KCinematicActorTrackSegmentDefinition segment)
            : base(runtimeId, stepIndex, stepId, actionLabel, blocking)
        {
            _run = run;
            _step = step;
            _defaultNpcId = defaultNpcId;
            _trackId = trackId;
            _segment = segment;
            _startedAt = run.StepStartedAt;
        }

        public override void Tick(WH40KCinematicSystem system)
        {
            if (_complete)
                return;

            if (_waitingOn != null && !_waitingOn.IsComplete(system))
                return;

            _waitingOn = null;
            var elapsed = Math.Max(0f, (float) (system._timing.CurTime - _startedAt).TotalSeconds);

            while (_nextEntryIndex < _segment.Entries.Count)
            {
                var entry = _segment.Entries[_nextEntryIndex];
                if (entry.AtSeconds > elapsed + 0.0001f)
                    return;

                _nextEntryIndex++;
                var clonedAction = entry.Action.Clone();
                clonedAction.NpcId ??= _defaultNpcId;
                clonedAction.Id ??= $"{_trackId}:{_segment.Id}:{_nextEntryIndex}:{NpcTrackEntryRuntimeIdSalt.ToString(CultureInfo.InvariantCulture)}";

                if (!system.TryExecuteAction(_run, _step, clonedAction, _nextEntryIndex - 1, out var failureReason))
                {
                    system.Log.Warning($"{ActionLabel}: skipped track entry {_nextEntryIndex} because it failed: {failureReason}");
                    continue;
                }

                if (!entry.WaitForCompletion)
                    continue;

                _waitingOn = _run.ActiveActions.LastOrDefault(runtime => runtime != this && runtime.RuntimeId == clonedAction.Id);
                if (_waitingOn != null)
                    return;
            }

            _complete = true;
        }

        public override bool IsComplete(WH40KCinematicSystem system)
        {
            return _complete;
        }
    }

    private sealed class NpcRecordingSession
    {
        public NetUserId UserId { get; }
        public int RunSerial { get; }
        public string CinematicId { get; }
        public string NpcId { get; }
        public EntityUid RecordedEntity { get; }
        public string TrackId { get; }
        public string SegmentId { get; }
        public EntityUid? ControllerMindId { get; }
        public TimeSpan StartedAt { get; }
        public TimeSpan PausedDuration;
        public TimeSpan? PausedAt;
        public TimeSpan NextMovementSampleAt;
        public Vector2 LastSamplePosition;
        public float LastSampleRotationDegrees;
        public bool IsPaused;
        public bool IsActive = true;
        public List<RecordedTrackEntry> Entries { get; } = new();

        public NpcRecordingSession(
            NetUserId userId,
            int runSerial,
            string cinematicId,
            string npcId,
            EntityUid recordedEntity,
            string trackId,
            string segmentId,
            EntityUid? controllerMindId,
            TimeSpan startedAt)
        {
            UserId = userId;
            RunSerial = runSerial;
            CinematicId = cinematicId;
            NpcId = npcId;
            RecordedEntity = recordedEntity;
            TrackId = trackId;
            SegmentId = segmentId;
            ControllerMindId = controllerMindId;
            StartedAt = startedAt;
        }
    }

    private readonly record struct RecordedTrackEntry(float AtSeconds, bool WaitForCompletion, WH40KCinematicActionDefinition Action);
}
