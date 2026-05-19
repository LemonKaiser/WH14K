using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.WaveDefence;

[RegisterComponent]
public sealed partial class WH40KWaveSpawnPointComponent : Component
{
    [DataField("spawnType", required: true)]
    public WH40KWaveSpawnPointType SpawnType;

    [DataField("teamId")]
    public string TeamId = string.Empty;

    [DataField("spawnId")]
    public string SpawnId = string.Empty;

    [DataField("laneIds")]
    public List<string> LaneIds = new();

    [DataField("priority")]
    public int Priority = 1;
}

[RegisterComponent]
public sealed partial class WH40KWaveLanePointComponent : Component
{
    [DataField("laneId", required: true)]
    public string LaneId = string.Empty;

    [DataField("pointId")]
    public string PointId = string.Empty;

    [DataField("order")]
    public int Order;

    [DataField("autoOrder")]
    public bool AutoOrder;

    [DataField("pointType")]
    public WH40KWaveLanePointType PointType = WH40KWaveLanePointType.Waypoint;

    [DataField("arrivalRange")]
    public float ArrivalRange;

    [DataField("segmentWidth")]
    public float SegmentWidth;

    [DataField("progressGateWidth")]
    public float ProgressGateWidth;

    [DataField("fallbackAnchor")]
    public bool FallbackAnchor;

    [DataField("allowedRoles")]
    public List<WH40KWaveSquadRole> AllowedRoles = new();

    [DataField("enabled")]
    public bool Enabled = true;
}

[RegisterComponent]
public sealed partial class WH40KWaveImperiumBaseComponent : Component
{
    [DataField("teamId")]
    public string TeamId = "Imperium";
}
