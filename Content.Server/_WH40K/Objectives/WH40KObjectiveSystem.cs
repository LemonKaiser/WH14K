using System;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Objectives.Components;
using Content.Server.Chat.Managers;
using Content.Shared.Chat;
using Content.Shared._WH40K.Objectives;
using Content.Shared._WH40K.Overlays;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.Equipment.Components;
using Content.Shared.Mobs;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Trigger;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Systems;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.Objectives;

public sealed class WH40KObjectiveSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KObjectiveComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KObjectiveComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WH40KObjectiveComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<WH40KObjectiveComponent, TriggerEvent>(OnTrigger);
    }

    private void OnMapInit(EntityUid uid, WH40KObjectiveComponent component, MapInitEvent args)
    {
        if (!_teamRule.AreObjectivesEnabled())
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
    }

    private void OnBeforeDamageChanged(EntityUid uid, WH40KObjectiveComponent component, ref BeforeDamageChangedEvent args)
    {
        if (component.Destroying || component.Destroyed)
        {
            args.Cancelled = true;
            return;
        }

        if (args.Origin == null)
            return;

        if (!TryResolveAttacker(args.Origin.Value, out var attacker))
            return;

        if (!_teamRule.TryGetTeamIdFromEntity(attacker, out var attackerTeamId))
            return;

        if (attackerTeamId == component.TeamId)
            args.Cancelled = true;
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

        var totalDamage = damageable.TotalDamage;
        var remainingRatio = (maxHealth - totalDamage).Float() / maxHealth.Float();

        if (!component.LowHealthAnnounced &&
            remainingRatio <= component.WarnAtPercent &&
            CountObjectivesForTeam(component.TeamId, includeDestroyed: true) <= 1)
        {
            component.LowHealthAnnounced = true;
            var message = Loc.GetString("wh40k-objective-low-health",
                ("target", Loc.GetString(component.Name)));
            DispatchObjectiveAllyMessage(component.TeamId, message, Color.Red);
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

    private int CountRemainingObjectives(string teamId)
    {
        return CountObjectivesForTeam(teamId, includeDestroyed: false);
    }

    private int CountObjectivesForTeam(string teamId, bool includeDestroyed)
    {
        if (string.IsNullOrEmpty(teamId))
            return 0;

        var count = 0;
        var query = EntityQueryEnumerator<WH40KObjectiveComponent>();
        while (query.MoveNext(out _, out var objective))
        {
            if (objective.TeamId != teamId)
                continue;

            if (!includeDestroyed && objective.Destroyed)
                continue;

            count++;
        }

        return count;
    }

    private void DispatchObjectiveRemainingMessages(string teamId, string targetName, int remaining, string teamName)
    {
        var allyText = Loc.GetString("wh40k-objective-destroyed-remaining-allies",
            ("target", targetName),
            ("remaining", remaining));
        var enemyText = Loc.GetString("wh40k-objective-destroyed-remaining-enemies",
            ("target", targetName),
            ("remaining", remaining),
            ("team", teamName));

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
                _chat.DispatchServerMessage(player, enemyMessage);
                continue;
            }

            if (playerTeam == teamId)
                DispatchColoredServerMessage(player, allyMessage, Color.Red);
            else
                DispatchColoredServerMessage(player, enemyMessage, Color.LimeGreen);
        }
    }

    private void DispatchObjectiveAllyMessage(string teamId, string message, Color color)
    {
        foreach (var player in _players.Sessions)
        {
            if (player.AttachedEntity is not { } attached)
                continue;

            if (!_teamRule.TryGetTeamIdFromEntity(attached, out var playerTeam))
                continue;

            if (playerTeam == teamId)
                DispatchColoredServerMessage(player, message, color);
        }
    }

    private void DispatchColoredServerMessage(ICommonSession player, string message, Color color)
    {
        var wrappedMessage = Loc.GetString("chat-manager-server-wrap-message",
            ("message", FormattedMessage.EscapeText(message)));
        _chat.ChatMessageToOne(ChatChannel.Server, message, wrappedMessage, default, false, player.Channel, colorOverride: color);
    }


    private bool TryResolveAttacker(EntityUid origin, out EntityUid attacker)
    {
        return TryResolveAttacker(origin, out attacker, 0);
    }

    private bool TryResolveAttacker(EntityUid origin, out EntityUid attacker, int depth)
    {
        attacker = default;

        if (depth > 6)
            return false;

        if (HasComp<ActorComponent>(origin))
        {
            attacker = origin;
            return true;
        }

        if (TryResolveMechPilot(origin, out attacker))
            return true;

        if (TryComp(origin, out MechEquipmentComponent? mechEquipment) &&
            mechEquipment.EquipmentOwner is { } mechOwner &&
            TryResolveMechPilot(mechOwner, out attacker))
        {
            return true;
        }

        if (TryComp<ProjectileComponent>(origin, out var projectile) &&
            projectile.Shooter is { } shooter)
        {
            if (shooter == origin)
                return false;

            return TryResolveAttacker(shooter, out attacker, depth + 1);
        }

        if (TryComp<ThrownItemComponent>(origin, out var thrown) &&
            thrown.Thrower is { } thrower)
        {
            if (thrower == origin)
                return false;

            return TryResolveAttacker(thrower, out attacker, depth + 1);
        }

        if (TryComp<TimerTriggerComponent>(origin, out var timer) &&
            timer.User is { } timerUser)
        {
            if (timerUser == origin)
                return false;

            return TryResolveAttacker(timerUser, out attacker, depth + 1);
        }

        if (TryResolveAttackerFromContainer(origin, out attacker))
            return true;

        return false;
    }

    private bool TryResolveAttackerFromContainer(EntityUid origin, out EntityUid attacker)
    {
        attacker = default;

        var current = origin;
        for (var i = 0; i < 6; i++)
        {
            if (!_container.TryGetContainingContainer((current, null, null), out var container))
                return false;

            var owner = container.Owner;
            if (!owner.IsValid() || owner == current)
                return false;

            if (HasComp<ActorComponent>(owner))
            {
                attacker = owner;
                return true;
            }

            if (TryResolveMechPilot(owner, out attacker))
                return true;

            current = owner;
        }

        return false;
    }

    private bool TryResolveMechPilot(EntityUid mech, out EntityUid pilot)
    {
        pilot = default;

        if (!TryComp(mech, out MechComponent? mechComp))
            return false;

        if (mechComp.PilotSlot.ContainedEntity is not { } pilotEntity)
            return false;

        pilot = pilotEntity;
        return true;
    }
}
