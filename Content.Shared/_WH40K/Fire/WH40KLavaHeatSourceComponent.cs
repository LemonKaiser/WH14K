using System;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Fire;

[RegisterComponent]
public sealed partial class WH40KLavaHeatSourceComponent : Component
{
    [DataField("sourceTileMinTemperature")]
    public float SourceTileMinTemperature = 1100f;

    [DataField("onlyOutdoorAtmosphericHeat")]
    public bool OnlyOutdoorAtmosphericHeat = true;

    [DataField("heatIntervalSeconds")]
    public float HeatIntervalSeconds = 0.5f;

    [DataField("exposeSourceHotspot")]
    public bool ExposeSourceHotspot = true;

    [DataField("hotspotTemperature")]
    public float HotspotTemperature = 1400f;

    [DataField("hotspotVolume")]
    public float HotspotVolume = 35f;

    [DataField("igniteRadius")]
    public int IgniteRadius = 0;

    [DataField("igniteIntervalSeconds")]
    public float IgniteIntervalSeconds = 0.35f;

    [DataField("igniteFireStacks")]
    public float IgniteFireStacks = 2.5f;

    [DataField("burnableTileIgniteRadius")]
    public int BurnableTileIgniteRadius = 1;

    [ViewVariables]
    public TimeSpan NextHeatAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextIgniteAt = TimeSpan.Zero;
}
