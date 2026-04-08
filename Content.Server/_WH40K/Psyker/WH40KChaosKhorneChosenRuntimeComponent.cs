using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WH40K.Psyker;

[RegisterComponent]
public sealed partial class WH40KChaosKhorneChosenRuntimeComponent : Component
{
    public EntityUid? BladeUid;

    public int KillRushStacks;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan BladeExpiresAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextPassiveHealAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan KillRushExpiresAt;
}
