using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.GunGame;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KGunGameLockedComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool BlockThrow = true;

    [DataField, AutoNetworkedField]
    public bool BlockInteractUsing;
}
