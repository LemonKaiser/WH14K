using Content.Server._WH40K.StrategicPoints;
using Content.Shared._WH40K.StrategicPoints;
using Content.Shared.Construction;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.StrategicPoints.Construction;

[UsedImplicitly]
public sealed partial class WH40KBindStrategicPoint : IGraphAction
{
    [DataField("pointType", required: true)]
    public WH40KStrategicPointType PointType;

    [DataField("tier")]
    public WH40KStrategicPointTier Tier = WH40KStrategicPointTier.T1;

    [DataField("profile", required: true)]
    public ProtoId<WH40KStrategicPointProfilePrototype> Profile;

    [DataField("maxDistance")]
    public float MaxDistance = 0.75f;

    public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
    {
        var system = entityManager.System<WH40KStrategicPointSystem>();
        EntityUid? preferredAnchor = null;
        if (entityManager.TryGetComponent<WH40KPendingStrategicAnchorComponent>(uid, out var pending))
            preferredAnchor = pending.Anchor;

        system.TryBindConstructedPoint(uid, userUid, PointType, Tier, Profile, MaxDistance, preferredAnchor);

        if (entityManager.HasComponent<WH40KPendingStrategicAnchorComponent>(uid))
            entityManager.RemoveComponentDeferred<WH40KPendingStrategicAnchorComponent>(uid);
    }
}
