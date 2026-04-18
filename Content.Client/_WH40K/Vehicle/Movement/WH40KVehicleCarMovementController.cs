using Content.Client.PhysicsSystem.Controllers;
using Content.Shared._WH40K.Vehicle.Movement;

namespace Content.Client._WH40K.Vehicle.Movement;

public sealed class WH40KVehicleCarMovementController : SharedWH40KVehicleCarMovementController
{
    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(MoverController));
        base.Initialize();
    }
}
