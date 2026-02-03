using System;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared.Roles;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(WH40KTeamBattleRuleSystem))]
public sealed partial class WH40KTeamBattleRuleComponent : Component
{
    [DataField("teams", required: true)]
    public List<WH40KTeamDefinition> Teams = new();

    /// <summary>
    /// Seconds between win-condition checks.
    /// </summary>
    [DataField("checkInterval")]
    public float CheckInterval = 3f;

    /// <summary>
    /// If true, victory checks are skipped until every team has at least one assigned member.
    /// </summary>
    [DataField("requireAllTeamsPresent")]
    public bool RequireAllTeamsPresent = true;

    /// <summary>
    /// If true, critical (but not dead) players still count as alive.
    /// </summary>
    [DataField("countCriticalAsAlive")]
    public bool CountCriticalAsAlive = true;

    [DataField("announceTeamOnSpawn")]
    public bool AnnounceTeamOnSpawn = true;

    [DataField("announceWinner")]
    public bool AnnounceWinner = true;

    /// <summary>
    /// Round time limit in seconds. 0 disables the limit.
    /// </summary>
    [DataField("roundTimeLimitSeconds")]
    public float RoundTimeLimitSeconds = 3600f;

    [ViewVariables]
    public TimeSpan NextCheck;

    [ViewVariables]
    public TimeSpan RoundStartTime;

    [ViewVariables]
    public bool RoundEnding;

    [ViewVariables]
    public string? WinnerTeamId;

    [ViewVariables]
    public bool Draw;

    [ViewVariables]
    public bool TimeLimitReached;

    [ViewVariables]
    public int[] TeamKills = Array.Empty<int>();

    [ViewVariables]
    public int[] TeamDeaths = Array.Empty<int>();

    [ViewVariables]
    public Dictionary<NetUserId, int> PlayerKills = new();

    [ViewVariables]
    public Dictionary<NetUserId, TimeSpan> NextFriendlyFireAhelpTime = new();

    [ViewVariables]
    public Dictionary<ProtoId<DepartmentPrototype>, int> DepartmentToTeam = new();
}

[DataDefinition]
public sealed partial class WH40KTeamDefinition
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("name", required: true)]
    public LocId Name = string.Empty;

    [DataField("departments")]
    public List<ProtoId<DepartmentPrototype>> Departments = new();
}
