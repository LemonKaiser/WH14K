using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.Combat.PhantomStep;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KPhantomStepActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Charges;

    [DataField, AutoNetworkedField]
    public int MaxCharges = 1;

    [DataField, AutoNetworkedField]
    public TimeSpan RechargeDuration = TimeSpan.FromSeconds(20);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan NextRecharge = TimeSpan.Zero;
}
