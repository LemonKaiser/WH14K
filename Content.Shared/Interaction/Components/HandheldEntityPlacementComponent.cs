using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Interaction.Components;

[RegisterComponent]
public sealed partial class HandheldEntityPlacementComponent : Component
{
    [DataField("entityType")]
    public EntProtoId EntityType = default!;

    [DataField("previewEntityType")]
    public EntProtoId? PreviewEntityType;

    [DataField("placementMode")]
    public string PlacementMode = "SnapgridCenter";

    [DataField("range")]
    public int Range = 2;

    [DataField("canRotate")]
    public bool CanRotate = false;
}

[Serializable, NetSerializable]
public sealed partial class RequestHandheldEntityPlacementEvent : EntityEventArgs
{
    public NetEntity Item;
    public NetCoordinates Coordinates;
    public Direction Direction = Direction.Invalid;

    public RequestHandheldEntityPlacementEvent()
    {
    }

    public RequestHandheldEntityPlacementEvent(NetEntity item, NetCoordinates coordinates, Direction direction)
    {
        Item = item;
        Coordinates = coordinates;
        Direction = direction;
    }
}

[Serializable, NetSerializable]
public sealed partial class RequestCancelHandheldEntityPlacementEvent : EntityEventArgs
{
    public NetEntity Item;

    public RequestCancelHandheldEntityPlacementEvent()
    {
    }

    public RequestCancelHandheldEntityPlacementEvent(NetEntity item)
    {
        Item = item;
    }
}
