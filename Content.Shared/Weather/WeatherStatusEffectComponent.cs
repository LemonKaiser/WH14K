using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.StatusEffectNew.Components;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Weather;

/// <summary>
/// Used only in conjure with <see cref="StatusEffectComponent"/> for status effects applied to map entities.
/// Contains basic information about all types of weather effects.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedWeatherSystem))]
public sealed partial class WeatherStatusEffectComponent : Component
{
    /// <summary>
    /// A texture that will tile and render as a weather effect across the entire map.
    /// </summary>
    [DataField(required: true)]
    public SpriteSpecifier Sprite = default!;

    /// <summary>
    /// Tint that will be applied to the weather texture.
    /// </summary>
    [DataField]
    public Color? Color;

    /// <summary>
    /// Weather scrolling speed.
    /// </summary>
    [DataField]
    public Vector2? Scrolling;

    /// <summary>
    /// Sound to play on the affected areas.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Determines where weather is considered active relative to roofs.
    /// </summary>
    [DataField]
    public WeatherExposureMode ExposureMode = WeatherExposureMode.UnroofedOnly;

    /// <summary>
    /// If true, weather additionally requires the tile's Weather flag to be enabled.
    /// </summary>
    [DataField]
    public bool RespectTileWeather = true;

    /// <summary>
    /// If true, ignores entities with <see cref="BlockWeatherComponent"/> on affected tiles.
    /// </summary>
    [DataField]
    public bool IgnoreBlockers;

    /// <summary>
    /// Server-side weather effects applied to entities.
    /// </summary>
    [DataField]
    public WeatherLocalEffects? Effects;

    /// <summary>
    /// Server-side map-wide weather effects.
    /// </summary>
    [DataField]
    public WeatherGlobalEffects? GlobalEffects;

    /// <summary>
    /// Client audio stream.
    /// Not used on the server.
    /// </summary>
    [ViewVariables]
    public EntityUid? Stream;

    [ViewVariables]
    public TimeSpan NextLocalEffectTick = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextGlobalEffectTick = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextWindDirectionChangeTick = TimeSpan.Zero;

    [ViewVariables]
    public float? CurrentWindDirectionDegrees;
}

public enum WeatherExposureMode : byte
{
    UnroofedOnly = 0,
    RoofedOnly,
    Everywhere,
}

[DataDefinition]
public sealed partial class WeatherLocalEffects
{
    [DataField("tickInterval")]
    public float TickInterval = 1f;

    [DataField("slowdown")]
    public WeatherSlowdownData? Slowdown;

    [DataField("mobDamage")]
    public DamageSpecifier? MobDamage;

    [DataField("mobDamageChance")]
    public float MobDamageChance = 1f;

    [DataField("structureDamage")]
    public DamageSpecifier? StructureDamage;

    [DataField("structureDamageChance")]
    public float StructureDamageChance = 1f;

    [DataField("wind")]
    public WeatherWindData? Wind;

    [DataField("emp")]
    public WeatherEmpData? Emp;

    [DataField("hazardSpawn")]
    public WeatherHazardSpawnData? HazardSpawn;

    /// <summary>
    /// If true, a breath mask protects from this weather's local effects.
    /// </summary>
    [DataField("protectedByGasMask")]
    public bool ProtectedByGasMask;

    /// <summary>
    /// If true, hardsuit/pressure-protective gear protects from this weather's local effects.
    /// </summary>
    [DataField("protectedByHardsuit")]
    public bool ProtectedByHardsuit;

    [DataField("puddle")]
    public WeatherPuddleData? Puddle;
}

[DataDefinition]
public sealed partial class WeatherGlobalEffects
{
    [DataField("tickInterval")]
    public float TickInterval = 4f;

    [DataField("lightFlicker")]
    public WeatherLightFlickerData? LightFlicker;

    [DataField("ambient")]
    public WeatherAmbientData? Ambient;
}

[DataDefinition]
public sealed partial class WeatherSlowdownData
{
    [DataField("statusEffect")]
    public EntProtoId StatusEffect = "WeatherSlowdownStatusEffect";

    [DataField("duration")]
    public float Duration = 1.5f;

    [DataField("walkModifier")]
    public float WalkModifier = 0.85f;

    [DataField("sprintModifier")]
    public float SprintModifier = 0.85f;
}

[DataDefinition]
public sealed partial class WeatherWindData
{
    [DataField("chance")]
    public float Chance = 0.2f;

    [DataField("impulse")]
    public float Impulse = 4f;

    [DataField("randomDirection")]
    public bool RandomDirection = true;

    [DataField("directionDegrees")]
    public float DirectionDegrees;

    /// <summary>
    /// If greater than zero and direction is not random every tick, the wind direction will be re-rolled every interval.
    /// </summary>
    [DataField("directionChangeInterval")]
    public float DirectionChangeInterval;
}

[DataDefinition]
public sealed partial class WeatherEmpData
{
    [DataField("chance")]
    public float Chance = 0.1f;

    [DataField("range")]
    public float Range = 2f;

    [DataField("energyConsumption")]
    public float EnergyConsumption = 35000f;

    [DataField("duration")]
    public float Duration = 5f;
}

[DataDefinition]
public sealed partial class WeatherHazardSpawnData
{
    [DataField("prototype", required: true)]
    public EntProtoId Prototype = default!;

    [DataField("chance")]
    public float Chance = 0.03f;

    [DataField("allowDuplicates")]
    public bool AllowDuplicates;
}

[DataDefinition]
public sealed partial class WeatherLightFlickerData
{
    [DataField("chance")]
    public float Chance = 0.1f;

    [DataField("durationMin")]
    public float DurationMin = 0.5f;

    [DataField("durationMax")]
    public float DurationMax = 2.5f;
}

[DataDefinition]
public sealed partial class WeatherAmbientData
{
    [DataField("sound")]
    public SoundSpecifier? Sound;

    [DataField("chance")]
    public float Chance = 0.03f;
}

[DataDefinition]
public sealed partial class WeatherPuddleData
{
    [DataField("reagent", required: true)]
    public string Reagent = default!;

    [DataField("quantity")]
    public float Quantity = 2f;

    [DataField("chance")]
    public float Chance = 0.1f;

    /// <summary>
    /// If false, weather will skip tiles that already contain puddles.
    /// </summary>
    [DataField("allowDuplicates")]
    public bool AllowDuplicates;

    /// <summary>
    /// If greater than zero, spawned puddles receive/refresh a timed despawn with this lifetime.
    /// </summary>
    [DataField("lifetime")]
    public float Lifetime;
}
