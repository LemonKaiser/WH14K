using System.Numerics;
using Content.Shared._WH40K.Combat;
using Content.Server.EnergyDome;
using Content.Server.Administration.Logs;
using Content.Server.Destructible;
using Content.Server.Effects;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.EnergyDome;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server.Projectiles;

public sealed class ProjectileSystem : SharedProjectileSystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly ColorFlashEffectSystem _color = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly DestructibleSystem _destructibleSystem = default!;
    [Dependency] private readonly GunSystem _guns = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _sharedCameraRecoil = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    private EntityQuery<WH40KDirectionalBarricadeComponent> _barricadeQuery;
    private EntityQuery<EnergyDomeComponent> _domeQuery;

    public override void Initialize()
    {
        base.Initialize();
        _barricadeQuery = GetEntityQuery<WH40KDirectionalBarricadeComponent>();
        _domeQuery = GetEntityQuery<EnergyDomeComponent>();
        SubscribeLocalEvent<ProjectileComponent, StartCollideEvent>(OnStartCollide, before: [typeof(TriggerSystem)]);
    }

    private void OnStartCollide(EntityUid uid, ProjectileComponent component, ref StartCollideEvent args)
    {
        // This is so entities that shouldn't get a collision are ignored.
        if (args.OurFixtureId != ProjectileFixture || !args.OtherFixture.Hard
            || component.ProjectileSpent || component is { Weapon: null, OnlyCollideWhenShot: true })
            return;

        TryHandleProjectileHit(uid, component, args.OtherEntity, args.OurBody.LinearVelocity);
    }

    public bool TryProcessPredictedHit(EntityUid projectileUid, EntityUid targetUid)
    {
        if (!TryComp(projectileUid, out ProjectileComponent? component) ||
            !TryComp(projectileUid, out PhysicsComponent? physics) ||
            component.ProjectileSpent ||
            component is { Weapon: null, OnlyCollideWhenShot: true })
        {
            return false;
        }

        return TryHandleProjectileHit(projectileUid, component, targetUid, physics.LinearVelocity);
    }

    private bool TryHandleProjectileHit(EntityUid uid, ProjectileComponent component, EntityUid target, Vector2 projectileVelocity)
    {
        if (TryAllowDirectionalBarricadePass(target, component, projectileVelocity))
        {
            RememberTriggerPassThrough(uid, target);
            return false;
        }

        if (TryAllowEnergyDomeInteriorPass(target, component))
        {
            RememberTriggerPassThrough(uid, target);
            return false;
        }

        // it's here so this check is only done once before possible hit
        var attemptEv = new ProjectileReflectAttemptEvent(uid, component, false);
        RaiseLocalEvent(target, ref attemptEv);
        if (attemptEv.Cancelled)
        {
            SetShooter(uid, component, target);
            return false;
        }

        var ev = new ProjectileHitEvent(component.Damage * _damageableSystem.UniversalProjectileDamageModifier, target, component.Shooter);
        RaiseLocalEvent(uid, ref ev);

        var otherName = ToPrettyString(target);
        var damageRequired = _destructibleSystem.DestroyedAt(target);
        if (TryComp<DamageableComponent>(target, out var damageableComponent))
        {
            damageRequired -= _damageableSystem.GetTotalDamage((target, damageableComponent));
            damageRequired = FixedPoint2.Max(damageRequired, FixedPoint2.Zero);
        }
        var deleted = Deleted(target);

        if (_damageableSystem.TryChangeDamage((target, damageableComponent), ev.Damage, out var damage, component.IgnoreResistances, origin: component.Shooter) && Exists(component.Shooter))
        {
            if (!deleted)
            {
                _color.RaiseEffect(Color.Red, new List<EntityUid> { target }, Filter.Pvs(target, entityManager: EntityManager));
            }

            _adminLogger.Add(LogType.BulletHit,
                LogImpact.Medium,
                $"Projectile {ToPrettyString(uid):projectile} shot by {ToPrettyString(component.Shooter!.Value):user} hit {otherName:target} and dealt {damage:damage} damage");

            component.ProjectileSpent = !TryPenetrate((uid, component), damage, damageRequired);
        }
        else
        {
            component.ProjectileSpent = true;
        }

        if (!deleted)
        {
            _guns.PlayImpactSound(target, damage, component.SoundHit, component.ForceSound);

            if (!projectileVelocity.IsLengthZero())
                _sharedCameraRecoil.KickCamera(target, projectileVelocity.Normalized());
        }

        if (component.DeleteOnCollide && component.ProjectileSpent)
            QueueDel(uid);

        if (component.ImpactEffect != null && TryComp(uid, out TransformComponent? xform))
        {
            RaiseNetworkEvent(new ImpactEffectEvent(component.ImpactEffect, GetNetCoordinates(xform.Coordinates)), Filter.Pvs(xform.Coordinates, entityMan: EntityManager));
        }

        return true;
    }

    private void RememberTriggerPassThrough(EntityUid projectile, EntityUid target)
    {
        var bypass = EnsureComp<ProjectileTriggerBypassComponent>(projectile);
        bypass.PassThroughTargets.Add(target);
    }

    private bool TryAllowDirectionalBarricadePass(EntityUid target, ProjectileComponent projectile, Vector2 projectileVelocity)
    {
        if (!_barricadeQuery.TryGetComponent(target, out var barricadeComp))
            return false;

        var targetMap = _transform.GetMapCoordinates(target);
        Vector2? originPosition = projectile.ShotOrigin;

        if (originPosition == null)
        {
            if (projectile.Shooter is not { } shooter || Deleted(shooter))
                return false;

            var shooterMap = _transform.GetMapCoordinates(shooter);
            if (shooterMap.MapId != targetMap.MapId)
                return false;

            originPosition = shooterMap.Position;
        }

        var shotDirection = projectileVelocity;
        if (shotDirection.LengthSquared() <= 0.0001f)
            shotDirection = projectile.Angle.ToWorldVec();

        if (shotDirection.LengthSquared() <= 0.0001f)
            return false;

        var passDirection = _transform.GetWorldRotation(target).ToWorldVec();
        if (barricadeComp.FlipPassSide)
            passDirection = -passDirection;

        var barricadePos = _transform.GetWorldPosition(target);
        var originDirection = originPosition.Value - barricadePos;

        return WH40KDirectionalBarricadeHelpers.ShouldPassFromOrigin(
            passDirection,
            shotDirection,
            originDirection,
            barricadeComp.PassSideMaxDistance,
            barricadeComp.BlockedSidePassChance,
            barricadeComp.BlockedSidePointBlankPassDistance,
            _random);
    }

    private bool TryAllowEnergyDomeInteriorPass(EntityUid target, ProjectileComponent projectile)
    {
        if (!_domeQuery.HasComp(target) ||
            _barricadeQuery.HasComp(target) ||
            !TryComp<EnergyDomeVisualsComponent>(target, out var visuals))
        {
            return false;
        }

        var domeMap = _transform.GetMapCoordinates(target);
        if (domeMap.MapId == MapId.Nullspace)
            return false;

        Vector2? originPosition = projectile.ShotOrigin;
        if (originPosition == null)
        {
            if (projectile.Shooter is not { } shooter || Deleted(shooter))
                return false;

            var shooterMap = _transform.GetMapCoordinates(shooter);
            if (shooterMap.MapId != domeMap.MapId)
                return false;

            originPosition = shooterMap.Position;
        }

        const float minInteriorRadius = 0.15f;
        var radius = MathF.Max(visuals.InsideTransparencyRadius, minInteriorRadius);
        var originOffset = originPosition.Value - domeMap.Position;
        return originOffset.LengthSquared() <= radius * radius;
    }

    private bool TryPenetrate(Entity<ProjectileComponent> projectile, DamageSpecifier damage, FixedPoint2 damageRequired)
    {
        // If penetration is to be considered, we need to do some checks to see if the projectile should stop.
        if (projectile.Comp.PenetrationThreshold == 0)
            return false;

        // If a damage type is required, stop the bullet if the hit entity doesn't have that type.
        if (projectile.Comp.PenetrationDamageTypeRequirement != null)
        {
            foreach (var requiredDamageType in projectile.Comp.PenetrationDamageTypeRequirement)
            {
                if (damage.DamageDict.Keys.Contains(requiredDamageType))
                    continue;

                return false;
            }
        }

        // If the object won't be destroyed, it "tanks" the penetration hit.
        if (damage.GetTotal() < damageRequired)
        {
            return false;
        }

        if (!projectile.Comp.ProjectileSpent)
        {
            projectile.Comp.PenetrationAmount += damageRequired;
            // The projectile has dealt enough damage to be spent.
            if (projectile.Comp.PenetrationAmount >= projectile.Comp.PenetrationThreshold)
            {
                return false;
            }
        }

        return true;
    }
}
