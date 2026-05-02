using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Cinematic;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KCinematicLockedComponent : Component
{
    [DataField, AutoNetworkedField]
    public int RunSerial;
}
