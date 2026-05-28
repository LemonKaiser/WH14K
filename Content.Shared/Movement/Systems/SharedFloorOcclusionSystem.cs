using Content.Shared.Movement.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.Movement.Systems;

/// <summary>
/// Applies an occlusion shader for any relevant entities.
/// </summary>
public abstract partial class SharedFloorOcclusionSystem : EntitySystem
{
    [Dependency] private  SharedMapSystem _map = default!;
    [Dependency] private  SharedPhysicsSystem _physics = default!;
    [Dependency] private  EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FloorOccluderComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<FloorOccluderComponent, EndCollideEvent>(OnEndCollide);
    }

    private void OnStartCollide(Entity<FloorOccluderComponent> entity, ref StartCollideEvent args)
    {
        var other = args.OtherEntity;

        if (!TryComp<FloorOcclusionComponent>(other, out var occlusion) ||
            occlusion.Colliding.Contains(entity.Owner))
        {
            return;
        }

        occlusion.Colliding.Add(entity.Owner);
        Dirty(other, occlusion);
        SetEnabled((other, occlusion));
    }

    private void OnEndCollide(Entity<FloorOccluderComponent> entity, ref EndCollideEvent args)
    {
        var other = args.OtherEntity;

        if (!TryComp<FloorOcclusionComponent>(other, out var occlusion))
            return;

        if (!occlusion.Colliding.Remove(entity.Owner))
            return;

        Dirty(other, occlusion);
        SetEnabled((other, occlusion));
    }

    protected virtual void SetEnabled(Entity<FloorOcclusionComponent> entity)
    {

    }

    protected bool ShouldApplyOcclusion(Entity<FloorOcclusionComponent> entity)
    {
        if (entity.Comp.Colliding.Count == 0)
            return false;

        TryComp(entity.Owner, out PhysicsComponent? physics);

        foreach (var occluderUid in entity.Comp.Colliding)
        {
            if (TerminatingOrDeleted(occluderUid))
                continue;

            if (!TryComp<FloorOccluderComponent>(occluderUid, out var occluder))
                continue;

            if (occluder.RequireSameTile && !AreOnSameTile(entity.Owner, occluderUid))
                continue;

            if (IsIntersectingWhitelistedEntity(entity.Owner, physics, occluderUid, occluder.IgnoreWhenIntersectingWhitelist))
                continue;

            return true;
        }

        return false;
    }

    private bool IsIntersectingWhitelistedEntity(
        EntityUid uid,
        PhysicsComponent? physics,
        EntityUid ignoredEntity,
        EntityWhitelist? whitelist)
    {
        if (whitelist == null)
            return false;

        if (physics != null)
        {
            foreach (var contact in _physics.GetContactingEntities(uid, physics))
            {
                if (contact == ignoredEntity)
                    continue;

                if (_whitelist.IsWhitelistPass(whitelist, contact))
                    return true;
            }
        }

        var xform = Transform(uid);
        if (xform.GridUid == null || !TryComp<MapGridComponent>(xform.GridUid, out var grid))
            return false;

        var tile = _map.LocalToTile(xform.GridUid.Value, grid, xform.Coordinates);
        var anchored = _map.GetAnchoredEntitiesEnumerator(uid, grid, tile);
        while (anchored.MoveNext(out var ent))
        {
            if (ent == ignoredEntity || ent == uid)
                continue;

            if (_whitelist.IsWhitelistPass(whitelist, ent.Value))
                return true;
        }

        return false;
    }

    private bool AreOnSameTile(EntityUid first, EntityUid second)
    {
        var firstXform = Transform(first);
        var secondXform = Transform(second);

        if (firstXform.GridUid == null || firstXform.GridUid != secondXform.GridUid)
            return false;

        if (!TryComp<MapGridComponent>(firstXform.GridUid, out var grid))
            return false;

        var firstTile = _map.LocalToTile(firstXform.GridUid.Value, grid, firstXform.Coordinates);
        var secondTile = _map.LocalToTile(secondXform.GridUid.Value, grid, secondXform.Coordinates);
        return firstTile == secondTile;
    }
}
