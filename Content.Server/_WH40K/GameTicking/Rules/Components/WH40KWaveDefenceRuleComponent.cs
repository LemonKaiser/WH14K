using System;
using System.Collections.Generic;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.WaveDefence;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(WH40KWaveDefenceRuleSystem))]
public sealed partial class WH40KWaveDefenceRuleComponent : Component
{
    [DataField("config", required: true)]
    public ProtoId<WH40KWaveDefenceConfigPrototype> Config = default!;

    [ViewVariables]
    public WH40KWaveDefencePhase Phase = WH40KWaveDefencePhase.Preparation;

    [ViewVariables]
    public WH40KWaveDefenceMode Mode = WH40KWaveDefenceMode.Fixed;

    [ViewVariables]
    public string DefendingTeamId = "Imperium";

    [ViewVariables]
    public string AttackingTeamId = "Heretics";

    [ViewVariables]
    public TimeSpan RoundStartTime = TimeSpan.Zero;

    [ViewVariables]
    public float PreparationDurationSeconds = 180f;

    [ViewVariables]
    public float IntermissionDurationSeconds = 60f;

    [ViewVariables]
    public int FinalWaveNumber = 10;

    [ViewVariables]
    public bool CountCritAsAlive = true;

    [ViewVariables]
    public int MinimumRequiredAttackLanes = 1;

    [ViewVariables]
    public bool LateJoinQueuesDuringWave = true;

    [ViewVariables]
    public bool ManualWaveAdvanceOnly = true;

    [ViewVariables]
    public TimeSpan NextPhaseChange = TimeSpan.Zero;

    [ViewVariables]
    public int CurrentWaveNumber;

    [ViewVariables]
    public EntityUid? PrimaryObjective;

    [ViewVariables]
    public EntityUid? Station;

    [ViewVariables]
    public bool AuthoringValid = true;

    [ViewVariables]
    public bool LayoutReady;

    [ViewVariables]
    public bool PreparationAnnounced;

    [ViewVariables]
    public int LayoutRetryCount;

    [ViewVariables]
    public TimeSpan NextLayoutRetryAt = TimeSpan.Zero;

    [ViewVariables]
    public string? EndReason;

    [ViewVariables]
    public int TeamStartingPoints = 50;

    [ViewVariables]
    public int FrontPointsPerKill = 1;

    [ViewVariables]
    public List<int> BaseLevelThresholds = new() { 120, 300, 600, 1000, 1500, 2200, 3100, 4200 };

    [ViewVariables]
    public int EconomyPreparationMultiplier = 1;

    [ViewVariables]
    public int EconomyAssaultMultiplier = 2;

    [ViewVariables]
    public int EconomyApocalypseMultiplier = 3;

    [ViewVariables]
    public float ReinforcementCurveDurationMinSeconds = 3600f;

    [ViewVariables]
    public float ReinforcementCurveDurationMaxSeconds = 7200f;

    [ViewVariables]
    public float ReinforcementCurveFallbackApocalypseSeconds = 1800f;

    [ViewVariables]
    public float ReinforcementCurveBaseMultiplier = 1f;

    [ViewVariables]
    public float ReinforcementCurveScale = 1.25f;

    [ViewVariables]
    public float ReinforcementCurveExponent = 2f;

    [ViewVariables]
    public List<ProtoId<WH40KWaveProfilePrototype>> WaveProfiles = new();

    [ViewVariables]
    public HashSet<EntityUid> ActiveAttackers = new();

    [ViewVariables]
    public List<WH40KWavePendingBatch> PendingBatches = new();

    [ViewVariables]
    public Dictionary<NetUserId, string?> QueuedLateJoinJobs = new();

    [ViewVariables]
    public HashSet<NetUserId> QueuedRespawns = new();

    [ViewVariables]
    public Dictionary<NetUserId, string?> LastKnownJobIds = new();

    [ViewVariables]
    public Dictionary<NetUserId, string> PlayerLastKnownTeam = new();

    [ViewVariables]
    public Dictionary<string, int> TeamFrontPoints = new(StringComparer.OrdinalIgnoreCase);

    [ViewVariables]
    public Dictionary<string, int> TeamCommandPoints = new(StringComparer.OrdinalIgnoreCase);

    [ViewVariables]
    public Dictionary<string, int> TeamResearchPoints = new(StringComparer.OrdinalIgnoreCase);

    [ViewVariables]
    public Dictionary<string, int> TeamBaseLevels = new(StringComparer.OrdinalIgnoreCase);

    [ViewVariables]
    public string LastBatchSummary = "No wave batch has spawned yet.";

    [ViewVariables]
    public string MapStabilitySummary = "Map stability safeguards have not run yet.";

    [ViewVariables]
    public string LastLayoutStatus = "WaveDefence layout has not been validated yet.";
}

public sealed class WH40KWavePendingBatch
{
    public required TimeSpan DueAt;
    public required WH40KWaveBatchEntry Batch;
    public bool Spawned;
}
