using System.Numerics;
using Content.Shared._WH40K.WaveDefence;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;

namespace Content.Client._WH40K.WaveDefence;

public sealed class WH40KWaveDefenceAiDebugOverlay : Overlay
{
    private static readonly Color VisionColor = Color.FromHex("#38BDF8");
    private static readonly Color AggroColor = Color.FromHex("#A78BFA");
    private static readonly Color CombatColor = Color.FromHex("#22C55E");
    private static readonly Color ObjectiveColor = Color.FromHex("#F59E0B");
    private static readonly Color MoveColor = Color.FromHex("#E5E7EB");
    private static readonly Color ErrorColor = Color.FromHex("#EF4444");
    private static readonly Color TextColor = Color.FromHex("#F8FAFC");

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
            var color = GetFocusColor(entry.FocusKind);

            if (entry.VisionRadius > 0.05f)
                handle.DrawCircle(npcPosition, entry.VisionRadius, VisionColor.WithAlpha(0.65f), false);

            if (entry.AggroVisionRadius > entry.VisionRadius + 0.05f)
                handle.DrawCircle(npcPosition, entry.AggroVisionRadius, AggroColor.WithAlpha(0.55f), false);

            if (entry.HasFocusPosition)
            {
                handle.DrawLine(npcPosition, entry.FocusPosition.Position, color.WithAlpha(0.95f));
                handle.DrawCircle(entry.FocusPosition.Position, 0.22f, color.WithAlpha(0.95f), false);
            }

            if (entry.NoPath)
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
            var labelPosition = screenPosition + new Vector2(10f, -42f);
            var label = BuildEntryLabel(entry);
            var color = entry.NoPath ? ErrorColor : GetFocusColor(entry.FocusKind);
            handle.DrawString(_font, labelPosition, label, color.WithAlpha(0.95f));
        }
    }

    private void DrawLegend(DrawingHandleScreen handle, Vector2i? windowSize)
    {
        var lineHeight = 12f;
        var lines = new (string Text, Color Color)[]
        {
            ("WH40K NPC AI Debug", TextColor),
            ("cyan circle = vision radius", VisionColor),
            ("violet circle = chase radius", AggroColor),
            ("green line = combat target", CombatColor),
            ("amber line = objective target", ObjectiveColor),
            ("white line = move target", MoveColor),
            ("red ring/text = no path", ErrorColor),
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

    private static string BuildEntryLabel(WH40KWaveDefenceAiDebugEntry entry)
    {
        return
            $"{entry.Label}{(entry.IsWaveAttacker ? " [wave]" : string.Empty)}\n" +
            $"task:{entry.CurrentTask} root:{entry.RootTask}\n" +
            $"focus:{entry.FocusLabel} steer:{entry.SteeringStatus}\n" +
            $"engaged:{entry.Engaged} nopath:{entry.NoPath}\n" +
            $"state:{entry.DebugState}";
    }

    private static Color GetFocusColor(WH40KWaveDefenceAiDebugTargetKind kind)
    {
        return kind switch
        {
            WH40KWaveDefenceAiDebugTargetKind.CombatTarget => CombatColor,
            WH40KWaveDefenceAiDebugTargetKind.ObjectiveTarget => ObjectiveColor,
            WH40KWaveDefenceAiDebugTargetKind.MoveTarget => MoveColor,
            _ => TextColor,
        };
    }
}
