using Content.Server._WH40K.MurderMystery;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared._WH40K.MurderMystery;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(WH40KMurderMysteryRuleSystem))]
public sealed partial class WH40KMurderMysteryRuleComponent : Component
{
    [DataField]
    public ProtoId<JobPrototype> FallbackJob = "MurderMysteryPlayer";

    [DataField]
    public EntProtoId MurderKnifePrototype = "WH40KMurderMysteryKnife";

    [DataField]
    public EntProtoId SheriffRevolverPrototype = "WH40KMurderMysterySheriffRevolver";

    [DataField]
    public EntProtoId SmokeAction = "ActionWH40KMurderMysterySmoke";

    [DataField]
    public EntProtoId FlashAction = "ActionWH40KMurderMysteryFlash";

    [DataField]
    public EntProtoId SmokeGrenadePrototype = "SmokeGrenade";

    [DataField]
    public TimeSpan RoundDuration = TimeSpan.FromMinutes(15);

    [DataField]
    public TimeSpan RestartDelay = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan RoleAssignmentDelay = TimeSpan.FromSeconds(30);

    [DataField]
    public int MinimumPlayersToRun = 2;

    [DataField]
    public int WinnerRewardXp = 500;

    [DataField]
    public int MurderAbilityUses = 3;

    [DataField]
    public float FlashRadius = 7f;

    [DataField]
    public TimeSpan FlashDuration = TimeSpan.FromSeconds(3);

    [DataField]
    public float FlashSlowTo = 0.25f;

    [DataField]
    public TimeSpan BloodCleanupInterval = TimeSpan.FromSeconds(1);

    [ViewVariables]
    public Dictionary<NetUserId, WH40KMurderMysteryRole> PlayerRoles = new();

    [ViewVariables]
    public Dictionary<NetUserId, HumanoidCharacterProfile> PlayerProfiles = new();

    [ViewVariables]
    public WH40KMurderMysteryVictoryTeam? WinnerTeam;

    [ViewVariables]
    public bool RewardsGranted;

    [ViewVariables]
    public bool WaitingForPlayers = true;

    [ViewVariables]
    public bool RolesAssigned;

    [ViewVariables]
    public TimeSpan AssignmentElapsed = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan ActiveRoundElapsed = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastRoundProgressUpdateAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextTimerSyncAt = TimeSpan.Zero;

    [ViewVariables]
    public int LastTimerDurationSeconds = -1;

    [ViewVariables]
    public int LastTimerElapsedSeconds = -1;

    [ViewVariables]
    public bool LastTimerStopped;

    [ViewVariables]
    public TimeSpan NextBloodCleanupAt = TimeSpan.Zero;
}
