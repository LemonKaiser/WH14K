using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Imperium psyker progression state for meditation and active casting growth.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WH40KPsykerProgressionComponent : Component
{
    [DataField("level"), AutoNetworkedField]
    public int Level = 1;

    [DataField("levelXp"), AutoNetworkedField]
    public float LevelXp;

    [DataField("totalXp"), AutoNetworkedField]
    public float TotalXp;

    [DataField("maxLevel"), AutoNetworkedField]
    public int MaxLevel = 10;

    [DataField("baseXpForNextLevel")]
    public float BaseXpForNextLevel = 100f;

    [DataField("xpGrowthFactor")]
    public float XpGrowthFactor = 1.35f;

    [DataField("meditationInterval"), AutoNetworkedField]
    public TimeSpan MeditationInterval = TimeSpan.FromSeconds(10);

    [DataField("meditationXpPerInterval"), AutoNetworkedField]
    public float MeditationXpPerInterval = 1.5f;

    [DataField("meditationBedBonusMultiplier")]
    public float MeditationBedBonusMultiplier = 1.5f;

    [DataField("castXpBase"), AutoNetworkedField]
    public float CastXpBase = 4f;

    [DataField("castRepeatWindow")]
    public TimeSpan CastRepeatWindow = TimeSpan.FromSeconds(12);

    [DataField("castRepeatFalloff")]
    public float CastRepeatFalloff = 0.75f;

    [DataField("castMinMultiplier")]
    public float CastMinMultiplier = 0.25f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextMeditationAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LastCastAt;

    [DataField]
    public string? LastCastActionPrototype;

    [DataField]
    public int RepeatCastStreak;
}
