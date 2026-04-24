using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server.Body.Systems;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared._WH40K.Psyker;
using Content.Server.Stunnable;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Psyker;

public sealed class WH40KChaosSlaaneshGiftAbilitySystem : EntitySystem
{
    private const string SlaaneshSwapAction = "ActionWH40KChaosSlaaneshSwap";
    private const string SlaaneshMasochismAction = "ActionWH40KChaosSlaaneshExquisiteTempo";
    private const string SlaaneshChoirAction = "ActionWH40KChaosSlaaneshMiasma";
    private const string SlaaneshArenaAction = "ActionWH40KChaosSlaaneshArena";
    private const string SlaaneshArenaWallPrototype = "WH40KWallForceSlaaneshArena";
    private const string TeamHeretics = "Heretics";
    private const string TeamImperium = "Imperium";

    private static readonly TimeSpan SlaaneshSwapDuration = TimeSpan.FromSeconds(5);
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> SlashDamageType = "Slash";
    private static readonly ProtoId<ReagentPrototype> StimulantsReagent = "Stimulants";
    private static readonly ProtoId<ReagentPrototype> OmnizineReagent = "Omnizine";
    private static readonly ProtoId<ReagentPrototype> TranexamicAcidReagent = "TranexamicAcid";

    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _nearby = new();
    private readonly List<SlaaneshPositionSwapState> _activePositionSwaps = new();

    private DamageTypePrototype _bluntDamage = default!;
    private DamageTypePrototype _slashDamage = default!;

    public override void Initialize()
    {
        _bluntDamage = _prototype.Index(BluntDamageType);
        _slashDamage = _prototype.Index(SlashDamageType);

        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, WH40KChaosSlaaneshSwapActionEvent>(OnSwap);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, WH40KChaosSlaaneshMasochismActionEvent>(OnMasochism);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, WH40KChaosSlaaneshStimAuraActionEvent>(OnStimAura);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, WH40KChaosSlaaneshArenaActionEvent>(OnArena);

        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<WH40KChaosGiftProgressionComponent, ModifySlowOnDamageSpeedEvent>(OnModifySlowOnDamage);
        SubscribeLocalEvent<WH40KChaosGiftProgressionComponent, ModifyStatusEffectDurationEvent>(OnModifyStatusEffectDuration);
        SubscribeLocalEvent<WH40KChaosGiftProgressionComponent, KnockDownAttemptEvent>(OnKnockdownAttempt);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        UpdateActivePositionSwaps(now);

        var query = EntityQueryEnumerator<WH40KChaosSlaaneshRuntimeComponent>();
        while (query.MoveNext(out var uid, out var runtime))
        {
            if (runtime.TempoExpiresAt == TimeSpan.Zero || now < runtime.TempoExpiresAt)
                continue;

            runtime.TempoExpiresAt = TimeSpan.Zero;
            runtime.TempoMultiplier = 1f;
            _movementSpeed.RefreshMovementSpeedModifiers(uid);
        }
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _activePositionSwaps.Clear();
    }

    private void OnSwap(Entity<WH40KChaosGiftRoleComponent> ent, ref WH40KChaosSlaaneshSwapActionEvent args)
    {
        if (!TryGetSlaaneshProgression(args.Performer, args.Action.Owner, SlaaneshSwapAction, out var progression))
            return;

        if (!IsValidSwapTarget(args.Performer, args.Target))
            return;

        if (IsInActivePositionSwap(args.Performer) || IsInActivePositionSwap(args.Target))
            return;

        var performerCoordinates = _transform.GetMapCoordinates(args.Performer);
        var targetCoordinates = _transform.GetMapCoordinates(args.Target);
        if (performerCoordinates.MapId != targetCoordinates.MapId)
            return;

        ApplyTieredCooldown(args.Performer, args.Action, 18f, progression.KhorneGiftOneCooldownTier);

        BreakPulls(args.Performer);
        BreakPulls(args.Target);

        _transform.SwapPositions(args.Performer, args.Target);
        _activePositionSwaps.Add(new SlaaneshPositionSwapState(
            args.Performer,
            args.Target,
            performerCoordinates,
            targetCoordinates,
            _timing.CurTime + SlaaneshSwapDuration));

        args.Handled = true;
    }

    private void OnMasochism(Entity<WH40KChaosGiftRoleComponent> ent, ref WH40KChaosSlaaneshMasochismActionEvent args)
    {
        if (!TryGetSlaaneshProgression(args.Performer, args.Action.Owner, SlaaneshMasochismAction, out var progression))
            return;

        if (!TryComp<DamageableComponent>(args.Performer, out var damageable))
            return;

        ApplyTieredCooldown(args.Performer, args.Action, 18f, progression.KhorneGiftThreeCooldownTier);
        var giftOneExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 1);

        var healAmount = FixedPoint2.New(GetMasochismHealAmount(progression.KhorneGiftOnePowerTier, giftOneExUnlocked));
        _damageable.HealEvenly((args.Performer, damageable), -healAmount, origin: args.Performer, ignoreGlobalModifiers: true);

        var selfDamage = new DamageSpecifier();
        var selfValue = FixedPoint2.New(GetMasochismSelfDamage(progression.KhorneGiftOneUtilityTier, giftOneExUnlocked));
        selfDamage.DamageDict[_bluntDamage.ID] = selfValue;
        selfDamage.DamageDict[_slashDamage.ID] = selfValue;
        _damageable.TryChangeDamage((args.Performer, damageable), selfDamage, ignoreResistances: true, interruptsDoAfters: false, origin: args.Performer);

        if (TryComp<BloodstreamComponent>(args.Performer, out var bloodstream))
        {
            _bloodstream.TryModifyBloodLevel(
                (args.Performer, bloodstream),
                FixedPoint2.New(-GetMasochismBloodCost(progression.KhorneGiftOneUtilityTier, giftOneExUnlocked)));
        }

        args.Handled = true;
    }

    private void OnStimAura(Entity<WH40KChaosGiftRoleComponent> ent, ref WH40KChaosSlaaneshStimAuraActionEvent args)
    {
        if (!TryGetSlaaneshProgression(args.Performer, args.Action.Owner, SlaaneshChoirAction, out var progression))
            return;

        ApplyTieredCooldown(args.Performer, args.Action, 26f, progression.KhorneGiftTwoCooldownTier);
        var giftTwoExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 2);

        var radius = GetStimAuraRadius(progression.KhorneGiftTwoUtilityTier, giftTwoExUnlocked);
        var stimulantAmount = FixedPoint2.New(GetStimAuraStimulants(progression.KhorneGiftTwoPowerTier, giftTwoExUnlocked));
        var omnizineAmount = FixedPoint2.New(GetStimAuraOmnizine(progression.KhorneGiftTwoPowerTier, giftTwoExUnlocked));
        var tranexAmount = FixedPoint2.New(GetStimAuraTranexamic(progression.KhorneGiftTwoPowerTier, giftTwoExUnlocked));

        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(args.Performer).Coordinates, radius, _nearby, LookupFlags.Dynamic | LookupFlags.Uncontained);
        _nearby.Add(args.Performer);

        foreach (var target in _nearby)
        {
            if (!IsSlaaneshFollower(target) ||
                !TryComp<BloodstreamComponent>(target, out var bloodstream) ||
                _mobState.IsDead(target))
            {
                continue;
            }

            var solution = new Solution();
            solution.AddReagent(StimulantsReagent, stimulantAmount);
            solution.AddReagent(OmnizineReagent, omnizineAmount);
            solution.AddReagent(TranexamicAcidReagent, tranexAmount);
            _bloodstream.TryAddToBloodstream((target, bloodstream), solution);
        }

        args.Handled = true;
    }

    private void OnArena(Entity<WH40KChaosGiftRoleComponent> ent, ref WH40KChaosSlaaneshArenaActionEvent args)
    {
        if (!TryGetSlaaneshProgression(args.Performer, args.Action.Owner, SlaaneshArenaAction, out var progression))
            return;

        if (args.Target == args.Performer || _mobState.IsDead(args.Target))
            return;

        ApplyTieredCooldown(args.Performer, args.Action, 95f, progression.KhorneGiftOneCooldownTier);
        var giftOneExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 1);

        var center = Transform(args.Performer).Coordinates;
        _transform.SetCoordinates(args.Target, center);
        _transform.AttachToGridOrMap(args.Target);

        var knockdown = TimeSpan.FromSeconds(giftOneExUnlocked ? 3f : 2.4f);
        var stun = TimeSpan.FromSeconds(giftOneExUnlocked ? 1.8f : 1.2f);

        _stun.TryKnockdown(args.Performer, knockdown, true, false, false, true);
        _stun.TryKnockdown(args.Target, knockdown, true, false, false, true);
        _stun.TryAddStunDuration(args.Performer, stun);
        _stun.TryAddStunDuration(args.Target, stun);

        SpawnArenaWalls(center);

        args.Handled = true;
    }

    private void OnRefreshMovementSpeed(Entity<WH40KChaosGiftRoleComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryComp<WH40KChaosGiftProgressionComponent>(ent.Owner, out var progression) ||
            progression.AttunedPatron != WH40KChaosPatron.Slaanesh)
            return;

        var passive = GetPassiveSpeedBonus(
            progression.KhornePassiveMeleeTier,
            WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression));
        var active = 1f;
        if (TryComp<WH40KChaosSlaaneshRuntimeComponent>(ent.Owner, out var runtime) && runtime.TempoExpiresAt > _timing.CurTime)
            active = runtime.TempoMultiplier;

        args.ModifySpeed(MathF.Max(0.1f, passive * active), MathF.Max(0.1f, passive * active), MovementSpeedModifierLayer.Status);
    }

    private void OnModifySlowOnDamage(Entity<WH40KChaosGiftProgressionComponent> ent, ref ModifySlowOnDamageSpeedEvent args)
    {
        if (ent.Comp.AttunedPatron != WH40KChaosPatron.Slaanesh)
            return;

        var reduction = GetPassiveSlowReduction(
            ent.Comp.KhornePassiveSpeedTier,
            WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(ent.Comp));
        if (reduction <= 0f)
            return;

        args.Speed += (1f - args.Speed) * reduction;
    }

    private void OnModifyStatusEffectDuration(Entity<WH40KChaosGiftProgressionComponent> ent, ref ModifyStatusEffectDurationEvent args)
    {
        if (ent.Comp.AttunedPatron != WH40KChaosPatron.Slaanesh ||
            !string.Equals(args.EffectProtoId, SharedStunSystem.StunId, StringComparison.Ordinal))
            return;

        args.Duration = TimeSpan.FromSeconds(args.Duration.TotalSeconds * GetPassiveStunMultiplier(
            ent.Comp.KhornePassiveHealthTier,
            WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(ent.Comp)));
    }

    private void OnKnockdownAttempt(Entity<WH40KChaosGiftProgressionComponent> ent, ref KnockDownAttemptEvent args)
    {
        if (ent.Comp.AttunedPatron != WH40KChaosPatron.Slaanesh || args.Time is not { } time)
            return;

        args.Time = TimeSpan.FromSeconds(time.TotalSeconds * GetPassiveStunMultiplier(
            ent.Comp.KhornePassiveHealthTier,
            WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(ent.Comp)));
    }

    private void ApplyTieredCooldown(EntityUid performer, Entity<ActionComponent> action, float baseSeconds, byte tier)
    {
        var duration = MathF.Max(0.1f, baseSeconds * WH40KChaosGiftUpgradeMath.CooldownMultiplier(tier));
        if (TryComp<WH40KChaosTzeentchAuraBuffComponent>(performer, out var tzeentchBuff) &&
            tzeentchBuff.CooldownExpiresAt > _timing.CurTime &&
            tzeentchBuff.CooldownMultiplier < 1f)
        {
            duration *= tzeentchBuff.CooldownMultiplier;
        }

        _actions.SetUseDelay((action.Owner, action.Comp), TimeSpan.FromSeconds(duration));
    }

    private bool TryGetSlaaneshProgression(EntityUid performer, EntityUid actionUid, string expectedActionPrototype, out WH40KChaosGiftProgressionComponent progression)
    {
        progression = null!;

        if (!TryComp<WH40KChaosGiftProgressionComponent>(performer, out var found) || found == null)
            return false;

        if (found.AttunedPatron != WH40KChaosPatron.Slaanesh)
            return false;

        var actionPrototype = MetaData(actionUid).EntityPrototype?.ID;
        if (!string.Equals(actionPrototype, expectedActionPrototype, StringComparison.Ordinal))
            return false;

        progression = found;
        return true;
    }

    private bool IsSlaaneshFollower(EntityUid uid)
    {
        return HasComp<WH40KChaosGiftRoleComponent>(uid) &&
               TryComp<WH40KChaosGiftProgressionComponent>(uid, out var progression) &&
               progression.AttunedPatron == WH40KChaosPatron.Slaanesh;
    }

    private bool IsValidSwapTarget(EntityUid performer, EntityUid target)
    {
        if (performer == target ||
            Deleted(performer) ||
            Deleted(target) ||
            !_mobState.IsAlive(performer) ||
            !_mobState.IsAlive(target))
        {
            return false;
        }

        if (!HasComp<ActorComponent>(target))
            return false;

        if (!_teamRule.TryGetTeamIdFromEntity(performer, out var performerTeam) ||
            !_teamRule.TryGetTeamIdFromEntity(target, out var targetTeam))
        {
            return false;
        }

        return string.Equals(performerTeam, TeamHeretics, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(targetTeam, TeamImperium, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInActivePositionSwap(EntityUid uid)
    {
        foreach (var swap in _activePositionSwaps)
        {
            if (swap.Performer == uid || swap.Target == uid)
                return true;
        }

        return false;
    }

    private void UpdateActivePositionSwaps(TimeSpan now)
    {
        for (var i = _activePositionSwaps.Count - 1; i >= 0; i--)
        {
            var swap = _activePositionSwaps[i];
            if (now < swap.ExpiresAt)
                continue;

            RestorePositionSwap(swap);
            _activePositionSwaps.RemoveAt(i);
        }
    }

    private void RestorePositionSwap(SlaaneshPositionSwapState swap)
    {
        RestorePosition(swap.Performer, swap.PerformerReturnCoordinates);
        RestorePosition(swap.Target, swap.TargetReturnCoordinates);
    }

    private void BreakPulls(EntityUid uid)
    {
        if (TryComp<PullableComponent>(uid, out var pullable) && _pulling.IsPulled(uid, pullable))
            _pulling.TryStopPull(uid, pullable);

        if (TryComp<PullerComponent>(uid, out var puller) &&
            TryComp<PullableComponent>(puller.Pulling, out var pulled))
        {
            _pulling.TryStopPull(puller.Pulling.Value, pulled);
        }
    }

    private void RestorePosition(EntityUid uid, MapCoordinates coordinates)
    {
        if (!uid.IsValid() || Deleted(uid))
            return;

        BreakPulls(uid);

        _transform.SetMapCoordinates(uid, coordinates);
        _transform.AttachToGridOrMap(uid);
    }

    private static float GetMasochismHealAmount(byte tier, bool exUnlocked)
    {
        var amount = tier switch
        {
            1 => 26f,
            2 => 36f,
            3 => 48f,
            _ => 18f,
        };

        return exUnlocked ? amount + 10f : amount;
    }

    private static float GetMasochismSelfDamage(byte tier, bool exUnlocked)
    {
        var amount = tier switch
        {
            1 => 8f,
            2 => 7f,
            3 => 6f,
            _ => 9f,
        };

        return exUnlocked ? MathF.Max(4f, amount - 1f) : amount;
    }

    private static float GetMasochismBloodCost(byte tier, bool exUnlocked)
    {
        var amount = tier switch
        {
            1 => 16f,
            2 => 12f,
            3 => 8f,
            _ => 20f,
        };

        return exUnlocked ? MathF.Max(4f, amount - 2f) : amount;
    }

    private static float GetStimAuraRadius(byte tier, bool exUnlocked)
    {
        var radius = tier switch
        {
            1 => 4.5f,
            2 => 5.5f,
            3 => 6.5f,
            _ => 4f,
        };

        return exUnlocked ? radius + 1f : radius;
    }

    private static float GetStimAuraStimulants(byte tier, bool exUnlocked)
    {
        var amount = tier switch
        {
            1 => 5f,
            2 => 7f,
            3 => 9f,
            _ => 4f,
        };

        return exUnlocked ? amount + 2f : amount;
    }

    private static float GetStimAuraOmnizine(byte tier, bool exUnlocked)
    {
        var amount = tier switch
        {
            1 => 2f,
            2 => 3f,
            3 => 4f,
            _ => 1f,
        };

        return exUnlocked ? amount + 1f : amount;
    }

    private static float GetStimAuraTranexamic(byte tier, bool exUnlocked)
    {
        var amount = tier switch
        {
            1 => 1f,
            2 => 2f,
            3 => 3f,
            _ => 1f,
        };

        return exUnlocked ? amount + 1f : amount;
    }

    private static float GetPassiveSlowReduction(byte tier, bool exUnlocked)
    {
        var reduction = tier switch
        {
            1 => 0.3f,
            2 => 0.5f,
            3 => 0.7f,
            _ => 0f,
        };

        return exUnlocked ? MathF.Min(0.8f, reduction + 0.1f) : reduction;
    }

    private static float GetPassiveStunMultiplier(byte tier, bool exUnlocked)
    {
        var multiplier = tier switch
        {
            1 => 0.85f,
            2 => 0.72f,
            3 => 0.60f,
            _ => 1f,
        };

        return exUnlocked ? MathF.Max(0.45f, multiplier - 0.08f) : multiplier;
    }

    private static float GetPassiveSpeedBonus(byte tier, bool exUnlocked)
    {
        var bonus = tier switch
        {
            1 => 1.03f,
            2 => 1.06f,
            3 => 1.10f,
            _ => 1f,
        };

        return exUnlocked ? bonus + 0.03f : bonus;
    }

    private void SpawnArenaWalls(EntityCoordinates center)
    {
        var sideWalls = new[]
        {
            (Offset: new Vector2(0f, 1f), Direction: Direction.South),
            (Offset: new Vector2(0f, -1f), Direction: Direction.North),
            (Offset: new Vector2(1f, 0f), Direction: Direction.West),
            (Offset: new Vector2(-1f, 0f), Direction: Direction.East),
        };

        foreach (var wall in sideWalls)
        {
            SpawnArenaWall(center.Offset(wall.Offset), wall.Direction);
        }

        var cornerWalls = new[]
        {
            (Offset: new Vector2(1f, 1f), First: Direction.South, Second: Direction.West),
            (Offset: new Vector2(1f, -1f), First: Direction.North, Second: Direction.West),
            (Offset: new Vector2(-1f, 1f), First: Direction.South, Second: Direction.East),
            (Offset: new Vector2(-1f, -1f), First: Direction.North, Second: Direction.East),
        };

        foreach (var corner in cornerWalls)
        {
            var cornerCoords = center.Offset(corner.Offset);
            SpawnArenaWall(cornerCoords, corner.First);
            SpawnArenaWall(cornerCoords, corner.Second);
        }
    }

    private void SpawnArenaWall(EntityCoordinates coordinates, Direction direction)
    {
        var wall = Spawn(SlaaneshArenaWallPrototype, coordinates);
        _transform.SetLocalRotation(wall, direction.ToAngle());
    }

    private readonly record struct SlaaneshPositionSwapState(
        EntityUid Performer,
        EntityUid Target,
        MapCoordinates PerformerReturnCoordinates,
        MapCoordinates TargetReturnCoordinates,
        TimeSpan ExpiresAt);
}
