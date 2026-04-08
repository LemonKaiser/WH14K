using Content.Shared._WH40K.Tank;
using Content.Shared.Buckle.Components;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Tank;

public sealed class WH40KTankInputSystem : EntitySystem
{
    private static readonly TimeSpan AimUpdateInterval = TimeSpan.FromSeconds(0.03f);
    private static readonly Angle AimUpdateTolerance = Angle.FromDegrees(1f);

    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityUid? _lastTank;
    private Angle? _lastAimAngle;
    private TimeSpan _nextAimUpdateAt = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;

        CommandBinds.Builder
            .BindAfter(EngineKeyFunctions.Use, new PointerInputCmdHandler(OnUse, true, true), typeof(SharedInteractionSystem))
            .Register<WH40KTankInputSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<WH40KTankInputSystem>();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted || !_input.MouseScreenPosition.IsValid)
            return;

        if (_player.LocalEntity is not { Valid: true } user || !_combat.IsInCombatMode(user))
        {
            ResetAimState();
            return;
        }

        if (!TryGetCurrentGunnerTank(user, out var tank))
        {
            ResetAimState();
            return;
        }

        var mouseMapCoordinates = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mouseMapCoordinates.MapId == MapId.Nullspace)
            return;

        TryAimTank(tank, mouseMapCoordinates, force: false);
    }

    private bool OnUse(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (args.State != BoundKeyState.Down || !_timing.IsFirstTimePredicted)
            return false;

        if (args.Session?.AttachedEntity is not { Valid: true } user || !_combat.IsInCombatMode(user))
            return false;

        if (!TryGetCurrentGunnerTank(user, out var tank))
            return false;

        var target = _transform.ToMapCoordinates(args.Coordinates);
        return TryFireMainGun(tank, target);
    }

    private bool TryAimTank(
        EntityUid tank,
        MapCoordinates target,
        bool force)
    {
        if (!TryGetAimAngle(tank, target, out var aimAngle))
            return false;

        var tankChanged = _lastTank != tank;
        var angleChanged = _lastAimAngle == null ||
                           Math.Abs(Angle.ShortestDistance(_lastAimAngle.Value, aimAngle).Theta) >= AimUpdateTolerance.Theta;
        var intervalElapsed = _timing.CurTime >= _nextAimUpdateAt;

        if (!force && !tankChanged && (!angleChanged || !intervalElapsed))
            return false;

        RaiseNetworkEvent(new WH40KTankAimRequestEvent(target));

        _lastTank = tank;
        _lastAimAngle = aimAngle;
        _nextAimUpdateAt = _timing.CurTime + AimUpdateInterval;
        return true;
    }

    private bool TryFireMainGun(EntityUid tank, MapCoordinates target)
    {
        if (!TryGetAimAngle(tank, target, out var aimAngle))
            return false;

        RaiseNetworkEvent(new WH40KTankFireMainGunRequestEvent(target));

        _lastTank = tank;
        _lastAimAngle = aimAngle;
        _nextAimUpdateAt = _timing.CurTime + AimUpdateInterval;
        return true;
    }

    private bool TryGetAimAngle(EntityUid tank, MapCoordinates target, out Angle angle)
    {
        angle = Angle.Zero;

        var tankCoordinates = _transform.GetMapCoordinates(tank);
        if (tankCoordinates.MapId == MapId.Nullspace || target.MapId == MapId.Nullspace || tankCoordinates.MapId != target.MapId)
            return false;

        var direction = target.Position - tankCoordinates.Position;
        if (direction.LengthSquared() <= 0.0001f)
            return false;

        angle = direction.ToWorldAngle();
        return true;
    }

    private bool TryGetCurrentGunnerTank(EntityUid user, out EntityUid tank)
    {
        tank = default;

        if (!TryComp<BuckleComponent>(user, out var buckle) ||
            buckle.BuckledTo is not { Valid: true } station ||
            !TryComp<WH40KTankStationComponent>(station, out var stationComp) ||
            stationComp.Role != WH40KTankCrewRole.Gunner ||
            stationComp.Tank is not { Valid: true } tankUid)
        {
            return false;
        }

        tank = tankUid;
        return true;
    }

    private void ResetAimState()
    {
        _lastTank = null;
        _lastAimAngle = null;
        _nextAimUpdateAt = TimeSpan.Zero;
    }
}
