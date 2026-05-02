using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Fire;

[Prototype("wh40kBurnableTile")]
public sealed partial class WH40KBurnableTilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField("tile", required: true)]
    public ProtoId<ContentTileDefinition> Tile;

    [DataField("burnTime")]
    public float BurnTimeSeconds = 3f;

    [DataField("spreadRadius")]
    public int SpreadRadius = 1;

    [DataField("spreadInterval")]
    public float SpreadIntervalSeconds = 1f;

    [DataField("spreadFireStacks")]
    public float SpreadFireStacks = 1f;

    [DataField("contactIgniteInterval")]
    public float ContactIgniteIntervalSeconds = 0.45f;

    [DataField("contactFireStacks")]
    public float ContactFireStacks = 1.5f;

    [DataField("hotspotTemperature")]
    public float HotspotTemperature = 900f;

    [DataField("hotspotVolume")]
    public float HotspotVolume = 15f;

    [DataField("fireEffectPrototype")]
    public EntProtoId FireEffectPrototype = "WH40KTileFireEffect";

    [DataField("resultTile", required: true)]
    public ProtoId<ContentTileDefinition> ResultTile;
}
