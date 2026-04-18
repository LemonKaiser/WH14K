using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._WH40K.Vehicle.Fuel;
using Content.Shared._WH40K.Vehicle.Movement;
using Content.Shared._WH40K.Vehicle.Visuals;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using ServerCarMovementController = Content.Server._WH40K.Vehicle.Movement.WH40KVehicleCarMovementController;

namespace Content.IntegrationTests.Tests._WH40K.Vehicle;

[TestFixture]
[TestOf(typeof(WH40KVehicleCarMovementComponent))]
public sealed class WH40KVehicleMovementTests : GameTest
{
    private const string MotorbikePrototype = "WH40KVehicleMotorbikeImperium";
    private static readonly string[] FabricatedMotorbikePrototypes =
    {
        "WH40KVehicleMotorbikeImperium",
        "WH40KVehicleMotorbikeChaos",
    };

    private const string DriverPrototype = "MobHuman";

    [Test]
    public async Task FabricatedMotorbikesAreUnarmedAndUseFullFifteenMinuteTank()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var fuelSystem = SEntMan.System<SharedWH40KVehicleFuelSystem>();

            foreach (var prototype in FabricatedMotorbikePrototypes)
            {
                var vehicle = SEntMan.SpawnEntity(prototype, map.GridCoords);
                var fuel = SComp<WH40KVehicleFuelComponent>(vehicle);

                Assert.Multiple(() =>
                {
                    Assert.That(SEntMan.HasComponent<GunComponent>(vehicle), Is.False, $"{prototype} should be an unarmed transport bike.");
                    Assert.That(SEntMan.HasComponent<BasicEntityAmmoProviderComponent>(vehicle), Is.False, $"{prototype} should not spawn hidden vehicle ammunition.");
                    Assert.That(fuel.FuelCapacity, Is.EqualTo(900f).Within(0.001f), $"{prototype} should keep a 900u tank.");
                    Assert.That(fuel.FuelLevel, Is.EqualTo(fuel.FuelCapacity).Within(0.001f), $"{prototype} should spawn with a full fabricated tank.");
                    Assert.That(fuel.FullTankRuntime, Is.EqualTo(15 * 60).Within(0.001f), $"{prototype} should burn a full tank over 15 minutes.");
                    Assert.That(fuelSystem.GetFuelBurnPerSecond(fuel), Is.EqualTo(1f).Within(0.001f), $"{prototype} should burn 1u/s from a full 900u tank.");
                });
            }
        });
    }

    [Test]
    public async Task MotorbikeCanStartEngineAndAccelerateForward()
    {
        var (vehicle, _) = await SpawnOccupiedRunningMotorbike();

        var startPosition = Vector2.Zero;
        var earlySpeed = 0f;
        var buttonsAfterThrottleTicks = MoveButtons.None;
        var finalPosition = Vector2.Zero;
        var finalSpeed = 0f;

        await Server.WaitAssertion(() =>
        {
            startPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(vehicle);
            SetMoveButtons(vehicle, MoveButtons.Up | MoveButtons.Walk);
        });

        await Pair.RunTicksSync(6);

        await Server.WaitAssertion(() =>
        {
            earlySpeed = SComp<PhysicsComponent>(vehicle).LinearVelocity.Length();
            buttonsAfterThrottleTicks = SComp<InputMoverComponent>(vehicle).HeldMoveButtons;
        });

        await Pair.RunTicksSync(84);

        await Server.WaitAssertion(() =>
        {
            finalPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(vehicle);
            finalSpeed = SComp<PhysicsComponent>(vehicle).LinearVelocity.Length();
            SetMoveButtons(vehicle, MoveButtons.Walk);
        });

        var displacement = (finalPosition - startPosition).Length();

        Assert.Multiple(() =>
        {
            var mover = SComp<InputMoverComponent>(vehicle);
            var engine = SComp<WH40KVehicleEngineComponent>(vehicle);
            var fuel = SComp<WH40KVehicleFuelComponent>(vehicle);
            var physics = SComp<PhysicsComponent>(vehicle);
            var diagnostic = $"buttonsNow={mover.HeldMoveButtons}, buttonsAfterThrottleTicks={buttonsAfterThrottleTicks}, canMove={mover.CanMove}, engine={engine.State}, fuel={fuel.FuelLevel}, body={physics.BodyType}, canCollide={physics.CanCollide}, awake={physics.Awake}, vel={physics.LinearVelocity}";

            Assert.That(displacement, Is.GreaterThan(2.0f), $"Motorbike did not travel forward after throttle input. {diagnostic}");
            Assert.That(finalSpeed, Is.GreaterThan(1.0f), $"Motorbike never built meaningful forward speed. {diagnostic}");
            Assert.That(earlySpeed, Is.LessThan(finalSpeed * 0.65f), "Motorbike acceleration is too instant; early speed is too close to final speed.");
        });
    }

    [Test]
    public async Task MotorbikeDoesNotLoseSpeedWhenCrossingPuddlesOrLoosePaper()
    {
        var (vehicle, _) = await SpawnOccupiedRunningMotorbike();

        await Server.WaitAssertion(() => SetMoveButtons(vehicle, MoveButtons.Up | MoveButtons.Walk));
        await Pair.RunTicksSync(90);

        var speedBefore = 0f;
        await Server.WaitAssertion(() =>
        {
            speedBefore = SComp<PhysicsComponent>(vehicle).LinearVelocity.Length();
            var coordinates = SComp<TransformComponent>(vehicle).Coordinates;

            SEntMan.SpawnEntity("PuddleBlood", coordinates);
            SEntMan.SpawnEntity("Paper", coordinates);
        });

        await Pair.RunTicksSync(10);

        var speedAfter = 0f;
        await Server.WaitAssertion(() =>
        {
            speedAfter = SComp<PhysicsComponent>(vehicle).LinearVelocity.Length();
            SetMoveButtons(vehicle, MoveButtons.Walk);
        });

        Assert.Multiple(() =>
        {
            Assert.That(speedBefore, Is.GreaterThan(2.75f), "Motorbike did not reach ramming speed before the collision filter check.");
            Assert.That(speedAfter, Is.GreaterThan(speedBefore * 0.7f), "Motorbike ram handling should ignore non-solid puddles and loose paper instead of braking like a hard impact.");
        });
    }

    [Test]
    public async Task MotorbikeRightSteeringIsSmoothAndBuildsTurnOverTime()
    {
        var (vehicle, _) = await SpawnOccupiedRunningMotorbike();

        await Server.WaitAssertion(() => SetMoveButtons(vehicle, MoveButtons.Up | MoveButtons.Walk));
        await Pair.RunTicksSync(45);

        var rotations = new List<double>();
        var speeds = new List<float>();

        await Server.WaitAssertion(() => SetMoveButtons(vehicle, MoveButtons.Up | MoveButtons.Right | MoveButtons.Walk));

        for (var i = 0; i < 75; i++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitAssertion(() =>
            {
                rotations.Add(SEntMan.System<SharedTransformSystem>().GetWorldRotation(vehicle).Theta);
                speeds.Add(SComp<PhysicsComponent>(vehicle).LinearVelocity.Length());
            });
        }

        await Server.WaitAssertion(() => SetMoveButtons(vehicle, MoveButtons.Walk));

        var totalTurnDegrees = Math.Abs(ShortestAngleDegrees(rotations[0], rotations[^1]));
        var perTickTurns = rotations.Zip(rotations.Skip(1), (from, to) => Math.Abs(ShortestAngleDegrees(from, to))).ToArray();
        var firstTickTurn = perTickTurns[0];
        var maxTickTurn = perTickTurns.Max();
        var averageSpeed = speeds.Average();

        Assert.Multiple(() =>
        {
            Assert.That(averageSpeed, Is.GreaterThan(1.0f), "Steering test never reached a useful driving speed.");
            Assert.That(totalTurnDegrees, Is.GreaterThan(18.0), "Motorbike did not meaningfully turn while steering right.");
            Assert.That(totalTurnDegrees, Is.LessThan(150.0), "Motorbike turned unrealistically far for this short steering window.");
            Assert.That(firstTickTurn, Is.LessThan(2.0), "Steering snaps too hard on the first tick instead of ramping in.");
            Assert.That(maxTickTurn, Is.LessThan(6.0), "Steering has a per-tick rotation spike instead of smooth turning.");
            Assert.That(perTickTurns.Count(turn => turn > 0.05), Is.GreaterThan(25), "Steering should be distributed over many ticks, not one big snap.");
        });
    }

    [Test]
    public async Task MotorbikeAcceptsTwoRidersAndKeepsFirstRiderDriving()
    {
        var map = await Pair.CreateTestMap();
        EntityUid vehicle = default;
        EntityUid driver = default;
        EntityUid passenger = default;
        EntityUid thirdRider = default;

        await Server.WaitAssertion(() =>
        {
            vehicle = SEntMan.SpawnEntity(MotorbikePrototype, map.GridCoords);
            driver = SEntMan.SpawnEntity(DriverPrototype, map.GridCoords);
            passenger = SEntMan.SpawnEntity(DriverPrototype, map.GridCoords);
            thirdRider = SEntMan.SpawnEntity(DriverPrototype, map.GridCoords);

            var buckleSystem = SEntMan.System<SharedBuckleSystem>();

            Assert.That(buckleSystem.TryBuckle(driver, driver, vehicle, popup: false), Is.True, "Driver failed to buckle into the motorbike.");
            Assert.That(buckleSystem.TryBuckle(passenger, passenger, vehicle, popup: false), Is.True, "Passenger failed to buckle into the second motorbike seat.");
            Assert.That(buckleSystem.TryBuckle(thirdRider, thirdRider, vehicle, popup: false), Is.False, "Motorbike accepted a third rider even though it only has two seats.");

            var strap = SComp<StrapComponent>(vehicle);
            var seats = SComp<WH40KVehicleRiderSeatComponent>(vehicle);
            var vehicleComp = SComp<VehicleComponent>(vehicle);

            Assert.That(strap.BuckledEntities, Has.Count.EqualTo(2), "Motorbike should physically hold exactly two buckled riders.");
            Assert.That(seats.SeatOccupants, Is.EqualTo(new[] { driver, passenger }), "Motorbike visual seats should preserve buckle order.");
            Assert.That(vehicleComp.Operator, Is.EqualTo(driver), "Passenger should not steal vehicle controls from the first rider.");
        });
    }

    [Test]
    public async Task MotorbikeKeepsBuckledRidersCenteredRotatedAndConfiguredForSeparateSeatsWhileTurning()
    {
        var (vehicle, driver) = await SpawnOccupiedRunningMotorbike();
        EntityUid passenger = default;

        await Server.WaitAssertion(() =>
        {
            passenger = SEntMan.SpawnEntity(DriverPrototype, SComp<TransformComponent>(vehicle).Coordinates);
            var buckleSystem = SEntMan.System<SharedBuckleSystem>();
            Assert.That(buckleSystem.TryBuckle(passenger, passenger, vehicle, popup: false), Is.True, "Passenger failed to buckle into the motorbike.");
            Assert.That(SComp<VehicleComponent>(vehicle).Operator, Is.EqualTo(driver), "Passenger should not replace the original driver.");
        });

        await Server.WaitAssertion(() => SetMoveButtons(vehicle, MoveButtons.Up | MoveButtons.Right | MoveButtons.Walk));
        await Pair.RunTicksSync(75);

        var driverLocalPosition = Vector2.Zero;
        var passengerLocalPosition = Vector2.Zero;
        var strapOffset = Vector2.Zero;
        var driverVisualOffset = Vector2.Zero;
        var passengerVisualOffset = Vector2.Zero;
        var vehicleRotation = 0.0;
        var driverRotation = 0.0;
        var passengerRotation = 0.0;

        await Server.WaitAssertion(() =>
        {
            var xform = SEntMan.System<SharedTransformSystem>();
            var driverXform = SComp<TransformComponent>(driver);
            var passengerXform = SComp<TransformComponent>(passenger);
            var strap = SComp<StrapComponent>(vehicle);
            var seats = SComp<WH40KVehicleRiderSeatComponent>(vehicle);

            driverLocalPosition = driverXform.LocalPosition;
            passengerLocalPosition = passengerXform.LocalPosition;
            strapOffset = strap.BuckleOffset;
            driverVisualOffset = seats.SeatOffsets[0];
            passengerVisualOffset = seats.SeatOffsets[1];
            vehicleRotation = xform.GetWorldRotation(vehicle).Theta;
            driverRotation = xform.GetWorldRotation(driver).Theta;
            passengerRotation = xform.GetWorldRotation(passenger).Theta;

            Assert.That(driverXform.ParentUid, Is.EqualTo(vehicle), "Driver should remain buckled to the motorbike while it turns.");
            Assert.That(passengerXform.ParentUid, Is.EqualTo(vehicle), "Passenger should remain buckled to the motorbike while it turns.");
            SetMoveButtons(vehicle, MoveButtons.Walk);
        });

        Assert.Multiple(() =>
        {
            Assert.That((driverLocalPosition - strapOffset).Length(), Is.LessThan(0.01f), "Driver drifted away from the motorbike strap offset while the vehicle turned.");
            Assert.That((passengerLocalPosition - strapOffset).Length(), Is.LessThan(0.01f), "Passenger drifted away from the motorbike strap offset while the vehicle turned.");
            Assert.That((driverVisualOffset - passengerVisualOffset).Length(), Is.GreaterThan(0.25f), "Driver and passenger visual seats should not be stacked into the same motorbike seat.");
            Assert.That(driverVisualOffset.X, Is.GreaterThan(passengerVisualOffset.X), "Driver visual seat should be forward of the passenger visual seat in motorbike-local space.");
            Assert.That(Math.Abs(ShortestAngleDegrees(0.0, vehicleRotation)), Is.GreaterThan(12.0), "Motorbike did not turn enough for the rider pose check to matter.");
            Assert.That(Math.Abs(ShortestAngleDegrees(vehicleRotation, driverRotation)), Is.LessThan(2.0), "Buckled driver should inherit the motorbike transform rotation while turning.");
            Assert.That(Math.Abs(ShortestAngleDegrees(vehicleRotation, passengerRotation)), Is.LessThan(2.0), "Buckled passenger should inherit the motorbike transform rotation while turning.");
        });
    }

    private async Task<(EntityUid Vehicle, EntityUid Driver)> SpawnOccupiedRunningMotorbike()
    {
        var map = await Pair.CreateTestMap();
        EntityUid vehicle = default;
        EntityUid driver = default;

        await Server.WaitAssertion(() =>
        {
            vehicle = SEntMan.SpawnEntity(MotorbikePrototype, map.GridCoords);
            driver = SEntMan.SpawnEntity(DriverPrototype, map.GridCoords);

            Assert.That(SEntMan.HasComponent<WH40KVehicleCarMovementComponent>(vehicle), Is.True, "Motorbike prototype is missing WH40KVehicleCarMovement.");
            Assert.That(SEntMan.HasComponent<InputMoverComponent>(vehicle), Is.True, "Motorbike prototype is missing InputMover.");
            Assert.That(SEntMan.HasComponent<PhysicsComponent>(vehicle), Is.True, "Motorbike prototype is missing Physics.");
            Assert.That(SEntMan.EntitySysManager.TryGetEntitySystem<ServerCarMovementController>(out _), Is.True, "Server WH40K car movement controller is not registered.");

            var fuel = SComp<WH40KVehicleFuelComponent>(vehicle);
            Assert.That(fuel.FuelLevel, Is.GreaterThan(0f), "Fabricated motorbike should spawn with starter fuel for immediate use tests.");

            var buckleSystem = SEntMan.System<SharedBuckleSystem>();
            Assert.That(buckleSystem.TryBuckle(driver, driver, vehicle, popup: false), Is.True, "Test driver failed to buckle into the motorbike.");

            var engine = SComp<WH40KVehicleEngineComponent>(vehicle);
            Assert.That(engine.ToggleActionEntity, Is.Not.Null, "Engine toggle action was not granted by the vehicle.");

            var actions = SEntMan.System<SharedActionsSystem>();
            Assert.That(actions.TryPerformAction(driver, engine.ToggleActionEntity!.Value, null, null, predicted: false), Is.True, "Engine toggle action failed.");
            Assert.That(engine.State, Is.EqualTo(WH40KVehicleEngineState.Starting), "Engine did not enter starting state.");
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<WH40KVehicleEngineComponent>(vehicle).State, Is.EqualTo(WH40KVehicleEngineState.Running), "Engine did not reach running state.");
            Assert.That(SComp<WH40KVehicleFuelComponent>(vehicle).FuelLevel, Is.GreaterThan(0f), "Motorbike ran out of fuel before movement test.");
            Assert.That(SComp<VehicleComponent>(vehicle).Operator, Is.EqualTo(driver), "Motorbike lost its test operator.");
            Assert.That(SComp<InputMoverComponent>(vehicle).CanMove, Is.True, "Motorbike InputMover is blocked even after the engine is running.");
        });

        return (vehicle, driver);
    }

    private void SetMoveButtons(EntityUid vehicle, MoveButtons buttons)
    {
        SComp<InputMoverComponent>(vehicle).HeldMoveButtons = buttons;
    }

    private static double ShortestAngleDegrees(double from, double to)
    {
        var delta = (to - from) % Math.Tau;
        delta = (2 * delta) % Math.Tau - delta;
        return delta * 180.0 / Math.PI;
    }
}
