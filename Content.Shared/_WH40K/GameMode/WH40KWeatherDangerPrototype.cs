using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.GameMode;

[Prototype("wh40kWeatherDangerProfile")]
public sealed partial class WH40KWeatherDangerProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultDanger")]
    public int DefaultDanger = 3;

    [DataField("weatherDanger")]
    public List<WH40KWeatherDangerEntry> WeatherDanger = new();
}

[DataDefinition]
public sealed partial class WH40KWeatherDangerEntry
{
    [DataField("weatherId", required: true)]
    public EntProtoId WeatherId = string.Empty;

    [DataField("danger")]
    public int Danger = 3;
}
