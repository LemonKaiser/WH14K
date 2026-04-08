using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WH40K.Psyker;

[RegisterComponent]
public sealed partial class WH40KChaosTzeentchAuraBuffComponent : Component
{
    public float SpeedMultiplier = 1f;
    public float CooldownMultiplier = 1f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan SpeedExpiresAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan VisionExpiresAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan CooldownExpiresAt;

    public bool EyeBaselineCaptured;
    public bool BaselineDrawFov = true;
    public bool BaselineDrawLight = true;
}
