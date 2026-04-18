using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Owner-only astral discipline progression for the Imperium psyker path.
/// Keeps unlocked constellation nodes and point economy separate from chaos progression.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WH40KPsykerAstralProgressionComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [DataField, AutoNetworkedField]
    public int DisciplinePoints;

    [DataField, AutoNetworkedField]
    public int TotalDisciplinePointsEarned;

    [DataField, AutoNetworkedField]
    public List<string> UnlockedNodes = new();

    [DataField, AutoNetworkedField]
    public List<string> PendingUnlockEffects = new();

    [DataField, AutoNetworkedField]
    public int AstralDepth = 1;

    [DataField, AutoNetworkedField]
    public float AstralStrain;

    [DataField, AutoNetworkedField]
    public string ConstellationLayoutId = string.Empty;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan AstralFatigueUntil;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan LastAstralSessionAt;

    [DataField, AutoNetworkedField]
    public int UnlockedCapstoneCount;

    [DataField, AutoNetworkedField]
    public List<WH40KPsykerAstralCollectibleStar> CollectibleStars = new();

    [ViewVariables]
    public TimeSpan NextStrainDecayAt;

    [ViewVariables]
    public TimeSpan NextCollectibleStarAt;

    [ViewVariables]
    public int NextCollectibleStarId = 1;
}

[DataDefinition, Serializable, NetSerializable]
public readonly partial record struct WH40KPsykerAstralCollectibleStar(
    [property: DataField("id")] int Id,
    [property: DataField("x")] float X,
    [property: DataField("y")] float Y,
    [property: DataField("xpReward")] int XpReward,
    [property: DataField("scale")] float Scale,
    [property: DataField("variant")] byte Variant)
{
    public WH40KPsykerAstralCollectibleStar() : this(0, 0.5f, 0.5f, 5, 1f, 0)
    {
    }
}
