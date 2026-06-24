using Content.Server._WH40K.PropHunt;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Whitelist;
using Content.Shared._WH40K.PropHunt;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(WH40KPropHuntRuleSystem))]
public sealed partial class WH40KPropHuntRuleComponent : Component
{
    [DataField]
    public ProtoId<JobPrototype> FallbackJob = "PropHuntPlayer";

    [DataField]
    public EntProtoId DisguisePrototype = "ChameleonDisguise";

    [DataField]
    public EntProtoId MorphProjectorPrototype = "WH40KPropHuntMorphProjector";

    [DataField]
    public EntProtoId HiderJumpsuit = "ClothingUniformJumpsuitColorBlue";

    [DataField]
    public EntProtoId HiderShoes = "ClothingShoesColorBlue";

    [DataField]
    public EntProtoId SeekerJumpsuit = "ClothingUniformJumpsuitColorRed";

    [DataField]
    public EntProtoId SeekerShoes = "ClothingShoesColorRed";

    [DataField]
    public EntProtoId SeekerRangedWeapon = "WH40KPropHuntWeaponLaserLasgun";

    [DataField]
    public EntProtoId MorphAction = "ActionWH40KPropHuntMorph";

    [DataField]
    public EntProtoId AnchorAction = "ActionWH40KPropHuntAnchor";

    [DataField]
    public EntProtoId HonkAction = "ActionWH40KPropHuntHonk";

    [DataField]
    public EntProtoId InvisibilityAction = "ActionWH40KPropHuntInvisible";

    [DataField]
    public EntProtoId SmokeAction = "ActionWH40KPropHuntSmoke";

    [DataField]
    public EntProtoId SeekerPulseAction = "ActionWH40KPropHuntSeekerPulse";

    [DataField]
    public EntProtoId SeekerDashAction = "ActionWH40KPropHuntSeekerDash";

    [DataField]
    public EntProtoId SmokeGrenadePrototype = "SmokeGrenade";

    [DataField]
    public TimeSpan RoundDuration = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan RestartDelay = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan LateJoinGraceDuration = TimeSpan.FromSeconds(30);

    [DataField]
    public TimeSpan SeekerFreezeDuration = TimeSpan.FromSeconds(30);

    [DataField]
    public TimeSpan InvisibilityDuration = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan PeriodicRevealInterval = TimeSpan.FromMinutes(5);

    [DataField]
    public TimeSpan PeriodicRevealDuration = TimeSpan.FromSeconds(8);

    [DataField]
    public int MinimumPlayersToRun = 2;

    [DataField]
    public int WinnerRewardXp = 500;

    [DataField]
    public float SeekerPulseRadius = 10f;

    [DataField]
    public float SeekerDashRange = 10f;

    [DataField]
    public float SeekerDashSpeed = 26f;

    [DataField(required: true)]
    public EntityWhitelist? MorphWhitelist;

    [DataField(required: true)]
    public EntityWhitelist? MorphBlacklist;

    [DataField]
    public List<EntProtoId> MorphAllowedPrototypes = new();

    [DataField]
    public List<string> MorphAllowedPrototypePrefixes = new();

    [ViewVariables]
    public Dictionary<NetUserId, WH40KPropHuntRole> PlayerRoles = new();

    [ViewVariables]
    public Dictionary<NetUserId, int> PlayerKills = new();

    [ViewVariables]
    public Dictionary<NetUserId, HumanoidCharacterProfile> PlayerProfiles = new();

    [ViewVariables]
    public WH40KPropHuntRole? WinnerRole;

    [ViewVariables]
    public NetUserId? MvpSeeker;

    [ViewVariables]
    public bool RewardsGranted;

    [ViewVariables]
    public TimeSpan NextTimerSyncAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan ActiveRoundElapsed = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastRoundProgressUpdateAt = TimeSpan.Zero;

    [ViewVariables]
    public bool WaitingForPlayers = true;

    [ViewVariables]
    public int LastTimerDurationSeconds = -1;

    [ViewVariables]
    public int LastTimerElapsedSeconds = -1;

    [ViewVariables]
    public bool LastTimerStopped;

    [ViewVariables]
    public TimeSpan NextCountdownSyncAt = TimeSpan.Zero;

    [ViewVariables]
    public int LastCountdownRemainingSeconds = -1;

    [ViewVariables]
    public int LastRoleHudSeekerCount = -1;

    [ViewVariables]
    public int LastRoleHudHiderCount = -1;

    [ViewVariables]
    public TimeSpan NextPeriodicRevealAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan RevealActiveUntil = TimeSpan.Zero;
}
