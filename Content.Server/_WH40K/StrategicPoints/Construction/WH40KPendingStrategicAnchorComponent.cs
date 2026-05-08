using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.StrategicPoints.Construction;

[RegisterComponent]
public sealed partial class WH40KPendingStrategicAnchorComponent : Component
{
    public EntityUid Anchor;
}
