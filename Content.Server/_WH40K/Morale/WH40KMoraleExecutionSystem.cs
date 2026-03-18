using Content.Server._WH40K.Combat;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Morale.Components;
using Content.Server.Popups;
using Content.Shared.Alert;
using Content.Shared.CCVar;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.Morale;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Morale;

public sealed class WH40KMoraleExecutionSystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly WH40KAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;

    private static readonly ProtoId<AlertPrototype> MoraleBuffAlert = "WH40KMoraleBoosted";
    private readonly HashSet<EntityUid> _nearby = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KFriendlyFireAllowedComponent, ComponentStartup>(OnFriendlyFireAllowedStartup);
        SubscribeLocalEvent<WH40KMoraleExecutionComponent, ComponentStartup>(OnExecutionStartup);
        SubscribeLocalEvent<WH40KMoraleExecutionComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KMoraleExecutionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
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
            ShowReadyAlert(uid, execution);
            Dirty(uid, execution);
        }
    }

    private void OnMapInit(EntityUid uid, WH40KMoraleExecutionComponent component, MapInitEvent args)
    {
        InitializeExecutionState(uid, component);
    }

    private void OnExecutionStartup(EntityUid uid, WH40KMoraleExecutionComponent component, ComponentStartup args)
    {
        InitializeExecutionState(uid, component);
    }

    private void InitializeExecutionState(EntityUid uid, WH40KMoraleExecutionComponent component)
    {
        component.NextBlockedKillPopupTime = TimeSpan.Zero;

        var now = _timing.CurTime;
        if (component.NextUseTime > now)
        {
            component.CooldownShown = true;
            ShowCooldownAlert(uid, component, now);
            Dirty(uid, component);
            return;
        }

        component.CooldownShown = false;
        component.NextUseTime = TimeSpan.Zero;
        ShowReadyAlert(uid, component);
        Dirty(uid, component);
    }

    private void OnShutdown(EntityUid uid, WH40KMoraleExecutionComponent component, ComponentShutdown args)
    {
        _alerts.ClearAlert(uid, component.CooldownAlert);
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
            ShouldBlockFriendlyDamageDuringCooldown(attacker, uid, out var execution))
        {
            args.Cancelled = true;
            TryShowBlockedKillPopup(attacker, execution);
            return;
        }

        if (_config.GetCVar(CCVars.WH40KFriendlyFireDisabled) &&
            TryComp<WH40KMoraleExecutionComponent>(attacker, out var activeExecution) &&
            TryGetSameTeam(attacker, uid) &&
            !IsAllowedMoraleExecutionTarget(uid, activeExecution))
        {
            args.Cancelled = true;
            TryShowInvalidTargetPopup(attacker, activeExecution);
            return;
        }

        if (_config.GetCVar(CCVars.WH40KFriendlyFireDisabled) &&
            TryGetSameTeam(attacker, uid) &&
            !HasComp<WH40KFriendlyFireAllowedComponent>(attacker))
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

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.OldMobState == MobState.Dead || args.NewMobState != MobState.Dead || args.Origin == null)
            return;

        if (!_attackerResolver.TryResolveAttacker(args.Origin.Value, out var attacker, out _))
            attacker = args.Origin.Value;

        if (attacker == args.Target)
            return;

        if (!HasComp<WH40KFriendlyFireAllowedComponent>(attacker))
            return;

        if (!TryComp<WH40KMoraleExecutionComponent>(attacker, out var execution))
            return;

        var now = _timing.CurTime;
        if (now < execution.NextUseTime)
            return;

        if (!TryGetSameTeam(attacker, args.Target))
            return;

        if (!IsAllowedMoraleExecutionTarget(args.Target, execution))
            return;

        StartExecutionCooldown(attacker, execution, now);
        ApplyMoraleAura(attacker, execution, now);
    }

    private bool ShouldBlockFriendlyDamageDuringCooldown(
        EntityUid attacker,
        EntityUid victim,
        out WH40KMoraleExecutionComponent execution)
    {
        execution = null!;

        if (!HasComp<WH40KFriendlyFireAllowedComponent>(attacker))
            return false;

        if (!TryComp<WH40KMoraleExecutionComponent>(attacker, out var moraleExecution))
            return false;

        execution = moraleExecution!;

        if (_timing.CurTime >= execution.NextUseTime)
            return false;

        if (!TryGetSameTeam(attacker, victim))
            return false;

        return true;
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

    private bool IsAllowedMoraleExecutionTarget(EntityUid victim, WH40KMoraleExecutionComponent execution)
    {
        return HasComp<WH40KMoraleExecutionTargetComponent>(victim);
    }

    private void StartExecutionCooldown(EntityUid attacker, WH40KMoraleExecutionComponent execution, TimeSpan now)
    {
        var cooldown = TimeSpan.FromSeconds(Math.Max(1f, execution.CooldownSeconds));
        execution.NextUseTime = now + cooldown;
        execution.CooldownShown = true;
        Dirty(attacker, execution);
        ShowCooldownAlert(attacker, execution, now);
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

    private void ShowReadyAlert(EntityUid uid, WH40KMoraleExecutionComponent execution)
    {
        _alerts.ShowAlert(uid, execution.CooldownAlert, cooldown: null, autoRemove: false, showCooldown: false);
    }

    private void ShowCooldownAlert(EntityUid uid, WH40KMoraleExecutionComponent execution, TimeSpan start)
    {
        _alerts.ShowAlert(uid, execution.CooldownAlert, cooldown: (start, execution.NextUseTime), autoRemove: false, showCooldown: true);
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
        _popup.PopupEntity(Loc.GetString("wh40k-morale-execution-cooldown-blocked", ("seconds", seconds)), attacker, attacker);

        execution.NextBlockedKillPopupTime =
            now + TimeSpan.FromSeconds(Math.Max(0.1f, execution.BlockedKillPopupCooldownSeconds));
        Dirty(attacker, execution);
    }

    private void TryShowInvalidTargetPopup(EntityUid attacker, WH40KMoraleExecutionComponent execution)
    {
        var now = _timing.CurTime;
        if (now < execution.NextBlockedKillPopupTime)
            return;

        _popup.PopupEntity(Loc.GetString("wh40k-morale-execution-invalid-target"), attacker, attacker);

        execution.NextBlockedKillPopupTime =
            now + TimeSpan.FromSeconds(Math.Max(0.1f, execution.BlockedKillPopupCooldownSeconds));
        Dirty(attacker, execution);
    }
}
