using Robust.Shared.Map;

namespace Content.Shared._WH40K.Signals.Flare;

[RegisterComponent]
public sealed partial class WH40KSignalFlareTargetComponent : Component
{
    [DataField]
    public int Id;

    [DataField]
    public string TeamId = string.Empty;

    [DataField]
    public EntityUid Source = EntityUid.Invalid;

    [DataField]
    public EntityUid? Grid;

    [DataField]
    public Vector2i Tile;

    [DataField]
    public TimeSpan ExpiresAt;
}
