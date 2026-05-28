using System;
using System.Numerics;
using Content.Server.Body.Systems;
using Content.Server.KillTracking;
using Content.Server._WH40K.MetaProgress;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Reflect;
using Content.Shared._WH40K.Psyker;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Psyker;

public sealed partial class WH40KChaosKhorneChosenAbilitySystem : EntitySystem
{
    private const string KhorneBladeAction = "ActionWH40KChaosKhorneRepulse";
    private const string KhorneBloodHealAction = "ActionWH40KChaosKhorneExecutionStep";

    private static readonly EntProtoId KhorneBladePrototype = "WH40KChaosKhorneStealerBlade";
    private static readonly EntProtoId KhorneBladeExPrototype = "WH40KChaosKhorneStealerBladeEx";

    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> SlashDamageType = "Slash";
    private static readonly ProtoId<DamageTypePrototype> PiercingDamageType = "Piercing";
    private static readonly ProtoId<DamageTypePrototype> HeatDamageType = "Heat";

    [Dependency] private  SharedActionsSystem _actions = default!;
    [Dependency] private  BloodstreamSystem _bloodstream = default!;
    [Dependency] private  DamageableSystem _damageable = default!;
    [Dependency] private  SharedHandsSystem _hands = default!;
    [Dependency] private  MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private  SharedPhysicsSystem _physics = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  IPrototypeManager _prototype = default!;
    [Dependency] private  IRobustRandom _random = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    private DamageTypePrototype _bluntDamage = default!;
    private DamageTypePrototype _slashDamage = default!;
    private DamageTypePrototype _piercingDamage = default!;
    private DamageTypePrototype _heatDamage = default!;

    private static readonly Angle ReflectSpread = Angle.FromDegrees(35);

    public override void Initialize()
    {
        _bluntDamage = _prototype.Index(BluntDamageType);
        _slashDamage = _prototype.Index(SlashDamageType);
        _piercingDamage = _prototype.Index(PiercingDamageType);
        _heatDamage = _prototype.Index(HeatDamageType);

        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, WH40KChaosKhorneBladeActionEvent>(OnBladeManifest);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, WH40KChaosKhorneBloodHealActionEvent>(OnBloodHeal);
        SubscribeLocalEvent<WH40KChaosKhorneChosenRuntimeComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, ProjectileReflectAttemptEvent>(OnProjectileReflectAttempt);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, HitScanReflectAttemptEvent>(OnHitScanReflectAttempt);
        SubscribeLocalEvent<GunComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<MeleeWeaponComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
        SubscribeLocalEvent<WH40KChaosKhorneChosenRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
        SubscribeLocalEvent<WH40KConfirmedEliminationEvent>(OnConfirmedElimination);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KChaosGiftProgressionComponent>();
        while (query.MoveNext(out var uid, out var progression))
        {
            if (progression.AttunedPatron != WH40KChaosPatron.Khorne)
                continue;

            var runtime = EnsureComp<WH40KChaosKhorneChosenRuntimeComponent>(uid);

            if (runtime.BladeUid is { } blade && runtime.BladeExpiresAt != TimeSpan.Zero && now >= runtime.BladeExpiresAt)
            {
                runtime.BladeUid = null;
                runtime.BladeExpiresAt = TimeSpan.Zero;

                if (!TerminatingOrDeleted(blade))
                    QueueDel(blade);
            }

            if (runtime.KillRushStacks > 0 && runtime.KillRushExpiresAt != TimeSpan.Zero && now >= runtime.KillRushExpiresAt)
            {
                runtime.KillRushStacks = 0;
                runtime.KillRushExpiresAt = TimeSpan.Zero;
                _movementSpeed.RefreshMovementSpeedModifiers(uid);
            }

            if (now < runtime.NextPassiveHealAt)
                continue;

            runtime.NextPassiveHealAt = now + TimeSpan.FromSeconds(1);
            TryApplyPassiveHealing(uid, progression);
        }
    }

    private void OnRuntimeShutdown(Entity<WH40KChaosKhorneChosenRuntimeComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.BladeUid is { } blade && !TerminatingOrDeleted(blade))
            QueueDel(blade);

        if (ent.Comp.KillRushStacks > 0)
            _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnBladeManifest(Entity<WH40KChaosGiftRoleComponent> ent, ref WH40KChaosKhorneBladeActionEvent args)
    {
        if (!TryGetChosenKhorneProgression(args.Performer, args.Action.Owner, KhorneBladeAction, out var progression))
            return;

        ApplyTieredCooldown(args.Performer, args.Action, 14f, progression.KhorneGiftOneCooldownTier);
        var giftOneExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 1);

        var runtime = EnsureComp<WH40KChaosKhorneChosenRuntimeComponent>(args.Performer);
        if (runtime.BladeUid is { } blade && !TerminatingOrDeleted(blade))
            QueueDel(blade);

        var spawned = Spawn(
            giftOneExUnlocked ? KhorneBladeExPrototype : KhorneBladePrototype,
            Transform(args.Performer).Coordinates);

        runtime.BladeUid = spawned;
        runtime.BladeExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(GetBladeDuration(progression.KhorneGiftOneUtilityTier, giftOneExUnlocked));

        if (HasComp<HandsComponent>(args.Performer))
            _hands.TryForcePickupAnyHand(args.Performer, spawned, checkActionBlocker: false);

        args.Handled = true;
    }

    private void OnBloodHeal(Entity<WH40KChaosGiftRoleComponent> ent, ref WH40KChaosKhorneBloodHealActionEvent args)
    {
        if (!TryGetChosenKhorneProgression(args.Performer, args.Action.Owner, KhorneBloodHealAction, out var progression))
            return;

        if (!TryComp<DamageableComponent>(args.Performer, out var damageable))
            return;

        ApplyTieredCooldown(args.Performer, args.Action, 16f, progression.KhorneGiftTwoCooldownTier);
        var giftTwoExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 2);

        var bloodFactor = 1f;
        if (TryComp<BloodstreamComponent>(args.Performer, out var bloodstream))
        {
            bloodFactor = MathF.Max(0.35f, _bloodstream.GetBloodLevel((args.Performer, bloodstream)));
            _bloodstream.TryModifyBloodLevel(
                (args.Performer, bloodstream),
                FixedPoint2.New(-GetBloodHealCost(progression.KhorneGiftTwoUtilityTier, giftTwoExUnlocked)));
        }

        var amount = FixedPoint2.New(GetBloodHealAmount(progression.KhorneGiftTwoPowerTier, giftTwoExUnlocked) * bloodFactor);
        _damageable.HealEvenly((args.Performer, damageable), -amount, origin: args.Performer, ignoreGlobalModifiers: true);

        args.Handled = true;
    }

    private void OnRefreshMovementSpeed(Entity<WH40KChaosKhorneChosenRuntimeComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryGetLeaderKhorneProgression(ent.Owner, out var progression))
            return;

        if (ent.Comp.KillRushStacks <= 0)
            return;

        var passiveExUnlocked = WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression);
        var bonus = GetKillRushMultiplier(ent.Comp.KillRushStacks, progression.KhornePassiveSpeedTier, passiveExUnlocked);
        args.ModifySpeed(bonus, bonus, MovementSpeedModifierLayer.Status);
    }

    private void OnProjectileReflectAttempt(Entity<WH40KChaosGiftRoleComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled || !TryGetLeaderKhorneProgression(ent.Owner, out var progression))
            return;

        if (!_random.Prob(GetReflectChance(WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression))))
            return;

        if (!TryComp<PhysicsComponent>(args.ProjUid, out var physics))
            return;

        var rotation = _random.NextAngle(-ReflectSpread / 2, ReflectSpread / 2).Opposite();
        var existingVelocity = _physics.GetMapLinearVelocity(args.ProjUid, component: physics);
        var relativeVelocity = existingVelocity - _physics.GetMapLinearVelocity(ent.Owner);
        var newVelocity = rotation.RotateVec(relativeVelocity);
        var difference = newVelocity - existingVelocity;

        _physics.SetLinearVelocity(args.ProjUid, physics.LinearVelocity + difference, body: physics);

        var locRot = Transform(args.ProjUid).LocalRotation;
        var newRot = rotation.RotateVec(locRot.ToVec());
        _transform.SetLocalRotation(args.ProjUid, newRot.ToAngle());

        args.Component.Shooter = ent.Owner;
        args.Component.Weapon = ent.Owner;
        Dirty(args.ProjUid, args.Component);
        args.Cancelled = true;
    }

    private void OnHitScanReflectAttempt(Entity<WH40KChaosGiftRoleComponent> ent, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected || !TryGetLeaderKhorneProgression(ent.Owner, out var progression))
            return;

        if (!_random.Prob(GetReflectChance(WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression))))
            return;

        var spread = _random.NextAngle(-ReflectSpread / 2, ReflectSpread / 2);
        args.Direction = -spread.RotateVec(args.Direction);
        args.Reflected = true;
    }

    private void OnAttemptShoot(Entity<GunComponent> ent, ref AttemptShootEvent args)
    {
        if (!TryGetLeaderKhorneProgression(args.User, out _))
            return;

        args.Cancelled = true;
        args.Message = Loc.GetString("w40k-ch-khorne-no-guns");
    }

    private void OnConfirmedElimination(WH40KConfirmedEliminationEvent ev)
    {
        if (ev.Suicide)
            return;

        var source = ev.Primary;
        if (!TryGetSessionById(source.PlayerId, out var killer))
            return;

        if (!TryGetLeaderKhorneProgression(killer, out var progression) || progression.KhornePassiveSpeedTier <= 0)
            return;

        var runtime = EnsureComp<WH40KChaosKhorneChosenRuntimeComponent>(killer);
        var previousStacks = runtime.KillRushStacks;
        var passiveExUnlocked = WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression);
        runtime.KillRushStacks = Math.Min(
            GetKillRushMaxStacks(progression.KhornePassiveSpeedTier, passiveExUnlocked),
            runtime.KillRushStacks + 1);
        runtime.KillRushExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(GetKillRushDuration(progression.KhornePassiveHealthTier, passiveExUnlocked));

        if (runtime.KillRushStacks != previousStacks)
            _movementSpeed.RefreshMovementSpeedModifiers(killer);
    }

    private void OnGetMeleeDamage(Entity<MeleeWeaponComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (!TryComp<WH40KChaosGiftProgressionComponent>(args.User, out var progression) || progression.AttunedPatron != WH40KChaosPatron.Khorne)
            return;

        var prototype = MetaData(ent.Owner).EntityPrototype?.ID;
        if (!string.Equals(prototype, KhorneBladePrototype, StringComparison.Ordinal) &&
            !string.Equals(prototype, KhorneBladeExPrototype, StringComparison.Ordinal))
        {
            return;
        }

        args.Damage *= GetBladeDamageMultiplier(
            progression.KhorneGiftOnePowerTier,
            WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 1));
    }

    private void TryApplyPassiveHealing(EntityUid uid, WH40KChaosGiftProgressionComponent progression)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return;

        var brute = GetPassiveBruteHeal(progression.KhornePassiveSpeedTier);
        var pierce = GetPassivePierceHeal(progression.KhornePassiveHealthTier);
        var burn = GetPassiveBurnHeal(progression.KhornePassiveMeleeTier);
        var passiveExUnlocked = WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression);

        if (passiveExUnlocked)
        {
            brute *= 1.35f;
            pierce *= 1.35f;
            burn *= 1.35f;
        }

        if (brute <= 0f && pierce <= 0f && burn <= 0f)
            return;

        var healing = new DamageSpecifier();
        if (brute > 0f)
        {
            var bruteHeal = FixedPoint2.New(-brute * 0.5f);
            healing.DamageDict[_bluntDamage.ID] = bruteHeal;
            healing.DamageDict[_slashDamage.ID] = bruteHeal;
        }

        if (pierce > 0f)
            healing.DamageDict[_piercingDamage.ID] = FixedPoint2.New(-pierce);

        if (burn > 0f)
            healing.DamageDict[_heatDamage.ID] = FixedPoint2.New(-burn);

        _damageable.TryChangeDamage((uid, damageable), healing, ignoreResistances: true, interruptsDoAfters: false, origin: uid);
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

    private bool TryGetChosenKhorneProgression(EntityUid performer, EntityUid actionUid, string expectedActionPrototype, out WH40KChaosGiftProgressionComponent progression)
    {
        progression = null!;

        if (!TryComp<WH40KChaosGiftProgressionComponent>(performer, out var found) || found == null)
            return false;

        if (found.AttunedPatron != WH40KChaosPatron.Khorne)
            return false;

        var actionPrototype = MetaData(actionUid).EntityPrototype?.ID;
        if (!string.Equals(actionPrototype, expectedActionPrototype, StringComparison.Ordinal))
            return false;

        progression = found;
        return true;
    }

    private bool TryGetLeaderKhorneProgression(EntityUid uid, out WH40KChaosGiftProgressionComponent progression)
    {
        progression = null!;

        if (!TryComp<WH40KChaosGiftProgressionComponent>(uid, out var found) || found == null)
            return false;

        if (found.AttunedPatron != WH40KChaosPatron.Khorne)
            return false;

        progression = found;
        return true;
    }

    private bool TryGetSessionById(NetUserId playerId, out EntityUid killer)
    {
        killer = default;

        var actorQuery = EntityQueryEnumerator<ActorComponent>();
        while (actorQuery.MoveNext(out var uid, out var actor))
        {
            if (actor.PlayerSession.UserId != playerId)
                continue;

            killer = uid;
            return true;
        }

        return false;
    }

    private static float GetBladeDamageMultiplier(byte tier, bool exUnlocked)
    {
        var multiplier = tier switch
        {
            1 => 1.15f,
            2 => 1.30f,
            3 => 1.50f,
            _ => 1.05f,
        };

        return exUnlocked ? multiplier * 1.12f : multiplier;
    }

    private static float GetBladeDuration(byte tier, bool exUnlocked)
    {
        var seconds = tier switch
        {
            1 => 11f,
            2 => 14f,
            3 => 17f,
            _ => 8f,
        };

        return exUnlocked ? seconds + 4f : seconds;
    }

    private static float GetBloodHealAmount(byte tier, bool exUnlocked)
    {
        var amount = tier switch
        {
            1 => 26f,
            2 => 34f,
            3 => 44f,
            _ => 20f,
        };

        return exUnlocked ? amount + 10f : amount;
    }

    private static float GetBloodHealCost(byte tier, bool exUnlocked)
    {
        var amount = tier switch
        {
            1 => 22f,
            2 => 18f,
            3 => 14f,
            _ => 26f,
        };

        return exUnlocked ? MathF.Max(6f, amount - 3f) : amount;
    }

    private static float GetPassiveBruteHeal(byte tier)
    {
        return tier switch
        {
            1 => 0.45f,
            2 => 0.7f,
            3 => 1f,
            _ => 0f,
        };
    }

    private static float GetPassivePierceHeal(byte tier)
    {
        return tier switch
        {
            1 => 0.25f,
            2 => 0.4f,
            3 => 0.65f,
            _ => 0f,
        };
    }

    private static float GetPassiveBurnHeal(byte tier)
    {
        return tier switch
        {
            1 => 0.2f,
            2 => 0.35f,
            3 => 0.5f,
            _ => 0f,
        };
    }

    private static int GetKillRushMaxStacks(byte tier, bool exUnlocked)
    {
        var value = tier switch
        {
            1 => 2,
            2 => 3,
            3 => 5,
            _ => 1,
        };

        return exUnlocked ? value + 1 : value;
    }

    private static float GetKillRushDuration(byte tier, bool exUnlocked)
    {
        var seconds = tier switch
        {
            1 => 12f,
            2 => 15f,
            3 => 18f,
            _ => 10f,
        };

        return exUnlocked ? seconds + 3f : seconds;
    }

    private static float GetKillRushMultiplier(int stacks, byte tier, bool exUnlocked)
    {
        if (stacks <= 0)
            return 1f;

        var perStack = tier switch
        {
            1 => 0.025f,
            2 => 0.035f,
            3 => 0.045f,
            _ => 0.02f,
        };

        if (exUnlocked)
            perStack += 0.005f;

        return 1f + stacks * perStack;
    }

    private static float GetReflectChance(bool exUnlocked)
    {
        return exUnlocked ? 0.55f : 0.5f;
    }
}
