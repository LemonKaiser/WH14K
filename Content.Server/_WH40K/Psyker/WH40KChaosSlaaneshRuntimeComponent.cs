using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WH40K.Psyker;

[RegisterComponent]
public sealed partial class WH40KChaosSlaaneshRuntimeComponent : Component
{
    public float TempoMultiplier = 1f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan TempoExpiresAt;
}
