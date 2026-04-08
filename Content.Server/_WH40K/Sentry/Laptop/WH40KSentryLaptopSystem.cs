using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Destructible;
using Content.Server.Popups;
using Content.Server.Turrets;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Localizations;
using Content.Shared._WH40K.Sentry.Laptop;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.GameTicking;
using Content.Shared.Destructible.Thresholds.Triggers;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Turrets;
using Content.Shared.UserInterface;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Sentry.Laptop;

public sealed class WH40KSentryLaptopSystem : EntitySystem
{
    private enum AlertKind : byte
    {
        LowAmmo,
        CriticalHealth,
        Firing,
        Broken,
    }

    private readonly record struct LaptopAlertRecord(
        string Message,
        WH40KSentryLaptopAlertSeverity Severity,
        TimeSpan CreatedAt);

    private readonly record struct AlertCooldownKey(EntityUid Laptop, EntityUid Turret, AlertKind Kind);

    [Dependency] private readonly DeployableTurretSystem _deployableTurret = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly NpcFactionSystem _npcFactions = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly WH40KPlayerCultureTracker _culture = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ViewSubscriberSystem _viewSubscribers = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly Dictionary<EntityUid, List<LaptopAlertRecord>> _alertsByLaptop = new();
    private readonly Dictionary<AlertCooldownKey, TimeSpan> _alertCooldowns = new();
    private readonly Dictionary<(EntityUid Laptop, EntityUid Turret), DeployableTurretState> _lastTurretStates = new();

    private TimeSpan _nextPeriodicUpdate = TimeSpan.Zero;
    private static readonly TimeSpan PeriodicUpdateInterval = TimeSpan.FromSeconds(5);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KSentryLaptopComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WH40KSentryLaptopComponent, ComponentShutdown>(OnLaptopShutdown);
        SubscribeLocalEvent<WH40KSentryLaptopComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<WH40KSentryLaptopComponent, BoundUIClosedEvent>(OnUiClosed);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        SubscribeLocalEvent<DeployableTurretComponent, ComponentShutdown>(OnTurretShutdown);

        SubscribeLocalEvent<WH40KSentryLaptopWatcherComponent, ComponentShutdown>(OnWatcherShutdown);
        SubscribeLocalEvent<WH40KSentryLaptopWatcherComponent, PlayerDetachedEvent>(OnWatcherDetached);

        Subs.BuiEvents<WH40KSentryLaptopComponent>(WH40KSentryLaptopUiKey.Key, subs =>
        {
            subs.Event<WH40KSentryLaptopRefreshBuiMsg>(OnRefreshRequested);
            subs.Event<WH40KSentryLaptopUnlinkBuiMsg>(OnUnlinkRequested);
            subs.Event<WH40KSentryLaptopUnlinkAllBuiMsg>(OnUnlinkAllRequested);

            subs.Event<WH40KSentryLaptopTogglePowerBuiMsg>(OnTogglePowerRequested);
            subs.Event<WH40KSentryLaptopSetPowerAllBuiMsg>(OnSetPowerAllRequested);

            subs.Event<WH40KSentryLaptopResetTargetingBuiMsg>(OnResetTargetingRequested);
            subs.Event<WH40KSentryLaptopResetTargetingAllBuiMsg>(OnResetTargetingAllRequested);

            subs.Event<WH40KSentryLaptopSetIffTeamBuiMsg>(OnSetIffTeamRequested);
            subs.Event<WH40KSentryLaptopSetIffTeamAllBuiMsg>(OnSetIffTeamAllRequested);

            subs.Event<WH40KSentryLaptopViewCameraBuiMsg>(OnViewCameraRequested);
            subs.Event<WH40KSentryLaptopCloseCameraBuiMsg>(OnCloseCameraRequested);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextPeriodicUpdate)
            return;

        _nextPeriodicUpdate = _timing.CurTime + PeriodicUpdateInterval;

        var laptopQuery = EntityQueryEnumerator<WH40KSentryLaptopComponent>();
        while (laptopQuery.MoveNext(out var laptopUid, out var laptop))
        {
            PruneInvalidLinks((laptopUid, laptop));
            EvaluateTurretAlerts((laptopUid, laptop));

            if (_ui.IsUiOpen(laptopUid, WH40KSentryLaptopUiKey.Key))
                UpdateUi((laptopUid, laptop));
        }

        CleanupInvalidWatchers();
    }

    private void OnAfterInteract(Entity<WH40KSentryLaptopComponent> laptop, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (!TryComp<DeployableTurretComponent>(target, out _))
            return;

        if (!CanUseLaptop(laptop, args.User, out var userTeamId, popupOnFail: true))
            return;

        if (laptop.Comp.LinkedTurrets.Contains(target))
        {
            if (UnlinkTurret(laptop, target))
            {
                _popup.PopupEntity(
                    _culture.GetPlayerString(args.User, "wh40k-sentry-laptop-popup-unlinked", ("turret", Name(target))),
                    laptop.Owner,
                    args.User);
                UpdateUi(laptop);
                args.Handled = true;
            }

            return;
        }

        if (laptop.Comp.LinkedTurrets.Count >= laptop.Comp.MaxLinkedTurrets)
        {
            _popup.PopupEntity(
                _culture.GetPlayerString(args.User, "wh40k-sentry-laptop-popup-link-limit", ("max", laptop.Comp.MaxLinkedTurrets)),
                laptop.Owner,
                args.User);
            return;
        }

        if (TryComp<WH40KSentryLinkedComponent>(target, out var existingLink) &&
            existingLink.LinkedLaptop is { } otherLaptop &&
            otherLaptop != laptop.Owner &&
            !TerminatingOrDeleted(otherLaptop))
        {
            _popup.PopupEntity(
                _culture.GetPlayerString(args.User, "wh40k-sentry-laptop-popup-already-linked"),
                laptop.Owner,
                args.User);
            return;
        }

        var turretTeamId = ResolveTurretPrimaryTeam(target, laptop.Comp.IffTeamOptions);
        if (laptop.Comp.RequireTeam &&
            !string.IsNullOrWhiteSpace(userTeamId) &&
            !string.IsNullOrWhiteSpace(turretTeamId) &&
            !string.Equals(userTeamId, turretTeamId, StringComparison.OrdinalIgnoreCase))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.User, "wh40k-sentry-laptop-popup-team-mismatch"), laptop.Owner, args.User);
            return;
        }

        if (!LinkTurret(laptop, target))
            return;

        _popup.PopupEntity(
            _culture.GetPlayerString(args.User, "wh40k-sentry-laptop-popup-linked", ("turret", Name(target))),
            laptop.Owner,
            args.User);

        UpdateUi(laptop);
        args.Handled = true;
    }

    private void OnLaptopShutdown(Entity<WH40KSentryLaptopComponent> laptop, ref ComponentShutdown args)
    {
        UnlinkAllTurrets(laptop);
        ClearWatchersForLaptop(laptop.Owner);
        ClearLaptopRuntimeState(laptop.Owner);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent _)
    {
        _alertsByLaptop.Clear();
        _alertCooldowns.Clear();
        _lastTurretStates.Clear();
    }

    private void OnUiOpened(Entity<WH40KSentryLaptopComponent> laptop, ref BoundUIOpenedEvent args)
    {
        if (!CanUseLaptop(laptop, args.Actor, out _, popupOnFail: true))
        {
            _ui.CloseUi(laptop.Owner, WH40KSentryLaptopUiKey.Key, args.Actor);
            return;
        }

        UpdateUi(laptop);
    }

    private void OnUiClosed(Entity<WH40KSentryLaptopComponent> laptop, ref BoundUIClosedEvent args)
    {
        if (!TryComp<WH40KSentryLaptopWatcherComponent>(args.Actor, out var watcher))
            return;

        if (watcher.Laptop != laptop.Owner)
            return;

        ClearWatcher(args.Actor, watcher, removeComponent: true);
    }

    private void OnTurretShutdown(Entity<DeployableTurretComponent> turret, ref ComponentShutdown args)
    {
        if (TryComp<WH40KSentryLinkedComponent>(turret.Owner, out var linked) &&
            linked.LinkedLaptop is { } laptopUid &&
            TryComp<WH40KSentryLaptopComponent>(laptopUid, out var laptop))
        {
            UnlinkTurret((laptopUid, laptop), turret.Owner, restoreBaseline: false);
            UpdateUi((laptopUid, laptop));
        }

        ClearWatchersForTurret(turret.Owner);
    }

    private void OnWatcherShutdown(Entity<WH40KSentryLaptopWatcherComponent> watcher, ref ComponentShutdown args)
    {
        ClearWatcher(watcher.Owner, watcher.Comp, removeComponent: false);
    }

    private void OnWatcherDetached(Entity<WH40KSentryLaptopWatcherComponent> watcher, ref PlayerDetachedEvent args)
    {
        if (watcher.Comp.CurrentTurret is { } turret)
            _viewSubscribers.RemoveViewSubscriber(turret, args.Player);

        watcher.Comp.Laptop = null;
        watcher.Comp.CurrentTurret = null;
    }

    private void OnRefreshRequested(Entity<WH40KSentryLaptopComponent> laptop, ref WH40KSentryLaptopRefreshBuiMsg args)
    {
        if (!CanUseLaptop(laptop, args.Actor, out _, popupOnFail: false))
            return;

        UpdateUi(laptop);
    }

    private void OnUnlinkRequested(Entity<WH40KSentryLaptopComponent> laptop, ref WH40KSentryLaptopUnlinkBuiMsg args)
    {
        if (!CanUseLaptop(laptop, args.Actor, out _, popupOnFail: false))
            return;

        if (!TryGetEntity(args.Turret, out var turretUid))
            return;

        if (!UnlinkTurret(laptop, turretUid.Value))
            return;

        _popup.PopupEntity(
            _culture.GetPlayerString(args.Actor, "wh40k-sentry-laptop-popup-unlinked", ("turret", Name(turretUid.Value))),
            laptop.Owner,
            args.Actor);
        UpdateUi(laptop);
    }

    private void OnUnlinkAllRequested(Entity<WH40KSentryLaptopComponent> laptop, ref WH40KSentryLaptopUnlinkAllBuiMsg args)
    {
        if (!CanUseLaptop(laptop, args.Actor, out _, popupOnFail: false))
            return;

        UnlinkAllTurrets(laptop);
        _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "wh40k-sentry-laptop-popup-unlinked-all"), laptop.Owner, args.Actor);
        UpdateUi(laptop);
    }

    private void OnTogglePowerRequested(Entity<WH40KSentryLaptopComponent> laptop, ref WH40KSentryLaptopTogglePowerBuiMsg args)
    {
        if (!CanUseLaptop(laptop, args.Actor, out _, popupOnFail: false))
            return;

        if (!TryGetLinkedTurret(laptop, args.Turret, out var turret, out var deployable))
            return;

        _deployableTurret.TrySetState((turret, deployable), !deployable.Enabled, args.Actor);
        UpdateUi(laptop);
    }

    private void OnSetPowerAllRequested(Entity<WH40KSentryLaptopComponent> laptop, ref WH40KSentryLaptopSetPowerAllBuiMsg args)
    {
        if (!CanUseLaptop(laptop, args.Actor, out _, popupOnFail: false))
            return;

        foreach (var turretUid in laptop.Comp.LinkedTurrets)
        {
            if (!TryComp<DeployableTurretComponent>(turretUid, out var deployable))
                continue;

            _deployableTurret.TrySetState((turretUid, deployable), args.Enabled, args.Actor);
        }

        UpdateUi(laptop);
    }

    private void OnResetTargetingRequested(Entity<WH40KSentryLaptopComponent> laptop, ref WH40KSentryLaptopResetTargetingBuiMsg args)
    {
        if (!CanUseLaptop(laptop, args.Actor, out _, popupOnFail: false))
            return;

        if (!TryGetEntity(args.Turret, out var turretUid) || !laptop.Comp.LinkedTurrets.Contains(turretUid.Value))
            return;

        ResetTurretTargeting(laptop, turretUid.Value);
        UpdateUi(laptop);
    }

    private void OnResetTargetingAllRequested(Entity<WH40KSentryLaptopComponent> laptop, ref WH40KSentryLaptopResetTargetingAllBuiMsg args)
    {
        if (!CanUseLaptop(laptop, args.Actor, out _, popupOnFail: false))
            return;

        foreach (var turretUid in laptop.Comp.LinkedTurrets)
        {
            if (TerminatingOrDeleted(turretUid))
                continue;

            ResetTurretTargeting(laptop, turretUid);
        }

        UpdateUi(laptop);
    }

    private void OnSetIffTeamRequested(Entity<WH40KSentryLaptopComponent> laptop, ref WH40KSentryLaptopSetIffTeamBuiMsg args)
    {
        if (!CanUseLaptop(laptop, args.Actor, out _, popupOnFail: false))
            return;

        if (!TryGetEntity(args.Turret, out var turretUid) || !laptop.Comp.LinkedTurrets.Contains(turretUid.Value))
            return;

        if (!SetTurretIffTeam(laptop, turretUid.Value, args.TeamId, args.Allowed, args.Actor))
            return;

        UpdateUi(laptop);
    }

    private void OnSetIffTeamAllRequested(Entity<WH40KSentryLaptopComponent> laptop, ref WH40KSentryLaptopSetIffTeamAllBuiMsg args)
    {
        if (!CanUseLaptop(laptop, args.Actor, out _, popupOnFail: false))
            return;

        foreach (var turretUid in laptop.Comp.LinkedTurrets)
        {
            if (TerminatingOrDeleted(turretUid))
                continue;

            SetTurretIffTeam(laptop, turretUid, args.TeamId, args.Allowed, args.Actor);
        }

        UpdateUi(laptop);
    }

    private void OnViewCameraRequested(Entity<WH40KSentryLaptopComponent> laptop, ref WH40KSentryLaptopViewCameraBuiMsg args)
    {
        if (!CanUseLaptop(laptop, args.Actor, out _, popupOnFail: false))
            return;

        if (!TryGetEntity(args.Turret, out var turretUid) || !laptop.Comp.LinkedTurrets.Contains(turretUid.Value))
            return;

        if (!TryComp<ActorComponent>(args.Actor, out var actor))
            return;

        var watcher = EnsureComp<WH40KSentryLaptopWatcherComponent>(args.Actor);

        if (watcher.CurrentTurret is { } previousTurret)
            _viewSubscribers.RemoveViewSubscriber(previousTurret, actor.PlayerSession);

        watcher.Laptop = laptop.Owner;
        watcher.CurrentTurret = turretUid.Value;
        _viewSubscribers.AddViewSubscriber(turretUid.Value, actor.PlayerSession);
    }

    private void OnCloseCameraRequested(Entity<WH40KSentryLaptopComponent> laptop, ref WH40KSentryLaptopCloseCameraBuiMsg args)
    {
        if (!TryComp<WH40KSentryLaptopWatcherComponent>(args.Actor, out var watcher))
            return;

        if (watcher.Laptop != laptop.Owner)
            return;

        ClearWatcher(args.Actor, watcher, removeComponent: true);
    }

    private bool CanUseLaptop(
        Entity<WH40KSentryLaptopComponent> laptop,
        EntityUid user,
        out string? teamId,
        bool popupOnFail)
    {
        teamId = null;

        if (TryComp<GhostComponent>(user, out var ghost) && ghost.CanGhostInteract)
            return true;

        if (!laptop.Comp.RequireTeam)
            return true;

        if (!TryResolveUserTeam(user, out var resolvedTeam))
        {
            if (popupOnFail)
                _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-sentry-laptop-popup-no-team"), laptop.Owner, user);
            return false;
        }

        teamId = resolvedTeam;

        if (laptop.Comp.AllowedTeamIds.Count == 0)
            return true;

        foreach (var allowedTeam in laptop.Comp.AllowedTeamIds)
        {
            if (string.Equals(allowedTeam, resolvedTeam, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (popupOnFail)
            _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-sentry-laptop-popup-wrong-team"), laptop.Owner, user);

        return false;
    }

    private bool TryResolveUserTeam(EntityUid user, out string teamId)
    {
        if (_teamRule.TryGetTeamIdFromEntity(user, out teamId))
            return true;

        if (!TryComp<MindComponent>(user, out var mind))
        {
            teamId = string.Empty;
            return false;
        }

        if (mind.CurrentEntity is { } currentEntity &&
            _teamRule.TryGetTeamIdFromEntity(currentEntity, out teamId))
        {
            return true;
        }

        if (mind.UserId is not { } userId)
        {
            teamId = string.Empty;
            return false;
        }

        return _teamRule.TryGetRememberedTeam(userId, out teamId);
    }

    private bool LinkTurret(Entity<WH40KSentryLaptopComponent> laptop, EntityUid turret)
    {
        if (laptop.Comp.LinkedTurrets.Contains(turret))
            return false;

        var linked = EnsureComp<WH40KSentryLinkedComponent>(turret);
        linked.LinkedLaptop = laptop.Owner;
        linked.BaselineFactions = GetTurretFactions(turret);

        laptop.Comp.LinkedTurrets.Add(turret);
        return true;
    }

    private bool UnlinkTurret(
        Entity<WH40KSentryLaptopComponent> laptop,
        EntityUid turret,
        bool restoreBaseline = true)
    {
        if (!laptop.Comp.LinkedTurrets.Remove(turret))
            return false;

        if (restoreBaseline)
            RestoreTurretBaselineIfNeeded(laptop.Owner, turret);

        if (TryComp<WH40KSentryLinkedComponent>(turret, out var linked) && linked.LinkedLaptop == laptop.Owner)
            RemComp<WH40KSentryLinkedComponent>(turret);

        CleanupTurretRuntimeState(laptop.Owner, turret);
        ClearWatchersForTurret(turret);
        return true;
    }

    private void UnlinkAllTurrets(Entity<WH40KSentryLaptopComponent> laptop)
    {
        var linked = new List<EntityUid>(laptop.Comp.LinkedTurrets);
        foreach (var turret in linked)
        {
            if (TerminatingOrDeleted(turret))
                continue;

            UnlinkTurret(laptop, turret);
        }

        laptop.Comp.LinkedTurrets.Clear();
    }

    private void PruneInvalidLinks(Entity<WH40KSentryLaptopComponent> laptop)
    {
        if (laptop.Comp.LinkedTurrets.Count == 0)
            return;

        var toRemove = new List<EntityUid>();

        foreach (var turretUid in laptop.Comp.LinkedTurrets)
        {
            if (TerminatingOrDeleted(turretUid) || !HasComp<DeployableTurretComponent>(turretUid))
            {
                toRemove.Add(turretUid);
                continue;
            }

            if (!TryComp<WH40KSentryLinkedComponent>(turretUid, out var linked) || linked.LinkedLaptop != laptop.Owner)
                toRemove.Add(turretUid);
        }

        foreach (var turretUid in toRemove)
        {
            laptop.Comp.LinkedTurrets.Remove(turretUid);
            CleanupTurretRuntimeState(laptop.Owner, turretUid);
        }

    }

    private bool TryGetLinkedTurret(
        Entity<WH40KSentryLaptopComponent> laptop,
        NetEntity turretNet,
        out EntityUid turretUid,
        out DeployableTurretComponent deployable)
    {
        turretUid = default;
        deployable = default!;

        if (!TryGetEntity(turretNet, out var turretEntity))
            return false;

        turretUid = turretEntity.Value;

        if (!laptop.Comp.LinkedTurrets.Contains(turretUid))
            return false;

        if (!TryComp(turretUid, out DeployableTurretComponent? deployableComp))
            return false;

        deployable = deployableComp;
        return true;
    }

    private bool SetTurretIffTeam(
        Entity<WH40KSentryLaptopComponent> laptop,
        EntityUid turret,
        string teamId,
        bool allowed,
        EntityUid actor)
    {
        if (!laptop.Comp.IffTeamOptions.Contains(teamId, StringComparer.OrdinalIgnoreCase))
            return false;

        if (!_proto.HasIndex<NpcFactionPrototype>(teamId))
            return false;

        if (!TryComp<NpcFactionMemberComponent>(turret, out var factions))
            return false;

        var primaryTeam = ResolveTurretPrimaryTeam(turret, laptop.Comp.IffTeamOptions);
        if (!allowed &&
            !string.IsNullOrWhiteSpace(primaryTeam) &&
            string.Equals(primaryTeam, teamId, StringComparison.OrdinalIgnoreCase))
        {
            _popup.PopupEntity(_culture.GetPlayerString(actor, "wh40k-sentry-laptop-popup-iff-cannot-disable-primary"), laptop.Owner, actor);
            return false;
        }

        if (allowed)
        {
            _npcFactions.AddFaction((turret, factions), teamId);
            return true;
        }

        if (factions.Factions.Count <= 1 && HasFaction(factions.Factions, teamId))
        {
            _popup.PopupEntity(_culture.GetPlayerString(actor, "wh40k-sentry-laptop-popup-iff-cannot-remove-last-team"), laptop.Owner, actor);
            return false;
        }

        _npcFactions.RemoveFaction((turret, factions), teamId);
        return true;
    }

    private void ResetTurretTargeting(Entity<WH40KSentryLaptopComponent> laptop, EntityUid turret)
    {
        RestoreTurretBaselineIfNeeded(laptop.Owner, turret);
    }

    private void RestoreTurretBaselineIfNeeded(EntityUid laptop, EntityUid turret)
    {
        if (!TryComp<NpcFactionMemberComponent>(turret, out var factions) ||
            !TryComp<WH40KSentryLinkedComponent>(turret, out var linked) ||
            linked.LinkedLaptop != laptop)
        {
            return;
        }

        ApplyFactionSet(turret, factions, linked.BaselineFactions);
    }

    private void ApplyFactionSet(EntityUid turret, NpcFactionMemberComponent factions, HashSet<string> factionsToApply)
    {
        var targetFactions = new HashSet<ProtoId<NpcFactionPrototype>>();
        foreach (var faction in factionsToApply)
        {
            if (_proto.HasIndex<NpcFactionPrototype>(faction))
                targetFactions.Add(new ProtoId<NpcFactionPrototype>(faction));
        }

        if (targetFactions.Count == 0)
        {
            foreach (var fallback in factions.Factions)
            {
                if (_proto.HasIndex(fallback))
                    targetFactions.Add(fallback);
            }
        }

        if (targetFactions.Count == 0)
            targetFactions.Add(new ProtoId<NpcFactionPrototype>("Imperium"));

        _npcFactions.ClearFactions((turret, factions), dirty: false);
        _npcFactions.AddFactions((turret, factions), targetFactions, dirty: true);
    }

    private string ResolveTurretPrimaryTeam(EntityUid turret, IReadOnlyList<string> teamOptions)
    {
        if (!TryComp<NpcFactionMemberComponent>(turret, out var factions))
            return string.Empty;

        foreach (var option in teamOptions)
        {
            if (HasFaction(factions.Factions, option))
                return option;
        }

        foreach (var faction in factions.Factions)
        {
            var raw = faction.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
                return raw;
        }

        return string.Empty;
    }

    private List<string> ResolveFriendlyTeams(EntityUid turret, IReadOnlyList<string> teamOptions)
    {
        var result = new List<string>();
        if (!TryComp<NpcFactionMemberComponent>(turret, out var factions))
            return result;

        foreach (var option in teamOptions)
        {
            if (!_proto.HasIndex<NpcFactionPrototype>(option))
                continue;

            if (_npcFactions.IsFactionFriendly(option, (turret, factions)))
                result.Add(option);
        }

        return result;
    }

    private static bool HasFaction(IEnumerable<ProtoId<NpcFactionPrototype>> set, string teamId)
    {
        foreach (var faction in set)
        {
            if (string.Equals(faction.ToString(), teamId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private HashSet<string> GetTurretFactions(EntityUid turret)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!TryComp<NpcFactionMemberComponent>(turret, out var factions))
            return result;

        foreach (var faction in factions.Factions)
        {
            var value = faction.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(value);
        }

        return result;
    }

    private void EvaluateTurretAlerts(Entity<WH40KSentryLaptopComponent> laptop)
    {
        foreach (var turretUid in laptop.Comp.LinkedTurrets)
        {
            if (!TryComp<DeployableTurretComponent>(turretUid, out var deployable))
                continue;

            EvaluateTurretStateAlerts(laptop, turretUid, deployable.CurrentState);
            EvaluateTurretAmmoAlerts(laptop, turretUid);
            EvaluateTurretHealthAlerts(laptop, turretUid);
        }
    }

    private void EvaluateTurretStateAlerts(
        Entity<WH40KSentryLaptopComponent> laptop,
        EntityUid turret,
        DeployableTurretState currentState)
    {
        var key = (laptop.Owner, turret);
        var hadPrevious = _lastTurretStates.TryGetValue(key, out var previousState);

        if (currentState == DeployableTurretState.Firing && previousState != DeployableTurretState.Firing)
        {
            AddAlert(laptop,
                turret,
                AlertKind.Firing,
                WH40KSentryLaptopAlertSeverity.Info,
                Loc.GetString("wh40k-sentry-laptop-alert-firing", ("turret", Name(turret))));
        }

        if (currentState == DeployableTurretState.Broken && (!hadPrevious || previousState != DeployableTurretState.Broken))
        {
            AddAlert(laptop,
                turret,
                AlertKind.Broken,
                WH40KSentryLaptopAlertSeverity.Critical,
                Loc.GetString("wh40k-sentry-laptop-alert-broken", ("turret", Name(turret))));
        }

        _lastTurretStates[key] = currentState;
    }

    private void EvaluateTurretAmmoAlerts(Entity<WH40KSentryLaptopComponent> laptop, EntityUid turret)
    {
        var ammo = GetTurretAmmo(turret, out var capacity);
        if (capacity <= 0)
            return;

        var ratio = (float) ammo / capacity;
        if (ratio > laptop.Comp.LowAmmoAlertThreshold)
            return;

        AddAlert(laptop,
            turret,
            AlertKind.LowAmmo,
            WH40KSentryLaptopAlertSeverity.Warning,
            Loc.GetString("wh40k-sentry-laptop-alert-low-ammo",
                ("turret", Name(turret)),
                ("ammo", ammo),
                ("capacity", capacity)));
    }

    private void EvaluateTurretHealthAlerts(Entity<WH40KSentryLaptopComponent> laptop, EntityUid turret)
    {
        if (!TryGetTurretHealth(turret, out var health, out var maxHealth) || maxHealth <= 0f)
            return;

        var ratio = health / maxHealth;
        if (ratio > laptop.Comp.CriticalHealthAlertThreshold)
            return;

        var healthPercent = Math.Clamp((int) Math.Round(ratio * 100f), 0, 100);
        AddAlert(laptop,
            turret,
            AlertKind.CriticalHealth,
            WH40KSentryLaptopAlertSeverity.Critical,
            Loc.GetString("wh40k-sentry-laptop-alert-critical-health",
                ("turret", Name(turret)),
                ("health", healthPercent)));
    }

    private void AddAlert(
        Entity<WH40KSentryLaptopComponent> laptop,
        EntityUid turret,
        AlertKind kind,
        WH40KSentryLaptopAlertSeverity severity,
        string message)
    {
        var now = _timing.CurTime;
        var cooldownKey = new AlertCooldownKey(laptop.Owner, turret, kind);

        if (_alertCooldowns.TryGetValue(cooldownKey, out var lastTriggered))
        {
            var elapsed = now - lastTriggered;
            if (elapsed < TimeSpan.FromSeconds(Math.Max(0.1f, laptop.Comp.AlertCooldownSeconds)))
                return;
        }

        _alertCooldowns[cooldownKey] = now;

        if (!_alertsByLaptop.TryGetValue(laptop.Owner, out var alerts))
        {
            alerts = new List<LaptopAlertRecord>();
            _alertsByLaptop[laptop.Owner] = alerts;
        }

        alerts.Insert(0, new LaptopAlertRecord(message, severity, now));

        var maxAlerts = Math.Max(1, laptop.Comp.AlertHistoryLimit);
        if (alerts.Count > maxAlerts)
            alerts.RemoveRange(maxAlerts, alerts.Count - maxAlerts);
    }

    private int GetTurretAmmo(EntityUid turret, out int capacity)
    {
        var ammoEvent = new GetAmmoCountEvent();
        RaiseLocalEvent(turret, ref ammoEvent);

        capacity = ammoEvent.Capacity;
        return ammoEvent.Count;
    }

    private bool TryGetTurretHealth(EntityUid turret, out float health, out float maxHealth)
    {
        health = 0f;
        maxHealth = 0f;

        if (!TryComp<DamageableComponent>(turret, out var damageable))
            return false;

        maxHealth = GetTurretMaxHealth(turret);
        if (maxHealth <= 0f)
            maxHealth = 100f;

#pragma warning disable CS0618 // GetTotalDamage: no alternative API for health calculation
        var damage = _damageable.GetTotalDamage((turret, damageable)).Float();
#pragma warning restore CS0618
        health = Math.Max(0f, maxHealth - damage);
        return true;
    }

    private float GetTurretMaxHealth(EntityUid turret)
    {
        if (!TryComp<DestructibleComponent>(turret, out var destructible))
            return 100f;

        var maxHealth = 0f;
        foreach (var threshold in destructible.Thresholds)
        {
            if (threshold.Trigger is DamageTrigger damageTrigger)
                maxHealth = Math.Max(maxHealth, damageTrigger.Damage.Float());
        }

        return maxHealth > 0f ? maxHealth : 100f;
    }

    private void UpdateUi(Entity<WH40KSentryLaptopComponent> laptop)
    {
        var turrets = new List<WH40KSentryLaptopTurretInfo>();

        foreach (var turretUid in laptop.Comp.LinkedTurrets)
        {
            if (!TryComp<DeployableTurretComponent>(turretUid, out var deployable))
                continue;

            var ammo = GetTurretAmmo(turretUid, out var ammoCapacity);
            var teamId = ResolveTurretPrimaryTeam(turretUid, laptop.Comp.IffTeamOptions);
            var friendlyTeams = ResolveFriendlyTeams(turretUid, laptop.Comp.IffTeamOptions);
            var broken = deployable.CurrentState == DeployableTurretState.Broken;

            turrets.Add(new WH40KSentryLaptopTurretInfo(
                GetNetEntity(turretUid),
                Name(turretUid),
                teamId,
                deployable.CurrentState,
                ammo,
                ammoCapacity,
                broken,
                deployable.Enabled,
                friendlyTeams));
        }

        turrets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var now = _timing.CurTime;
        var alertInfos = new List<WH40KSentryLaptopAlertInfo>();
        if (_alertsByLaptop.TryGetValue(laptop.Owner, out var alerts))
        {
            foreach (var alert in alerts)
            {
                var ageSeconds = Math.Max(0, (int) (now - alert.CreatedAt).TotalSeconds);
                alertInfos.Add(new WH40KSentryLaptopAlertInfo(alert.Message, alert.Severity, ageSeconds));
            }
        }

        var state = new WH40KSentryLaptopBuiState(
            turrets,
            turrets.Count,
            laptop.Comp.MaxLinkedTurrets,
            new List<string>(laptop.Comp.IffTeamOptions),
            alertInfos);

        _ui.SetUiState(laptop.Owner, WH40KSentryLaptopUiKey.Key, state);
    }

    private void ClearWatchersForLaptop(EntityUid laptop)
    {
        var watcherQuery = EntityQueryEnumerator<WH40KSentryLaptopWatcherComponent>();
        while (watcherQuery.MoveNext(out var watcherUid, out var watcher))
        {
            if (watcher.Laptop != laptop)
                continue;

            ClearWatcher(watcherUid, watcher, removeComponent: true);
        }
    }

    private void ClearWatchersForTurret(EntityUid turret)
    {
        var watcherQuery = EntityQueryEnumerator<WH40KSentryLaptopWatcherComponent>();
        while (watcherQuery.MoveNext(out var watcherUid, out var watcher))
        {
            if (watcher.CurrentTurret != turret)
                continue;

            ClearWatcher(watcherUid, watcher, removeComponent: true);
        }
    }

    private void CleanupInvalidWatchers()
    {
        var watcherQuery = EntityQueryEnumerator<WH40KSentryLaptopWatcherComponent>();
        while (watcherQuery.MoveNext(out var watcherUid, out var watcher))
        {
            if (!TryComp<ActorComponent>(watcherUid, out _))
            {
                ClearWatcher(watcherUid, watcher, removeComponent: true);
                continue;
            }

            if (watcher.CurrentTurret is not { } turret)
            {
                if (watcher.Laptop != null)
                    ClearWatcher(watcherUid, watcher, removeComponent: true);
                continue;
            }

            if (TerminatingOrDeleted(turret) || !HasComp<DeployableTurretComponent>(turret))
            {
                ClearWatcher(watcherUid, watcher, removeComponent: true);
                continue;
            }

            if (watcher.Laptop is not { } laptop ||
                TerminatingOrDeleted(laptop) ||
                !TryComp<WH40KSentryLaptopComponent>(laptop, out var laptopComp) ||
                !laptopComp.LinkedTurrets.Contains(turret) ||
                !_ui.IsUiOpen(laptop, WH40KSentryLaptopUiKey.Key))
            {
                ClearWatcher(watcherUid, watcher, removeComponent: true);
            }
        }
    }

    private void ClearWatcher(EntityUid watcherUid, WH40KSentryLaptopWatcherComponent watcher, bool removeComponent)
    {
        if (TryComp<ActorComponent>(watcherUid, out var actor) && watcher.CurrentTurret is { } turret)
            _viewSubscribers.RemoveViewSubscriber(turret, actor.PlayerSession);

        watcher.Laptop = null;
        watcher.CurrentTurret = null;

        if (removeComponent)
            RemCompDeferred<WH40KSentryLaptopWatcherComponent>(watcherUid);
    }

    private void CleanupTurretRuntimeState(EntityUid laptop, EntityUid turret)
    {
        _lastTurretStates.Remove((laptop, turret));

        var cooldownKeys = new List<AlertCooldownKey>();
        foreach (var key in _alertCooldowns.Keys)
        {
            if (key.Laptop == laptop && key.Turret == turret)
                cooldownKeys.Add(key);
        }

        foreach (var key in cooldownKeys)
        {
            _alertCooldowns.Remove(key);
        }
    }

    private void ClearLaptopRuntimeState(EntityUid laptop)
    {
        _alertsByLaptop.Remove(laptop);

        var cooldownKeys = new List<AlertCooldownKey>();
        foreach (var key in _alertCooldowns.Keys)
        {
            if (key.Laptop == laptop)
                cooldownKeys.Add(key);
        }

        foreach (var key in cooldownKeys)
        {
            _alertCooldowns.Remove(key);
        }

        var turretKeys = new List<(EntityUid Laptop, EntityUid Turret)>();
        foreach (var key in _lastTurretStates.Keys)
        {
            if (key.Laptop == laptop)
                turretKeys.Add(key);
        }

        foreach (var key in turretKeys)
        {
            _lastTurretStates.Remove(key);
        }
    }
}
