using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Vehicle.Visuals;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KVehicleRiderSeatComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<Vector2> SeatOffsets = new();

    [DataField, AutoNetworkedField]
    public List<EntityUid> SeatOccupants = new();
}
