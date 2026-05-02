namespace Content.Shared._WH40K.Cinematic;

[RegisterComponent]
public sealed partial class WH40KCinematicCameraPointComponent : Component
{
    [DataField("pointId", required: true)]
    public string PointId = string.Empty;

    [DataField("zoom")]
    public float Zoom = 1f;

    [DataField("rotation")]
    public float RotationDegrees;
}
