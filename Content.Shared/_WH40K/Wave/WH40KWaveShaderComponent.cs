namespace Content.Shared._WH40K.Wave;

[RegisterComponent]
public sealed partial class WH40KWaveShaderComponent : Component
{
    [DataField]
    public float Speed = 10f;

    [DataField]
    public float Dis = 10f;

    [DataField]
    public float Offset = 0f;
}
