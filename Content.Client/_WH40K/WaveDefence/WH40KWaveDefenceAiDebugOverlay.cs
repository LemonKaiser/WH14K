using System.Numerics;
using Content.Shared._WH40K.WaveDefence;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._WH40K.WaveDefence;

public sealed class WH40KWaveDefenceAiDebugOverlay : Overlay
{
    private static readonly Color VisionColor = Color.FromHex("#4FC3F7");
    private static readonly Color AggroColor = Color.FromHex("#8B5CF6");
    private static readonly Color ObjectiveColor = Color.FromHex("#3B82F6");
    private static readonly Color VisiblePlayerColor = Color.FromHex("#22C55E");
    private static readonly Color MemoryColor = Color.FromHex("#F59E0B");
    private static readonly Color LaneColor = Color.FromHex("#E5E7EB");
    private static readonly Color BreachColor = Color.FromHex("#FB7185");
    private static readonly Color SiegeColor = Color.FromHex("#FACC15");
    private static readonly Color ForcedColor = Color.FromHex("#D946EF");
    private static readonly Color CommittedRouteColor = Color.FromHex("#14B8A6");
    private static readonly Color ShadowRouteColor = Color.FromHex("#F97316");
    private static readonly Color RouteNodeColor = Color.FromHex("#ECFEFF");
    private static readonly Color ErrorColor = Color.FromHex("#EF4444");
    private static readonly Color DynamicBlockColor = Color.FromHex("#FBBF24");
    private static readonly Color TextColor = Color.FromHex("#F8FAFC");
    private static readonly Color MutedTextColor = Color.FromHex("#94A3B8");

    private readonly Font _font;
    private readonly WH40KWaveDefenceAiDebugOverlaySystem _system;

    public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

    public WH40KWaveDefenceAiDebugOverlay(WH40KWaveDefenceAiDebugOverlaySystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;

        var cache = IoCManager.Resolve<IResourceCache>();
        _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        switch (args.Space)
        {
            case OverlaySpace.WorldSpace:
                DrawWorld(args);
                break;
            case OverlaySpace.ScreenSpace:
                DrawScreen(args);
                break;
        }
    }

    private void DrawWorld(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;

        foreach (var entry in _system.Entries)
        {
            if (entry.NpcPosition.MapId != args.MapId || !args.WorldAABB.Contains(entry.NpcPosition.Position))
                continue;

            var npcPosition = entry.NpcPosition.Position;
            var targetColor = GetTargetColor(entry);

            if (entry.VisionRadius > 0.05f)
                handle.DrawCircle(npcPosition, entry.VisionRadius, VisionColor.WithAlpha(0.7f), false);

            if (entry.AggroVisionRadius > entry.VisionRadius + 0.05f)
                handle.DrawCircle(npcPosition, entry.AggroVisionRadius, AggroColor.WithAlpha(0.55f), false);

            if (entry.HasCurrentTargetPosition)
            {
                handle.DrawLine(npcPosition, entry.CurrentTargetPosition.Position, targetColor.WithAlpha(0.95f));
                handle.DrawCircle(entry.CurrentTargetPosition.Position, 0.22f, targetColor.WithAlpha(0.95f), false);
            }

            if (entry.HasClearanceDebugSamplePosition)
                handle.DrawCircle(entry.ClearanceDebugSamplePosition.Position, MathF.Max(0.16f, entry.BodyClearanceRadius), ErrorColor.WithAlpha(0.8f), false);

            if (entry.HasDynamicClearanceDebugSamplePosition)
                handle.DrawCircle(entry.DynamicClearanceDebugSamplePosition.Position, MathF.Max(0.14f, entry.BodyClearanceRadius * 0.85f), DynamicBlockColor.WithAlpha(0.85f), false);

            DrawRoute(handle, args.MapId, entry.ShadowRoutePoints, ShadowRouteColor.WithAlpha(0.65f), 0.12f);
            DrawRoute(handle, args.MapId, entry.CommittedRoutePoints, CommittedRouteColor.WithAlpha(0.9f), 0.16f);

            if (entry.TargetKind == WH40KWaveDefenceAiDebugTargetKind.Objective && entry.HasObjectivePosition)
                handle.DrawCircle(entry.ObjectivePosition.Position, 0.35f, ObjectiveColor.WithAlpha(0.95f), false);

            if (entry.TargetKind == WH40KWaveDefenceAiDebugTargetKind.RememberedPlayer && entry.HasRememberedTargetPosition)
                handle.DrawCircle(entry.RememberedTargetPosition.Position, 0.28f, MemoryColor.WithAlpha(0.95f), false);

            if (entry.NoPath || entry.RecoveryLevel > 0)
                handle.DrawCircle(npcPosition, 0.55f, ErrorColor.WithAlpha(0.95f), false);
        }
    }

    private void DrawScreen(in OverlayDrawArgs args)
    {
        if (args.ViewportControl == null)
            return;

        var handle = args.ScreenHandle;
        DrawLegend(handle, args.ViewportControl.Window?.Size);

        foreach (var entry in _system.Entries)
        {
            if (entry.NpcPosition.MapId != args.MapId || !args.WorldAABB.Contains(entry.NpcPosition.Position))
                continue;

            var screenPosition = args.ViewportControl.WorldToScreen(entry.NpcPosition.Position);
            var labelPosition = screenPosition + new Vector2(10f, -56f);
            var labelColor = entry.NoPath ? ErrorColor : GetTargetColor(entry);
            var label = BuildEntryLabel(entry);
            handle.DrawString(_font, labelPosition, label, labelColor.WithAlpha(0.95f));
            DrawRouteCosts(handle, args, entry.ShadowRoutePoints, entry.ShadowRouteCumulativeCosts, ShadowRouteColor.WithAlpha(0.85f), "S");
            DrawRouteCosts(handle, args, entry.CommittedRoutePoints, entry.CommittedRouteCumulativeCosts, CommittedRouteColor.WithAlpha(0.95f), "C");
        }
    }

    private void DrawLegend(DrawingHandleScreen handle, Vector2i? windowSize)
    {
        var lineHeight = 12f;
        var lines = new (string Text, Color Color)[]
        {
            ("WH40K Wave AI Debug", TextColor),
            ("cyan circle = vision radius", VisionColor),
            ("violet circle = chase radius", AggroColor),
            ("green line = sees player now", VisiblePlayerColor),
            ("orange line = chasing remembered player", MemoryColor),
            ("blue line = objective", ObjectiveColor),
            ("white line = lane advance", LaneColor),
            ("pink line = breach route point", BreachColor),
            ("yellow line = siege route point", SiegeColor),
            ("magenta line = forced recovery target", ForcedColor),
            ("teal route = committed path + cost", CommittedRouteColor),
            ("orange route = shadow candidate + cost", ShadowRouteColor),
            ("amber ring = dynamic crowd/reservation block", DynamicBlockColor),
            ("red ring/text = no path or recovery", ErrorColor),
        };

        var maxWidth = 0f;
        foreach (var (text, _) in lines)
        {
            maxWidth = MathF.Max(maxWidth, handle.GetDimensions(_font, text.AsSpan(), 1f).X);
        }

        var totalHeight = lineHeight * lines.Length;
        var origin = windowSize is { } size
            ? new Vector2(
                MathF.Max(18f, size.X - maxWidth - 18f),
                MathF.Max(18f, size.Y - totalHeight - 18f))
            : new Vector2(18f, 18f);

        for (var i = 0; i < lines.Length; i++)
        {
            var (text, color) = lines[i];
            handle.DrawString(_font, origin + new Vector2(0f, i * lineHeight), text, color.WithAlpha(i == 0 ? 1f : 0.9f));
        }
    }

    private static Color GetTargetColor(WH40KWaveDefenceAiDebugEntry entry)
    {
        return entry.TargetKind switch
        {
            WH40KWaveDefenceAiDebugTargetKind.VisiblePlayer => VisiblePlayerColor,
            WH40KWaveDefenceAiDebugTargetKind.RememberedPlayer => MemoryColor,
            WH40KWaveDefenceAiDebugTargetKind.Objective => ObjectiveColor,
            WH40KWaveDefenceAiDebugTargetKind.ForcedPoint => ForcedColor,
            WH40KWaveDefenceAiDebugTargetKind.LanePoint => GetLanePointColor(entry.CurrentLanePointType),
            WH40KWaveDefenceAiDebugTargetKind.None => MutedTextColor,
            _ => TextColor
        };
    }

    private static string BuildEntryLabel(WH40KWaveDefenceAiDebugEntry entry)
    {
        var visibility = entry.TargetKind switch
        {
            WH40KWaveDefenceAiDebugTargetKind.VisiblePlayer => "see=yes",
            WH40KWaveDefenceAiDebugTargetKind.RememberedPlayer => $"see=memory {entry.MemoryRemainingSeconds:0.0}s",
            WH40KWaveDefenceAiDebugTargetKind.Objective => "see=objective",
            _ => "see=no"
        };

        var routeLabel = entry.RouteCompleted
            ? $"route:done/{entry.TotalLanePointCount} {(entry.RouteProgressRatio * 100f):0}%"
            : $"route:{Math.Max(0, entry.CurrentLanePointIndex)}/{entry.TotalLanePointCount} {(entry.RouteProgressRatio * 100f):0}%";
        var currentPointLabel = entry.HasCurrentLanePoint
            ? $"{entry.CurrentLanePointId}[{ShortPointType(entry.CurrentLanePointType)}]"
            : "-";
        var lastPointLabel = entry.HasLastReachedLanePoint
            ? $"{entry.LastReachedLanePointId}[{ShortPointType(entry.LastReachedLanePointType)}]"
            : "-";
        var blockerLabel = entry.HasSiegeBlocker
            ? $"\nblk:{entry.SiegeBlockerLabel}"
            : string.Empty;
        var committedLabel = entry.HasCommittedRoute
            ? $"\nrt:C {entry.CommittedRouteRemainingCost:0.0}/{entry.CommittedRouteCost:0.0} topo:{entry.CommittedRouteTopologyVersion}"
            : "\nrt:C -";
        var shadowLabel = entry.HasShadowRoute
            ? $" S {entry.ShadowRouteCost:0.0} topo:{entry.ShadowRouteTopologyVersion}"
            : " S -";
        var clearanceLabel =
            $"\nbody:r{entry.BodyClearanceRadius:0.00} d{entry.BodyClearanceDiameter:0.00}" +
            $"\nstaticClr:{entry.ClearanceDebugLabel}:{entry.ClearanceDebugReason}" +
            (string.IsNullOrWhiteSpace(entry.ClearanceDebugBlockerLabel)
                ? string.Empty
                : $"\nstaticBlk:{entry.ClearanceDebugBlockerLabel}") +
            $"\ndynamicClr:{entry.DynamicClearanceDebugLabel}:{entry.DynamicClearanceDebugReason}" +
            (string.IsNullOrWhiteSpace(entry.DynamicClearanceDebugBlockerLabel)
                ? string.Empty
                : $"\ndynamicBlk:{entry.DynamicClearanceDebugBlockerLabel}");

        return
            $"{entry.Label} [{GetTargetLabel(entry.TargetKind)}]\n" +
            $"task:{entry.CurrentTask} root:{ShortRootTask(entry.RootTask)}\n" +
            $"brain:{entry.BrainOwner}\n" +
            $"combat:{entry.CombatOwner}\n" +
            $"move:{entry.MovementOwner}\n" +
            $"memory:{entry.MemoryOwner}\n" +
            $"recovery:{entry.RecoveryOwner}\n" +
            $"intent:{entry.Intent} lane:{entry.LaneId} steer:{entry.SteeringStatus}\n" +
            $"{routeLabel} hit:{Math.Max(-1, entry.LastReachedLanePointIndex)} fur:{Math.Max(-1, entry.FurthestReachedLanePointIndex)}\n" +
            $"curr:{currentPointLabel} last:{lastPointLabel}\n" +
            $"{visibility} los:{(entry.HasLineOfSightToPlayer ? "yes" : "no")} rec:{entry.RecoveryLevel}{blockerLabel}" +
            $"{clearanceLabel}" +
            $"{committedLabel}{shadowLabel}\n" +
            $"epochs:{entry.EpochSummary}\n" +
            $"rm:{entry.RouteMindDecision}";
    }

    private static void DrawRoute(
        DrawingHandleWorld handle,
        MapId mapId,
        MapCoordinates[] points,
        Color color,
        float radius)
    {
        if (points.Length == 0)
            return;

        for (var i = 0; i < points.Length; i++)
        {
            if (points[i].MapId != mapId)
                continue;

            if (i > 0 && points[i - 1].MapId == mapId)
                handle.DrawLine(points[i - 1].Position, points[i].Position, color);

            var nodeRadius = i == points.Length - 1 ? radius + 0.06f : radius;
            handle.DrawCircle(points[i].Position, nodeRadius, (i == points.Length - 1 ? RouteNodeColor : color).WithAlpha(color.A / 255f), false);
        }
    }

    private void DrawRouteCosts(
        DrawingHandleScreen handle,
        OverlayDrawArgs args,
        MapCoordinates[] points,
        float[] costs,
        Color color,
        string prefix)
    {
        if (args.ViewportControl == null || points.Length == 0 || costs.Length != points.Length)
            return;

        for (var i = 0; i < points.Length; i++)
        {
            if (points[i].MapId != args.MapId || !args.WorldAABB.Contains(points[i].Position))
                continue;

            var screen = args.ViewportControl.WorldToScreen(points[i].Position);
            handle.DrawString(
                _font,
                screen + new Vector2(4f, -8f),
                $"{prefix}{i}:{costs[i]:0.0}",
                color);
        }
    }

    private static Color GetLanePointColor(WH40KWaveLanePointType pointType)
    {
        return pointType switch
        {
            WH40KWaveLanePointType.Breach => BreachColor,
            WH40KWaveLanePointType.Siege => SiegeColor,
            _ => LaneColor
        };
    }

    private static string GetTargetLabel(WH40KWaveDefenceAiDebugTargetKind kind)
    {
        return kind switch
        {
            WH40KWaveDefenceAiDebugTargetKind.VisiblePlayer => "player",
            WH40KWaveDefenceAiDebugTargetKind.RememberedPlayer => "memory",
            WH40KWaveDefenceAiDebugTargetKind.Objective => "objective",
            WH40KWaveDefenceAiDebugTargetKind.LanePoint => "lane",
            WH40KWaveDefenceAiDebugTargetKind.ForcedPoint => "forced",
            WH40KWaveDefenceAiDebugTargetKind.None => "idle",
            _ => "unknown"
        };
    }

    private static string ShortPointType(WH40KWaveLanePointType pointType)
    {
        return pointType switch
        {
            WH40KWaveLanePointType.Waypoint => "way",
            WH40KWaveLanePointType.Rally => "rally",
            WH40KWaveLanePointType.Fallback => "fb",
            WH40KWaveLanePointType.Breach => "breach",
            WH40KWaveLanePointType.Siege => "siege",
            _ => "?"
        };
    }

    private static string ShortRootTask(string rootTask)
    {
        if (string.IsNullOrWhiteSpace(rootTask))
            return "<none>";

        return rootTask
            .Replace("WH40KWaveDefence", string.Empty, StringComparison.Ordinal)
            .Replace("AIProfile", string.Empty, StringComparison.Ordinal)
            .Trim();
    }
}
