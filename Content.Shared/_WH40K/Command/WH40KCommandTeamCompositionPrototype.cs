using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Command;

[Prototype("wh40kCommandTeamCompositionProfile")]
public sealed partial class WH40KCommandTeamCompositionProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("commandStaffing", required: true)]
    public List<WH40KCommandTeamCompositionStaffingRoleConfig> CommandStaffing = new();

    [DataField("officerRoles", required: true)]
    public List<ProtoId<JobPrototype>> OfficerRoles = new();

    [DataField("coreRoles")]
    public List<ProtoId<JobPrototype>> CoreRoles = new();

    [DataField("mechanicusRoles", required: true)]
    public List<ProtoId<JobPrototype>> MechanicusRoles = new();
}

[DataDefinition]
public sealed partial class WH40KCommandTeamCompositionStaffingRoleConfig
{
    [DataField("roleId", required: true)]
    public ProtoId<JobPrototype> RoleId = default!;

    [DataField("target")]
    public int Target = 1;
}

[Prototype("wh40kCommandTeamCompositionTeamMap")]
public sealed partial class WH40KCommandTeamCompositionTeamMapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultProfile", required: true)]
    public ProtoId<WH40KCommandTeamCompositionProfilePrototype> DefaultProfile = "WH40KCommandTeamCompositionProfileImperium";

    [DataField("teamProfiles")]
    public Dictionary<string, ProtoId<WH40KCommandTeamCompositionProfilePrototype>> TeamProfiles = new();
}
