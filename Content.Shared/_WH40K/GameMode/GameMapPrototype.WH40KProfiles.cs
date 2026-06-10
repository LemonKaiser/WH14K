using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Utility;

namespace Content.Shared.Maps;

public sealed partial class GameMapPrototype
{
    [DataField("wh40kWeatherProfile")]
    public string? WH40KWeatherProfile { get; private set; }

    [DataField("wh40kEventsProfile")]
    public string? WH40KEventsProfile { get; private set; }

    [DataField("wh40kTacticalMapSnapshot")]
    public ResPath? WH40KTacticalMapSnapshot { get; private set; }
}
