using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Server.Popups;
using Content.Server._WH40K.Command;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared.CCVar;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared._WH40K.Fulton;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Content.Server._WH40K.Localizations;

namespace Content.Server._WH40K.Fulton;

public sealed class WH40KTacticalFultonSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly WH40KCommandEventMissionRuntimeSystem _missionRuntime = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly WH40KPlayerCultureTracker _culture = default!;

    private const string FultonEffectPrototype = "FultonEffect";

    private readonly Dictionary<string, int> _teamFrontRewards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _teamCommandRewards = new(StringComparer.OrdinalIgnoreCase);

    private MapId? _fultonMap;
    private int _fultonMapOffset;
    private int _nextExtractionId = 700;

    private int _frontRewardCapPerRound;
    private int _commandRewardCapPerRound;
    private bool _missionHookEnabled;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KTacticalFultonComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WH40KTacticalFultonComponent, ExaminedEvent>(OnFultonExamined);
        SubscribeLocalEvent<WH40KTacticalFultonTargetComponent, ExaminedEvent>(OnTargetExamined);
        SubscribeLocalEvent<WH40KActiveFultonExtractionComponent, ExaminedEvent>(OnActiveExamined);
        SubscribeLocalEvent<WH40KPrepareFultonDoAfterEvent>(OnPrepareDoAfter);
        SubscribeLocalEvent<WH40KActiveFultonExtractionComponent, ComponentShutdown>(OnActiveShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        Subs.CVar(_cfg, CCVars.WH40KFultonFrontRewardCapPerRound, value =>
        {
            _frontRewardCapPerRound = Math.Max(0, value);
        }, true);

        Subs.CVar(_cfg, CCVars.WH40KFultonCommandRewardCapPerRound, value =>
        {
            _commandRewardCapPerRound = Math.Max(0, value);
        }, true);

        Subs.CVar(_cfg, CCVars.WH40KFultonMissionHookEnabled, value =>
        {
            _missionHookEnabled = value;
        }, true);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KActiveFultonExtractionComponent>();
        while (query.MoveNext(out var uid, out var active))
        {
            if (active.NextStateAt == TimeSpan.Zero || now < active.NextStateAt)
                continue;

            switch (active.State)
            {
                case WH40KFultonExtractionState.Pending:
                    ResolvePendingExtraction(uid, active);
                    break;
                case WH40KFultonExtractionState.Extracted:
                    ResolveExtractedCleanup(uid, active);
                    break;
                case WH40KFultonExtractionState.Failed:
                    ResolveFailedCleanup(uid, active);
                    break;
            }
        }
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _teamFrontRewards.Clear();
        _teamCommandRewards.Clear();
        _fultonMap = null;
        _fultonMapOffset = 0;
        _nextExtractionId = 700;
    }

    private void OnAfterInteract(Entity<WH40KTacticalFultonComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target || !args.CanReach)
            return;

        if (HasComp<WH40KActiveFultonExtractionComponent>(target))
        {
            PopupCaution(args.User, "wh40k-fulton-popup-already-pending");
            return;
        }

        if (!TryResolveAuthorizedTeamId(args.User, ent.Comp, out var teamId))
            return;

        if (CountPendingExtractions(teamId) >= Math.Max(1, ent.Comp.MaxPendingExtractionsPerTeam))
        {
            PopupCaution(
                args.User,
                "wh40k-fulton-popup-team-cap",
                ("count", Math.Max(1, ent.Comp.MaxPendingExtractionsPerTeam)));
            return;
        }

        if (!TryConsumeUserThrottle(args.User, ent.Comp))
            return;

        if (!TryResolveExtractionProfile(target, teamId, ent.Comp, out _, out var denyPopupKey))
        {
            PopupCaution(args.User, denyPopupKey);
            return;
        }

        var ev = new WH40KPrepareFultonDoAfterEvent();
        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.AttachDelay, ev, target, target, args.Used)
        {
            BreakOnMove = true,
            NeedHand = true,
            Broadcast = true,
            MovementThreshold = 0.5f,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        args.Handled = true;
        _popup.PopupEntity(
            _culture.GetPlayerString(args.User, "wh40k-fulton-popup-attach-start", ("target", Name(target))),
            args.User,
            args.User,
            PopupType.Small);
    }

    private void OnPrepareDoAfter(WH40KPrepareFultonDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target || args.Used is not { } used)
            return;

        if (!TryComp(used, out WH40KTacticalFultonComponent? fulton))
            return;

        if (!Exists(args.User) || !TryResolveAuthorizedTeamId(args.User, fulton, out var teamId))
            return;

        if (HasComp<WH40KActiveFultonExtractionComponent>(target))
        {
            PopupCaution(args.User, "wh40k-fulton-popup-already-pending");
            return;
        }

        var pendingCap = Math.Max(1, fulton.MaxPendingExtractionsPerTeam);
        if (CountPendingExtractions(teamId) >= pendingCap)
        {
            PopupCaution(
                args.User,
                "wh40k-fulton-popup-team-cap",
                ("count", pendingCap));
            return;
        }

        if (!TryResolveExtractionProfile(target, teamId, fulton, out var profile, out var denyPopupKey))
        {
            PopupCaution(args.User, denyPopupKey);
            return;
        }

        if (!_stack.TryUse(used, 1))
        {
            PopupCaution(args.User, "wh40k-fulton-popup-empty");
            return;
        }

        args.Handled = true;

        var active = EnsureComp<WH40KActiveFultonExtractionComponent>(target);
        active.User = args.User;
        active.TeamId = teamId;
        active.ExtractionId = _nextExtractionId++;
        active.State = WH40KFultonExtractionState.Pending;
        active.NextStateAt = _timing.CurTime + fulton.ExtractionDelay;
        active.ExtractedCleanupDelay = fulton.ExtractedCleanupDelay;
        active.FailedCleanupDelay = fulton.FailedCleanupDelay;
        active.ReturnCoordinates = _transform.GetMoverCoordinates(target);
        active.FrontReward = profile.FrontReward;
        active.CommandReward = profile.CommandReward;
        active.CompleteMissionCargoOnExtract = profile.CompleteMissionCargoOnExtract;
        active.RemoveOnExtract = profile.RemoveOnExtract;
        active.Label = profile.Label;
        active.ExtractedSound = fulton.ExtractedSound;
        active.FailedSound = fulton.FailedSound;

        if (TryComp(target, out TransformComponent? xform) &&
            xform.MapID != MapId.Nullspace)
        {
            active.Effect = Spawn(FultonEffectPrototype, new EntityCoordinates(target, Vector2.Zero));
        }

        Dirty(target, active);
        _audio.PlayPredicted(fulton.AttachSound, target, args.User);

        PopupOwner(
            active.User,
            "wh40k-fulton-popup-pending",
            PopupType.Small,
            ("id", active.ExtractionId),
            ("target", Name(target)),
            ("seconds", Math.Max(1, (int) Math.Ceiling(fulton.ExtractionDelay.TotalSeconds))));
    }

    private void OnActiveShutdown(Entity<WH40KActiveFultonExtractionComponent> ent, ref ComponentShutdown args)
    {
        if (Exists(ent.Comp.Effect))
            QueueDel(ent.Comp.Effect);

        ent.Comp.Effect = EntityUid.Invalid;
    }

    private void OnFultonExamined(Entity<WH40KTacticalFultonComponent> ent, ref ExaminedEvent args)
    {
        using var scope = _culture.CreateScope(args.Examiner);
        using (args.PushGroup(nameof(WH40KTacticalFultonComponent)))
        {
            args.PushMarkup(Loc.GetString(
                "wh40k-fulton-examine-policy",
                ("attach", Math.Max(1, (int) Math.Ceiling(ent.Comp.AttachDelay.TotalSeconds))),
                ("extract", Math.Max(1, (int) Math.Ceiling(ent.Comp.ExtractionDelay.TotalSeconds))),
                ("window", Math.Max(1, (int) Math.Ceiling(ent.Comp.RateLimitWindow.TotalSeconds))),
                ("count", Math.Max(1, ent.Comp.MaxUsesPerWindow)),
                ("pending", Math.Max(1, ent.Comp.MaxPendingExtractionsPerTeam))));

            args.PushMarkup(Loc.GetString(
                "wh40k-fulton-examine-caps",
                ("front", _frontRewardCapPerRound),
                ("command", _commandRewardCapPerRound)));
        }
    }

    private void OnTargetExamined(Entity<WH40KTacticalFultonTargetComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        using var scope = _culture.CreateScope(args.Examiner);
        using (args.PushGroup(nameof(WH40KTacticalFultonTargetComponent)))
        {
            args.PushMarkup(Loc.GetString(
                "wh40k-fulton-target-examine",
                ("front", Math.Max(0, ent.Comp.FrontReward)),
                ("command", Math.Max(0, ent.Comp.CommandReward))));
        }
    }

    private void OnActiveExamined(Entity<WH40KActiveFultonExtractionComponent> ent, ref ExaminedEvent args)
    {
        var remaining = ent.Comp.NextStateAt > _timing.CurTime
            ? Math.Max(0, (int) Math.Ceiling((ent.Comp.NextStateAt - _timing.CurTime).TotalSeconds))
            : 0;

        using var scope = _culture.CreateScope(args.Examiner);
        using (args.PushGroup(nameof(WH40KActiveFultonExtractionComponent)))
        {
            switch (ent.Comp.State)
            {
                case WH40KFultonExtractionState.Pending:
                    args.PushMarkup(Loc.GetString(
                        "wh40k-fulton-status-pending",
                        ("id", ent.Comp.ExtractionId),
                        ("seconds", remaining)));
                    break;
                case WH40KFultonExtractionState.Extracted:
                    args.PushMarkup(Loc.GetString(
                        "wh40k-fulton-status-extracted",
                        ("id", ent.Comp.ExtractionId),
                        ("seconds", remaining)));
                    break;
                case WH40KFultonExtractionState.Failed:
                    args.PushMarkup(Loc.GetString(
                        "wh40k-fulton-status-failed",
                        ("id", ent.Comp.ExtractionId),
                        ("seconds", remaining)));
                    break;
            }
        }
    }

    private void ResolvePendingExtraction(EntityUid uid, WH40KActiveFultonExtractionComponent active)
    {
        if (!TryValidateExtractionTarget(uid))
        {
            MarkFailed(uid, active, "wh40k-fulton-popup-failed-invalid");
            return;
        }

        if (!TryMoveToFultonMap(uid))
        {
            MarkFailed(uid, active, "wh40k-fulton-popup-failed-map");
            return;
        }

        var missionCompleted = false;
        if (_missionHookEnabled &&
            active.CompleteMissionCargoOnExtract &&
            !string.IsNullOrWhiteSpace(active.TeamId))
        {
            missionCompleted = _missionRuntime.TryHandleFultonExtraction(uid, active.TeamId, out _);
        }

        var (frontApplied, commandApplied) = ApplyExtractionRewards(
            active.TeamId,
            active.FrontReward,
            active.CommandReward);

        active.State = WH40KFultonExtractionState.Extracted;
        active.NextStateAt = _timing.CurTime + active.ExtractedCleanupDelay;
        Dirty(uid, active);

        _audio.PlayPvs(active.ExtractedSound, uid);

        PopupOwner(
            active.User,
            "wh40k-fulton-popup-extracted",
            PopupType.Small,
            ("id", active.ExtractionId),
            ("front", frontApplied),
            ("command", commandApplied));

        if (missionCompleted)
        {
            DispatchTeamMessage(
                active.TeamId,
                "wh40k-fulton-team-message-extracted-mission",
                ("target", Name(uid)),
                ("id", active.ExtractionId),
                ("front", frontApplied),
                ("command", commandApplied));
            return;
        }

        DispatchTeamMessage(
            active.TeamId,
            "wh40k-fulton-team-message-extracted",
            ("target", Name(uid)),
            ("id", active.ExtractionId),
            ("front", frontApplied),
            ("command", commandApplied));
    }

    private void ResolveExtractedCleanup(EntityUid uid, WH40KActiveFultonExtractionComponent active)
    {
        if (!active.RemoveOnExtract)
        {
            if (active.ReturnCoordinates != EntityCoordinates.Invalid)
                _transform.SetCoordinates(uid, active.ReturnCoordinates);

            RemCompDeferred<WH40KActiveFultonExtractionComponent>(uid);
            return;
        }

        QueueDel(uid);
    }

    private void ResolveFailedCleanup(EntityUid uid, WH40KActiveFultonExtractionComponent active)
    {
        if (active.ReturnCoordinates != EntityCoordinates.Invalid &&
            TryComp(uid, out TransformComponent? xform) &&
            xform.MapID == MapId.Nullspace)
        {
            _transform.SetCoordinates(uid, active.ReturnCoordinates);
        }

        RemCompDeferred<WH40KActiveFultonExtractionComponent>(uid);
    }

    private void MarkFailed(EntityUid uid, WH40KActiveFultonExtractionComponent active, string popupKey)
    {
        if (TryComp(uid, out TransformComponent? xform) &&
            xform.MapID == MapId.Nullspace &&
            active.ReturnCoordinates != EntityCoordinates.Invalid)
        {
            _transform.SetCoordinates(uid, active.ReturnCoordinates);
        }

        active.State = WH40KFultonExtractionState.Failed;
        active.NextStateAt = _timing.CurTime + active.FailedCleanupDelay;
        Dirty(uid, active);

        _audio.PlayPvs(active.FailedSound, uid);
        PopupOwner(active.User, popupKey, PopupType.SmallCaution, ("id", active.ExtractionId));
    }

    private bool TryResolveAuthorizedTeamId(
        EntityUid user,
        WH40KTacticalFultonComponent fulton,
        out string teamId)
    {
        teamId = string.Empty;
        var hasTeam = _teamRule.TryGetTeamIdFromEntity(user, out teamId);
        if (!hasTeam)
        {
            if (!fulton.RequireTeam && fulton.AllowedTeamIds.Count == 0)
                return true;

            PopupCaution(user, "wh40k-fulton-popup-no-team");
            return false;
        }

        if (fulton.AllowedTeamIds.Count == 0)
            return true;

        var resolvedTeamId = teamId;
        var allowed = fulton.AllowedTeamIds.Any(allowedTeamId =>
            string.Equals(allowedTeamId, resolvedTeamId, StringComparison.OrdinalIgnoreCase));
        if (allowed)
            return true;

        PopupCaution(user, "wh40k-fulton-popup-wrong-team");
        return false;
    }

    private bool TryConsumeUserThrottle(EntityUid user, WH40KTacticalFultonComponent fulton)
    {
        var throttle = EnsureComp<WH40KFultonUserThrottleComponent>(user);
        var now = _timing.CurTime;

        if (throttle.NextAllowedUseAt > now)
        {
            var seconds = Math.Max(1, (int) Math.Ceiling((throttle.NextAllowedUseAt - now).TotalSeconds));
            PopupCaution(user, "wh40k-fulton-popup-user-cooldown", ("seconds", seconds));
            return false;
        }

        while (throttle.RecentUses.Count > 0 &&
               now - throttle.RecentUses.Peek() > fulton.RateLimitWindow)
        {
            throttle.RecentUses.Dequeue();
        }

        if (throttle.RecentUses.Count >= Math.Max(1, fulton.MaxUsesPerWindow))
        {
            var nextAt = throttle.RecentUses.Peek() + fulton.RateLimitWindow;
            var seconds = Math.Max(1, (int) Math.Ceiling((nextAt - now).TotalSeconds));
            PopupCaution(
                user,
                "wh40k-fulton-popup-rate-limit",
                ("seconds", seconds),
                ("count", Math.Max(1, fulton.MaxUsesPerWindow)));
            return false;
        }

        throttle.RecentUses.Enqueue(now);
        throttle.NextAllowedUseAt = now + fulton.UserCooldown;
        return true;
    }

    private int CountPendingExtractions(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return 0;

        var count = 0;
        var query = EntityQueryEnumerator<WH40KActiveFultonExtractionComponent>();
        while (query.MoveNext(out _, out var active))
        {
            if (active.State != WH40KFultonExtractionState.Pending)
                continue;

            if (!string.Equals(active.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            count++;
        }

        return count;
    }

    private bool TryResolveExtractionProfile(
        EntityUid target,
        string teamId,
        WH40KTacticalFultonComponent fulton,
        out ExtractionProfile profile,
        out string denyPopupKey)
    {
        profile = default;
        denyPopupKey = "wh40k-fulton-popup-invalid-target";

        if (_container.IsEntityInContainer(target))
        {
            denyPopupKey = "wh40k-fulton-popup-target-in-container";
            return false;
        }

        if (TryComp(target, out WH40KTacticalFultonTargetComponent? targetComp) &&
            targetComp.Enabled)
        {
            if (TryComp(target, out TransformComponent? targetXform) &&
                targetXform.Anchored &&
                !targetComp.AllowWhenAnchored)
            {
                denyPopupKey = "wh40k-fulton-popup-target-anchored";
                return false;
            }

            if (targetComp.RequireTeam && string.IsNullOrWhiteSpace(teamId))
            {
                denyPopupKey = "wh40k-fulton-popup-no-team";
                return false;
            }

            if (targetComp.AllowedTeamIds.Count > 0)
            {
                var allowed = targetComp.AllowedTeamIds.Any(allowedTeamId =>
                    string.Equals(allowedTeamId, teamId, StringComparison.OrdinalIgnoreCase));
                if (!allowed)
                {
                    denyPopupKey = "wh40k-fulton-popup-wrong-team";
                    return false;
                }
            }

            profile = new ExtractionProfile(
                Label: targetComp.Label,
                FrontReward: Math.Max(0, targetComp.FrontReward),
                CommandReward: Math.Max(0, targetComp.CommandReward),
                CompleteMissionCargoOnExtract: targetComp.CompleteMissionCargoOnExtract,
                RemoveOnExtract: targetComp.RemoveOnExtract);
            return true;
        }

        if (!fulton.AllowDeadBodies || !HasComp<MobStateComponent>(target) || !_mobState.IsDead(target))
            return false;

        if (TryComp(target, out TransformComponent? xform) && xform.Anchored)
        {
            denyPopupKey = "wh40k-fulton-popup-target-anchored";
            return false;
        }

        var frontReward = Math.Max(0, fulton.DefaultCorpseFrontReward);
        var commandReward = Math.Max(0, fulton.DefaultCorpseCommandReward);

        if (fulton.DenyFriendlyCorpseReward &&
            _teamRule.TryGetTeamIdFromEntity(target, out var targetTeamId) &&
            string.Equals(targetTeamId, teamId, StringComparison.OrdinalIgnoreCase))
        {
            frontReward = 0;
            commandReward = 0;
        }

        profile = new ExtractionProfile(
            Label: "wh40k-fulton-target-label-corpse",
            FrontReward: frontReward,
            CommandReward: commandReward,
            CompleteMissionCargoOnExtract: false,
            RemoveOnExtract: true);
        return true;
    }

    private bool TryValidateExtractionTarget(EntityUid target)
    {
        if (Deleted(target))
            return false;

        if (_container.IsEntityInContainer(target))
            return false;

        if (TryComp(target, out WH40KTacticalFultonTargetComponent? targetComp) && targetComp.Enabled)
        {
            if (TryComp(target, out TransformComponent? targetXform) &&
                targetXform.Anchored &&
                !targetComp.AllowWhenAnchored)
            {
                return false;
            }

            return true;
        }

        return HasComp<MobStateComponent>(target) && _mobState.IsDead(target);
    }

    private bool TryMoveToFultonMap(EntityUid target)
    {
        if (Deleted(target))
            return false;

        var mapId = EnsureFultonMap();
        var coordinates = new MapCoordinates(new Vector2(_fultonMapOffset++ * 4f, 0f), mapId);
        _transform.SetMapCoordinates(target, coordinates);
        return true;
    }

    private MapId EnsureFultonMap()
    {
        if (_fultonMap is { } existingMap && _map.MapExists(existingMap))
            return existingMap;

        _map.CreateMap(out var createdMap);
        _fultonMap = createdMap;
        _fultonMapOffset = 0;
        return createdMap;
    }

    private (int FrontApplied, int CommandApplied) ApplyExtractionRewards(
        string teamId,
        int frontReward,
        int commandReward)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return (0, 0);

        var frontApplied = 0;
        var commandApplied = 0;
        var resolvedTeam = teamId;

        var frontRequest = Math.Max(0, frontReward);
        var cappedFront = ApplyRoundCap(_teamFrontRewards, teamId, frontRequest, _frontRewardCapPerRound);
        if (cappedFront > 0 &&
            _teamRule.TryAdjustTeamFrontPoints(teamId, cappedFront, out var resolvedFrontTeam, out _, out _, source: "fulton"))
        {
            resolvedTeam = resolvedFrontTeam;
            _teamFrontRewards[resolvedTeam] = _teamFrontRewards.GetValueOrDefault(resolvedTeam, 0) + cappedFront;
            frontApplied = cappedFront;
        }

        var commandRequest = Math.Max(0, commandReward);
        var cappedCommand = ApplyRoundCap(_teamCommandRewards, resolvedTeam, commandRequest, _commandRewardCapPerRound);
        if (cappedCommand > 0 &&
            _teamRule.TryAdjustTeamCommandPoints(resolvedTeam, cappedCommand, out var resolvedCommandTeam, out _, source: "fulton"))
        {
            resolvedTeam = resolvedCommandTeam;
            _teamCommandRewards[resolvedTeam] = _teamCommandRewards.GetValueOrDefault(resolvedTeam, 0) + cappedCommand;
            commandApplied = cappedCommand;
        }

        return (frontApplied, commandApplied);
    }

    private static int ApplyRoundCap(
        Dictionary<string, int> consumedByTeam,
        string teamId,
        int requested,
        int roundCap)
    {
        if (requested <= 0)
            return 0;

        if (roundCap <= 0)
            return requested;

        var consumed = consumedByTeam.GetValueOrDefault(teamId, 0);
        var remaining = Math.Max(0, roundCap - consumed);
        return Math.Min(requested, remaining);
    }

    private void DispatchTeamMessage(
        string teamId,
        string messageKey,
        params (string, object)[] args)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return;

        foreach (var session in _players.Sessions)
        {
            if (!_teamRule.TryGetTeamIdForUser(session.UserId, out var sessionTeam))
                continue;

            if (!string.Equals(sessionTeam, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            using var scope = _culture.CreateScope(session);
            _chat.DispatchServerMessage(session, Loc.GetString(messageKey, args));
        }
    }

    private void PopupCaution(EntityUid user, string key, params (string, object)[] args)
    {
        _popup.PopupEntity(_culture.GetPlayerString(user, key, args), user, user, PopupType.SmallCaution);
    }

    private void PopupOwner(
        EntityUid? user,
        string key,
        PopupType type = PopupType.Small,
        params (string, object)[] args)
    {
        if (user == null || !Exists(user.Value))
            return;

        _popup.PopupEntity(_culture.GetPlayerString(user.Value, key, args), user.Value, user.Value, type);
    }

    private readonly record struct ExtractionProfile(
        string Label,
        int FrontReward,
        int CommandReward,
        bool CompleteMissionCargoOnExtract,
        bool RemoveOnExtract);
}
