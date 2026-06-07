using System.Collections.Generic;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Utility;

namespace Content.Shared.Maps;

public sealed partial class GameMapPrototype
{
    [DataField("wh40kWeatherProfile")]
    public string? WH40KWeatherProfile { get; private set; }

    [DataField("wh40kEventsProfile")]
    public string? WH40KEventsProfile { get; private set; }

    [DataField("wh40kTacticalMapSnapshot")]
    public ResPath? WH40KTacticalMapSnapshot { get; private set; }

    [DataField("wh40kTeamBattleFactions")]
    public List<string>? WH40KTeamBattleFactions { get; private set; }

    [DataField("wh40kTeamOverrides")]
    public List<WH40KMapTeamOverride>? WH40KTeamOverrides { get; private set; }
}

[DataDefinition]
public sealed partial class WH40KMapTeamOverride
{
    [DataField("teamId", required: true)]
    public string TeamId = string.Empty;

    [DataField("balanceGroup")]
    public string? BalanceGroup;

    [DataField("maxPlayers")]
    public int? MaxPlayers;

    [DataField("sameFactionStreakLimit")]
    public int? SameFactionStreakLimit;

    [DataField("selectionEnabled")]
    public bool? SelectionEnabled;

    [DataField("requiredForPresence")]
    public bool? RequiredForPresence;
}
