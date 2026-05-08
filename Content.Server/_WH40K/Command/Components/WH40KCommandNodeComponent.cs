using Content.Server._WH40K.Command;
using Content.Shared._WH40K.Command;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Command.Components;

[RegisterComponent, Access(typeof(WH40KCommandNodeSystem), typeof(WH40KCommandEventMissionRuntimeSystem))]
public sealed partial class WH40KCommandNodeComponent : Component
{
    [DataField(required: true)]
    public string TeamId = string.Empty;

    /// <summary>
    /// Store categories that are considered this team's catalog.
    /// </summary>
    [DataField]
    public List<ProtoId<StoreCategoryPrototype>> TrackedCategories = new();

    [DataField]
    public int UpgradeLevel = 0;

    [DataField]
    public List<string> PurchasedTreeNodeIds = new();

    [DataField]
    public int UpgradeMaxLevel = 4;

    [DataField]
    public int UpgradeBaseCost = 12;

    [DataField]
    public int UpgradeCostStep = 8;

    [DataField]
    public int ReinforcementCost = 20;

    [DataField]
    public float ReinforcementCooldownSeconds = 180f;

    [DataField]
    public string ActiveBattleTacticId = WH40KCommandNodeTactics.DefaultTacticId;

    [DataField]
    public string ActiveDoctrineId = string.Empty;

    [DataField]
    public bool DoctrineLocked;

    [DataField]
    public string ActiveMissionTaskId = string.Empty;

    [DataField]
    public List<string> MissionBoardOfferedTaskIds = new();

    [DataField]
    public bool MissionBoardHadActiveFactionMission;

    [DataField]
    public float BattleTacticChangeCooldownSeconds = 300f;

    /// <summary>
    /// Interval in seconds for passive fallback income from this command node.
    /// </summary>
    [DataField]
    public float PassivePointIntervalSeconds = 75f;

    /// <summary>
    /// Interval reduction per command-node upgrade level.
    /// </summary>
    [DataField]
    public float PassiveIntervalReductionPerUpgradeSeconds = 5f;

    /// <summary>
    /// Lower bound for passive income interval after upgrades.
    /// </summary>
    [DataField]
    public float PassivePointMinIntervalSeconds = 36f;

    /// <summary>
    /// Base TeamXP / influence-equivalent granted per passive fallback tick.
    /// Funds are derived from the same amount via WH40KCommandEconomyCalculator.
    /// </summary>
    [DataField]
    public int PassiveFrontPointsPerInterval = 1;

    [ViewVariables]
    public TimeSpan NextReinforcementAvailable = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextBattleTacticChangeAvailable = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextPassivePointTick = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextUiRefresh;
}
