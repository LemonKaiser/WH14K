namespace Content.Shared._WH40K.Rangefinder;

[RegisterComponent]
public sealed partial class WH40KActiveLaserDesignatorComponent : Component
{
    [DataField]
    public int Id;

    [DataField]
    public EntityUid? Marker;

    [DataField]
    public EntityUid? Grid;

    [DataField]
    public Vector2i Tile;

    [DataField]
    public TimeSpan ExpiresAt;
}
