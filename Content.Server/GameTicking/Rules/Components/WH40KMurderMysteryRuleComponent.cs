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

    /// <summary>
    /// Pinpointer handed to every participant. Points at the sheriff revolver
    /// only while it is loose on the ground (drops/throws); stays silent while
    /// the revolver is in any player's inventory so the sheriff is not exposed.
    /// </summary>
    [DataField]
    public EntProtoId SheriffPinpointerPrototype = "WH40KMurderMysterySheriffPinpointer";

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

    /// <summary>
    /// Random clothing pools handed to every participant at spawn (replaces
    /// the fixed job startingGear so players don't all look identical).
    /// </summary>
    [DataField]
    public List<EntProtoId> JumpsuitPool = new();

    [DataField]
    public List<EntProtoId> ShoesPool = new();

    [DataField]
    public List<EntProtoId> GlassesPool = new();

    [DataField]
    public List<EntProtoId> HeadPool = new();

    [DataField]
    public List<EntProtoId> GlovesPool = new();

    [DataField]
    public List<EntProtoId> BackPool = new();

    [DataField]
    public List<EntProtoId> MaskPool = new();

    [DataField]
    public List<EntProtoId> OuterClothingPool = new();

    [DataField]
    public float GlassesChance = 0.3f;

    [DataField]
    public float HeadChance = 0.3f;

    [DataField]
    public float GlovesChance = 0.25f;

    [DataField]
    public float MaskChance = 0.2f;

    [DataField]
    public float OuterClothingChance = 0.2f;

    /// <summary>
    /// Clue entity spawned periodically on the play grid. Civilians pick them
    /// up; collecting <see cref="CluesToRevolver"/> grants the sheriff revolver.
    /// </summary>
    [DataField]
    public EntProtoId CluePrototype = "WH40KMurderMysteryClue";

    [DataField]
    public int CluesToRevolver = 7;

    [DataField]
    public TimeSpan ClueSpawnInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum number of clues that may exist on the map at once. Stops the
    /// map from flooding if players are slow to collect them.
    /// </summary>
    [DataField]
    public int MaxConcurrentClues = 14;

    [ViewVariables]
    public TimeSpan NextClueSpawnAt = TimeSpan.Zero;

    /// <summary>
    /// Per-player running tally of clues collected this round. Reset on round
    /// start. Reaching <see cref="CluesToRevolver"/> promotes the player to
    /// sheriff and grants the revolver.
    /// </summary>
    [ViewVariables]
    public Dictionary<NetUserId, int> CluesCollected = new();

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
