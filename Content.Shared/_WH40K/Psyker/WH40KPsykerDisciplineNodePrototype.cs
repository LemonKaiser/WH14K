using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Psyker;

[Prototype("wh40kPsykerDisciplineNode")]
public sealed partial class WH40KPsykerDisciplineNodePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name = string.Empty;

    [DataField("description", required: true)]
    public string Description = string.Empty;

    [DataField("discipline", required: true)]
    public string Discipline = string.Empty;

    [DataField("tier")]
    public int Tier;

    [DataField("x")]
    public float X;

    [DataField("y")]
    public float Y;

    [DataField("cost")]
    public int Cost;

    [DataField("requiredLevel")]
    public int RequiredLevel = 1;

    [DataField("requires")]
    public List<string> Requires = new();

    [DataField("plannedAction")]
    public string? PlannedAction;

    [DataField("instabilityRisk")]
    public float InstabilityRisk;
}
