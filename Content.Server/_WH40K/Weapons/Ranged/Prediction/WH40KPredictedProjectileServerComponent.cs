using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Weapons.Ranged.Prediction;

[RegisterComponent]
public sealed partial class WH40KPredictedProjectileServerComponent : Component
{
    public TimeSpan SpawnTime;
    public bool Consumed;
    public HashSet<EntityUid> AcceptedTargets = [];
}
