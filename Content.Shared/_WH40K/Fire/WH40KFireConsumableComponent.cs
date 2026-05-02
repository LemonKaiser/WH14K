using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Fire;

[RegisterComponent]
public sealed partial class WH40KFireConsumableComponent : Component
{
    [DataField("burnTime")]
    public float BurnTimeSeconds = 5f;

    [DataField("resultPrototype")]
    public EntProtoId? ResultPrototype;

    [DataField("deleteOnBurn")]
    public bool DeleteOnBurn = true;

    [DataField("hotspotTemperature")]
    public float HotspotTemperature = 1100f;

    [DataField("hotspotVolume")]
    public float HotspotVolume = 25f;

    [DataField("hotspotExposeInterval")]
    public float HotspotExposeIntervalSeconds = 1f;

    [DataField("spreadRadius")]
    public int SpreadRadius = 1;

    [DataField("spreadInterval")]
    public float SpreadIntervalSeconds = 1f;

    [DataField("spreadFireStacks")]
    public float SpreadFireStacks = 1f;

    [DataField("spreadHotspotTemperature")]
    public float SpreadHotspotTemperature = 900f;

    [DataField("spreadHotspotVolume")]
    public float SpreadHotspotVolume = 15f;

    [DataField("resetProgressOnExtinguish")]
    public bool ResetProgressOnExtinguish = true;

    [DataField("burnWithoutAtmosphere")]
    public bool BurnWithoutAtmosphere;

    [ViewVariables]
    public float BurnAccumulatedSeconds;

    [ViewVariables]
    public TimeSpan NextHotspotExposeTime = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextSpreadTime = TimeSpan.Zero;
}
