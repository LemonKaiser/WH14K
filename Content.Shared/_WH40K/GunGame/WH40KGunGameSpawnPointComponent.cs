using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.GunGame;

[RegisterComponent]
public sealed partial class WH40KGunGameSpawnPointComponent : Component
{
    [DataField]
    public int Priority;
}
