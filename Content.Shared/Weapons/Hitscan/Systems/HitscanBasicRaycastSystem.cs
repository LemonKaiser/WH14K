using System.Numerics;
using Content.Shared._WH40K.Combat;
using Content.Shared.Administration.Logs;
using Content.Shared.Damage.Components;
using Content.Shared.Database;
using Content.Shared.EnergyDome;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Hitscan.Systems;

public sealed partial class HitscanBasicRaycastSystem : EntitySystem
{
    [Dependency] private  SharedPhysicsSystem _physics = default!;
    [Dependency] private  SharedContainerSystem _container = default!;
    [Dependency] private  ISharedAdminLogManager _log = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;
    [Dependency] private  IRobustRandom _random = default!;
    private EntityQuery<HitscanBasicVisualsComponent> _visualsQuery = default!;
    private EntityQuery<WH40KDirectionalBarricadeComponent> _barricadeQuery = default!;
    private EntityQuery<EnergyDomeVisualsComponent> _domeVisualsQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        _visualsQuery = GetEntityQuery<HitscanBasicVisualsComponent>();
        _barricadeQuery = GetEntityQuery<WH40KDirectionalBarricadeComponent>();
        _domeVisualsQuery = GetEntityQuery<EnergyDomeVisualsComponent>();
        SubscribeLocalEvent<HitscanBasicRaycastComponent, HitscanTraceEvent>(OnHitscanFired);
    }

    private void OnHitscanFired(Entity<HitscanBasicRaycastComponent> ent, ref HitscanTraceEvent args)
    {
        var shooter = args.Shooter ?? args.Gun;
        var mapCords = _transform.ToMapCoordinates(args.FromCoordinates);
        var ray = new CollisionRay(mapCords.Position, args.ShotDirection, (int) ent.Comp.CollisionMask);
        var rayCastResults = _physics.IntersectRay(mapCords.MapId, ray, ent.Comp.MaxDistance, shooter, false);

        var target = args.Target;
        // If you are in a container, use the raycast result.
        // Otherwise:
        //  1.) Hit the first entity that you targeted.
        //  2.) Hit the first entity that doesn't require you to aim at it specifically to be hit.
        var useRawRaycast = _container.IsEntityOrParentInContainer(shooter);
        RayCastResults? result = null;

        foreach (var hit in rayCastResults)
        {
            if (!useRawRaycast &&
                hit.HitEntity != target &&
                CompOrNull<RequireProjectileTargetComponent>(hit.HitEntity)?.Active == true)
            {
                continue;
            }

            if (TryAllowDirectionalBarricadePass(hit, args.FromCoordinates, args.ShotDirection))
                continue;

            if (TryAllowEnergyDomeInteriorPass(hit, args.FromCoordinates))
                continue;

            result = hit;
            break;
        }

        var distanceTried = result?.Distance ?? ent.Comp.MaxDistance;

        // Do visuals without an event. They should always happen and putting it on the attempt event is weird!
        // If more stuff gets added here, it should probably be turned into an event.
        FireEffects(args.FromCoordinates, distanceTried, args.ShotDirection.ToAngle(), ent.Owner);

        // Admin logging
        if (result?.HitEntity != null)
        {
            _log.Add(LogType.HitScanHit,
                $"{ToPrettyString(shooter):user} hit {ToPrettyString(result.Value.HitEntity):target}"
                + $" using {ToPrettyString(args.Gun):entity}.");
        }

        var data = new HitscanRaycastFiredData
        {
            ShotDirection = args.ShotDirection,
            Gun = args.Gun,
            Shooter = args.Shooter,
            HitEntity = result?.HitEntity,
        };

        var attemptEvent = new AttemptHitscanRaycastFiredEvent { Data = data };
        RaiseLocalEvent(ent, ref attemptEvent);

        if (attemptEvent.Cancelled)
            return;

        var hitEvent = new HitscanRaycastFiredEvent { Data = data };
        RaiseLocalEvent(ent, ref hitEvent);
    }

    private bool TryAllowDirectionalBarricadePass(RayCastResults hit, EntityCoordinates fromCoordinates, Vector2 shotDirection)
    {
        if (!_barricadeQuery.TryGetComponent(hit.HitEntity, out var barricadeComp))
            return false;

        var fromMap = _transform.ToMapCoordinates(fromCoordinates);
        var passDirection = _transform.GetWorldRotation(hit.HitEntity).ToWorldVec();
        if (barricadeComp.FlipPassSide)
            passDirection = -passDirection;

        var barricadePos = _transform.GetWorldPosition(hit.HitEntity);
        var originDirection = fromMap.Position - barricadePos;

        return WH40KDirectionalBarricadeHelpers.ShouldPassFromOrigin(
            passDirection,
            shotDirection,
            originDirection,
            barricadeComp.PassSideMaxDistance,
            barricadeComp.BlockedSidePassChance,
            barricadeComp.BlockedSidePointBlankPassDistance,
            _random);
    }

    private bool TryAllowEnergyDomeInteriorPass(RayCastResults hit, EntityCoordinates fromCoordinates)
    {
        if (!_domeVisualsQuery.TryGetComponent(hit.HitEntity, out var visuals) ||
            _barricadeQuery.HasComp(hit.HitEntity))
        {
            return false;
        }

        var fromMap = _transform.ToMapCoordinates(fromCoordinates);
        var domePos = _transform.GetWorldPosition(hit.HitEntity);
        const float minInteriorRadius = 0.15f;
        var radius = MathF.Max(visuals.InsideTransparencyRadius, minInteriorRadius);
        return (fromMap.Position - domePos).LengthSquared() <= radius * radius;
    }

    /// <summary>
    /// Create visual effects for the fired hitscan weapon.
    /// </summary>
    /// <param name="fromCoordinates">Location to start the effect.</param>
    /// <param name="distance">Distance of the hitscan shot.</param>
    /// <param name="shotAngle">Angle of the shot.</param>
    /// <param name="hitscanUid">The hitscan entity itself.</param>
    private void FireEffects(EntityCoordinates fromCoordinates, float distance, Angle shotAngle, EntityUid hitscanUid)
    {
        if (distance == 0 || !_visualsQuery.TryComp(hitscanUid, out var vizComp))
            return;

        var sprites = new List<(NetCoordinates coordinates, Angle angle, SpriteSpecifier sprite, float scale)>();
        var fromXform = Transform(fromCoordinates.EntityId);

        // We'll get the effects relative to the grid / map of the firer
        // Look you could probably optimise this a bit with redundant transforms at this point.

        var gridUid = fromXform.GridUid;
        if (gridUid != fromCoordinates.EntityId && TryComp(gridUid, out TransformComponent? gridXform))
        {
            var (_, gridRot, gridInvMatrix) = _transform.GetWorldPositionRotationInvMatrix(gridXform);
            var map = _transform.ToMapCoordinates(fromCoordinates);
            fromCoordinates = new EntityCoordinates(gridUid.Value, Vector2.Transform(map.Position, gridInvMatrix));
            shotAngle -= gridRot;
        }
        else
        {
            shotAngle -= _transform.GetWorldRotation(fromXform);
        }

        if (distance >= 1f)
        {
            if (vizComp.MuzzleFlash != null)
            {
                var coords = fromCoordinates.Offset(shotAngle.ToVec().Normalized() / 2);
                var netCoords = GetNetCoordinates(coords);

                sprites.Add((netCoords, shotAngle, vizComp.MuzzleFlash, 1f));
            }

            if (vizComp.TravelFlash != null)
            {
                var coords = fromCoordinates.Offset(shotAngle.ToVec() * (distance + 0.5f) / 2);
                var netCoords = GetNetCoordinates(coords);

                sprites.Add((netCoords, shotAngle, vizComp.TravelFlash, distance - 1.5f));
            }
        }

        if (vizComp.ImpactFlash != null)
        {
            var coords = fromCoordinates.Offset(shotAngle.ToVec() * distance);
            var netCoords = GetNetCoordinates(coords);

            sprites.Add((netCoords, shotAngle.FlipPositive(), vizComp.ImpactFlash, 1f));
        }

        if (sprites.Count > 0)
        {
            RaiseNetworkEvent(new SharedGunSystem.HitscanEvent
            {
                Sprites = sprites,
            }, Filter.Pvs(fromCoordinates, entityMan: EntityManager));
        }
    }
}
