using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.PropHunt;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WH40KPropHuntInvisibleComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active;
}
