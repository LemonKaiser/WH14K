using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Command;

[Prototype("wh40kTeamIdentityProfile")]
public sealed partial class WH40KTeamIdentityProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("teamId", required: true)]
    public string TeamId = string.Empty;

    [DataField("interfaceThemeId")]
    public string? InterfaceThemeId;

    [DataField("accentColorHex")]
    public string AccentColorHex = "#F3C548";

    [DataField("reinforcementProfile")]
    public ProtoId<WH40KCommandReinforcementProfilePrototype>? ReinforcementProfile;

    [DataField("commandTreeProfile")]
    public ProtoId<WH40KCommandTreeProfilePrototype>? CommandTreeProfile;
}

[Prototype("wh40kTeamIdentityMap")]
public sealed partial class WH40KTeamIdentityMapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultProfile", required: true)]
    public ProtoId<WH40KTeamIdentityProfilePrototype> DefaultProfile = "WH40KTeamIdentityProfileImperium";

    [DataField("teamProfiles")]
    public Dictionary<string, ProtoId<WH40KTeamIdentityProfilePrototype>> TeamProfiles = new();
}
