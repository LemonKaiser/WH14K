using System;
using System.Numerics;
using Content.Shared._WH40K.Vehicle.Fuel;
using Content.Shared._WH40K.Vehicle.Visuals;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Vehicle.Components;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WH40K.Vehicle.Movement;

public abstract class SharedWH40KVehicleCarMovementController : VirtualController
{
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<VehicleComponent> _vehicleQuery;
    private EntityQuery<BuckleComponent> _buckleQuery;
    private EntityQuery<StrapComponent> _strapQuery;
    private EntityQuery<WH40KVehicleEngineComponent> _engineQuery;
    private EntityQuery<WH40KVehicleFuelComponent> _fuelQuery;
    private EntityQuery<WH40KVehicleHandlingHealthComponent> _handlingQuery;
    private EntityQuery<WH40KVehicleRiderSeatComponent> _riderSeatQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SharedMoverController));

        _vehicleQuery = GetEntityQuery<VehicleComponent>();
        _buckleQuery = GetEntityQuery<BuckleComponent>();
        _strapQuery = GetEntityQuery<StrapComponent>();
        _engineQuery = GetEntityQuery<WH40KVehicleEngineComponent>();
        _fuelQuery = GetEntityQuery<WH40KVehicleFuelComponent>();
        _handlingQuery = GetEntityQuery<WH40KVehicleHandlingHealthComponent>();
        _riderSeatQuery = GetEntityQuery<WH40KVehicleRiderSeatComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<WH40KVehicleCarMovementComponent, ComponentStartup>(OnCarMovementStartup);
        SubscribeLocalEvent<WH40KVehicleCarMovementComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<WH40KVehicleCarMovementComponent, RefreshFrictionModifiersEvent>(OnRefreshFriction);

        base.Initialize();
    }

    private void OnCarMovementStartup(Entity<WH40KVehicleCarMovementComponent> ent, ref ComponentStartup args)
    {
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
        _movement.RefreshFrictionModifiers(ent.Owner);
    }

    private void OnRefreshMovementSpeed(Entity<WH40KVehicleCarMovementComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        // The normal mover treats WASD as direct movement. This vehicle controller consumes the same input as car controls.
        args.ModifySpeed(0f);
    }

    private void OnRefreshFriction(Entity<WH40KVehicleCarMovementComponent> ent, ref RefreshFrictionModifiersEvent args)
    {
        // Keep the default mover from fighting our own traction, braking and coasting model.
        args.ModifyFriction(0f);
        args.ModifyAcceleration(0f);
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        var dt = Math.Clamp(frameTime, 0f, 0.1f);
        if (dt <= 0f)
            return;

        var query = EntityQueryEnumerator<WH40KVehicleCarMovementComponent, InputMoverComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var car, out var mover, out var body, out var xform))
        {
            if (body.BodyType is BodyType.Static)
                continue;

            SimulateCar(uid, car, mover, body, xform, dt);
        }
    }

    private void SimulateCar(
        EntityUid uid,
        WH40KVehicleCarMovementComponent car,
        InputMoverComponent mover,
        PhysicsComponent body,
        TransformComponent xform,
        float frameTime)
    {
        var worldRot = _transform.GetWorldRotation(xform);
        var forward = worldRot.ToWorldVec();
        var right = new Vector2(forward.Y, -forward.X);
        var velocity = body.LinearVelocity;

        var forwardSpeed = Vector2.Dot(velocity, forward);
        var lateralSpeed = Vector2.Dot(velocity, right);
        var buttons = mover.HeldMoveButtons;
        var hasOperator = HasOperator(uid);
        var canApplyEnginePower = hasOperator && CanApplyEnginePower(uid);

        var throttleInput = hasOperator ? GetThrottleInput(buttons) : 0f;
        var steerInput = hasOperator ? GetSteerInput(buttons) : 0f;

        UpdateSteering(car, steerInput, frameTime);
        UpdateForwardSpeed(car, throttleInput, canApplyEnginePower, frameTime, ref forwardSpeed);
        UpdateLateralSpeed(car, frameTime, ref lateralSpeed);

        worldRot = ApplySteering(uid, car, worldRot, forwardSpeed, frameTime);
        forward = worldRot.ToWorldVec();
        right = new Vector2(forward.Y, -forward.X);

        var engineIsPushing = canApplyEnginePower && MathF.Abs(throttleInput) > 0f;
        if (!engineIsPushing && MathF.Abs(forwardSpeed) < car.StopSpeed)
            forwardSpeed = 0f;

        if (MathF.Abs(lateralSpeed) < car.StopSpeed)
            lateralSpeed = 0f;

        var finalVelocity = forward * forwardSpeed + right * lateralSpeed;
        _physics.SetLinearVelocity(uid, finalVelocity, body: body);
        _physics.SetAngularVelocity(uid, 0f, body: body);
        SyncBuckledOperatorPose(uid, xform);
    }

    private void SyncBuckledOperatorPose(EntityUid uid, TransformComponent vehicleXform)
    {
        if (!_strapQuery.TryComp(uid, out var strap))
            return;

        if (_riderSeatQuery.TryComp(uid, out var seats))
        {
            var seatCount = Math.Min(seats.SeatOffsets.Count, seats.SeatOccupants.Count);
            for (var i = 0; i < seatCount; i++)
            {
                var rider = seats.SeatOccupants[i];
                SyncBuckledRiderPose(uid, rider, strap.BuckleOffset, vehicleXform, inheritVehicleRotation: true);
            }

            return;
        }

        if (!_vehicleQuery.TryComp(uid, out var vehicle) || vehicle.Operator is not { } operatorUid)
            return;

        SyncBuckledRiderPose(uid, operatorUid, strap.BuckleOffset, vehicleXform, inheritVehicleRotation: false);
    }

    private void SyncBuckledRiderPose(
        EntityUid vehicleUid,
        EntityUid riderUid,
        Vector2 localSeatOffset,
        TransformComponent vehicleXform,
        bool inheritVehicleRotation)
    {
        if (!_buckleQuery.TryComp(riderUid, out var buckle) ||
            buckle.BuckledTo != vehicleUid ||
            !_xformQuery.TryComp(riderUid, out var riderXform) ||
            riderXform.ParentUid != vehicleUid)
        {
            return;
        }

        // Single-seat generic vehicles keep humanoids upright. Motorbike-style rider seats inherit vehicle rotation
        // so the rider visually follows diagonal turns instead of standing still on top of a rotating bike.
        var desiredLocalRotation = inheritVehicleRotation
            ? Angle.Zero
            : new Angle(-_transform.GetWorldRotation(vehicleXform).Theta);

        _transform.SetCoordinates(riderUid, riderXform, new EntityCoordinates(vehicleUid, localSeatOffset), desiredLocalRotation);
        riderXform.ActivelyLerping = false;
    }

    private bool HasOperator(EntityUid uid)
    {
        return _vehicleQuery.TryComp(uid, out var vehicle) && vehicle.Operator != null;
    }

    private bool CanApplyEnginePower(EntityUid uid)
    {
        if (!HasOperator(uid))
            return false;

        if (_engineQuery.TryComp(uid, out var engine) &&
            engine.State != WH40KVehicleEngineState.Running)
        {
            return false;
        }

        if (_fuelQuery.TryComp(uid, out var fuel) &&
            fuel.FuelLevel <= 0.01f)
        {
            return false;
        }

        if (_handlingQuery.TryComp(uid, out var handling) &&
            handling.ServiceState == WH40KVehicleServiceState.Disabled)
        {
            return false;
        }

        return true;
    }

    private static void UpdateSteering(WH40KVehicleCarMovementComponent car, float steerInput, float frameTime)
    {
        var rate = MathF.Abs(steerInput) > 0f ? car.SteerInputRate : car.SteerReturnRate;
        car.CurrentSteer = Approach(car.CurrentSteer, steerInput, rate * frameTime);
    }

    private static void UpdateForwardSpeed(
        WH40KVehicleCarMovementComponent car,
        float throttleInput,
        bool canApplyEnginePower,
        float frameTime,
        ref float forwardSpeed)
    {
        if (!canApplyEnginePower)
        {
            var deceleration = IsBrakingInput(throttleInput, forwardSpeed, car.StopSpeed)
                ? car.BrakeDeceleration
                : car.CoastDeceleration;

            forwardSpeed = Approach(forwardSpeed, 0f, deceleration * frameTime);
            return;
        }

        if (throttleInput > 0f)
        {
            if (forwardSpeed < -car.StopSpeed)
            {
                forwardSpeed = Approach(forwardSpeed, 0f, car.BrakeDeceleration * frameTime);
                return;
            }

            var speedRatio = Math.Clamp(forwardSpeed / Math.Max(car.MaxForwardSpeed, 0.01f), 0f, 1f);
            var engineForce = car.ForwardAcceleration * MathF.Pow(1f - speedRatio, car.EngineFalloffPower);
            forwardSpeed = Approach(forwardSpeed, car.MaxForwardSpeed, engineForce * frameTime);
            return;
        }

        if (throttleInput < 0f)
        {
            if (forwardSpeed > car.StopSpeed)
            {
                forwardSpeed = Approach(forwardSpeed, 0f, car.BrakeDeceleration * frameTime);
                return;
            }

            forwardSpeed = Approach(forwardSpeed, -car.MaxReverseSpeed, car.ReverseAcceleration * frameTime);
            return;
        }

        forwardSpeed = Approach(forwardSpeed, 0f, car.CoastDeceleration * frameTime);
    }

    private static bool IsBrakingInput(float throttleInput, float forwardSpeed, float stopSpeed)
    {
        return throttleInput > 0f && forwardSpeed < -stopSpeed ||
               throttleInput < 0f && forwardSpeed > stopSpeed;
    }

    private static void UpdateLateralSpeed(WH40KVehicleCarMovementComponent car, float frameTime, ref float lateralSpeed)
    {
        lateralSpeed = Approach(lateralSpeed, 0f, car.LateralGrip * frameTime);
    }

    private Angle ApplySteering(
        EntityUid uid,
        WH40KVehicleCarMovementComponent car,
        Angle worldRot,
        float forwardSpeed,
        float frameTime)
    {
        var speed = MathF.Abs(forwardSpeed);
        if (speed < car.StopSpeed || MathF.Abs(car.CurrentSteer) < 0.001f)
            return worldRot;

        var speedRatio = Math.Clamp(speed / Math.Max(car.MaxForwardSpeed, 0.01f), 0f, 1f);
        var steerBuild = Math.Clamp(speed / Math.Max(car.SpeedForFullSteer, 0.01f), 0f, 1f);
        var turnRateDegrees = Lerp(car.LowSpeedTurnRateDegrees, car.HighSpeedTurnRateDegrees, speedRatio);
        var turnRate = turnRateDegrees * MathF.PI / 180f;
        var reverseSign = forwardSpeed < 0f ? -1f : 1f;
        var rotationDelta = car.CurrentSteer * reverseSign * turnRate * steerBuild * frameTime;

        var newRot = worldRot + new Angle(rotationDelta);
        _transform.SetWorldRotation(uid, newRot);
        return newRot;
    }

    private static float GetThrottleInput(MoveButtons buttons)
    {
        var throttle = 0f;

        if ((buttons & MoveButtons.Up) != 0)
            throttle += 1f;

        if ((buttons & MoveButtons.Down) != 0)
            throttle -= 1f;

        return Math.Clamp(throttle, -1f, 1f);
    }

    private static float GetSteerInput(MoveButtons buttons)
    {
        var steer = 0f;

        if ((buttons & MoveButtons.Left) != 0)
            steer += 1f;

        if ((buttons & MoveButtons.Right) != 0)
            steer -= 1f;

        return Math.Clamp(steer, -1f, 1f);
    }

    private static float Approach(float current, float target, float maxDelta)
    {
        if (current < target)
            return MathF.Min(current + maxDelta, target);

        if (current > target)
            return MathF.Max(current - maxDelta, target);

        return current;
    }

    private static float Lerp(float from, float to, float t)
    {
        return from + (to - from) * Math.Clamp(t, 0f, 1f);
    }
}
