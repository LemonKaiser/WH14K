using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared.Camera;
using Content.Server.Chat.Systems;
using Content.Server.Camera;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.Notifications;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared._WH40K.Cinematic;
using Content.Shared._WH40K.Notifications;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Cinematic;

public sealed partial class WH40KCinematicSystem : EntitySystem
{
    private const float AudienceShakeDefaultPulseIntervalSeconds = 0.08f;
    private const float AudienceShakeMinPulseIntervalSeconds = 0.03f;
    private const float AudienceShakeMaxPulseIntervalSeconds = 0.25f;
    private const float AudienceShakeBaseKickMagnitude = 0.02f;
    private const float AudienceShakeKickScale = 0.055f;
    private const float AudienceShakeMaxKickMagnitude = 0.25f;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _cameraRecoil = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly WH40KNotificationSystem _notifications = default!;
    [Dependency] private readonly ViewSubscriberSystem _viewSubscribers = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private readonly Queue<QueuedCinematicRequest> _queue = new();
    private readonly HashSet<string> _completedNonRepeatable = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ActiveActionRuntime> _persistentActions = new();
    private static readonly TimeSpan ActiveStateResyncInterval = TimeSpan.FromSeconds(0.25);

    private bool _traceLoggingEnabled;
    private ActiveCinematicRun? _active;
    private int _runSerial;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, CCVars.WH40KCinematicTrace, value => _traceLoggingEnabled = value, true);

        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<WH40KQueueCinematicEvent>(OnQueueCinematic);
        SubscribeLocalEvent<WH40KStopCinematicEvent>(OnStopCinematic);
        InitializeScopedAudienceFeatures();
        InitializeSceneControlFeatures();
        InitializeNpcTrackFeatures();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        RefreshPersistentActions();
        UpdateScopedRuns();

        if (_active == null)
            TryStartNextQueued();
        else
            UpdateRun(_active);

        TryStartNextQueuedScoped();
        UpdateNpcTrackRecording(frameTime);
    }

    public bool TryQueue(ProtoId<WH40KCinematicPrototype> cinematicId, out string message)
    {
        message = string.Empty;

        if (!_prototypes.TryIndex(cinematicId, out var prototype))
        {
            message = $"Unknown cinematic prototype '{cinematicId}'.";
            return false;
        }

        return TryQueue(prototype, out message);
    }

    public bool TryQueue(string cinematicId, out string message)
    {
        return TryQueue(new ProtoId<WH40KCinematicPrototype>(cinematicId), out message);
    }

    public bool TryQueue(WH40KCinematicPrototype prototype, out string message)
    {
        var errors = ValidatePrototype(prototype);
        if (errors.Count > 0)
        {
            message = $"Cinematic '{prototype.ID}' is invalid: {string.Join("; ", errors)}";
            return false;
        }

        if (!prototype.AllowRepeat)
        {
            if (_completedNonRepeatable.Contains(prototype.ID))
            {
                message = $"Cinematic '{prototype.ID}' is non-repeatable and has already completed this round.";
                return false;
            }

            if (HasActiveOrQueuedPrototype(prototype.ID))
            {
                message = $"Cinematic '{prototype.ID}' is already active or queued and cannot repeat.";
                return false;
            }
        }

        if (_active == null)
        {
            StartPrototype(prototype, queuedAt: _timing.CurTime);
            message = $"Started cinematic '{prototype.ID}'.";
            return true;
        }

        if (prototype.QueueMode == WH40KCinematicQueueMode.IgnoreIfBusy)
        {
            message = $"Cinematic '{prototype.ID}' is configured to ignore busy state and was not queued.";
            return false;
        }

        _queue.Enqueue(new QueuedCinematicRequest(prototype, _timing.CurTime));
        BroadcastActiveState();
        message = $"Queued cinematic '{prototype.ID}' at position {_queue.Count}.";
        return true;
    }

    public bool TryStopActive(string reason = "Stopped", bool markCompleted = false)
    {
        if (_active == null)
            return false;

        AbortActive(reason, markCompleted);
        return true;
    }

    public void ClearQueue()
    {
        _queue.Clear();
        ClearScopedQueue();
        BroadcastActiveState();
    }

    public WH40KCinematicRuntimeSnapshot GetSnapshot()
    {
        return new WH40KCinematicRuntimeSnapshot(
            _active != null,
            _active?.Prototype.ID,
            _active?.CurrentStepIndex ?? -1,
            _active?.CurrentStep.Id,
            _active?.WaitMode,
            _queue.Count,
            _completedNonRepeatable.Count);
    }

    public IReadOnlyList<string> ValidatePrototype(ProtoId<WH40KCinematicPrototype> cinematicId)
    {
        if (!_prototypes.TryIndex(cinematicId, out var prototype))
            return [$"Unknown cinematic prototype '{cinematicId}'."];

        return ValidatePrototype(prototype);
    }

    public IReadOnlyList<string> ValidatePrototype(string cinematicId)
    {
        return ValidatePrototype(new ProtoId<WH40KCinematicPrototype>(cinematicId));
    }

    public List<string> ValidatePrototype(WH40KCinematicPrototype prototype)
    {
        var errors = new List<string>();
        var actionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validatedTrackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (prototype.Steps.Count == 0)
            errors.Add("Timeline must contain at least one step.");

        if (prototype.RestoreInputDelaySeconds < 0f)
            errors.Add("restoreInputDelay must be >= 0.");

        if (prototype.DefaultWaitTimeoutSeconds < 0f)
            errors.Add("defaultWaitTimeout must be >= 0 when provided.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < prototype.Steps.Count; i++)
        {
            var step = prototype.Steps[i];
            var label = $"step[{i}]";

            if (string.IsNullOrWhiteSpace(step.Id))
            {
                errors.Add($"{label}: id must not be empty.");
            }
            else if (!ids.Add(step.Id))
            {
                errors.Add($"{label}: duplicate id '{step.Id}'.");
            }

            switch (step.WaitMode)
            {
                case WH40KCinematicWaitMode.Instant:
                case WH40KCinematicWaitMode.Duration:
                case WH40KCinematicWaitMode.AwaitCompletion:
                case WH40KCinematicWaitMode.AwaitCompletionOrTimeout:
                case WH40KCinematicWaitMode.AwaitSignal:
                case WH40KCinematicWaitMode.AwaitSignalOrTimeout:
                case WH40KCinematicWaitMode.AwaitEntitySetEmpty:
                case WH40KCinematicWaitMode.Terminal:
                    break;

                default:
                    errors.Add($"{label}: unknown waitMode '{step.WaitMode}'.");
                    break;
            }

            switch (step.Type)
            {
                case WH40KCinematicStepType.Marker:
                case WH40KCinematicStepType.Shot:
                case WH40KCinematicStepType.EndCinematic:
                    break;

                default:
                    errors.Add($"{label}: unknown stepType '{step.Type}'.");
                    break;
            }

            if (step.WaitMode == WH40KCinematicWaitMode.Duration && step.DurationSeconds <= 0f)
                errors.Add($"{label}: duration waitMode requires duration > 0.");

            if (step.WaitMode == WH40KCinematicWaitMode.AwaitCompletionOrTimeout && step.TimeoutSeconds <= 0f)
                errors.Add($"{label}: AwaitCompletionOrTimeout requires timeout > 0.");

            if (step.WaitMode == WH40KCinematicWaitMode.AwaitSignal &&
                step.WaitSignals.Count == 0)
            {
                errors.Add($"{label}: AwaitSignal requires at least one waitSignal.");
            }

            if (step.WaitMode == WH40KCinematicWaitMode.AwaitSignalOrTimeout)
            {
                if (step.WaitSignals.Count == 0)
                    errors.Add($"{label}: AwaitSignalOrTimeout requires at least one waitSignal.");

                if (step.TimeoutSeconds <= 0f && prototype.DefaultWaitTimeoutSeconds is not > 0f)
                    errors.Add($"{label}: AwaitSignalOrTimeout requires timeout > 0 or prototype defaultWaitTimeout > 0.");
            }

            if (step.WaitMode == WH40KCinematicWaitMode.AwaitEntitySetEmpty &&
                step.WaitEntitySets.Count == 0)
            {
                errors.Add($"{label}: AwaitEntitySetEmpty requires at least one waitEntitySet.");
            }

            if (step.Type == WH40KCinematicStepType.EndCinematic && step.WaitMode != WH40KCinematicWaitMode.Terminal)
                errors.Add($"{label}: endCinematic step must use Terminal waitMode.");

            if (step.Type == WH40KCinematicStepType.Shot)
            {
                if (step.CameraSource == WH40KCinematicCameraSource.FixedPoint &&
                    string.IsNullOrWhiteSpace(step.CameraPointId))
                {
                    errors.Add($"{label}: fixed-point shot step requires cameraPoint.");
                }

                if (step.WaitMode == WH40KCinematicWaitMode.Terminal)
                    errors.Add($"{label}: shot step cannot use Terminal waitMode.");

                if (step.CameraTransition == WH40KCinematicCameraTransitionMode.Blend &&
                    step.CameraSource == WH40KCinematicCameraSource.FixedPoint &&
                    step.BlendDurationSeconds <= 0f)
                {
                    errors.Add($"{label}: blend transition requires blendDuration > 0.");
                }

                if (step.CameraSource != WH40KCinematicCameraSource.FixedPoint &&
                    step.AudienceLock != WH40KCinematicAudienceLockDirective.Unlock)
                {
                    errors.Add($"{label}: non-fixed shot camera sources require audienceLock: Unlock.");
                }

                if (step.CameraZoom is <= 0f)
                    errors.Add($"{label}: cameraZoom must be > 0 when provided.");
            }

            for (var actionIndex = 0; actionIndex < step.Actions.Count; actionIndex++)
            {
                var action = step.Actions[actionIndex];
                var actionLabel = $"{label}.action[{actionIndex}]";

                if (string.IsNullOrWhiteSpace(action.Id))
                    continue;

                action.Id = action.Id.Trim();
                if (!actionIds.Add(action.Id))
                    errors.Add($"{actionLabel}: duplicate action id '{action.Id}'. Action ids must be unique inside a cinematic prototype.");
            }
        }

        for (var i = 0; i < prototype.Steps.Count; i++)
        {
            var step = prototype.Steps[i];
            ValidateStepActions(prototype, step, $"step[{i}]", errors, actionIds, validatedTrackIds);
        }

        return errors;
    }

    private void ValidateStepActions(
        WH40KCinematicPrototype prototype,
        WH40KCinematicStepDefinition step,
        string stepLabel,
        List<string> errors,
        IReadOnlySet<string> knownActionIds,
        HashSet<string> validatedTrackIds)
    {
        for (var actionIndex = 0; actionIndex < step.Actions.Count; actionIndex++)
        {
            var action = step.Actions[actionIndex];
            var label = $"{stepLabel}.action[{actionIndex}]";
            ValidateActionDefinition(prototype, step, action, label, errors, knownActionIds, validatedTrackIds, allowNestedActorTrack: true);
        }
    }

    private void ValidateActionDefinition(
        WH40KCinematicPrototype prototype,
        WH40KCinematicStepDefinition step,
        WH40KCinematicActionDefinition action,
        string label,
        List<string> errors,
        IReadOnlySet<string> knownActionIds,
        HashSet<string> validatedTrackIds,
        bool allowNestedActorTrack)
    {
        if (action.PersistAfterCinematic &&
            action.Type is not WH40KCinematicActionType.PlayGlobalSound and
                not WH40KCinematicActionType.PlayAnchorSound and
                not WH40KCinematicActionType.RunLavaFlow and
                not WH40KCinematicActionType.StartAudienceShake)
        {
            errors.Add($"{label}: persistAfterCinematic is only supported for sound, audience shake, and runLavaFlow actions.");
        }

        if (action.Blocking &&
            action.Type is WH40KCinematicActionType.Notify or
                WH40KCinematicActionType.Announcement or
                WH40KCinematicActionType.StartAudienceShake or
                WH40KCinematicActionType.StopActions or
                WH40KCinematicActionType.NpcSpeak or
                WH40KCinematicActionType.NpcEmote or
                WH40KCinematicActionType.NpcFaceDirection or
                WH40KCinematicActionType.NpcSetHTNEnabled or
                WH40KCinematicActionType.NpcReleaseScriptControl)
        {
            errors.Add($"{label}: blocking is not meaningful for this action type.");
        }

        if (action.Blocking &&
            action.PersistAfterCinematic &&
            action.Type is WH40KCinematicActionType.PlayGlobalSound or WH40KCinematicActionType.PlayAnchorSound)
        {
            errors.Add($"{label}: a blocking sound action cannot also persist after the cinematic.");
        }

        switch (action.Type)
        {
            case WH40KCinematicActionType.Notify:
                if (string.IsNullOrWhiteSpace(action.Text) && string.IsNullOrWhiteSpace(action.TextLoc))
                    errors.Add($"{label}: notify requires text or textLoc.");
                break;

            case WH40KCinematicActionType.Announcement:
                if (string.IsNullOrWhiteSpace(action.Message) && string.IsNullOrWhiteSpace(action.MessageLoc))
                    errors.Add($"{label}: announcement requires message or messageLoc.");
                break;

            case WH40KCinematicActionType.StartAudienceShake:
                if (action.ShakeIntensity is null or <= 0f)
                    errors.Add($"{label}: startAudienceShake requires shakeIntensity > 0.");

                if (action.ShakeRampDurationSeconds is < 0f)
                    errors.Add($"{label}: startAudienceShake shakeRampDuration must be >= 0 when provided.");

                if (action.ShakePulseIntervalSeconds is <= 0f)
                    errors.Add($"{label}: startAudienceShake shakePulseInterval must be > 0 when provided.");
                break;

            case WH40KCinematicActionType.PlayGlobalSound:
                if (action.Sound == null)
                    errors.Add($"{label}: playGlobalSound requires sound.");

                if (action.DeliveryScope is WH40KCinematicSoundDeliveryScope.Pvs or
                    WH40KCinematicSoundDeliveryScope.Radius or
                    WH40KCinematicSoundDeliveryScope.Map)
                {
                    errors.Add($"{label}: playGlobalSound only supports Audience or Broadcast deliveryScope.");
                }

                if (action.Blocking &&
                    step.WaitMode == WH40KCinematicWaitMode.AwaitCompletion &&
                    ResolveEffectiveLoop(action))
                {
                    errors.Add($"{label}: blocking looped sound requires AwaitCompletionOrTimeout or a duration-based step.");
                }
                break;

            case WH40KCinematicActionType.PlayAnchorSound:
                if (action.Sound == null)
                    errors.Add($"{label}: playAnchorSound requires sound.");

                if (string.IsNullOrWhiteSpace(action.AnchorId))
                    errors.Add($"{label}: playAnchorSound requires anchorId.");

                if (action.DeliveryScope == WH40KCinematicSoundDeliveryScope.Radius &&
                    action.Radius is null or <= 0f)
                {
                    errors.Add($"{label}: playAnchorSound with Radius deliveryScope requires radius > 0.");
                }

                if (action.Blocking &&
                    step.WaitMode == WH40KCinematicWaitMode.AwaitCompletion &&
                    ResolveEffectiveLoop(action))
                {
                    errors.Add($"{label}: blocking looped sound requires AwaitCompletionOrTimeout or a duration-based step.");
                }
                break;

            case WH40KCinematicActionType.ApplyLocalDamageToAudience:
                if (action.Damage == null || action.Damage.Empty)
                    errors.Add($"{label}: applyLocalDamageToAudience requires a non-empty damage specifier.");
                break;

            case WH40KCinematicActionType.StopActions:
            {
                var targets = ResolveTargetActionIds(action);
                if (targets.Count == 0)
                {
                    errors.Add($"{label}: stopActions requires targetActionId or targetActionIds.");
                    break;
                }

                foreach (var target in targets)
                {
                    if (!knownActionIds.Contains(target))
                        errors.Add($"{label}: unknown target action id '{target}'.");
                }

                break;
            }

            case WH40KCinematicActionType.SpawnAtAnchor:
                if (string.IsNullOrWhiteSpace(action.AnchorId))
                    errors.Add($"{label}: spawnAtAnchor requires anchorId.");

                if (action.Prototype == null)
                    errors.Add($"{label}: spawnAtAnchor requires prototype.");
                break;

            case WH40KCinematicActionType.RunLavaFlow:
                if (prototype.WorldFreezeMode == WH40KCinematicWorldFreezeMode.PauseMap)
                {
                    errors.Add(
                        $"{label}: runLavaFlow is not compatible with PauseMap because tile mutation and entity-based lava overlay progression must continue while the scene is active.");
                }

                if (string.IsNullOrWhiteSpace(action.FlowId))
                    errors.Add($"{label}: runLavaFlow requires flowId.");

                if (action.Width is <= 0)
                    errors.Add($"{label}: runLavaFlow width override must be > 0 when provided.");

                if (action.AdvanceIntervalSeconds is < 0f)
                    errors.Add($"{label}: runLavaFlow advanceInterval must be >= 0 when provided.");

                if (action.TilesPerAdvance is <= 0)
                    errors.Add($"{label}: runLavaFlow tilesPerAdvance must be > 0 when provided.");
                break;

            case WH40KCinematicActionType.LoadSceneMap:
                if (string.IsNullOrWhiteSpace(action.ContextId))
                    errors.Add($"{label}: loadSceneMap requires contextId.");

                if (action.SceneMapPath == null == (action.SceneGridPath == null))
                    errors.Add($"{label}: loadSceneMap requires exactly one of sceneMapPath or sceneGridPath.");

                if (action.SceneTransferMode == WH40KCinematicSceneTransferMode.TeleportParticipants &&
                    string.IsNullOrWhiteSpace(action.EntryAnchorId))
                {
                    errors.Add($"{label}: TeleportParticipants scene load requires entryAnchorId.");
                }

                if (action.SceneReturnPolicy == WH40KCinematicSceneReturnPolicy.ReturnAnchor &&
                    string.IsNullOrWhiteSpace(action.ReturnAnchorId))
                {
                    errors.Add($"{label}: scene return policy ReturnAnchor requires returnAnchorId.");
                }

                if (action.SceneTransferMode == WH40KCinematicSceneTransferMode.TeleportParticipants &&
                    action.SceneCleanupPolicy == WH40KCinematicSceneCleanupPolicy.DestroyOnFinish &&
                    action.SceneReturnPolicy == WH40KCinematicSceneReturnPolicy.None)
                {
                    errors.Add($"{label}: TeleportParticipants scene load cannot use DestroyOnFinish together with sceneReturnPolicy=None.");
                }
                break;

            case WH40KCinematicActionType.UnloadSceneMap:
            case WH40KCinematicActionType.SwitchContext:
                if (string.IsNullOrWhiteSpace(action.ContextId))
                    errors.Add($"{label}: {action.Type} requires contextId.");
                break;

            case WH40KCinematicActionType.EmitSignal:
                if (string.IsNullOrWhiteSpace(action.Signal))
                    errors.Add($"{label}: emitSignal requires signal.");
                break;

            case WH40KCinematicActionType.ClearEntitySet:
                if (string.IsNullOrWhiteSpace(action.EntitySetId))
                    errors.Add($"{label}: clearEntitySet requires entitySetId.");
                break;

            case WH40KCinematicActionType.SpawnNpc:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: spawnNpc requires npcId.");

                if (string.IsNullOrWhiteSpace(action.AnchorId))
                    errors.Add($"{label}: spawnNpc requires anchorId.");
                break;

            case WH40KCinematicActionType.BindExistingEntityAsNpc:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: bindExistingEntityAsNpc requires npcId.");

                if (string.IsNullOrWhiteSpace(action.AnchorId) && action.Prototype == null)
                    errors.Add($"{label}: bindExistingEntityAsNpc requires anchorId or prototype.");
                break;

            case WH40KCinematicActionType.DespawnNpc:
            case WH40KCinematicActionType.NpcReleaseScriptControl:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: {action.Type} requires npcId.");
                break;

            case WH40KCinematicActionType.NpcSpeak:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: npcSpeak requires npcId.");

                if (string.IsNullOrWhiteSpace(action.Message) && string.IsNullOrWhiteSpace(action.MessageLoc))
                    errors.Add($"{label}: npcSpeak requires message or messageLoc.");
                break;

            case WH40KCinematicActionType.NpcEmote:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: npcEmote requires npcId.");

                if (string.IsNullOrWhiteSpace(action.Message) && string.IsNullOrWhiteSpace(action.Text))
                    errors.Add($"{label}: npcEmote requires message or text containing the emote id.");
                break;

            case WH40KCinematicActionType.NpcFaceDirection:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: npcFaceDirection requires npcId.");

                if (action.FacingDirection == null &&
                    string.IsNullOrWhiteSpace(action.TargetNpcId) &&
                    string.IsNullOrWhiteSpace(action.AnchorId))
                {
                    errors.Add($"{label}: npcFaceDirection requires facingDirection, targetNpcId, or anchorId.");
                }
                break;

            case WH40KCinematicActionType.NpcMoveByOffset:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: npcMoveByOffset requires npcId.");

                if (action.Offset == null)
                    errors.Add($"{label}: npcMoveByOffset requires offset.");
                break;

            case WH40KCinematicActionType.NpcMoveToAnchor:
            case WH40KCinematicActionType.NpcPathToAnchor:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: {action.Type} requires npcId.");

                if (string.IsNullOrWhiteSpace(action.AnchorId))
                    errors.Add($"{label}: {action.Type} requires anchorId.");
                break;

            case WH40KCinematicActionType.NpcPathThroughAnchors:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: npcPathThroughAnchors requires npcId.");

                if (action.AnchorIds.Count == 0)
                    errors.Add($"{label}: npcPathThroughAnchors requires anchorIds.");
                break;

            case WH40KCinematicActionType.NpcAttackDirection:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: npcAttackDirection requires npcId.");
                break;

            case WH40KCinematicActionType.NpcUseEntity:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: npcUseEntity requires npcId.");

                if (string.IsNullOrWhiteSpace(action.TargetNpcId) &&
                    string.IsNullOrWhiteSpace(action.AnchorId) &&
                    action.Prototype == null)
                {
                    errors.Add($"{label}: npcUseEntity requires targetNpcId, anchorId, or prototype.");
                }
                break;

            case WH40KCinematicActionType.NpcEquipPrototype:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: npcEquipPrototype requires npcId.");

                if (action.Prototype == null)
                    errors.Add($"{label}: npcEquipPrototype requires prototype.");
                break;

            case WH40KCinematicActionType.NpcUnequipSlot:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: npcUnequipSlot requires npcId.");

                if (string.IsNullOrWhiteSpace(action.Slot))
                    errors.Add($"{label}: npcUnequipSlot requires slot.");
                break;

            case WH40KCinematicActionType.NpcSetHTNEnabled:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: npcSetHTNEnabled requires npcId.");

                if (action.HtnEnabled == null)
                    errors.Add($"{label}: npcSetHTNEnabled requires htnEnabled.");
                break;

            case WH40KCinematicActionType.NpcWait:
                if (action.DurationSeconds is null or <= 0f)
                    errors.Add($"{label}: npcWait requires duration > 0.");
                break;

            case WH40KCinematicActionType.PlayActorTrack:
                if (string.IsNullOrWhiteSpace(action.NpcId))
                    errors.Add($"{label}: playActorTrack requires npcId.");

                if (!allowNestedActorTrack)
                {
                    errors.Add($"{label}: nested playActorTrack is not supported inside actor tracks.");
                    break;
                }

                if (action.TrackId == null)
                {
                    errors.Add($"{label}: playActorTrack requires trackId.");
                }
                else if (!_prototypes.HasIndex<WH40KCinematicActorTrackPrototype>(action.TrackId.Value))
                {
                    errors.Add($"{label}: unknown actor track prototype '{action.TrackId.Value}'.");
                }
                else
                {
                    var track = _prototypes.Index<WH40KCinematicActorTrackPrototype>(action.TrackId.Value);
                    if (!string.IsNullOrWhiteSpace(action.TrackSegmentId) &&
                        !track.Segments.Any(segment => string.Equals(segment.Id, action.TrackSegmentId, StringComparison.OrdinalIgnoreCase)))
                    {
                        errors.Add($"{label}: actor track '{track.ID}' does not contain segment '{action.TrackSegmentId}'.");
                    }

                    if (validatedTrackIds.Add(track.ID))
                        ValidateActorTrackPrototype(prototype, step, track, label, errors, knownActionIds, validatedTrackIds);
                }
                break;

            default:
                errors.Add($"{label}: unknown actionType '{action.Type}'.");
                break;
        }
    }

    private void ValidateActorTrackPrototype(
        WH40KCinematicPrototype prototype,
        WH40KCinematicStepDefinition parentStep,
        WH40KCinematicActorTrackPrototype track,
        string label,
        List<string> errors,
        IReadOnlySet<string> knownActionIds,
        HashSet<string> validatedTrackIds)
    {
        if (track.Segments.Count == 0)
        {
            errors.Add($"{label}: actor track '{track.ID}' must contain at least one segment.");
            return;
        }

        var segmentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var segmentIndex = 0; segmentIndex < track.Segments.Count; segmentIndex++)
        {
            var segment = track.Segments[segmentIndex];
            var segmentLabel = $"{label}.track '{track.ID}'.segment[{segmentIndex}]";

            if (string.IsNullOrWhiteSpace(segment.Id))
            {
                errors.Add($"{segmentLabel}: id must not be empty.");
            }
            else if (!segmentIds.Add(segment.Id))
            {
                errors.Add($"{segmentLabel}: duplicate segment id '{segment.Id}'.");
            }

            if (segment.Entries.Count == 0)
            {
                errors.Add($"{segmentLabel}: entries must contain at least one action.");
                continue;
            }

            var lastAt = -0.0001f;
            for (var entryIndex = 0; entryIndex < segment.Entries.Count; entryIndex++)
            {
                var entry = segment.Entries[entryIndex];
                var entryLabel = $"{segmentLabel}.entry[{entryIndex}]";

                if (entry.AtSeconds < 0f)
                    errors.Add($"{entryLabel}: at must be >= 0.");

                if (entry.AtSeconds + 0.0001f < lastAt)
                    errors.Add($"{entryLabel}: entries must be sorted by ascending at.");

                lastAt = Math.Max(lastAt, entry.AtSeconds);

                if (!string.IsNullOrWhiteSpace(entry.Action.Id))
                    errors.Add($"{entryLabel}: explicit action id is not supported inside actor tracks; runtime ids are generated automatically.");

                if (entry.Action.Type == WH40KCinematicActionType.StopActions)
                    errors.Add($"{entryLabel}: stopActions is not supported inside actor tracks.");

                ValidateActionDefinition(
                    prototype,
                    parentStep,
                    entry.Action,
                    $"{entryLabel}.action",
                    errors,
                    knownActionIds,
                    validatedTrackIds,
                    allowNestedActorTrack: false);
            }
        }
    }

    private void StartPrototype(WH40KCinematicPrototype prototype, TimeSpan queuedAt)
    {
        _active = new ActiveCinematicRun(++_runSerial, prototype, queuedAt, _timing.CurTime)
        {
            Priority = prototype.Priority,
            GhostAudiencePolicy = prototype.GhostAudiencePolicy
        };
        ApplyGlobalRunConflictPolicy(_active);
        _active.AudienceLocked = prototype.LockAudienceOnStart;
        EnrollCurrentAudience(_active);
        EnsureRunMainContext(_active);
        TraceInfo($"Started WH40K cinematic '{prototype.ID}' with {prototype.Steps.Count} step(s).");
        AdvanceToNextStep(_active, "Start");
    }

    private void TryStartNextQueued()
    {
        while (_active == null && _queue.TryDequeue(out var queued))
        {
            var errors = ValidatePrototype(queued.Prototype);
            if (errors.Count > 0)
            {
                Log.Warning($"Skipping queued cinematic '{queued.Prototype.ID}' because it is no longer valid: {string.Join("; ", errors)}");
                continue;
            }

            if (!queued.Prototype.AllowRepeat &&
                _completedNonRepeatable.Contains(queued.Prototype.ID))
            {
                Log.Warning($"Skipping queued cinematic '{queued.Prototype.ID}' because it has already completed and cannot repeat.");
                continue;
            }

            StartPrototype(queued.Prototype, queued.QueuedAt);
        }
    }

    private void AdvanceToNextStep(string reason)
    {
        if (_active == null || _active.RestorePhaseActive)
            return;

        AdvanceToNextStep(_active, reason);
    }

    private bool TryResolveShot(
        WH40KCinematicPrototype prototype,
        WH40KCinematicStepDefinition step,
        out WH40KCinematicShotRuntimeState shot)
    {
        return TryResolveShot(prototype, active: null, step, out shot);
    }

    private bool TryResolveShot(
        WH40KCinematicPrototype prototype,
        ActiveCinematicRun? active,
        WH40KCinematicStepDefinition step,
        out WH40KCinematicShotRuntimeState shot)
    {
        shot = default!;

        if (string.IsNullOrWhiteSpace(step.CameraPointId))
            return false;

        if (TryResolveShotInternal(prototype, active, step, respectContext: true, out shot))
            return true;

        return ShouldFallbackToAnyContext(active, step.ContextId) &&
               TryResolveShotInternal(prototype, active, step, respectContext: false, out shot);
    }

    private bool TryResolveShotInternal(
        WH40KCinematicPrototype prototype,
        ActiveCinematicRun? active,
        WH40KCinematicStepDefinition step,
        bool respectContext,
        out WH40KCinematicShotRuntimeState shot)
    {
        shot = default!;
        var found = false;
        var query = AllEntityQuery<WH40KCinematicCameraPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var point, out var xform))
        {
            if (!string.Equals(point.PointId, step.CameraPointId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (respectContext && active != null && !DoesEntityMatchContext(uid, active, step.ContextId))
                continue;

            if (found)
            {
                Log.Warning($"Found duplicate WH40K cinematic camera point id '{step.CameraPointId}'. Using the first match.");
                continue;
            }

            found = true;
            shot = new WH40KCinematicShotRuntimeState(
                uid,
                point.PointId,
                GetNetCoordinates(xform.Coordinates),
                step.CameraZoom ?? point.Zoom,
                step.CameraRotationDegrees ?? point.RotationDegrees,
                step.CameraTransition,
                step.CameraEasing,
                step.BlendDurationSeconds,
                step.ShakeIntensity,
                step.DrawFov ?? prototype.DefaultDrawFov,
                step.DrawLight ?? prototype.DefaultDrawLight);
        }

        return found;
    }

    private bool TryExecuteStepActions(
        ActiveCinematicRun active,
        WH40KCinematicStepDefinition step,
        out string failureReason)
    {
        failureReason = string.Empty;

        for (var actionIndex = 0; actionIndex < step.Actions.Count; actionIndex++)
        {
            var action = step.Actions[actionIndex];
            if (!TryExecuteAction(active, step, action, actionIndex, out failureReason))
                return false;
        }

        return true;
    }

    private bool TryExecuteAction(
        ActiveCinematicRun active,
        WH40KCinematicStepDefinition step,
        WH40KCinematicActionDefinition action,
        int actionIndex,
        out string failureReason)
    {
        failureReason = string.Empty;
        var actionLabel = DescribeAction(active, step, action, actionIndex);

        try
        {
            switch (action.Type)
            {
                case WH40KCinematicActionType.Notify:
                    ExecuteNotifyAction(active, action);
                    return true;

                case WH40KCinematicActionType.Announcement:
                    ExecuteAnnouncementAction(active, action);
                    return true;

                case WH40KCinematicActionType.StartAudienceShake:
                    ExecuteAudienceShakeAction(active, action, actionLabel);
                    return true;

                case WH40KCinematicActionType.PlayGlobalSound:
                    ExecuteGlobalSoundAction(active, action, actionLabel);
                    return true;

                case WH40KCinematicActionType.PlayAnchorSound:
                    return TryExecuteAnchorSoundAction(active, action, actionLabel, out failureReason);

                case WH40KCinematicActionType.ApplyLocalDamageToAudience:
                    ExecuteLocalAudienceDamageAction(active, action);
                    return true;

                case WH40KCinematicActionType.StopActions:
                    ExecuteStopActions(active, action, actionLabel);
                    return true;

                case WH40KCinematicActionType.SpawnAtAnchor:
                    return TryExecuteSpawnAction(active, action, actionLabel, out failureReason);

                case WH40KCinematicActionType.RunLavaFlow:
                    return TryExecuteLavaFlowAction(active, action, actionLabel, out failureReason);

                case WH40KCinematicActionType.LoadSceneMap:
                    return TryLoadSceneMapAction(active, action, out failureReason);

                case WH40KCinematicActionType.UnloadSceneMap:
                    return TryUnloadSceneMapAction(active, action, out failureReason);

                case WH40KCinematicActionType.SwitchContext:
                    return TrySwitchContextAction(active, action.ContextId, out failureReason);

                case WH40KCinematicActionType.EmitSignal:
                    EmitSignal(active, action.Signal ?? string.Empty);
                    return true;

                case WH40KCinematicActionType.ClearEntitySet:
                    ClearEntitySet(active, action.EntitySetId);
                    return true;

                case WH40KCinematicActionType.SpawnNpc:
                case WH40KCinematicActionType.BindExistingEntityAsNpc:
                case WH40KCinematicActionType.DespawnNpc:
                case WH40KCinematicActionType.NpcSpeak:
                case WH40KCinematicActionType.NpcEmote:
                case WH40KCinematicActionType.NpcFaceDirection:
                case WH40KCinematicActionType.NpcMoveByOffset:
                case WH40KCinematicActionType.NpcMoveToAnchor:
                case WH40KCinematicActionType.NpcPathToAnchor:
                case WH40KCinematicActionType.NpcPathThroughAnchors:
                case WH40KCinematicActionType.NpcAttackDirection:
                case WH40KCinematicActionType.NpcUseEntity:
                case WH40KCinematicActionType.NpcEquipPrototype:
                case WH40KCinematicActionType.NpcUnequipSlot:
                case WH40KCinematicActionType.NpcSetHTNEnabled:
                case WH40KCinematicActionType.NpcReleaseScriptControl:
                case WH40KCinematicActionType.NpcWait:
                case WH40KCinematicActionType.PlayActorTrack:
                    return TryExecuteNpcScriptAction(active, step, action, actionLabel, out failureReason);

                default:
                    failureReason = $"Unsupported action type '{action.Type}' in cinematic '{active.Prototype.ID}'.";
                    return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"WH40K cinematic action failure in {actionLabel}: {ex}");
            failureReason = $"Action failed: {actionLabel}";
            return false;
        }
    }

    private void ExecuteNotifyAction(ActiveCinematicRun active, WH40KCinematicActionDefinition action)
    {
        var filter = BuildAudienceFilter(active, action.TeamId);
        if (filter.Count == 0)
            return;

        var accentColor = ResolveNotificationAccent(action);
        var category = ResolveNotificationCategory(action);
        var duration = action.DurationSeconds ?? 8f;
        var titleLocOrRaw = ResolveNotificationTitle(action, category, accentColor, localizedPath: !string.IsNullOrWhiteSpace(action.TextLoc));

        if (!string.IsNullOrWhiteSpace(action.TextLoc))
        {
            _notifications.SendFilteredLocalized(
                filter,
                action.TextLoc!,
                titleLocOrRaw,
                action.LocArgs,
                action.ResolveLocArgValues,
                accentColor,
                duration,
                action.Marquee,
                action.Size,
                category,
                action.Priority,
                action.Icon,
                action.StackKey,
                action.IgnoreUserPreferences,
                sound: action.Sound);
            return;
        }

        var text = action.Text ?? string.Empty;
        _notifications.SendFiltered(
            filter,
            titleLocOrRaw,
            text,
            accentColor,
            duration,
            action.Marquee,
            action.Size,
            category,
            action.Priority,
            action.Icon,
            action.StackKey,
            action.IgnoreUserPreferences,
            sound: action.Sound);
    }

    private void ExecuteAnnouncementAction(ActiveCinematicRun active, WH40KCinematicActionDefinition action)
    {
        var filter = BuildAudienceFilter(active, action.TeamId);
        if (filter.Count == 0)
            return;

        var message = ResolveOptionalLocalizedString(action.Message, action.MessageLoc, action.LocArgs, action.ResolveLocArgValues);
        var sender = ResolveOptionalLocalizedString(action.Sender, action.SenderLoc, action.LocArgs, action.ResolveLocArgValues);
        _chat.DispatchFilteredAnnouncement(
            filter,
            message,
            sender: string.IsNullOrWhiteSpace(sender) ? null : sender,
            playSound: action.PlaySound,
            announcementSound: action.Sound,
            colorOverride: action.AccentColor);
    }

    private void ExecuteAudienceShakeAction(
        ActiveCinematicRun active,
        WH40KCinematicActionDefinition action,
        string actionLabel)
    {
        var intensity = Math.Max(0f, action.ShakeIntensity ?? 0f);
        if (intensity <= 0f)
            return;

        var pulseIntervalSeconds = Math.Clamp(
            action.ShakePulseIntervalSeconds ?? AudienceShakeDefaultPulseIntervalSeconds,
            AudienceShakeMinPulseIntervalSeconds,
            AudienceShakeMaxPulseIntervalSeconds);

        active.ActiveActions.Add(new AudienceShakeActionRuntime(
            action.Id,
            active.CurrentStepIndex,
            active.CurrentStep.Id,
            actionLabel,
            action.PersistAfterCinematic,
            active.AudienceUserIds,
            action.TeamId,
            _timing.CurTime,
            intensity,
            Math.Max(0f, action.ShakeRampDurationSeconds ?? 0f),
            pulseIntervalSeconds));
    }

    private void ExecuteGlobalSoundAction(
        ActiveCinematicRun active,
        WH40KCinematicActionDefinition action,
        string actionLabel)
    {
        var filter = action.DeliveryScope == WH40KCinematicSoundDeliveryScope.Broadcast
            ? Filter.Broadcast()
            : BuildAudienceFilter(active, action.TeamId);

        if (action.DeliveryScope != WH40KCinematicSoundDeliveryScope.Broadcast &&
            filter.Count == 0)
        {
            return;
        }

        var audio = ResolveAudioParams(action);
        var stream = _audio.PlayGlobal(action.Sound, filter, action.RecordReplay, audio);
        if (stream == null)
            return;

        active.ActiveActions.Add(new AudioActionRuntime(
            action.Id,
            active.CurrentStepIndex,
            active.CurrentStep.Id,
            actionLabel,
            action.Blocking,
            action.PersistAfterCinematic,
            [stream.Value.Entity]));
    }

    private bool TryExecuteAnchorSoundAction(
        ActiveCinematicRun active,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        var anchors = ResolveSoundAnchors(active, action.AnchorId, action.ContextId);
        if (anchors.Count == 0)
        {
            if (action.OptionalAnchor)
            {
                Log.Warning($"Skipping optional cinematic sound anchor '{action.AnchorId}' for {actionLabel}.");
                return true;
            }

            failureReason = $"Missing required cinematic sound anchor '{action.AnchorId}' for {actionLabel}.";
            return false;
        }

        var audio = ResolveAudioParams(action);
        var streams = new List<EntityUid>();
        foreach (var anchor in anchors)
        {
            var coords = _xform.ToMapCoordinates(Transform(anchor).Coordinates);
            var filter = BuildSoundFilter(active, action, anchor, coords);
            if (action.DeliveryScope != WH40KCinematicSoundDeliveryScope.Broadcast &&
                filter.Count == 0)
            {
                continue;
            }

            var stream = _audio.PlayEntity(action.Sound, filter, anchor, action.RecordReplay, audio);
            if (stream != null)
                streams.Add(stream.Value.Entity);
        }

        if (streams.Count > 0)
        {
            active.ActiveActions.Add(new AudioActionRuntime(
                action.Id,
                active.CurrentStepIndex,
                active.CurrentStep.Id,
                actionLabel,
                action.Blocking,
                action.PersistAfterCinematic,
                streams));
        }

        return true;
    }

    private bool TryExecuteSpawnAction(
        ActiveCinematicRun active,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;
        var anchors = ResolveSpawnAnchors(active, action.AnchorId, action.ContextId);
        if (anchors.Count == 0)
        {
            if (action.OptionalAnchor)
            {
                Log.Warning($"Skipping optional cinematic spawn anchor '{action.AnchorId}' for {actionLabel}.");
                return true;
            }

            failureReason = $"Missing required cinematic spawn anchor '{action.AnchorId}' for {actionLabel}.";
            return false;
        }

        var spawned = new List<EntityUid>();
        foreach (var anchor in anchors)
        {
            if (Deleted(anchor))
                continue;

            spawned.Add(Spawn(action.Prototype!.Value, Transform(anchor).Coordinates));
        }

        TrackEntitiesInSet(active, action.EntitySetId, spawned);

        if (action.Blocking && spawned.Count > 0)
        {
            active.ActiveActions.Add(new SpawnActionRuntime(
                action.Id,
                active.CurrentStepIndex,
                active.CurrentStep.Id,
                actionLabel,
                spawned));
        }

        return true;
    }

    private void ExecuteStopActions(
        ActiveCinematicRun active,
        WH40KCinematicActionDefinition action,
        string actionLabel)
    {
        var targetIds = ResolveTargetActionIds(action);
        if (targetIds.Count == 0)
            return;

        var stopped = StopActionRuntimes(active.ActiveActions, targetIds);
        stopped += StopActionRuntimes(_persistentActions, targetIds);

        if (stopped == 0)
            Log.Warning($"WH40K cinematic stopActions in {actionLabel} did not find any running action with ids: {string.Join(", ", targetIds)}");
    }

    private void RefreshActionRuntimes(ActiveCinematicRun active)
    {
        for (var i = active.ActiveActions.Count - 1; i >= 0; i--)
        {
            active.ActiveActions[i].Tick(this);
            if (!active.ActiveActions[i].IsComplete(this))
                continue;

            active.ActiveActions.RemoveAt(i);
        }
    }

    private void RefreshPersistentActions()
    {
        for (var i = _persistentActions.Count - 1; i >= 0; i--)
        {
            var action = _persistentActions[i];
            action.Tick(this);
            if (!action.IsComplete(this))
                continue;

            action.Cleanup(this);
            _persistentActions.RemoveAt(i);
        }
    }

    private int StopActionRuntimes(List<ActiveActionRuntime> runtimes, HashSet<string> targetIds)
    {
        var stopped = 0;
        for (var i = runtimes.Count - 1; i >= 0; i--)
        {
            var runtime = runtimes[i];
            if (string.IsNullOrWhiteSpace(runtime.RuntimeId) || !targetIds.Contains(runtime.RuntimeId))
                continue;

            runtime.ForceStop(this);
            runtimes.RemoveAt(i);
            stopped++;
        }

        return stopped;
    }

    private bool AreCurrentStepBlockingActionsComplete(ActiveCinematicRun active)
    {
        foreach (var action in active.ActiveActions)
        {
            if (action.StepIndex == active.CurrentStepIndex && action.Blocking)
                return false;
        }

        return true;
    }

    private void BeginRestorePhase(string reason, bool markCompleted)
    {
        if (_active == null || _active.RestorePhaseActive)
            return;

        BeginRestorePhase(_active, reason, markCompleted);
    }

    private void CompleteRestorePhase(string reason, bool markCompleted)
    {
        if (_active == null)
            return;

        CompleteRestorePhase(_active, reason, markCompleted);
    }

    private void TraceInfo(string message)
    {
        if (_traceLoggingEnabled)
            Log.Info(message);
    }

    private void AbortActive(string reason, bool markCompleted)
    {
        if (_active == null)
            return;

        AbortRun(_active, reason, markCompleted);
    }

    private void BroadcastActiveState()
    {
        if (_active == null || _active.RestorePhaseActive || string.IsNullOrWhiteSpace(_active.CurrentStep.Id))
            return;

        BroadcastActiveState(_active);
    }

    private void EnsureAudienceSynced(ActiveCinematicRun active)
    {
        foreach (var session in _players.Sessions)
        {
            if (active.AudienceUserIds.Contains(session.UserId))
                continue;

            TryEnrollSession(active, session);
        }
    }

    private WH40KCinematicNetState BuildActiveNetState(ActiveCinematicRun active)
    {
        float remaining = 0f;
        if (active.StepEndsAt != null)
            remaining = Math.Max(0f, (float) (active.StepEndsAt.Value - _timing.CurTime).TotalSeconds);

        var audienceShakeIntensity = GetActiveAudienceShakeIntensity(active, _timing.CurTime);

        var shot = active.CurrentShot == null
            ? null
            : new WH40KCinematicShotNetState(
                active.CurrentShot.CameraPointId,
                active.CurrentShot.Coordinates,
                active.CurrentShot.Zoom,
                active.CurrentShot.RotationDegrees,
                active.CurrentShot.TransitionMode,
                active.CurrentShot.TransitionEasing,
                active.CurrentShot.BlendDurationSeconds,
                active.CurrentShot.ShakeIntensity,
                active.CurrentShot.DrawFovOverride,
                active.CurrentShot.DrawLightOverride);

        return new WH40KCinematicNetState(
            active.RunSerial,
            active.Prototype.ID,
            active.CurrentStepIndex,
            active.CurrentStep.Id,
            active.CurrentStep.Type,
            active.WaitMode,
            remaining,
            GetRunQueueLength(active),
            active.AudienceLocked,
            audienceShakeIntensity,
            shot);
    }

    private static float GetActiveAudienceShakeIntensity(ActiveCinematicRun active, TimeSpan now)
    {
        var intensity = 0f;
        foreach (var action in active.ActiveActions)
        {
            if (action is not AudienceShakeActionRuntime shake)
                continue;

            intensity = Math.Max(intensity, shake.GetCurrentIntensity(now));
        }

        return intensity;
    }

    private void BroadcastStoppedEvent(
        ActiveCinematicRun active,
        bool completed,
        string reason,
        float unlockDelaySeconds)
    {
        var ev = new WH40KCinematicStoppedEvent(
            active.RunSerial,
            active.Prototype.ID,
            completed,
            reason,
            GetRunQueueLength(active),
            unlockDelaySeconds);

        foreach (var session in _players.Sessions)
        {
            if (!active.AudienceUserIds.Contains(session.UserId))
                continue;

            RaiseNetworkEvent(ev, session);
        }
    }

    private void EnrollCurrentAudience(ActiveCinematicRun active)
    {
        foreach (var session in _players.Sessions)
        {
            TryEnrollSession(active, session);
        }
    }

    private void TryEnrollSession(ActiveCinematicRun active, ICommonSession session, EntityUid? attachedOverride = null)
    {
        var attached = attachedOverride ?? session.AttachedEntity;
        if (attached is not { Valid: true } entity || Deleted(entity))
            return;

        EnsureRunMainContext(active);

        if (!ShouldAffectEntity(active, session, entity))
            return;

        active.AudienceUserIds.Add(session.UserId);
        if (active.AudienceLocked)
        {
            ApplyLock(entity, active.RunSerial);
            ApplyProtection(entity, active);
            EnsurePausedMapForEntity(active, entity);
        }
        SyncAudienceViewSubscription(active, session);

        if (!active.RestorePhaseActive && active.CurrentStepIndex >= 0 && !string.IsNullOrWhiteSpace(active.CurrentStep.Id))
            RaiseNetworkEvent(new WH40KCinematicStateEvent(BuildActiveNetState(active)), session);
    }

    private bool ShouldAffectEntity(ActiveCinematicRun active, ICommonSession session, EntityUid entity)
    {
        return ShouldAffectSession(active, session, entity);
    }

    private void ApplyLock(EntityUid entity, int runSerial)
    {
        var comp = EnsureComp<WH40KCinematicLockedComponent>(entity);
        if (comp.RunSerial == runSerial)
            return;

        comp.RunSerial = runSerial;
        Dirty(entity, comp);
    }

    private void ReleaseLock(EntityUid entity, int runSerial)
    {
        if (!TryComp<WH40KCinematicLockedComponent>(entity, out var locked) || locked.RunSerial != runSerial)
            return;

        RemComp<WH40KCinematicLockedComponent>(entity);
    }

    private void EnsurePausedMapForEntity(ActiveCinematicRun active, EntityUid entity)
    {
        if (active.Prototype.WorldFreezeMode != WH40KCinematicWorldFreezeMode.PauseMap)
            return;

        if (active.SuppressAudienceMapPause)
            return;

        var mapId = Transform(entity).MapID;
        if (mapId == MapId.Nullspace || active.PausedMaps.ContainsKey(mapId))
            return;

        var wasPaused = _map.IsPaused(mapId);
        active.PausedMaps[mapId] = wasPaused;

        if (!wasPaused)
            _map.SetPaused(mapId, true);
    }

    private void ReleasePausedMaps(ActiveCinematicRun active)
    {
        foreach (var (mapId, wasPaused) in active.PausedMaps)
        {
            if (!_map.MapExists(mapId))
                continue;

            if (!wasPaused && _map.IsPaused(mapId))
                _map.SetPaused(mapId, false);
        }

        active.PausedMaps.Clear();
    }

    private void ApplyStepAudienceLockDirective(ActiveCinematicRun active, WH40KCinematicStepDefinition step)
    {
        switch (step.AudienceLock)
        {
            case WH40KCinematicAudienceLockDirective.Inherit:
                return;

            case WH40KCinematicAudienceLockDirective.Lock:
                SetAudienceLockState(active, true);
                return;

            case WH40KCinematicAudienceLockDirective.Unlock:
                SetAudienceLockState(active, false);
                return;
        }
    }

    private void SetAudienceLockState(ActiveCinematicRun active, bool locked)
    {
        if (active.AudienceLocked == locked)
            return;

        active.AudienceLocked = locked;

        foreach (var session in _players.Sessions)
        {
            if (!active.AudienceUserIds.Contains(session.UserId))
                continue;

            if (session.AttachedEntity is not { Valid: true } entity || Deleted(entity))
                continue;

            if (locked)
            {
                ApplyLock(entity, active.RunSerial);
                ApplyProtection(entity, active);
                EnsurePausedMapForEntity(active, entity);
            }
            else
            {
                ReleaseLock(entity, active.RunSerial);
                ReleaseProtection(entity, active.RunSerial);
            }
        }

        if (!locked && !active.SceneContexts.Values.Any(context => context.PauseSourceMap && context.IsRuntimeScene))
            ReleasePausedMaps(active);
    }

    private void CleanupRun(ActiveCinematicRun run)
    {
        ClearAudienceViewSubscriptions(run);
        CleanupSceneContexts(run);
        CleanupNpcTrackRuntime(run);

        foreach (var action in run.ActiveActions)
        {
            if (action.TryPromoteToPersistent(this))
            {
                _persistentActions.Add(action);
                continue;
            }

            action.Cleanup(this);
        }

        run.ActiveActions.Clear();

        var lockQuery = AllEntityQuery<WH40KCinematicLockedComponent>();
        while (lockQuery.MoveNext(out var uid, out var locked))
        {
            if (locked.RunSerial != run.RunSerial)
                continue;

            RemCompDeferred<WH40KCinematicLockedComponent>(uid);
        }

        var protectedQuery = AllEntityQuery<WH40KCinematicProtectedComponent>();
        while (protectedQuery.MoveNext(out var uid, out var protectedComp))
        {
            if (protectedComp.RunSerial != run.RunSerial)
                continue;

            ReleaseProtection(uid, run.RunSerial);
        }

        foreach (var (mapId, wasPaused) in run.PausedMaps)
        {
            if (!_map.MapExists(mapId))
                continue;

            if (_map.IsPaused(mapId) != wasPaused)
                _map.SetPaused(mapId, wasPaused);
        }
    }

    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        if (_active != null)
            TryEnrollSession(_active, ev.Player, ev.Entity);

        foreach (var run in _scopedRuns)
        {
            TryEnrollSession(run, ev.Player, ev.Entity);
        }
    }

    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        HandleDetachedEntityForRun(_active, ev);
        foreach (var run in _scopedRuns)
        {
            HandleDetachedEntityForRun(run, ev);
        }
    }

    private void SyncAudienceViewSubscriptions(ActiveCinematicRun active)
    {
        foreach (var session in _players.Sessions)
        {
            SyncAudienceViewSubscription(active, session);
        }
    }

    private void SyncAudienceViewSubscription(ActiveCinematicRun active, ICommonSession session)
    {
        if (!active.AudienceUserIds.Contains(session.UserId))
        {
            RemoveAudienceViewSubscription(active, session);
            return;
        }

        var cameraPoint = active.CurrentShot?.CameraPointEntity;
        if (cameraPoint is not { Valid: true } target || Deleted(target))
        {
            RemoveAudienceViewSubscription(active, session);
            return;
        }

        if (active.ActiveViewSubscriptions.TryGetValue(session.UserId, out var current) &&
            current == target)
        {
            return;
        }

        RemoveAudienceViewSubscription(active, session);
        _viewSubscribers.AddViewSubscriber(target, session);
        active.ActiveViewSubscriptions[session.UserId] = target;
    }

    private void ClearAudienceViewSubscriptions(ActiveCinematicRun active)
    {
        foreach (var session in _players.Sessions)
        {
            RemoveAudienceViewSubscription(active, session);
        }

        active.ActiveViewSubscriptions.Clear();
    }

    private void RemoveAudienceViewSubscription(ActiveCinematicRun active, ICommonSession session)
    {
        if (!active.ActiveViewSubscriptions.TryGetValue(session.UserId, out var existing))
            return;

        if (existing.Valid && !Deleted(existing))
            _viewSubscribers.RemoveViewSubscriber(existing, session);

        active.ActiveViewSubscriptions.Remove(session.UserId);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        if (_active != null)
            CleanupRun(_active);

        foreach (var action in _persistentActions)
        {
            action.Cleanup(this);
        }

        _persistentActions.Clear();

        _queue.Clear();
        _completedNonRepeatable.Clear();
        _active = null;
        ResetScopedAudienceRuntimeState();
    }

    private void OnQueueCinematic(WH40KQueueCinematicEvent ev)
    {
        if (!TryQueue(ev.CinematicId, out var message))
            Log.Warning($"WH40K cinematic queue event failed: {message}");
    }

    private void OnStopCinematic(WH40KStopCinematicEvent ev)
    {
        TryStopActive(ev.Reason, ev.MarkCompleted);
    }

    private Filter BuildAudienceFilter(ActiveCinematicRun active, string? teamIdOverride)
    {
        var filter = Filter.Empty();

        foreach (var session in _players.Sessions)
        {
            if (!active.AudienceUserIds.Contains(session.UserId))
                continue;

            if (string.IsNullOrWhiteSpace(teamIdOverride))
            {
                filter.AddPlayer(session);
                continue;
            }

            if (session.AttachedEntity is not { Valid: true } entity || Deleted(entity))
                continue;

            if (TryComp<WH40KTeamMemberComponent>(entity, out var member) &&
                string.Equals(member.TeamId, teamIdOverride, StringComparison.OrdinalIgnoreCase))
            {
                filter.AddPlayer(session);
            }
        }

        return filter;
    }

    private List<EntityUid> ResolveSoundAnchors(string? anchorId)
    {
        return ResolveSoundAnchors(run: null, anchorId, explicitContextId: null);
    }

    private List<EntityUid> ResolveSoundAnchors(ActiveCinematicRun? run, string? anchorId, string? explicitContextId)
    {
        var result = ResolveSoundAnchorsInternal(run, anchorId, explicitContextId, respectContext: true);
        if (result.Count > 0 || !ShouldFallbackToAnyContext(run, explicitContextId))
            return result;

        return ResolveSoundAnchorsInternal(run, anchorId, explicitContextId, respectContext: false);
    }

    private List<EntityUid> ResolveSoundAnchorsInternal(
        ActiveCinematicRun? run,
        string? anchorId,
        string? explicitContextId,
        bool respectContext)
    {
        var result = new List<EntityUid>();
        if (string.IsNullOrWhiteSpace(anchorId))
            return result;

        var soundQuery = AllEntityQuery<WH40KCinematicSoundAnchorComponent>();
        while (soundQuery.MoveNext(out var uid, out var anchor))
        {
            if (respectContext && run != null && !DoesEntityMatchContext(uid, run, explicitContextId))
                continue;

            if (string.Equals(anchor.AnchorId, anchorId, StringComparison.OrdinalIgnoreCase))
                result.Add(uid);
        }

        var actionQuery = AllEntityQuery<WH40KCinematicActionAnchorComponent>();
        while (actionQuery.MoveNext(out var uid, out var anchor))
        {
            if (respectContext && run != null && !DoesEntityMatchContext(uid, run, explicitContextId))
                continue;

            if (string.Equals(anchor.AnchorId, anchorId, StringComparison.OrdinalIgnoreCase))
                result.Add(uid);
        }

        return result;
    }

    private List<EntityUid> ResolveSpawnAnchors(string? anchorId)
    {
        return ResolveSpawnAnchors(run: null, anchorId, explicitContextId: null);
    }

    private List<EntityUid> ResolveSpawnAnchors(ActiveCinematicRun? run, string? anchorId, string? explicitContextId)
    {
        var result = ResolveSpawnAnchorsInternal(run, anchorId, explicitContextId, respectContext: true);
        if (result.Count > 0 || !ShouldFallbackToAnyContext(run, explicitContextId))
            return result;

        return ResolveSpawnAnchorsInternal(run, anchorId, explicitContextId, respectContext: false);
    }

    private List<EntityUid> ResolveSpawnAnchorsInternal(
        ActiveCinematicRun? run,
        string? anchorId,
        string? explicitContextId,
        bool respectContext)
    {
        var result = new List<EntityUid>();
        if (string.IsNullOrWhiteSpace(anchorId))
            return result;

        var spawnQuery = AllEntityQuery<WH40KCinematicSpawnAnchorComponent>();
        while (spawnQuery.MoveNext(out var uid, out var anchor))
        {
            if (respectContext && run != null && !DoesEntityMatchContext(uid, run, explicitContextId))
                continue;

            if (string.Equals(anchor.AnchorId, anchorId, StringComparison.OrdinalIgnoreCase))
                result.Add(uid);
        }

        var actionQuery = AllEntityQuery<WH40KCinematicActionAnchorComponent>();
        while (actionQuery.MoveNext(out var uid, out var anchor))
        {
            if (respectContext && run != null && !DoesEntityMatchContext(uid, run, explicitContextId))
                continue;

            if (string.Equals(anchor.AnchorId, anchorId, StringComparison.OrdinalIgnoreCase))
                result.Add(uid);
        }

        return result;
    }

    private static bool ResolveEffectiveLoop(WH40KCinematicActionDefinition action)
    {
        if (action.Audio.HasValue)
            return action.Audio.Value.Loop;

        return action.Sound?.Params.Loop == true;
    }

    private static HashSet<string> ResolveTargetActionIds(WH40KCinematicActionDefinition action)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(action.TargetActionId))
            ids.Add(action.TargetActionId.Trim());

        if (action.TargetActionIds == null)
            return ids;

        foreach (var target in action.TargetActionIds)
        {
            if (string.IsNullOrWhiteSpace(target))
                continue;

            ids.Add(target.Trim());
        }

        return ids;
    }

    private static string DescribeAction(
        ActiveCinematicRun active,
        WH40KCinematicStepDefinition step,
        WH40KCinematicActionDefinition action,
        int actionIndex)
    {
        var actionId = string.IsNullOrWhiteSpace(action.Id) ? $"action[{actionIndex}]" : $"action '{action.Id}'";
        return $"cinematic '{active.Prototype.ID}' step '{step.Id}' {actionId} ({action.Type})";
    }

    private static string ResolveOptionalLocalizedString(
        string? rawValue,
        string? locKey,
        Dictionary<string, string>? locArgs,
        bool resolveLocArgValues)
    {
        if (!string.IsNullOrWhiteSpace(locKey))
            return ResolveLocalizedString(locKey, locArgs, resolveLocArgValues);

        return rawValue ?? string.Empty;
    }

    private static string ResolveLocalizedString(
        string locKey,
        Dictionary<string, string>? locArgs,
        bool resolveLocArgValues)
    {
        if (locArgs == null || locArgs.Count == 0)
            return Robust.Shared.Localization.Loc.GetString(locKey);

        var args = new (string, object)[locArgs.Count];
        var i = 0;
        foreach (var kv in locArgs)
        {
            object value = kv.Value;
            if (resolveLocArgValues)
                value = Robust.Shared.Localization.Loc.GetString(kv.Value);

            args[i++] = (kv.Key, value);
        }

        return Robust.Shared.Localization.Loc.GetString(locKey, args);
    }

    private static WH40KNotificationCategory ResolveNotificationCategory(WH40KCinematicActionDefinition action)
    {
        if (action.Category != WH40KNotificationCategory.Auto)
            return action.Category;

        if (!string.IsNullOrWhiteSpace(action.TextLoc))
            return WH40KNotificationMetadata.InferCategoryFromLocKey(action.TextLoc);

        return WH40KNotificationCategory.Info;
    }

    private static Color ResolveNotificationAccent(WH40KCinematicActionDefinition action)
    {
        if (action.AccentColor is { } explicitColor)
            return explicitColor;

        if (!string.IsNullOrWhiteSpace(action.TeamId))
            return WH40KNotificationColors.ForTeam(action.TeamId);

        var category = ResolveNotificationCategory(action);
        return category switch
        {
            WH40KNotificationCategory.Admin => WH40KNotificationColors.Admin,
            WH40KNotificationCategory.Critical => WH40KNotificationColors.Warning,
            WH40KNotificationCategory.Weather => WH40KNotificationColors.Weather,
            WH40KNotificationCategory.Event => WH40KNotificationColors.Event,
            WH40KNotificationCategory.Objective => WH40KNotificationColors.Objective,
            _ => WH40KNotificationColors.Neutral
        };
    }

    private static string ResolveNotificationTitle(
        WH40KCinematicActionDefinition action,
        WH40KNotificationCategory category,
        Color accentColor,
        bool localizedPath)
    {
        if (!string.IsNullOrWhiteSpace(action.TitleLoc))
        {
            return localizedPath
                ? action.TitleLoc
                : ResolveLocalizedString(action.TitleLoc, action.LocArgs, action.ResolveLocArgValues);
        }

        if (!string.IsNullOrWhiteSpace(action.Title))
            return action.Title;

        var defaultTitle = WH40KNotificationMetadata.DefaultTitle(category, accentColor);
        return localizedPath ? defaultTitle : Robust.Shared.Localization.Loc.GetString(defaultTitle);
    }

    private static Robust.Shared.Audio.AudioParams? ResolveAudioParams(WH40KCinematicActionDefinition action)
    {
        return action.Audio ?? action.Sound?.Params;
    }

    private sealed class ActiveCinematicRun
    {
        public int RunSerial { get; }
        public WH40KCinematicPrototype Prototype { get; }
        public TimeSpan QueuedAt { get; }
        public TimeSpan StartedAt { get; }
        public bool IsScoped;
        public int Priority;
        public NetUserId? TriggerUserId;
        public WH40KCinematicGhostAudiencePolicy GhostAudiencePolicy = WH40KCinematicGhostAudiencePolicy.MirrorAudience;
        public int CurrentStepIndex = -1;
        public WH40KCinematicStepDefinition CurrentStep = new();
        public WH40KCinematicWaitMode WaitMode = WH40KCinematicWaitMode.Instant;
        public TimeSpan StepStartedAt;
        public TimeSpan? StepEndsAt;
        public WH40KCinematicShotRuntimeState? CurrentShot;
        public bool AudienceLocked;
        public bool RestorePhaseActive;
        public bool ManuallyPaused;
        public bool SuppressAudienceMapPause;
        public TimeSpan? UnlockAt;
        public TimeSpan NextStateBroadcastAt;
        public string CurrentContextId = MainSceneContextId;
        public HashSet<NetUserId> RequestedAudienceUserIds { get; } = new();
        public HashSet<NetUserId> ExcludedAudienceUserIds { get; } = new();
        public HashSet<NetUserId> AudienceUserIds { get; } = new();
        public Dictionary<NetUserId, EntityUid> ActiveViewSubscriptions { get; } = new();
        public Dictionary<MapId, bool> PausedMaps { get; } = new();
        public Dictionary<string, SceneContextRuntime> SceneContexts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> PendingSignals { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<EntityUid>> EntitySets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<NetUserId, ParticipantReturnState> ParticipantReturns { get; } = new();
        public Dictionary<string, NpcActorRuntime> NpcActors { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ActiveActionRuntime> ActiveActions { get; } = new();

        public ActiveCinematicRun(int runSerial, WH40KCinematicPrototype prototype, TimeSpan queuedAt, TimeSpan startedAt)
        {
            RunSerial = runSerial;
            Prototype = prototype;
            QueuedAt = queuedAt;
            StartedAt = startedAt;
        }
    }

    private sealed class WH40KCinematicShotRuntimeState
    {
        public EntityUid CameraPointEntity { get; }
        public string CameraPointId { get; }
        public NetCoordinates Coordinates { get; }
        public float Zoom { get; }
        public float RotationDegrees { get; }
        public WH40KCinematicCameraTransitionMode TransitionMode { get; }
        public WH40KCinematicCameraTransitionEasing TransitionEasing { get; }
        public float BlendDurationSeconds { get; }
        public float ShakeIntensity { get; }
        public bool? DrawFovOverride { get; }
        public bool? DrawLightOverride { get; }

        public WH40KCinematicShotRuntimeState(
            EntityUid cameraPointEntity,
            string cameraPointId,
            NetCoordinates coordinates,
            float zoom,
            float rotationDegrees,
            WH40KCinematicCameraTransitionMode transitionMode,
            WH40KCinematicCameraTransitionEasing transitionEasing,
            float blendDurationSeconds,
            float shakeIntensity,
            bool? drawFovOverride,
            bool? drawLightOverride)
        {
            CameraPointEntity = cameraPointEntity;
            CameraPointId = cameraPointId;
            Coordinates = coordinates;
            Zoom = zoom;
            RotationDegrees = rotationDegrees;
            TransitionMode = transitionMode;
            TransitionEasing = transitionEasing;
            BlendDurationSeconds = blendDurationSeconds;
            ShakeIntensity = shakeIntensity;
            DrawFovOverride = drawFovOverride;
            DrawLightOverride = drawLightOverride;
        }
    }

    private abstract class ActiveActionRuntime
    {
        public string? RuntimeId { get; }
        public int StepIndex { get; }
        public string StepId { get; }
        public string ActionLabel { get; }
        public bool Blocking { get; }

        protected ActiveActionRuntime(string? runtimeId, int stepIndex, string stepId, string actionLabel, bool blocking)
        {
            RuntimeId = string.IsNullOrWhiteSpace(runtimeId) ? null : runtimeId.Trim();
            StepIndex = stepIndex;
            StepId = stepId;
            ActionLabel = actionLabel;
            Blocking = blocking;
        }

        public abstract bool IsComplete(WH40KCinematicSystem system);
        public virtual void Tick(WH40KCinematicSystem system)
        {
        }

        public virtual void Cleanup(WH40KCinematicSystem system)
        {
        }

        public virtual void ForceStop(WH40KCinematicSystem system)
        {
            Cleanup(system);
        }

        public virtual bool TryPromoteToPersistent(WH40KCinematicSystem system)
        {
            return false;
        }
    }

    private sealed class AudienceShakeActionRuntime : ActiveActionRuntime
    {
        private readonly bool _persistAfterCinematic;
        private readonly HashSet<NetUserId> _persistentAudience = new();
        private HashSet<NetUserId>? _activeAudience;
        private readonly string? _teamId;
        private readonly TimeSpan _startedAt;
        private readonly float _intensity;
        private readonly float _rampDurationSeconds;
        private readonly float _pulseIntervalSeconds;
        private TimeSpan _nextPulseAt;
        private bool _stopped;

        public AudienceShakeActionRuntime(
            string? runtimeId,
            int stepIndex,
            string stepId,
            string actionLabel,
            bool persistAfterCinematic,
            HashSet<NetUserId> activeAudience,
            string? teamId,
            TimeSpan startedAt,
            float intensity,
            float rampDurationSeconds,
            float pulseIntervalSeconds)
            : base(runtimeId, stepIndex, stepId, actionLabel, blocking: false)
        {
            _persistAfterCinematic = persistAfterCinematic;
            _activeAudience = activeAudience;
            _teamId = string.IsNullOrWhiteSpace(teamId) ? null : teamId.Trim();
            _startedAt = startedAt;
            _intensity = intensity;
            _rampDurationSeconds = rampDurationSeconds;
            _pulseIntervalSeconds = pulseIntervalSeconds;
            _nextPulseAt = startedAt;
        }

        public override bool IsComplete(WH40KCinematicSystem system)
        {
            return _stopped;
        }

        public override void Tick(WH40KCinematicSystem system)
        {
            if (_stopped)
                return;

            var now = system._timing.CurTime;
            if (now < _nextPulseAt)
                return;

            var maxCatchUpPulses = 4;
            var emitted = 0;
            while (!_stopped && now >= _nextPulseAt && emitted < maxCatchUpPulses)
            {
                EmitPulse(system, now);
                _nextPulseAt += TimeSpan.FromSeconds(_pulseIntervalSeconds);
                emitted++;
            }

            if (_nextPulseAt < now)
                _nextPulseAt = now + TimeSpan.FromSeconds(_pulseIntervalSeconds);
        }

        public override void Cleanup(WH40KCinematicSystem system)
        {
            _stopped = true;
        }

        public override void ForceStop(WH40KCinematicSystem system)
        {
            _stopped = true;
        }

        public override bool TryPromoteToPersistent(WH40KCinematicSystem system)
        {
            if (!_persistAfterCinematic || _stopped)
                return false;

            _persistentAudience.Clear();
            if (_activeAudience != null)
            {
                foreach (var userId in _activeAudience)
                {
                    _persistentAudience.Add(userId);
                }
            }

            _activeAudience = null;
            return true;
        }

        public float GetCurrentIntensity(TimeSpan now)
        {
            if (_stopped)
                return 0f;

            return _intensity * CalculateRampScale(now);
        }

        private void EmitPulse(WH40KCinematicSystem system, TimeSpan now)
        {
            var kickMagnitude = CalculateKickMagnitude(now);
            if (kickMagnitude <= 0.0001f)
                return;

            var direction = new Vector2(
                system._random.NextFloat(-1f, 1f),
                system._random.NextFloat(-1f, 1f));

            if (direction.LengthSquared() <= 0.0001f)
                direction = Vector2.UnitY;
            else
                direction = Vector2.Normalize(direction);

            var kick = direction * kickMagnitude;
            var audience = _activeAudience ?? _persistentAudience;

            foreach (var session in system._players.Sessions)
            {
                if (!audience.Contains(session.UserId))
                    continue;

                if (session.AttachedEntity is not { Valid: true } entity || system.Deleted(entity))
                    continue;

                if (system.HasComp<GhostComponent>(entity))
                    continue;

                if (_teamId != null &&
                    (!system.TryComp<WH40KTeamMemberComponent>(entity, out var member) ||
                     !string.Equals(member.TeamId, _teamId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!system.HasComp<EyeComponent>(entity))
                    continue;

                system.EnsureComp<CameraRecoilComponent>(entity);
                system._cameraRecoil.KickCamera(entity, kick);
            }
        }

        private float CalculateKickMagnitude(TimeSpan now)
        {
            var rampScale = CalculateRampScale(now);

            return Math.Clamp(
                (AudienceShakeBaseKickMagnitude + _intensity * AudienceShakeKickScale) * rampScale,
                0f,
                AudienceShakeMaxKickMagnitude);
        }

        private float CalculateRampScale(TimeSpan now)
        {
            if (_rampDurationSeconds <= 0.0001f)
                return 1f;

            var elapsedSeconds = Math.Max(0f, (float) (now - _startedAt).TotalSeconds);
            var rampScale = Math.Clamp(elapsedSeconds / _rampDurationSeconds, 0f, 1f);
            return rampScale * rampScale * (3f - 2f * rampScale);
        }
    }

    private sealed class AudioActionRuntime : ActiveActionRuntime
    {
        private readonly bool _persistAfterCinematic;
        private readonly List<EntityUid> _streams;

        public AudioActionRuntime(
            string? runtimeId,
            int stepIndex,
            string stepId,
            string actionLabel,
            bool blocking,
            bool persistAfterCinematic,
            List<EntityUid> streams)
            : base(runtimeId, stepIndex, stepId, actionLabel, blocking)
        {
            _persistAfterCinematic = persistAfterCinematic;
            _streams = streams;
        }

        public override bool IsComplete(WH40KCinematicSystem system)
        {
            foreach (var stream in _streams)
            {
                if (!system.Deleted(stream) && system._audio.IsPlaying(stream))
                    return false;
            }

            return true;
        }

        public override void Cleanup(WH40KCinematicSystem system)
        {
            if (_persistAfterCinematic)
                return;

            StopStreams(system);
        }

        public override void ForceStop(WH40KCinematicSystem system)
        {
            StopStreams(system);
        }

        public override bool TryPromoteToPersistent(WH40KCinematicSystem system)
        {
            return _persistAfterCinematic && !IsComplete(system);
        }

        private void StopStreams(WH40KCinematicSystem system)
        {
            foreach (var stream in _streams)
            {
                if (!system.Deleted(stream))
                    system._audio.Stop(stream);
            }
        }
    }

    private sealed class SpawnActionRuntime : ActiveActionRuntime
    {
        private readonly List<EntityUid> _spawned;

        public SpawnActionRuntime(string? runtimeId, int stepIndex, string stepId, string actionLabel, List<EntityUid> spawned)
            : base(runtimeId, stepIndex, stepId, actionLabel, blocking: true)
        {
            _spawned = spawned;
        }

        public override bool IsComplete(WH40KCinematicSystem system)
        {
            foreach (var entity in _spawned)
            {
                if (!system.Deleted(entity))
                    return false;
            }

            return true;
        }
    }

    private readonly record struct QueuedCinematicRequest(WH40KCinematicPrototype Prototype, TimeSpan QueuedAt);
}

public readonly record struct WH40KCinematicRuntimeSnapshot(
    bool IsActive,
    string? ActiveCinematicId,
    int ActiveStepIndex,
    string? ActiveStepId,
    WH40KCinematicWaitMode? ActiveWaitMode,
    int QueueLength,
    int CompletedNonRepeatableCount);
