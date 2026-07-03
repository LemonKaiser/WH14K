using System;
using Content.Shared._WH40K.GameMode;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Store;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

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
    /// If true, WH40K objectives will be spawned for the teams.
    /// </summary>
    [DataField("objectivesEnabled")]
    public bool ObjectivesEnabled = true;

    /// <summary>
    /// Round time limit in seconds. 0 disables the limit.
    /// </summary>
    [DataField("roundTimeLimitSeconds")]
    public float RoundTimeLimitSeconds = 10800f;

    /// <summary>
    /// Controls which victory conditions can end the round.
    /// </summary>
    [DataField("victoryCondition")]
    public WH40KVictoryCondition VictoryCondition = WH40KVictoryCondition.Either;

    /// <summary>
    /// Length of preparation phase in seconds.
    /// </summary>
    [DataField("preparationDurationSeconds")]
    public float PreparationDurationSeconds = 600f;

    /// <summary>
    /// Length of assault phase in seconds.
    /// </summary>
    [DataField("assaultDurationSeconds")]
    public float AssaultDurationSeconds = 3600f;

    /// <summary>
    /// Before this time from round start, objective/team victory is locked.
    /// </summary>
    [DataField("earlyVictoryLockSeconds")]
    public float EarlyVictoryLockSeconds = 1800f;

    /// <summary>
    /// Optional external profile for non-core mode tuning
    /// (points, weather, round events, logistics, orbital/black-front params).
    /// </summary>
    [DataField("configProfile")]
    public ProtoId<WH40KTeamBattleConfigPrototype>? ConfigProfile;

    /// <summary>
    /// Frontline points needed to reach each next level. Level starts at 1.
    /// </summary>
    [DataField("baseLevelThresholds")]
    public List<int> BaseLevelThresholds = new() { 120, 300, 600, 1000, 1500, 2200, 3100, 4200 };

    /// <summary>
    /// Initial economy points granted to each team at round start.
    /// </summary>
    [DataField("teamStartingPoints")]
    public int TeamStartingPoints = 50;

    /// <summary>
    /// TeamXP granted to the killer's team for a valid enemy kill.
    /// The same amount is mirrored to the legacy command-point store as influence.
    /// </summary>
    [DataField("frontPointsPerKill")]
    public int FrontPointsPerKill = 1;

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

    /// <summary>
    /// Per-level random team buff: construction do-after multiplier.
    /// Lower is faster.
    /// </summary>
    [DataField("levelBuffConstructionDoAfterMultiplier")]
    public float LevelBuffConstructionDoAfterMultiplier = 0.75f;

    /// <summary>
    /// Per-level random team buff: medical do-after multiplier.
    /// Lower is faster.
    /// </summary>
    [DataField("levelBuffMedicalDoAfterMultiplier")]
    public float LevelBuffMedicalDoAfterMultiplier = 0.8f;

    [DataField("levelBuffPool")]
    public List<WH40KTeamBattleLevelBuffPoolEntry> LevelBuffPool = new()
    {
        new() { BuffType = WH40KLevelBuffType.Pulling, Weight = 1 },
        new() { BuffType = WH40KLevelBuffType.Medical, Weight = 1 },
        new() { BuffType = WH40KLevelBuffType.Construction, Weight = 1 },
    };

    /// <summary>
    /// Weather can not start before this many seconds after round start.
    /// </summary>
    [DataField("weatherMinStartDelaySeconds")]
    public float WeatherMinStartDelaySeconds = 300f;

    /// <summary>
    /// Additional random delay before first weather event.
    /// </summary>
    [DataField("weatherFirstStartJitterSeconds")]
    public float WeatherFirstStartJitterSeconds = 360f;

    /// <summary>
    /// Chance that this round has no weather at all.
    /// </summary>
    [DataField("weatherNoRoundChance")]
    public float WeatherNoRoundChance = 0.35f;

    [DataField("weatherMinDurationSeconds")]
    public float WeatherMinDurationSeconds = 180f;

    [DataField("weatherMaxDurationSeconds")]
    public float WeatherMaxDurationSeconds = 600f;

    [DataField("weatherGapMinSeconds")]
    public float WeatherGapMinSeconds = 180f;

    [DataField("weatherGapMaxSeconds")]
    public float WeatherGapMaxSeconds = 420f;

    /// <summary>
    /// Chance to schedule another weather event after current event ended.
    /// </summary>
    [DataField("weatherRepeatChance")]
    public float WeatherRepeatChance = 0.55f;

    /// <summary>
    /// Warning lead time before a weather front starts.
    /// </summary>
    [DataField("weatherWarningLeadSeconds")]
    public float WeatherWarningLeadSeconds = 30f;

    [DataField("weatherPool")]
    public List<EntProtoId> WeatherPool = new()
    {
        "WHAsh",
        "WHToxicAshFront",
        "WHAcidRain",
        "WHRadFront",
        "WHIonStorm",
        "WHBlackIce",
        "WHSandHurricane",
        "WHMetalHail",
        "WHSporeDrift",
        "WHGellarTremor",
        "WHMachineCorrosionStorm"
    };

    [DataField("weatherWeightedPool")]
    public List<WH40KTeamBattleWeightedWeatherEntry> WeatherWeightedPool = new();

    [DataField("weatherDangerProfile")]
    public ProtoId<WH40KWeatherDangerProfilePrototype> WeatherDangerProfile = "WH40KWeatherDangerProfileDefault";

    [DataField("roundEventsEnabled")]
    public bool RoundEventsEnabled = true;

    [DataField("roundEventMinStartDelaySeconds")]
    public float RoundEventMinStartDelaySeconds = 480f;

    [DataField("roundEventFirstStartJitterSeconds")]
    public float RoundEventFirstStartJitterSeconds = 480f;

    [DataField("roundEventNoRoundChance")]
    public float RoundEventNoRoundChance = 0.2f;

    [DataField("roundEventMinDurationSeconds")]
    public float RoundEventMinDurationSeconds = 180f;

    [DataField("roundEventMaxDurationSeconds")]
    public float RoundEventMaxDurationSeconds = 420f;

    [DataField("roundEventGapMinSeconds")]
    public float RoundEventGapMinSeconds = 480f;

    [DataField("roundEventGapMaxSeconds")]
    public float RoundEventGapMaxSeconds = 960f;

    [DataField("roundEventRepeatChance")]
    public float RoundEventRepeatChance = 0.85f;

    [DataField("roundEventWarningLeadSeconds")]
    public float RoundEventWarningLeadSeconds = 30f;

    [DataField("roundEventPool")]
    public List<WH40KRoundEventType> RoundEventPool = new()
    {
        WH40KRoundEventType.LogisticsSurge,
        WH40KRoundEventType.OrbitalBombardment,
        WH40KRoundEventType.BlackFront
    };

    [DataField("roundEventWeightedPool")]
    public List<WH40KTeamBattleWeightedRoundEventEntry> RoundEventWeightedPool = new();

    [DataField("logisticsAmmoPriceMultiplier")]
    public float LogisticsAmmoPriceMultiplier = 0.7f;

    [DataField("logisticsAmmoCategories")]
    public List<ProtoId<StoreCategoryPrototype>> LogisticsAmmoCategories = new()
    {
        "VoxAmmo",
        "AltarAmmo"
    };

    [DataField("logisticsCooldownMultiplier")]
    public float LogisticsCooldownMultiplier = 0.7f;

    [DataField("logisticsConstructionDoAfterMultiplier")]
    public float LogisticsConstructionDoAfterMultiplier = 0.65f;

    [DataField("logisticsMedicalDoAfterMultiplier")]
    public float LogisticsMedicalDoAfterMultiplier = 0.7f;

    [DataField("blackFrontInfluenceMultiplier")]
    public int BlackFrontInfluenceMultiplier = 2;

    [DataField("blackFrontWeatherId")]
    public EntProtoId BlackFrontWeatherId = "WHBlackFront";

    [DataField("orbitalBombardmentDurationSeconds")]
    public float OrbitalBombardmentDurationSeconds = 75f;

    [DataField("orbitalWaveIntervalSeconds")]
    public float OrbitalWaveIntervalSeconds = 5f;

    [DataField("orbitalStrikesPerWaveMin")]
    public int OrbitalStrikesPerWaveMin = 2;

    [DataField("orbitalStrikesPerWaveMax")]
    public int OrbitalStrikesPerWaveMax = 4;

    [DataField("orbitalStrikeDelaySeconds")]
    public float OrbitalStrikeDelaySeconds = 2.5f;

    [DataField("orbitalTargetScatterRadius")]
    public float OrbitalTargetScatterRadius = 3f;

    [DataField("orbitalExplosionIntensity")]
    public float OrbitalExplosionIntensity = 220f;

    [DataField("orbitalExplosionSlope")]
    public float OrbitalExplosionSlope = 3f;

    [DataField("orbitalExplosionMaxTileIntensity")]
    public float OrbitalExplosionMaxTileIntensity = 14f;

    [DataField("orbitalMarkerPrototype")]
    public EntProtoId OrbitalMarkerPrototype = "WH40KOrbitalStrikeMarker";

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

    [ViewVariables]
    public WH40KBattlePhase CurrentPhase = WH40KBattlePhase.Preparation;

    [ViewVariables]
    public TimeSpan NextPhaseChange;

    [ViewVariables]
    public Dictionary<string, int> TeamFrontPoints = new();

    [ViewVariables]
    public Dictionary<string, int> TeamCommandPoints = new();

    [ViewVariables]
    public Dictionary<string, int> TeamResearchPoints = new();

    [ViewVariables]
    public Dictionary<string, int> TeamArtifactPoints = new();

    [ViewVariables]
    public Dictionary<string, int> TeamBaseLevels = new();

    [ViewVariables]
    public Dictionary<string, WH40KLevelBuffType> TeamLevelBuffs = new();

    [ViewVariables]
    public Dictionary<NetUserId, string> PlayerLastKnownTeam = new();

    [ViewVariables]
    public bool WeatherSuppressedForRound;

    [ViewVariables]
    public TimeSpan? NextWeatherStart;

    [ViewVariables]
    public TimeSpan? ActiveWeatherEnd;

    [ViewVariables]
    public EntProtoId? ActiveWeather;

    [ViewVariables]
    public EntProtoId? PendingWeather;

    [ViewVariables]
    public TimeSpan? LastWeatherWarningForStart;

    [ViewVariables]
    public bool RoundEventsSuppressedForRound;

    [ViewVariables]
    public WH40KRoundEventType ActiveRoundEvent = WH40KRoundEventType.None;

    [ViewVariables]
    public WH40KRoundEventType? PendingRoundEvent;

    [ViewVariables]
    public TimeSpan? NextRoundEventStart;

    [ViewVariables]
    public TimeSpan? ActiveRoundEventEnd;

    [ViewVariables]
    public TimeSpan? LastRoundEventWarningForStart;

    [ViewVariables]
    public TimeSpan NextOrbitalWaveAt = TimeSpan.Zero;

    [ViewVariables]
    public List<WH40KPendingOrbitalStrike> PendingOrbitalStrikes = new();
}

[DataDefinition]
public sealed partial class WH40KTeamDefinition
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("name", required: true)]
    public LocId Name = string.Empty;

    [DataField("logo")]
    public SpriteSpecifier? Logo;

    [DataField("color")]
    public Color Color = Color.White;

    [DataField("departments")]
    public List<ProtoId<DepartmentPrototype>> Departments = new();
}

public enum WH40KVictoryCondition
{
    Teams,
    Objectives,
    Either,
    None
}

public enum WH40KRoundEventType : byte
{
    None = 0,
    LogisticsSurge,
    OrbitalBombardment,
    BlackFront
}

public enum WH40KLevelBuffType : byte
{
    None = 0,
    Pulling,
    Medical,
    Construction
}

public sealed record WH40KPendingOrbitalStrike(MapCoordinates Target, TimeSpan DetonateAt);
