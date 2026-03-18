using Robust.Shared.GameStates;
using Robust.Shared.Maths;

namespace Content.Shared._WH40K.Command;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KMissionObjectiveVisualComponent : Component
{
    [DataField("teamId"), AutoNetworkedField]
    public string TeamId = string.Empty;

    [DataField("label"), AutoNetworkedField]
    public string Label = string.Empty;

    [DataField("radius"), AutoNetworkedField]
    public float Radius = 4.5f;

    [DataField("color"), AutoNetworkedField]
    public Color Color = Color.FromHex("#FFD250");

    [DataField("pulse"), AutoNetworkedField]
    public bool Pulse = true;
}
