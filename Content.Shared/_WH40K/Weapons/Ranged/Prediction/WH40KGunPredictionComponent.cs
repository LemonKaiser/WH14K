using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Weapons.Ranged.Prediction;

[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KGunPredictionComponent : Component
{
    [DataField]
    public bool Enabled = true;
}
