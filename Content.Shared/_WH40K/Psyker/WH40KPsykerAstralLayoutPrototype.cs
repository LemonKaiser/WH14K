using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Psyker;

[Prototype("wh40kPsykerAstralLayout")]
public sealed partial class WH40KPsykerAstralLayoutPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("positions", required: true)]
    public List<WH40KPsykerAstralLayoutNode> Positions = new();
}

[DataDefinition]
public sealed partial class WH40KPsykerAstralLayoutNode
{
    [DataField("node", required: true)]
    public string Node = string.Empty;

    [DataField("x", required: true)]
    public float X;

    [DataField("y", required: true)]
    public float Y;
}
