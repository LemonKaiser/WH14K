using System.Collections.Generic;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Cinematic;

[RegisterComponent]
public sealed partial class WH40KCinematicLavaMarkerComponent : Component
{
    [DataField("flowId", required: true)]
    public string FlowId = "1";

    [DataField("role", required: true)]
    public WH40KCinematicLavaMarkerRole Role = WH40KCinematicLavaMarkerRole.Guide;

    [DataField("nodeIndex")]
    public int NodeIndex = 1;

    [DataField("width")]
    public int Width = 1;

    [DataField("widthShape")]
    public WH40KCinematicLavaWidthShape WidthShape = WH40KCinematicLavaWidthShape.Diamond;

    [DataField("obstacleMode")]
    public WH40KCinematicLavaObstacleMode ObstacleMode = WH40KCinematicLavaObstacleMode.StopOnWallOrEmpty;

    [DataField("preserveExistingFloor")]
    public bool PreserveExistingFloor = true;

    [DataField("floorTile")]
    public ProtoId<ContentTileDefinition> FloorTile = "FloorBasalt";

    [DataField("lavaPrototype")]
    public EntProtoId LavaPrototype = "FloorLavaEntity";

    [DataField("advanceInterval")]
    public float AdvanceIntervalSeconds = 0.05f;

    [DataField("tilesPerAdvance")]
    public int TilesPerAdvance = 1;

    [DataField("startClearRadius")]
    public int StartClearRadius = 0;

    [DataField("startClearPrototypes")]
    public List<EntProtoId> StartClearPrototypes = new();

    [DataField("startFillRadius")]
    public int StartFillRadius = 0;

    [DataField("startFillShape")]
    public WH40KCinematicLavaWidthShape StartFillShape = WH40KCinematicLavaWidthShape.Square;
}
