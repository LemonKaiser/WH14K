using Content.Server._WH40K.GunGame;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(WH40KGunGameRuleSystem))]
public sealed partial class WH40KGunGameRuleComponent : Component
{
    [DataField(required: true)]
    public List<EntProtoId> WeaponPool = new();

    [DataField]
    public int WeaponCount = 30;

    [DataField("finalWeapon")]
    public EntProtoId FinalWeapon = "KitchenKnife";

    [DataField("backupWeapon")]
    public EntProtoId BackupWeapon = "CombatKnife";

    [DataField]
    public int ConsecutiveDeathThreshold = 5;

    [ViewVariables]
    public List<EntProtoId> WeaponSequence = new();

    [ViewVariables]
    public Dictionary<NetUserId, int> PlayerLevel = new();

    [ViewVariables]
    public Dictionary<NetUserId, int> PlayerKills = new();

    [ViewVariables]
    public Dictionary<NetUserId, HumanoidCharacterProfile> PlayerProfiles = new();

    [ViewVariables]
    public Dictionary<NetUserId, int> ConsecutiveDeaths = new();

    [ViewVariables]
    public NetUserId? Victor;

    [DataField]
    public TimeSpan RespawnDelay = TimeSpan.FromSeconds(5);

    [DataField("restartDelay")]
    public TimeSpan RestartDelay = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan RoundDuration = TimeSpan.FromMinutes(15);

    [ViewVariables]
    public TimeSpan NextTimerSyncAt = TimeSpan.Zero;

    [ViewVariables]
    public int LastTimerDurationSeconds = -1;

    [ViewVariables]
    public int LastTimerElapsedSeconds = -1;

    [ViewVariables]
    public bool LastTimerStopped;

    [ViewVariables]
    public bool PlacementRewardsGranted;

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
    public float GlassesChance = 0.3f;

    [DataField]
    public float HeadChance = 0.3f;

    [DataField]
    public float GlovesChance = 0.25f;
}
