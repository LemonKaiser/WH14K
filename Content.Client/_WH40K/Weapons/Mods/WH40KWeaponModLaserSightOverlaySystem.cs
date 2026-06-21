using System.Linq;
using System.Numerics;
using Content.Shared._WH40K.Weapons.Mods;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Physics;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Weapons.Mods;

public sealed partial class WH40KWeaponModLaserSightOverlaySystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    private WH40KWeaponModLaserSightOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new WH40KWeaponModLaserSightOverlay(this);
        _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_overlay != null)
            _overlayManager.RemoveOverlay(_overlay);

        _overlay = null;
    }

    public bool TryGetBeam(MapId mapId, out LaserBeamState beam)
    {
        beam = default;

        if (_player.LocalSession?.AttachedEntity is not { Valid: true } user ||
            !_hands.TryGetActiveItem(user, out var activeItem) ||
            !TryComp(activeItem.Value, out WH40KWeaponModHostComponent? host))
        {
            return false;
        }

        if (!TryGetActiveLaser(host, out var laserUid, out var laser))
            return false;

        var gunUid = activeItem.Value;
        var gunMapId = _transform.GetMapId(gunUid);
        if (gunMapId == MapId.Nullspace || gunMapId != mapId)
            return false;

        var mouseMap = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mouseMap.MapId == MapId.Nullspace || mouseMap.MapId != gunMapId)
            return false;

        var origin = _transform.GetWorldPosition(gunUid);
        var desired = mouseMap.Position - origin;
        if (desired.LengthSquared() <= 0.0001f)
            return false;

        var maxDistance = MathF.Max(0.1f, laser.MaxRange);
        var direction = Vector2.Normalize(desired);
        var targetDistance = MathF.Min(desired.Length(), maxDistance);
        var ray = new CollisionRay(origin, direction, (int) CollisionGroup.Impassable);
        var hit = _physics.IntersectRayWithPredicate(
                gunMapId,
                ray,
                targetDistance,
                uid => ShouldIgnoreHit(uid, user, gunUid, laserUid),
                returnOnFirstHit: true)
            .FirstOrNull();

        var beamDistance = hit?.Distance ?? targetDistance;
        beam = new LaserBeamState(origin, origin + direction * beamDistance, laser.BeamColor);
        return true;
    }

    private bool TryGetActiveLaser(
        WH40KWeaponModHostComponent host,
        out EntityUid laserUid,
        out WH40KWeaponModLaserSightComponent laser)
    {
        laserUid = default;
        laser = default!;

        foreach (var definition in host.SlotDefinitions.OrderByDescending(x => x.Priority))
        {
            if (definition.SlotType != WH40KWeaponModSlotType.SideUtility)
                continue;

            var slotId = WH40KWeaponModHelper.GetSlotId(definition.Id);
            if (!host.ModSlots.TryGetValue(slotId, out var slot) ||
                slot.Item is not { } installed ||
                !TryComp(installed, out WH40KWeaponModLaserSightComponent? laserComp) ||
                !laserComp.Active)
            {
                continue;
            }

            laserUid = installed;
            laser = laserComp;
            return true;
        }

        return false;
    }

    private bool ShouldIgnoreHit(EntityUid uid, EntityUid user, EntityUid gunUid, EntityUid laserUid)
    {
        if (uid == user || uid == gunUid || uid == laserUid)
            return true;

        return TryComp(uid, out TransformComponent? xform) && xform.ParentUid == user;
    }

    public readonly record struct LaserBeamState(Vector2 Start, Vector2 End, Color Color);
}

public sealed class WH40KWeaponModLaserSightOverlay : Overlay
{
    private const int Segments = 8;
    private static readonly Vector2 BeamOffset = new(0.012f, 0.012f);

    private readonly WH40KWeaponModLaserSightOverlaySystem _system;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WH40KWeaponModLaserSightOverlay(WH40KWeaponModLaserSightOverlaySystem system)
    {
        _system = system;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_system.TryGetBeam(args.MapId, out var beam))
            return;

        var handle = args.WorldHandle;
        for (var i = 0; i < Segments; i++)
        {
            var startT = i / (float) Segments;
            var endT = (i + 1) / (float) Segments;
            var start = Vector2.Lerp(beam.Start, beam.End, startT);
            var end = Vector2.Lerp(beam.Start, beam.End, endT);
            var alpha = 0.95f - (0.85f * endT);
            var core = beam.Color.WithAlpha(alpha);
            var glow = beam.Color.WithAlpha(alpha * 0.32f);

            handle.DrawLine(start, end, core);
            handle.DrawLine(start + BeamOffset, end + BeamOffset, glow);
            handle.DrawLine(start - BeamOffset, end - BeamOffset, glow);
        }
    }
}
