using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WH40K.Vehicle.Combat;
using Content.Shared._WH40K.Vehicle.Fuel;
using Content.Shared._WH40K.Vehicle.Movement;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Stunnable;
using Content.Shared.Vehicle.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Vehicle.Combat;

public sealed class WH40KVehicleCombatSystem : EntitySystem
{
    private const int SolidImpactLayers = (int) (CollisionGroup.Impassable |
                                                 CollisionGroup.MidImpassable |
                                                 CollisionGroup.HighImpassable |
                                                 CollisionGroup.LowImpassable);

    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedStaminaSystem _stamina = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KVehicleRamComponent, StartCollideEvent>(OnRamStartCollide);
    }

    private void OnRamStartCollide(Entity<WH40KVehicleRamComponent> ent, ref StartCollideEvent args)
    {
        if (!args.OurFixture.Hard ||
            args.OurEntity != ent.Owner ||
            !TryComp(ent.Owner, out VehicleComponent? vehicle) ||
            vehicle.Operator == null)
        {
            return;
        }

        if (args.OtherEntity == ent.Owner || args.OtherEntity == vehicle.Operator.Value)
            return;

        if (TryComp(ent.Owner, out WH40KVehicleEngineComponent? engine) &&
            engine.State != WH40KVehicleEngineState.Running)
        {
            return;
        }

        if (TryComp(ent.Owner, out WH40KVehicleHandlingHealthComponent? handling) &&
            handling.ServiceState == WH40KVehicleServiceState.Disabled)
        {
            return;
        }

        var speed = args.OurBody.LinearVelocity.Length();
        TryComp(ent.Owner, out WH40KVehicleCarMovementComponent? movement);
        var minimumImpactSpeed = ent.Comp.GetMinimumImpactSpeed(movement);
        if (speed < minimumImpactSpeed)
            return;

        CleanupImpactCooldowns(ent.Comp);
        if (ent.Comp.RecentImpacts.TryGetValue(args.OtherEntity, out var blockedUntil) &&
            blockedUntil > _timing.CurTime)
        {
            return;
        }

        ent.Comp.RecentImpacts[args.OtherEntity] = _timing.CurTime + ent.Comp.ImpactCooldown;

        var scale = Math.Clamp(
            speed / Math.Max(minimumImpactSpeed, 0.01f),
            1f,
            Math.Max(1f, ent.Comp.MaxImpactScale));

        var direction = GetImpactDirection(ref args);
        var isSoftTarget = IsSoftTarget(args.OtherEntity);
        var isHardTarget = !isSoftTarget && IsHardImpactTarget(args.OtherEntity, ref args);

        if (!isSoftTarget && !isHardTarget)
            return;

        if (isSoftTarget)
        {
            if (!ent.Comp.SoftTargetDamage.Empty)
                _damageable.TryChangeDamage(args.OtherEntity, ent.Comp.SoftTargetDamage * scale, origin: ent.Owner);

            if (ent.Comp.StaminaDamage > 0f)
                _stamina.TakeStaminaDamage(args.OtherEntity, ent.Comp.StaminaDamage * scale, source: vehicle.Operator.Value, with: ent.Owner);

            if (ent.Comp.KnockdownTime > TimeSpan.Zero)
                _stun.TryKnockdown(args.OtherEntity, TimeSpan.FromSeconds(ent.Comp.KnockdownTime.TotalSeconds * MathF.Min(scale, 1.5f)), force: true);

            if (args.OtherBody.BodyType != Robust.Shared.Physics.BodyType.Static)
                _physics.ApplyLinearImpulse(args.OtherEntity, direction * args.OtherBody.Mass * ent.Comp.SoftTargetPushImpulse * scale, body: args.OtherBody);

            if (!ent.Comp.SelfSoftImpactDamage.Empty)
                _damageable.TryChangeDamage(ent.Owner, ent.Comp.SelfSoftImpactDamage * scale, origin: args.OtherEntity);

            DampenVelocity(ent.Owner, args.OurBody, ent.Comp.SoftImpactVelocityDampen);
        }
        else
        {
            if (HasComp<DamageableComponent>(args.OtherEntity) &&
                !ent.Comp.HardTargetDamage.Empty)
            {
                _damageable.TryChangeDamage(args.OtherEntity, ent.Comp.HardTargetDamage * scale, origin: ent.Owner);
            }

            if (!ent.Comp.SelfHardImpactDamage.Empty)
                _damageable.TryChangeDamage(ent.Owner, ent.Comp.SelfHardImpactDamage * scale, origin: args.OtherEntity);

            DampenVelocity(ent.Owner, args.OurBody, ent.Comp.HardImpactVelocityDampen);
        }

        if (ent.Comp.ImpactSound != null)
            _audio.PlayPvs(ent.Comp.ImpactSound, ent.Owner);
    }

    private void CleanupImpactCooldowns(WH40KVehicleRamComponent component)
    {
        if (component.RecentImpacts.Count == 0)
            return;

        var stale = new List<EntityUid>();
        foreach (var (target, until) in component.RecentImpacts)
        {
            if (until <= _timing.CurTime || Deleted(target))
                stale.Add(target);
        }

        foreach (var target in stale)
        {
            component.RecentImpacts.Remove(target);
        }
    }

    private Vector2 GetImpactDirection(ref StartCollideEvent args)
    {
        var velocity = args.OurBody.LinearVelocity;
        if (velocity.LengthSquared() > 0.0001f)
            return velocity.Normalized();

        if (args.WorldNormal.LengthSquared() > 0.0001f)
            return (-args.WorldNormal).Normalized();

        return -Vector2.UnitY;
    }

    private bool IsSoftTarget(EntityUid uid)
    {
        return TryComp(uid, out MobStateComponent? mobState) &&
               mobState.CurrentState is MobState.Alive or MobState.Critical;
    }

    private bool IsHardImpactTarget(EntityUid uid, ref StartCollideEvent args)
    {
        if (!args.OtherFixture.Hard)
            return false;

        if (HasComp<ItemComponent>(uid))
            return false;

        return (args.OtherFixture.CollisionLayer & SolidImpactLayers) != 0;
    }

    private void DampenVelocity(EntityUid uid, Robust.Shared.Physics.Components.PhysicsComponent body, float multiplier)
    {
        var clamped = Math.Clamp(multiplier, 0f, 1f);
        _physics.SetLinearVelocity(uid, body.LinearVelocity * clamped, body: body);
    }
}
