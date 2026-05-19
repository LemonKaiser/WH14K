using System;
using System.Collections.Generic;
using Content.Server._WH40K.Combat;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Notifications;
using Content.Server._WH40K.Objectives.Components;
using Content.Shared._WH40K.GameMode;
using Content.Shared._WH40K.Notifications;
using Content.Shared._WH40K.Objectives;
using Content.Shared._WH40K.Overlays;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Trigger;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Systems;
using Robust.Server.Player;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Objectives;

public sealed class WH40KObjectiveSystem : EntitySystem
{
    private const float ObjectiveNotificationDuration = 9f;

    [Dependency] private readonly WH40KNotificationSystem _notifications = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly WH40KAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly WH40KTeamRuleFacadeSystem _teamRule = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private readonly Dictionary<EntityUid, string> _objectiveTeams = new();
    private readonly Dictionary<string, int> _teamObjectiveTotals = new();
    private readonly Dictionary<string, int> _teamObjectiveRemaining = new();
    private TimeSpan _nextShieldVisualTick;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KObjectiveComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KObjectiveComponent, ComponentShutdown>(OnObjectiveShutdown);
        SubscribeLocalEvent<WH40KObjectiveComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WH40KObjectiveComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<WH40KObjectiveComponent, TriggerEvent>(OnTrigger);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextShieldVisualTick)
            return;

        _nextShieldVisualTick = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        var phase = _teamRule.GetCurrentPhase();
        var query = EntityQueryEnumerator<WH40KObjectiveComponent>();
        while (query.MoveNext(out var uid, out var objective))
        {
            UpdateShieldVisual(uid, objective, phase);
        }
    }

    private void OnMapInit(EntityUid uid, WH40KObjectiveComponent component, MapInitEvent args)
    {
        var objectivesEnabled = _teamRule.AreObjectivesEnabled();
        var hasActiveTeamBattle = _teamRule.GetTeamIds().Count > 0;
        if (!objectivesEnabled && hasActiveTeamBattle)
        {
            QueueDel(uid);
            return;
        }

        component.LowHealthAnnounced = false;
        component.Destroying = false;
        component.Destroyed = false;

        component.WarnAtPercent = MathHelper.Clamp(component.WarnAtPercent, 0f, 1f);
        if (component.MaxHealth <= FixedPoint2.Zero)
            component.MaxHealth = FixedPoint2.New(1);
        RegisterObjective(uid, component);

        if (TryComp<TimerTriggerComponent>(uid, out var timer))
        {
            var delay = TimeSpan.FromSeconds(Math.Max(0, component.DestructionDelaySeconds));
            if (timer.Delay != delay || timer.KeyOut != component.TriggerKey)
            {
                timer.Delay = delay;
                timer.KeyOut = component.TriggerKey;
                Dirty(uid, timer);
            }
        }

        if (TryComp(uid, out WH40KAlwaysShowHealthBarComponent? bar))
        {
            if (bar.MaxHealth != component.MaxHealth || bar.UseMobThresholds)
            {
                bar.MaxHealth = component.MaxHealth;
                bar.UseMobThresholds = false;
                Dirty(uid, bar);
            }
        }

        SetVisualState(uid, WH40KObjectiveVisualState.Intact);
        UpdateShieldVisual(uid, component, _teamRule.GetCurrentPhase());
    }

    private void OnBeforeDamageChanged(EntityUid uid, WH40KObjectiveComponent component, ref BeforeDamageChangedEvent args)
    {
        if (component.Destroying || component.Destroyed)
        {
            args.Cancelled = true;
            return;
        }

        if (_teamRule.IsEarlyVictoryLocked())
        {
            args.Cancelled = true;
            return;
        }

        if (args.Origin == null)
            return;

        if (!_attackerResolver.TryResolveAttacker(args.Origin.Value, out var attacker))
            attacker = args.Origin.Value;

        if (!_teamRule.TryGetTeamIdFromEntity(attacker, out var attackerTeamId))
            return;

        if (attackerTeamId == component.TeamId)
        {
            args.Cancelled = true;
            return;
        }

        if (_teamRule.GetCurrentPhase() < WH40KBattlePhase.Assault)
        {
            var multiplier = Math.Clamp(component.PreparationShieldDamageMultiplier, 0f, 1f);
            if (multiplier <= 0f)
            {
                args.Cancelled = true;
                return;
            }

            args.Damage = args.Damage * multiplier;
        }

    }

    private void OnDamageChanged(EntityUid uid, WH40KObjectiveComponent component, DamageChangedEvent args)
    {
        if (!args.DamageIncreased || component.Destroyed)
            return;

        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return;

        var maxHealth = component.MaxHealth;
        if (maxHealth <= FixedPoint2.Zero)
            return;

#pragma warning disable CS0618 // GetTotalDamage: no alternative API for health ratio calculation
        var totalDamage = _damageable.GetTotalDamage((uid, damageable));
#pragma warning restore CS0618
        var remainingRatio = (maxHealth - totalDamage).Float() / maxHealth.Float();

        if (!component.LowHealthAnnounced &&
            remainingRatio <= component.WarnAtPercent &&
            CountObjectivesForTeam(component.TeamId, includeDestroyed: true) <= 1)
        {
            component.LowHealthAnnounced = true;
            var message = Loc.GetString("wh40k-objective-low-health",
                ("target", Loc.GetString(component.Name)));
            DispatchObjectiveAllyMessage(component.TeamId, message);
        }

        if (component.Destroying || totalDamage < maxHealth)
            return;

        component.Destroying = true;
        SetVisualState(uid, WH40KObjectiveVisualState.Destroying);
        StartDestructionTimer(uid, component, args.Origin);
    }

    private void OnTrigger(EntityUid uid, WH40KObjectiveComponent component, ref TriggerEvent args)
    {
        if (args.Key != component.TriggerKey)
            return;

        FinalizeDestruction(uid, component);
    }

    private void StartDestructionTimer(EntityUid uid, WH40KObjectiveComponent component, EntityUid? cause)
    {
        if (!TryComp<TimerTriggerComponent>(uid, out var timer))
        {
            FinalizeDestruction(uid, component);
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Max(0, component.DestructionDelaySeconds));
        if (timer.Delay != delay || timer.KeyOut != component.TriggerKey)
        {
            timer.Delay = delay;
            timer.KeyOut = component.TriggerKey;
            Dirty(uid, timer);
        }

        _trigger.ActivateTimerTrigger((uid, timer), cause);
    }

    private void FinalizeDestruction(EntityUid uid, WH40KObjectiveComponent component)
    {
        if (component.Destroyed)
            return;

        component.Destroyed = true;
        component.Destroying = false;
        DecrementRemainingObjective(component.TeamId);
        SetVisualState(uid, WH40KObjectiveVisualState.Destroyed);

        var targetName = Loc.GetString(component.Name);
        var remaining = CountRemainingObjectives(component.TeamId);
        var hasTeamName = _teamRule.TryGetTeamDisplayName(component.TeamId, out var teamName);

        if (remaining > 0)
        {
            DispatchObjectiveRemainingMessages(component.TeamId, targetName, remaining, hasTeamName ? teamName : component.TeamId);
            return;
        }

        var destroyedMessage = Loc.GetString("wh40k-objective-destroyed",
            ("target", targetName));
        DispatchObjectiveColoredMessages(component.TeamId, destroyedMessage, destroyedMessage);

        _teamRule.HandleObjectiveDestroyed(component.TeamId);
    }

    private void SetVisualState(EntityUid uid, WH40KObjectiveVisualState state)
    {
        if (TryComp<AppearanceComponent>(uid, out var appearance))
            _appearance.SetData(uid, WH40KObjectiveVisuals.State, state, appearance);
    }

    private void UpdateShieldVisual(EntityUid uid, WH40KObjectiveComponent component, WH40KBattlePhase phase)
    {
        var shielded = !component.Destroying &&
                       !component.Destroyed &&
                       phase < WH40KBattlePhase.Assault;

        if (TryComp<AppearanceComponent>(uid, out var appearance))
            _appearance.SetData(uid, WH40KObjectiveVisuals.Shielded, shielded, appearance);
    }

    private int CountRemainingObjectives(string teamId)
    {
        return CountObjectivesForTeam(teamId, includeDestroyed: false);
    }

    private int CountObjectivesForTeam(string teamId, bool includeDestroyed)
    {
        if (string.IsNullOrEmpty(teamId))
            return 0;

        if (includeDestroyed)
            return _teamObjectiveTotals.GetValueOrDefault(teamId, 0);

        return _teamObjectiveRemaining.GetValueOrDefault(teamId, 0);
    }

    private void DispatchObjectiveRemainingMessages(string teamId, string targetName, int remaining, string teamName)
    {
        var allyText = Loc.GetString("wh40k-objective-destroyed-remaining-allies",
            ("target", targetName),
            ("remaining", remaining));
        var enemyText = Loc.GetString("wh40k-objective-destroyed-remaining-enemies",
            ("target", targetName),
            ("remaining", remaining),
            ("team", Loc.GetString(teamName)));

        DispatchObjectiveColoredMessages(teamId, allyText, enemyText);
    }

    private void DispatchObjectiveColoredMessages(string teamId, string allyMessage, string enemyMessage)
    {
        foreach (var player in _players.Sessions)
        {
            if (player.AttachedEntity is not { } attached)
                continue;

            if (!_teamRule.TryGetTeamIdFromEntity(attached, out var playerTeam))
            {
                DispatchNotificationToOne(player, enemyMessage, WH40KNotificationColors.Objective);
                continue;
            }

            if (playerTeam == teamId)
                DispatchNotificationToOne(player, allyMessage, GetNotificationTeamColor(teamId));
            else
                DispatchNotificationToOne(player, enemyMessage, GetNotificationTeamColor(playerTeam));
        }
    }

    private void DispatchObjectiveAllyMessage(string teamId, string message)
    {
        foreach (var player in _players.Sessions)
        {
            if (player.AttachedEntity is not { } attached)
                continue;

            if (!_teamRule.TryGetTeamIdFromEntity(attached, out var playerTeam))
                continue;

            if (playerTeam == teamId)
                DispatchNotificationToOne(player, message, GetNotificationTeamColor(teamId));
        }
    }

    private void DispatchNotificationToOne(ICommonSession player, string message, Color color)
    {
        _notifications.SendToSession(
            player,
            Loc.GetString("wh40k-notification-title-objective"),
            message,
            color,
            ObjectiveNotificationDuration,
            false,
            WH40KNotificationSize.Wide,
            WH40KNotificationCategory.Point,
            WH40KNotificationPriority.Point,
            WH40KNotificationIcon.Point,
            "objective:destroyed");
    }

    private Color GetNotificationTeamColor(string teamId)
    {
        return _teamRule.TryGetTeamColor(teamId, out var teamColor)
            ? teamColor
            : WH40KNotificationColors.ForTeam(teamId);
    }


    private void RegisterObjective(EntityUid uid, WH40KObjectiveComponent component)
    {
        if (string.IsNullOrEmpty(component.TeamId))
            return;

        if (_objectiveTeams.TryGetValue(uid, out var existingTeam))
        {
            if (!string.IsNullOrEmpty(existingTeam))
            {
                AdjustTeamCount(_teamObjectiveTotals, existingTeam, -1);
                if (!component.Destroyed)
                    AdjustTeamCount(_teamObjectiveRemaining, existingTeam, -1);
            }
        }

        _objectiveTeams[uid] = component.TeamId;
        AdjustTeamCount(_teamObjectiveTotals, component.TeamId, 1);
        if (!component.Destroyed)
            AdjustTeamCount(_teamObjectiveRemaining, component.TeamId, 1);
    }

    private void OnObjectiveShutdown(EntityUid uid, WH40KObjectiveComponent component, ComponentShutdown args)
    {
        UnregisterObjective(uid, component);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _objectiveTeams.Clear();
        _teamObjectiveTotals.Clear();
        _teamObjectiveRemaining.Clear();
    }

    private void UnregisterObjective(EntityUid uid, WH40KObjectiveComponent component)
    {
        if (!_objectiveTeams.Remove(uid, out var teamId))
            teamId = component.TeamId;

        if (string.IsNullOrEmpty(teamId))
            return;

        AdjustTeamCount(_teamObjectiveTotals, teamId, -1);
        if (!component.Destroyed)
            AdjustTeamCount(_teamObjectiveRemaining, teamId, -1);
    }

    private void DecrementRemainingObjective(string teamId)
    {
        if (string.IsNullOrEmpty(teamId))
            return;

        AdjustTeamCount(_teamObjectiveRemaining, teamId, -1);
    }

    private static void AdjustTeamCount(Dictionary<string, int> dictionary, string teamId, int delta)
    {
        if (string.IsNullOrEmpty(teamId) || delta == 0)
            return;

        var next = dictionary.GetValueOrDefault(teamId, 0) + delta;
        if (next <= 0)
            dictionary.Remove(teamId);
        else
            dictionary[teamId] = next;
    }
}
