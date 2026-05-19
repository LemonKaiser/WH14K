using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.WaveDefence;

[Prototype("wh40kWaveDefenceConfig")]
public sealed partial class WH40KWaveDefenceConfigPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("mode")]
    public WH40KWaveDefenceMode Mode = WH40KWaveDefenceMode.Fixed;

    [DataField("defendingTeamId")]
    public string DefendingTeamId = "Imperium";

    [DataField("attackingTeamId")]
    public string? AttackingTeamId;

    [DataField("preparationDurationSeconds")]
    public float PreparationDurationSeconds = 180f;

    [DataField("intermissionDurationSeconds")]
    public float IntermissionDurationSeconds = 60f;

    [DataField("lateJoinDuringWaveQueuesUntilPreparation")]
    public bool LateJoinDuringWaveQueuesUntilPreparation = true;

    [DataField("respawnUsesReinforcementMarkers")]
    public bool RespawnUsesReinforcementMarkers = true;

    [DataField("finalWaveNumber")]
    public int FinalWaveNumber = 10;

    [DataField("waveProfiles")]
    public List<ProtoId<WH40KWaveProfilePrototype>> WaveProfiles = new();

    [DataField("countCritAsAlive")]
    public bool CountCritAsAlive = true;

    [DataField("minimumRequiredAttackLanes")]
    public int MinimumRequiredAttackLanes = 1;

    [DataField("teamStartingPoints")]
    public int TeamStartingPoints = 50;

    [DataField("frontPointsPerKill")]
    public int FrontPointsPerKill = 1;

    [DataField("baseLevelThresholds")]
    public List<int> BaseLevelThresholds = new() { 120, 300, 600, 1000, 1500, 2200, 3100, 4200 };

    [DataField("economyPreparationMultiplier")]
    public int EconomyPreparationMultiplier = 1;

    [DataField("economyAssaultMultiplier")]
    public int EconomyAssaultMultiplier = 2;

    [DataField("economyApocalypseMultiplier")]
    public int EconomyApocalypseMultiplier = 3;

    [DataField("reinforcementCurveDurationMinSeconds")]
    public float ReinforcementCurveDurationMinSeconds = 3600f;

    [DataField("reinforcementCurveDurationMaxSeconds")]
    public float ReinforcementCurveDurationMaxSeconds = 7200f;

    [DataField("reinforcementCurveFallbackApocalypseSeconds")]
    public float ReinforcementCurveFallbackApocalypseSeconds = 1800f;

    [DataField("reinforcementCurveBaseMultiplier")]
    public float ReinforcementCurveBaseMultiplier = 1f;

    [DataField("reinforcementCurveScale")]
    public float ReinforcementCurveScale = 1.25f;

    [DataField("reinforcementCurveExponent")]
    public float ReinforcementCurveExponent = 2f;
}
