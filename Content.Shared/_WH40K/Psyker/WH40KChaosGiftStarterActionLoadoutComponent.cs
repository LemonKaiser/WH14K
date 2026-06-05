namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Starter action packs for the chaos gifts path:
/// baseline actions + patron-specific branch actions.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KChaosGiftStarterActionLoadoutComponent : Component
{
    // Spawn baseline: no free abilities at round start.
    // Gift access is driven by skrizhal choice/progression gates.
    [DataField("baseActions")]
    public List<string> BaseActions = new();

    [DataField("baseScaledActions")]
    public List<WH40KLevelLockedAction> BaseScaledActions = new()
    {
        new() { ActionPrototype = "ActionWH40KChaosWarpBlastSurge", RequiredLevel = 4 },
        new() { ActionPrototype = "ActionWH40KChaosWarpRiftStep", RequiredLevel = 7 },
    };

    [DataField("undividedActions")]
    public List<string> UndividedActions = new()
    {
        "ActionWH40KChaosUndividedBlink",
    };

    [DataField("undividedScaledActions")]
    public List<WH40KLevelLockedAction> UndividedScaledActions = new()
    {
        new() { ActionPrototype = "ActionWH40KChaosUndividedAegis", RequiredLevel = 6 },
    };

    [DataField("khorneActions")]
    public List<string> KhorneActions = new()
    {
        "ActionWH40KChaosKhorneRepulse",
    };

    [DataField("khorneScaledActions")]
    public List<WH40KLevelLockedAction> KhorneScaledActions = new()
    {
        new() { ActionPrototype = "ActionWH40KChaosKhorneExecutionStep", RequiredLevel = 6 },
    };

    [DataField("nurgleActions")]
    public List<string> NurgleActions = new()
    {
        "ActionWH40KChaosNurgleMiasma",
    };

    [DataField("nurgleScaledActions")]
    public List<WH40KLevelLockedAction> NurgleScaledActions = new()
    {
        new() { ActionPrototype = "ActionWH40KChaosNurgleRepulse", RequiredLevel = 6 },
    };

    [DataField("slaaneshActions")]
    public List<string> SlaaneshActions = new()
    {
        "ActionWH40KChaosSlaaneshSwap",
    };

    [DataField("slaaneshScaledActions")]
    public List<WH40KLevelLockedAction> SlaaneshScaledActions = new()
    {
        new() { ActionPrototype = "ActionWH40KChaosSlaaneshMiasma", RequiredLevel = 6 },
    };

    [DataField("tzeentchActions")]
    public List<string> TzeentchActions = new()
    {
        "ActionWH40KChaosTzeentchBarrier",
    };

    [DataField("tzeentchScaledActions")]
    public List<WH40KLevelLockedAction> TzeentchScaledActions = new()
    {
        new() { ActionPrototype = "ActionWH40KChaosTzeentchTimeField", RequiredLevel = 8 },
    };

    // R5 branch unlock economy contract:
    // exactly three branch gifts per patron path (slot 1..3).
    [DataField("khorneBranchActions")]
    public List<string> KhorneBranchActions = new()
    {
        "ActionWH40KChaosKhorneRepulse",
        "ActionWH40KChaosKhorneExecutionStep",
        "ActionWH40KChaosKhorneBloodstorm",
    };

    [DataField("khornePassiveExActions")]
    public List<string> KhornePassiveExActions = new();

    [DataField("khorneBonusActions")]
    public List<string> KhorneBonusActions = new();

    [DataField("nurgleBranchActions")]
    public List<string> NurgleBranchActions = new()
    {
        "ActionWH40KChaosNurgleMiasma",
        "ActionWH40KChaosNurgleRepulse",
        "ActionWH40KChaosNurgleCorpseBloom",
    };

    [DataField("nurgleBonusActions")]
    public List<string> NurgleBonusActions = new();

    [DataField("slaaneshBranchActions")]
    public List<string> SlaaneshBranchActions = new()
    {
        "ActionWH40KChaosSlaaneshSwap",
        "ActionWH40KChaosSlaaneshMiasma",
        "ActionWH40KChaosSlaaneshExquisiteTempo",
    };

    [DataField("slaaneshBonusActions")]
    public List<string> SlaaneshBonusActions = new()
    {
        "ActionWH40KChaosSlaaneshArena",
    };

    [DataField("tzeentchBranchActions")]
    public List<string> TzeentchBranchActions = new()
    {
        "ActionWH40KChaosTzeentchFireball",
        "ActionWH40KChaosTzeentchBarrier",
        "ActionWH40KChaosTzeentchWarpRewrite",
    };

    [DataField("tzeentchBonusActions")]
    public List<string> TzeentchBonusActions = new()
    {
        "ActionWH40KChaosTzeentchTimeField",
    };

    [DataField("leaderActions")]
    public List<string> LeaderActions = new()
    {
        "ActionWH40KChaosLeaderSacrifice",
    };

    [DataField]
    public List<EntityUid> GrantedActions = new();

    [DataField]
    public WH40KChaosPatron AppliedPatron = WH40KChaosPatron.None;

    [DataField]
    public int AppliedLevel;

    [DataField]
    public int AppliedPrimaryGiftSlot;

    [DataField]
    public int AppliedUnlockMask;

    [DataField]
    public bool AppliedKhornePassiveEx;

    [DataField]
    public bool AppliedLeaderState;

    [DataField]
    public bool AppliedCatastropheLockdown;
}
