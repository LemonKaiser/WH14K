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

    [DataField]
    public float SpeedVariance = 0.12f;

    [DataField]
    public float DisVariance = 0.08f;

    public float ResolvedPhaseOffset;
    public float ResolvedSpeedMultiplier = 1f;
    public float ResolvedDisMultiplier = 1f;
    public bool ResolvedWaveProfile;
}
