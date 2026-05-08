using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared._WH40K.GameMode;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.GameTicking.Rules.Prototypes;

[Prototype("wh40kTeamBattleConfig")]
public sealed partial class WH40KTeamBattleConfigPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("pointsProfile")]
    public ProtoId<WH40KTeamBattlePointsProfilePrototype>? PointsProfile;

    [DataField("weatherProfile")]
    public ProtoId<WH40KTeamBattleWeatherProfilePrototype>? WeatherProfile;

    [DataField("eventsProfile")]
    public ProtoId<WH40KTeamBattleRoundEventsProfilePrototype>? EventsProfile;

    [DataField("logisticsProfile")]
    public ProtoId<WH40KTeamBattleLogisticsProfilePrototype>? LogisticsProfile;

    [DataField("blackFrontProfile")]
    public ProtoId<WH40KTeamBattleBlackFrontProfilePrototype>? BlackFrontProfile;

    [DataField("orbitalProfile")]
    public ProtoId<WH40KTeamBattleOrbitalProfilePrototype>? OrbitalProfile;

    [DataField("economyProfile")]
    public ProtoId<WH40KTeamBattleEconomyProfilePrototype>? EconomyProfile;

    [DataField("levelBuffProfile")]
    public ProtoId<WH40KTeamBattleLevelBuffProfilePrototype>? LevelBuffProfile;

    [DataField("weatherDangerProfile")]
    public ProtoId<WH40KWeatherDangerProfilePrototype>? WeatherDangerProfile;

    [DataField("points")]
    public WH40KTeamBattlePointsConfig Points = new();

    [DataField("weather")]
    public WH40KTeamBattleWeatherConfig Weather = new();

    [DataField("events")]
    public WH40KTeamBattleRoundEventsConfig Events = new();

    [DataField("logistics")]
    public WH40KTeamBattleLogisticsConfig Logistics = new();

    [DataField("blackFront")]
    public WH40KTeamBattleBlackFrontConfig BlackFront = new();

    [DataField("orbital")]
    public WH40KTeamBattleOrbitalConfig Orbital = new();

    [DataField("economy")]
    public WH40KTeamBattleEconomyConfig Economy = new();

    [DataField("levelBuff")]
    public WH40KTeamBattleLevelBuffConfig LevelBuff = new();
}

[DataDefinition]
public sealed partial class WH40KTeamBattlePointsConfig
{
    [DataField("teamStartingPoints")]
    public int TeamStartingPoints = 50;

    [DataField("frontPointsPerKill")]
    public int FrontPointsPerKill = 1;

    [DataField("baseLevelThresholds")]
    public List<int> BaseLevelThresholds = new() { 120, 300, 600, 1000, 1500, 2200, 3100, 4200 };

    [DataField("levelBuffConstructionDoAfterMultiplier")]
    public float LevelBuffConstructionDoAfterMultiplier = 0.75f;

    [DataField("levelBuffMedicalDoAfterMultiplier")]
    public float LevelBuffMedicalDoAfterMultiplier = 0.8f;
}

[DataDefinition]
public sealed partial class WH40KTeamBattleWeatherConfig
{
    [DataField("minStartDelaySeconds")]
    public float MinStartDelaySeconds = 300f;

    [DataField("firstStartJitterSeconds")]
    public float FirstStartJitterSeconds = 360f;

    [DataField("noRoundChance")]
    public float NoRoundChance = 0.35f;

    [DataField("minDurationSeconds")]
    public float MinDurationSeconds = 180f;

    [DataField("maxDurationSeconds")]
    public float MaxDurationSeconds = 600f;

    [DataField("gapMinSeconds")]
    public float GapMinSeconds = 180f;

    [DataField("gapMaxSeconds")]
    public float GapMaxSeconds = 420f;

    [DataField("repeatChance")]
    public float RepeatChance = 0.55f;

    [DataField("warningLeadSeconds")]
    public float WarningLeadSeconds = 30f;

    [DataField("pool")]
    public List<EntProtoId> Pool = new()
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
}

[DataDefinition]
public sealed partial class WH40KTeamBattleRoundEventsConfig
{
    [DataField("enabled")]
    public bool Enabled = true;

    [DataField("minStartDelaySeconds")]
    public float MinStartDelaySeconds = 480f;

    [DataField("firstStartJitterSeconds")]
    public float FirstStartJitterSeconds = 480f;

    [DataField("noRoundChance")]
    public float NoRoundChance = 0.2f;

    [DataField("minDurationSeconds")]
    public float MinDurationSeconds = 180f;

    [DataField("maxDurationSeconds")]
    public float MaxDurationSeconds = 420f;

    [DataField("gapMinSeconds")]
    public float GapMinSeconds = 480f;

    [DataField("gapMaxSeconds")]
    public float GapMaxSeconds = 960f;

    [DataField("repeatChance")]
    public float RepeatChance = 0.85f;

    [DataField("warningLeadSeconds")]
    public float WarningLeadSeconds = 30f;

    [DataField("pool")]
    public List<WH40KRoundEventType> Pool = new()
    {
        WH40KRoundEventType.LogisticsSurge,
        WH40KRoundEventType.OrbitalBombardment,
        WH40KRoundEventType.BlackFront
    };
}

[DataDefinition]
public sealed partial class WH40KTeamBattleLogisticsConfig
{
    [DataField("ammoPriceMultiplier")]
    public float AmmoPriceMultiplier = 0.7f;

    [DataField("ammoCategories")]
    public List<ProtoId<StoreCategoryPrototype>> AmmoCategories = new()
    {
        "VoxAmmo",
        "AltarAmmo"
    };

    [DataField("cooldownMultiplier")]
    public float CooldownMultiplier = 0.7f;

    [DataField("constructionDoAfterMultiplier")]
    public float ConstructionDoAfterMultiplier = 0.65f;

    [DataField("medicalDoAfterMultiplier")]
    public float MedicalDoAfterMultiplier = 0.7f;
}

[DataDefinition]
public sealed partial class WH40KTeamBattleBlackFrontConfig
{
    [DataField("influenceMultiplier")]
    public int InfluenceMultiplier = 2;

    [DataField("weatherId")]
    public EntProtoId WeatherId = "WHBlackFront";
}

[DataDefinition]
public sealed partial class WH40KTeamBattleOrbitalConfig
{
    [DataField("bombardmentDurationSeconds")]
    public float BombardmentDurationSeconds = 75f;

    [DataField("waveIntervalSeconds")]
    public float WaveIntervalSeconds = 5f;

    [DataField("strikesPerWaveMin")]
    public int StrikesPerWaveMin = 2;

    [DataField("strikesPerWaveMax")]
    public int StrikesPerWaveMax = 4;

    [DataField("strikeDelaySeconds")]
    public float StrikeDelaySeconds = 2.5f;

    [DataField("targetScatterRadius")]
    public float TargetScatterRadius = 3f;

    [DataField("explosionIntensity")]
    public float ExplosionIntensity = 220f;

    [DataField("explosionSlope")]
    public float ExplosionSlope = 3f;

    [DataField("explosionMaxTileIntensity")]
    public float ExplosionMaxTileIntensity = 14f;

    [DataField("markerPrototype")]
    public EntProtoId MarkerPrototype = "WH40KOrbitalStrikeMarker";
}

[DataDefinition]
public sealed partial class WH40KTeamBattleEconomyConfig
{
    [DataField("preparationMultiplier")]
    public int PreparationMultiplier = 1;

    [DataField("assaultMultiplier")]
    public int AssaultMultiplier = 2;

    [DataField("apocalypseMultiplier")]
    public int ApocalypseMultiplier = 3;

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

[DataDefinition]
public sealed partial class WH40KTeamBattleLevelBuffPoolEntry
{
    [DataField("buffType", required: true)]
    public WH40KLevelBuffType BuffType = WH40KLevelBuffType.None;

    [DataField("weight")]
    public int Weight = 1;
}

[DataDefinition]
public sealed partial class WH40KTeamBattleLevelBuffConfig
{
    [DataField("pool")]
    public List<WH40KTeamBattleLevelBuffPoolEntry> Pool = new()
    {
        new() { BuffType = WH40KLevelBuffType.Pulling, Weight = 1 },
        new() { BuffType = WH40KLevelBuffType.Medical, Weight = 1 },
        new() { BuffType = WH40KLevelBuffType.Construction, Weight = 1 },
    };
}

[Prototype("wh40kTeamBattlePointsProfile")]
public sealed partial class WH40KTeamBattlePointsProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("config", required: true)]
    public WH40KTeamBattlePointsConfig Config = new();
}

[Prototype("wh40kTeamBattleWeatherProfile")]
public sealed partial class WH40KTeamBattleWeatherProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("config", required: true)]
    public WH40KTeamBattleWeatherConfig Config = new();
}

[Prototype("wh40kTeamBattleRoundEventsProfile")]
public sealed partial class WH40KTeamBattleRoundEventsProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("config", required: true)]
    public WH40KTeamBattleRoundEventsConfig Config = new();
}

[Prototype("wh40kTeamBattleLogisticsProfile")]
public sealed partial class WH40KTeamBattleLogisticsProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("config", required: true)]
    public WH40KTeamBattleLogisticsConfig Config = new();
}

[Prototype("wh40kTeamBattleBlackFrontProfile")]
public sealed partial class WH40KTeamBattleBlackFrontProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("config", required: true)]
    public WH40KTeamBattleBlackFrontConfig Config = new();
}

[Prototype("wh40kTeamBattleOrbitalProfile")]
public sealed partial class WH40KTeamBattleOrbitalProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("config", required: true)]
    public WH40KTeamBattleOrbitalConfig Config = new();
}

[Prototype("wh40kTeamBattleEconomyProfile")]
public sealed partial class WH40KTeamBattleEconomyProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("config", required: true)]
    public WH40KTeamBattleEconomyConfig Config = new();
}

[Prototype("wh40kTeamBattleLevelBuffProfile")]
public sealed partial class WH40KTeamBattleLevelBuffProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("config", required: true)]
    public WH40KTeamBattleLevelBuffConfig Config = new();
}
