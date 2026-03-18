using System.Collections.Generic;

namespace Content.Shared._WH40K.Fulton;

[RegisterComponent]
public sealed partial class WH40KTacticalFultonTargetComponent : Component
{
    [DataField]
    public bool Enabled = true;

    [DataField]
    public bool RequireTeam = false;

    [DataField]
    public List<string> AllowedTeamIds = new();

    [DataField]
    public bool AllowWhenAnchored = false;

    [DataField]
    public int FrontReward;

    [DataField]
    public int CommandReward;

    [DataField]
    public bool CompleteMissionCargoOnExtract = false;

    [DataField]
    public bool RemoveOnExtract = true;

    [DataField]
    public string Label = "wh40k-fulton-target-label-objective";
}
