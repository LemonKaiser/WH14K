using Content.Shared._WH40K.StrategicPoints;
using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Shared._WH40K.StrategicPoints.Construction;

[DataDefinition]
public sealed partial class WH40KStrategicPointAnchorCondition : IConstructionCondition
{
    [DataField("pointType", required: true)]
    public WH40KStrategicPointType PointType;

    [DataField("maxDistance")]
    public float MaxDistance = 0.75f;

    [DataField("requireFree")]
    public bool RequireFree = true;

    public ConstructionGuideEntry? GenerateGuideEntry()
    {
        return null;
    }

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entityManager = IoCManager.Resolve<IEntityManager>();
        var transform = entityManager.System<SharedTransformSystem>();
        var target = transform.ToMapCoordinates(location);
        var maxDistanceSquared = MaxDistance * MaxDistance;

        var query = entityManager.EntityQueryEnumerator<WH40KStrategicPointAnchorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var anchor, out var xform))
        {
            if (anchor.PointType != PointType)
                continue;

            if (RequireFree && anchor.BuiltPoint is { } built && entityManager.EntityExists(built))
                continue;

            var anchorCoords = transform.GetMapCoordinates(uid, xform: xform);
            if (anchorCoords.MapId != target.MapId)
                continue;

            var effectiveAnchorPosition = anchorCoords.Position + anchor.BuiltOffset;
            if ((effectiveAnchorPosition - target.Position).LengthSquared() <= maxDistanceSquared)
                return true;
        }

        return false;
    }
}
