using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Vendors;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedCMAutomatedVendorSystem))]
public sealed partial class CMVendorUserComponent : Component
{
    [DataField, AutoNetworkedField]
    public Dictionary<string, int> Choices = new();

    [DataField, AutoNetworkedField]
    public Dictionary<string, bool> TakeAll = new();

    [DataField, AutoNetworkedField]
    public Dictionary<string, bool> TakeOne = new();

    [DataField, AutoNetworkedField]
    public int Points;
}
