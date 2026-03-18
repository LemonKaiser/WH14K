namespace Content.Shared._WH40K.Rangefinder;

[RegisterComponent]
public sealed partial class WH40KLaserDesignatorTargetComponent : Component
{
    [DataField]
    public int Id;

    [DataField(required: true)]
    public EntityUid Source;

    [DataField]
    public EntityUid? Grid;

    [DataField]
    public Vector2i Tile;

    [DataField]
    public TimeSpan ExpiresAt;
}
