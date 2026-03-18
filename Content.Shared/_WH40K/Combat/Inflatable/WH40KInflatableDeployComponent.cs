using System;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Combat.Inflatable;

[RegisterComponent]
public sealed partial class WH40KInflatableDeployComponent : Component
{
    [DataField(required: true)]
    public EntProtoId DeployPrototype;

    [DataField]
    public bool IgnoreDistance;

    [DataField]
    public bool ConsumeOnDeploy = true;

    [DataField]
    public TimeSpan DeployDoAfter = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan ItemCooldown = TimeSpan.FromSeconds(1.2);

    [DataField]
    public string UseDelayId = "wh40k-inflatable-deploy";

    [DataField]
    public TimeSpan UserCooldown = TimeSpan.FromSeconds(0.6);

    [DataField]
    public TimeSpan DeployWindow = TimeSpan.FromSeconds(20);

    [DataField]
    public int MaxDeploysPerWindow = 8;

    [DataField]
    public int MaxActiveDeployables = 12;

    [DataField]
    public bool RequireTeam = true;

    [DataField]
    public List<string> AllowedTeamIds = new();
}
