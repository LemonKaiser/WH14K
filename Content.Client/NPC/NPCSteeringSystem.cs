using System.Numerics;
using Content.Client.Physics.Controllers;
using Content.Client.PhysicsSystem.Controllers;
using Content.Shared.Movement.Components;
using Content.Shared.NPC;
using Content.Shared.NPC.Events;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client.NPC;

public sealed partial class NPCSteeringSystem : SharedNPCSteeringSystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    public bool DrawSteeringVectors;
    public bool DrawPathWidth;

    public bool DebugEnabled
    {
        get => _debugEnabled;
        set
        {
            if (_debugEnabled == value)
                return;

            _debugEnabled = value;

            if (_debugEnabled)
            {
                _overlay.AddOverlay(new NPCSteeringOverlay(EntityManager, this));
                RaiseNetworkEvent(new RequestNPCSteeringDebugEvent()
                {
                    Enabled = true
                });
            }
            else
            {
                _overlay.RemoveOverlay<NPCSteeringOverlay>();
                RaiseNetworkEvent(new RequestNPCSteeringDebugEvent()
                {
                    Enabled = false
                });

                var query = AllEntityQuery<NPCSteeringComponent>();
                while (query.MoveNext(out var uid, out var npc))
                {
                    RemCompDeferred<NPCSteeringComponent>(uid);
                }
            }
        }
    }

    private bool _debugEnabled;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<NPCSteeringDebugEvent>(OnDebugEvent);
    }

    private void OnDebugEvent(NPCSteeringDebugEvent ev)
    {
        if (!DebugEnabled)
            return;

        foreach (var data in ev.Data)
        {
            var entity = GetEntity(data.EntityUid);

            if (!Exists(entity))
                continue;

            var comp = EnsureComp<NPCSteeringComponent>(entity);
            comp.Direction = data.Direction;
            comp.DangerMap = data.Danger;
            comp.InterestMap = data.Interest;
            comp.DangerPoints = data.DangerPoints;
            comp.Destination = data.Destination;
            comp.Radius = data.Radius;
            comp.CurrentPath = data.CurrentPath;
            comp.CurrentPathPolys = data.CurrentPathPolys;
        }
    }
}

public sealed class NPCSteeringOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly IEntityManager _entManager;
    private readonly NPCSteeringSystem _system;
    private readonly SharedTransformSystem _transformSystem;

    private static readonly Color PathCenterColor = new(0.05f, 0.85f, 1f, 0.95f);
    private static readonly Color PathWidthColor = new(0.05f, 0.85f, 1f, 0.55f);
    private static readonly Color PathPointColor = new(0.05f, 0.85f, 1f, 0.35f);
    private static readonly Color PathBlockedColor = new(1f, 0.2f, 0.08f, 0.55f);
    private static readonly Color PathDoorColor = new(1f, 0.75f, 0.1f, 0.45f);
    private static readonly Color PathClimbColor = new(0.55f, 1f, 0.15f, 0.45f);

    public NPCSteeringOverlay(IEntityManager entManager, NPCSteeringSystem system)
    {
        _entManager = entManager;
        _system = system;
        _transformSystem = _entManager.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        foreach (var (comp, mover, xform) in _entManager.EntityQuery<NPCSteeringComponent, InputMoverComponent, TransformComponent>(true))
        {
            if (xform.MapID != args.MapId)
            {
                continue;
            }

            var (worldPos, worldRot) = _transformSystem.GetWorldPositionRotation(xform);

            if (!args.WorldAABB.Contains(worldPos))
                continue;

            if (_system.DrawPathWidth)
                DrawPathWidth(args, comp, xform, worldPos);

            if (!_system.DrawSteeringVectors)
                continue;

            args.WorldHandle.DrawCircle(worldPos, 1f, Color.Green, false);
            var rotationOffset = _entManager.System<MoverController>().GetParentGridAngle(mover);

            foreach (var point in comp.DangerPoints)
            {
                args.WorldHandle.DrawCircle(point, 0.1f, Color.Red.WithAlpha(0.6f));
            }

            for (var i = 0; i < SharedNPCSteeringSystem.InterestDirections; i++)
            {
                var danger = comp.DangerMap[i];
                var interest = comp.InterestMap[i];
                var angle = Angle.FromDegrees(i * (360 / SharedNPCSteeringSystem.InterestDirections));
                args.WorldHandle.DrawLine(worldPos, worldPos + (rotationOffset + angle).RotateVec(new Vector2(interest, 0f)), Color.LimeGreen);
                args.WorldHandle.DrawLine(worldPos, worldPos + (rotationOffset + angle).RotateVec(new Vector2(danger, 0f)), Color.Red);
            }

            args.WorldHandle.DrawLine(worldPos, worldPos + rotationOffset.RotateVec(comp.Direction), Color.Cyan);
        }
    }

    private void DrawPathWidth(
        OverlayDrawArgs args,
        NPCSteeringComponent comp,
        TransformComponent xform,
        Vector2 worldPos)
    {
        var handle = args.WorldHandle;

        DrawPathPolys(handle, comp);
        handle.SetTransform(Matrix3x2.Identity);

        handle.DrawCircle(worldPos, comp.Radius, PathCenterColor, false);

        var points = new List<Vector2>(comp.CurrentPath.Count + 2)
        {
            worldPos,
        };

        foreach (var netCoordinates in comp.CurrentPath)
        {
            if (TryGetMapPosition(netCoordinates, args.MapId, out var pathPoint))
                points.Add(pathPoint);
        }

        if (TryGetMapPosition(comp.Destination, args.MapId, out var destination))
            points.Add(destination);

        for (var i = 1; i < points.Count; i++)
        {
            var start = points[i - 1];
            var end = points[i];
            var color = GetSegmentColor(comp, i - 1);

            DrawCorridor(handle, start, end, comp.Radius, color);
            handle.DrawCircle(end, comp.Radius, color.WithAlpha(PathPointColor.A), false);
        }
    }

    private void DrawPathPolys(DrawingHandleWorld handle, NPCSteeringComponent comp)
    {
        EntityUid? graph = null;

        foreach (var poly in comp.CurrentPathPolys)
        {
            var polyGraph = _entManager.GetEntity(poly.GraphUid);

            if (graph != polyGraph)
            {
                if (!_entManager.TryGetComponent<TransformComponent>(polyGraph, out var graphXform))
                    continue;

                graph = polyGraph;
                handle.SetTransform(_transformSystem.GetWorldMatrix(graphXform));
            }

            var color = GetPolyColor(poly.Data);
            handle.DrawRect(poly.Box, color.WithAlpha(0.12f));
            handle.DrawRect(poly.Box, color.WithAlpha(0.65f), false);
        }
    }

    private Color GetSegmentColor(NPCSteeringComponent comp, int pathIndex)
    {
        if (pathIndex < 0 || pathIndex >= comp.CurrentPathPolys.Count)
            return PathCenterColor;

        return GetPolyColor(comp.CurrentPathPolys[pathIndex].Data).WithAlpha(PathWidthColor.A);
    }

    private static Color GetPolyColor(PathfindingData data)
    {
        if (data.IsFreeSpace)
            return PathCenterColor;

        if ((data.Flags & PathfindingBreadcrumbFlag.Door) != 0x0)
            return PathDoorColor;

        if ((data.Flags & PathfindingBreadcrumbFlag.Climb) != 0x0)
            return PathClimbColor;

        return PathBlockedColor;
    }

    private void DrawCorridor(DrawingHandleWorld handle, Vector2 start, Vector2 end, float radius, Color color)
    {
        var delta = end - start;
        var length = delta.Length();

        if (length <= 0.01f)
            return;

        var direction = delta / length;
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var side = perpendicular * radius;
        var halfSide = side * 0.5f;

        handle.DrawLine(start, end, color.WithAlpha(0.95f));
        handle.DrawLine(start + side, end + side, color);
        handle.DrawLine(start - side, end - side, color);
        handle.DrawLine(start + halfSide, end + halfSide, color.WithAlpha(color.A * 0.5f));
        handle.DrawLine(start - halfSide, end - halfSide, color.WithAlpha(color.A * 0.5f));
    }

    private bool TryGetMapPosition(NetCoordinates netCoordinates, MapId mapId, out Vector2 position)
    {
        position = default;

        if (netCoordinates.Equals(NetCoordinates.Invalid))
            return false;

        var coordinates = _entManager.GetCoordinates(netCoordinates);
        if (!coordinates.IsValid(_entManager))
            return false;

        var mapCoordinates = _transformSystem.ToMapCoordinates(coordinates);
        if (mapCoordinates.MapId != mapId)
            return false;

        position = mapCoordinates.Position;
        return true;
    }
}
