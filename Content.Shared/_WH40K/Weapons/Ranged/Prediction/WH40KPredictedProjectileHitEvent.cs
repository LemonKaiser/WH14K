using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Weapons.Ranged.Prediction;

[Serializable, NetSerializable]
public sealed class WH40KPredictedProjectileHitEvent(NetEntity projectile, HashSet<(NetEntity Id, MapCoordinates Coordinates)> hit) : EntityEventArgs
{
    public readonly NetEntity Projectile = projectile;
    public readonly HashSet<(NetEntity Id, MapCoordinates Coordinates)> Hit = hit;
}
