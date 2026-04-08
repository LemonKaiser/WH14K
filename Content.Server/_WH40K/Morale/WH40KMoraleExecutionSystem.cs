using Content.Server._WH40K.Combat;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.Morale.Components;
using Content.Server.Popups;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.CCVar;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.Morale;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Actions.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Morale;

public sealed class WH40KMoraleExecutionSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly WH40KPlayerCultureTracker _culture = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly WH40KAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;

    private static readonly ProtoId<AlertPrototype> MoraleBuffAlert = "WH40KMoraleBoosted";
    private static readonly ProtoId<DamageTypePrototype> MoraleExecutionDamageType = "Piercing";
    private static readonly FixedPoint2 MoraleExecutionDamage = FixedPoint2.New(200);
    private readonly HashSet<EntityUid> _nearby = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KFriendlyFireAllowedComponent, ComponentStartup>(OnFriendlyFireAllowedStartup);
        SubscribeLocalEvent<WH40KMoraleExecutionComponent, ComponentStartup>(OnExecutionStartup);
        SubscribeLocalEvent<WH40KMoraleExecutionComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KMoraleExecutionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KMoraleExecutionComponent, WH40KMoraleExecutionActionEvent>(OnMoraleExecutionAction);
        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
    }

    private void OnFriendlyFireAllowedStartup(
        EntityUid uid,
        WH40KFriendlyFireAllowedComponent component,
        ComponentStartup args)
    {
        if (!HasComp<WH40KMoraleExecutionComponent>(uid))
            AddComp<WH40KMoraleExecutionComponent>(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var buffs = EntityQueryEnumerator<WH40KMoraleBoostedComponent>();
        while (buffs.MoveNext(out var uid, out var buff))
        {
            if (now < buff.ExpiresAt)
                continue;

            _alerts.ClearAlert(uid, MoraleBuffAlert);
            RemComp<WH40KMoraleBoostedComponent>(uid);
            _movement.RefreshMovementSpeedModifiers(uid);
        }

        var executions = EntityQueryEnumerator<WH40KMoraleExecutionComponent>();
        while (executions.MoveNext(out var uid, out var execution))
        {
            if (!execution.CooldownShown || now < execution.NextUseTime)
                continue;

            execution.CooldownShown = false;
            execution.NextUseTime = TimeSpan.Zero;
            _actions.RemoveCooldown(execution.ActionEntity);
            Dirty(uid, execution);
        }
    }

    private void OnMapInit(EntityUid uid, WH40KMoraleExecutionComponent component, MapInitEvent args)
    {
        EnsureExecutionAction(uid, component);
        CleanupDuplicateExecutionActions(uid, component);
        InitializeExecutionState(uid, component);
    }

    private void OnExecutionStartup(EntityUid uid, WH40KMoraleExecutionComponent component, ComponentStartup args)
    {
        EnsureExecutionAction(uid, component);
        CleanupDuplicateExecutionActions(uid, component);
        InitializeExecutionState(uid, component);
    }

    private void InitializeExecutionState(EntityUid uid, WH40KMoraleExecutionComponent component)
    {
        // We use the action cooldown UI for morale execution. Keep alert bar clean.
        _alerts.ClearAlert(uid, component.CooldownAlert);
        component.NextBlockedKillPopupTime = TimeSpan.Zero;
        var cooldown = TimeSpan.FromSeconds(Math.Max(1f, component.CooldownSeconds));
        _actions.SetUseDelay(component.ActionEntity, cooldown);

        var now = _timing.CurTime;
        if (component.NextUseTime > now)
        {
            component.CooldownShown = true;
            _actions.SetCooldown(component.ActionEntity, now, component.NextUseTime);
            Dirty(uid, component);
            return;
        }

        component.CooldownShown = false;
        component.NextUseTime = TimeSpan.Zero;
        _actions.RemoveCooldown(component.ActionEntity);
        Dirty(uid, component);
    }

    private void OnShutdown(EntityUid uid, WH40KMoraleExecutionComponent component, ComponentShutdown args)
    {
        _alerts.ClearAlert(uid, component.CooldownAlert);
        _actions.RemoveAction(uid, component.ActionEntity);
        component.ActionEntity = null;
    }

    private void OnMoraleExecutionAction(Entity<WH40KMoraleExecutionComponent> ent, ref WH40KMoraleExecutionActionEvent args)
    {
        if (args.Handled)
            return;

        if (args.Performer != ent.Owner)
            return;

        var now = _timing.CurTime;
        if (now < ent.Comp.NextUseTime)
        {
            TryShowBlockedKillPopup(ent.Owner, ent.Comp);
            return;
        }

        if (!TryGetSameTeam(ent.Owner, args.Target) ||
            !IsAllowedMoraleExecutionTarget(args.Target) ||
            !IsWithinExecutionRange(ent.Owner, args.Target, ent.Comp))
        {
            TryShowInvalidTargetPopup(ent.Owner, ent.Comp);
            return;
        }

        if (!TryComp<MobStateComponent>(args.Target, out var targetMobState) ||
            targetMobState.CurrentState == MobState.Dead)
        {
            TryShowInvalidTargetPopup(ent.Owner, ent.Comp);
            return;
        }

        if (!TryPerformExecutionWeaponAction(ent.Owner, args.Target))
        {
            TryShowInvalidTargetPopup(ent.Owner, ent.Comp);
            return;
        }

        TryApplyMoraleExecutionDamage(args.Target);

        // Keep historical behavior: target should always die on successful morale execution.
        if (targetMobState.CurrentState != MobState.Dead)
            _mobState.ChangeMobState(args.Target, MobState.Dead, targetMobState, ent.Owner);

        if (targetMobState.CurrentState != MobState.Dead)
            return;

        StartExecutionCooldown(ent.Owner, ent.Comp, now);
        ApplyMoraleAura(ent.Owner, ent.Comp, now);
        args.Handled = true;
    }

    private void OnBeforeDamageChanged(EntityUid uid, DamageableComponent component, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || args.Origin == null)
            return;

        if (!_attackerResolver.TryResolveAttacker(args.Origin.Value, out var attacker, out _))
            attacker = args.Origin.Value;

        if (attacker == uid)
            return;

        // Friendly-fire restrictions and morale execution should affect only harmful damage,
        // not healing (negative deltas from meds/topicals).
        if (!HasHarmfulDamage(args.Damage))
            return;

        var modifiedDamage = args.Damage;

        if (TryComp<WH40KMoraleBoostedComponent>(attacker, out var attackerBuff))
        {
            var outgoing = Math.Max(0.01f, attackerBuff.OutgoingDamageMultiplier);
            if (Math.Abs(outgoing - 1f) > 0.0001f)
                modifiedDamage = modifiedDamage * outgoing;
        }

        if (TryComp<WH40KTeamEventEffectComponent>(attacker, out var attackerTeamEventBuff))
        {
            var outgoing = Math.Max(0.01f, attackerTeamEventBuff.OutgoingDamageMultiplier);
            if (Math.Abs(outgoing - 1f) > 0.0001f)
                modifiedDamage = modifiedDamage * outgoing;
        }

        if (TryComp<WH40KMoraleBoostedComponent>(uid, out var defenderBuff))
        {
            var incoming = Math.Max(0.01f, defenderBuff.IncomingDamageMultiplier);
            if (Math.Abs(incoming - 1f) > 0.0001f)
                modifiedDamage = modifiedDamage * incoming;
        }

        if (TryComp<WH40KTeamEventEffectComponent>(uid, out var defenderTeamEventBuff))
        {
            var incoming = Math.Max(0.01f, defenderTeamEventBuff.IncomingDamageMultiplier);
            if (Math.Abs(incoming - 1f) > 0.0001f)
                modifiedDamage = modifiedDamage * incoming;
        }

        if (_config.GetCVar(CCVars.WH40KFriendlyFireDisabled) &&
            TryGetSameTeam(attacker, uid))
        {
            args.Cancelled = true;
            return;
        }

        args.Damage = modifiedDamage;
    }

    private static bool HasHarmfulDamage(DamageSpecifier damage)
    {
        foreach (var value in damage.DamageDict.Values)
        {
            if (value > 0)
                return true;
        }

        return false;
    }

    private bool TryGetSameTeam(EntityUid attacker, EntityUid victim)
    {
        if (!_teamRule.TryGetTeamIdFromEntity(attacker, out var attackerTeam) ||
            !_teamRule.TryGetTeamIdFromEntity(victim, out var victimTeam))
        {
            return false;
        }

        return attackerTeam == victimTeam;
    }

    private bool IsAllowedMoraleExecutionTarget(EntityUid victim)
    {
        return HasComp<WH40KMoraleExecutionTargetComponent>(victim);
    }

    private bool IsWithinExecutionRange(EntityUid attacker, EntityUid victim, WH40KMoraleExecutionComponent execution)
    {
        var attackerCoords = _transform.GetMapCoordinates(attacker);
        var victimCoords = _transform.GetMapCoordinates(victim);

        if (attackerCoords.MapId != victimCoords.MapId)
            return false;

        var range = Math.Max(0.5f, execution.ExecutionRange);
        return (victimCoords.Position - attackerCoords.Position).LengthSquared() <= range * range;
    }

    private void EnsureExecutionAction(EntityUid uid, WH40KMoraleExecutionComponent component)
    {
        _actions.AddAction(uid, ref component.ActionEntity, component.ActionPrototype, uid);
        var cooldown = TimeSpan.FromSeconds(Math.Max(1f, component.CooldownSeconds));
        _actions.SetUseDelay(component.ActionEntity, cooldown);
    }

    private void CleanupDuplicateExecutionActions(EntityUid uid, WH40KMoraleExecutionComponent component)
    {
        if (!TryComp<ActionsComponent>(uid, out var actions))
            return;

        EntityUid? primary = null;
        var duplicates = new List<EntityUid>();

        foreach (var actionUid in actions.Actions)
        {
            if (!TryComp(actionUid, out MetaDataComponent? meta) ||
                meta.EntityPrototype is not { ID: { } prototypeId } ||
                prototypeId != component.ActionPrototype.Id)
            {
                continue;
            }

            if (primary == null)
            {
                primary = actionUid;
                continue;
            }

            duplicates.Add(actionUid);
        }

        foreach (var duplicate in duplicates)
        {
            _actions.RemoveAction(uid, duplicate);
        }

        if (primary != null)
            component.ActionEntity = primary;
    }

    private bool TryPerformExecutionWeaponAction(EntityUid attacker, EntityUid target)
    {
        if (!_hands.TryGetActiveItem(attacker, out var activeItem))
            return false;

        var weapon = activeItem.Value;
        if (TryComp<GunComponent>(weapon, out var gun))
        {
            var targetCoords = Transform(target).Coordinates;
            return _gun.AttemptShoot(attacker, (weapon, gun), targetCoords, target);
        }

        if (!TryComp<MeleeWeaponComponent>(weapon, out var melee))
            return false;

        return _melee.AttemptLightAttack(attacker, weapon, melee, target);
    }

    private void TryApplyMoraleExecutionDamage(EntityUid target)
    {
        if (!TryComp<DamageableComponent>(target, out var damageable))
            return;

        var piercing = _prototypeManager.Index(MoraleExecutionDamageType);
        var damage = new DamageSpecifier(piercing, MoraleExecutionDamage);

        // Morale execution must go through even when team damage is disabled.
        _damageable.TryChangeDamage((target, damageable), damage, ignoreResistances: true, origin: null, ignoreGlobalModifiers: true);
    }

    private void StartExecutionCooldown(EntityUid attacker, WH40KMoraleExecutionComponent execution, TimeSpan now)
    {
        var cooldown = TimeSpan.FromSeconds(Math.Max(1f, execution.CooldownSeconds));
        execution.NextUseTime = now + cooldown;
        execution.CooldownShown = true;
        _actions.SetUseDelay(execution.ActionEntity, cooldown);
        Dirty(attacker, execution);
    }

    private void ApplyMoraleAura(EntityUid attacker, WH40KMoraleExecutionComponent execution, TimeSpan now)
    {
        if (!_teamRule.TryGetTeamIdFromEntity(attacker, out var teamId))
            return;

        var xformQuery = GetEntityQuery<TransformComponent>();
        if (!xformQuery.TryGetComponent(attacker, out var attackerXform))
            return;

        _nearby.Clear();

        var radius = Math.Max(0.5f, execution.AuraRadius);
        _lookup.GetEntitiesInRange(attackerXform.Coordinates, radius, _nearby, LookupFlags.Dynamic | LookupFlags.Approximate);

        var center = _transform.GetWorldPosition(attackerXform, xformQuery);
        var radiusSquared = radius * radius;

        foreach (var uid in _nearby)
        {
            if (uid == attacker)
                continue;

            if (!xformQuery.TryGetComponent(uid, out var xform) || xform.MapID != attackerXform.MapID)
                continue;

            var world = _transform.GetWorldPosition(xform, xformQuery);
            if ((world - center).LengthSquared() > radiusSquared)
                continue;

            if (!_teamRule.TryGetTeamIdFromEntity(uid, out var candidateTeam) || candidateTeam != teamId)
                continue;

            if (!_mobState.IsAlive(uid) && !_mobState.IsCritical(uid))
                continue;

            ApplyOrRefreshBuff(uid, execution, now);
        }
    }

    private void ApplyOrRefreshBuff(EntityUid uid, WH40KMoraleExecutionComponent execution, TimeSpan now)
    {
        var buff = EnsureComp<WH40KMoraleBoostedComponent>(uid);
        var expiresAt = now + TimeSpan.FromSeconds(Math.Max(1f, execution.BuffDurationSeconds));

        buff.ExpiresAt = expiresAt > buff.ExpiresAt ? expiresAt : buff.ExpiresAt;
        buff.SpeedMultiplier = Math.Max(1f, execution.SpeedMultiplier);
        buff.OutgoingDamageMultiplier = Math.Max(1f, execution.OutgoingDamageMultiplier);
        buff.IncomingDamageMultiplier = Math.Clamp(execution.IncomingDamageMultiplier, 0.01f, 1f);

        Dirty(uid, buff);
        ShowMoraleBuffAlert(uid, buff);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void ShowMoraleBuffAlert(EntityUid uid, WH40KMoraleBoostedComponent buff)
    {
        var now = _timing.CurTime;
        if (buff.ExpiresAt <= now)
        {
            _alerts.ShowAlert(uid, MoraleBuffAlert, cooldown: null, autoRemove: false, showCooldown: false);
            return;
        }

        _alerts.ShowAlert(uid, MoraleBuffAlert, cooldown: (now, buff.ExpiresAt), autoRemove: false, showCooldown: true);
    }

    private void TryShowBlockedKillPopup(EntityUid attacker, WH40KMoraleExecutionComponent execution)
    {
        var now = _timing.CurTime;
        if (now < execution.NextBlockedKillPopupTime)
            return;

        var remaining = execution.NextUseTime - now;
        var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        _popup.PopupEntity(_culture.GetPlayerString(attacker, "wh40k-morale-execution-cooldown-blocked", ("seconds", seconds)), attacker, attacker);

        execution.NextBlockedKillPopupTime =
            now + TimeSpan.FromSeconds(Math.Max(0.1f, execution.BlockedKillPopupCooldownSeconds));
        Dirty(attacker, execution);
    }

    private void TryShowInvalidTargetPopup(EntityUid attacker, WH40KMoraleExecutionComponent execution)
    {
        var now = _timing.CurTime;
        if (now < execution.NextBlockedKillPopupTime)
            return;

        _popup.PopupEntity(_culture.GetPlayerString(attacker, "wh40k-morale-execution-invalid-target"), attacker, attacker);

        execution.NextBlockedKillPopupTime =
            now + TimeSpan.FromSeconds(Math.Max(0.1f, execution.BlockedKillPopupCooldownSeconds));
        Dirty(attacker, execution);
    }
}
