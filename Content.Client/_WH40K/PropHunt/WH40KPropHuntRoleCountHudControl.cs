using System;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._WH40K.PropHunt;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.PropHunt;

public sealed class WH40KPropHuntRoleCountHudControl : Control
{
    private const float CountBoxWidth = 68f;
    private const float LabelBoxWidth = 186f;
    private const float CenterGap = 6f;
    private const float PanelHeight = 52f;

    public const float CanvasWidth = CountBoxWidth + LabelBoxWidth + CenterGap + LabelBoxWidth + CountBoxWidth;
    public const float CanvasHeight = PanelHeight;

    private static readonly Color PanelBackground = Color.Black.WithAlpha(0.82f);
    private static readonly Color CountFill = Color.Black.WithAlpha(0.92f);
    private static readonly Color RedOutline = Color.FromHex("#ff3b30").WithAlpha(0.96f);
    private static readonly Color BlueOutline = Color.FromHex("#00b7ff").WithAlpha(0.96f);
    private static readonly Color TextColor = Color.White.WithAlpha(0.98f);

    private readonly Font _countFont;
    private readonly Font _labelFont;
    private int _hiderCount;
    private int _seekerCount;

    public WH40KPropHuntRoleCountHudControl()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _countFont = cache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 24);
        _labelFont = cache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 15);

        Visible = false;
        MouseFilter = MouseFilterMode.Ignore;
        MinSize = new Vector2(CanvasWidth, CanvasHeight);
        SetSize = new Vector2(CanvasWidth, CanvasHeight);
    }

    public void Apply(WH40KPropHuntRoleCountEvent ev)
    {
        Visible = ev.Visible;
        _seekerCount = Math.Max(0, ev.SeekerCount);
        _hiderCount = Math.Max(0, ev.HiderCount);
    }

    public void Clear()
    {
        Visible = false;
        _seekerCount = 0;
        _hiderCount = 0;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (!Visible)
            return;

        var leftCount = UIBox2.FromDimensions(Vector2.Zero, new Vector2(CountBoxWidth, PanelHeight));
        var leftLabel = UIBox2.FromDimensions(new Vector2(CountBoxWidth, 0f), new Vector2(LabelBoxWidth, PanelHeight));
        var rightLabel = UIBox2.FromDimensions(new Vector2(CountBoxWidth + LabelBoxWidth + CenterGap, 0f), new Vector2(LabelBoxWidth, PanelHeight));
        var rightCount = UIBox2.FromDimensions(new Vector2(CanvasWidth - CountBoxWidth, 0f), new Vector2(CountBoxWidth, PanelHeight));

        DrawPanel(handle, leftCount, RedOutline, filled: CountFill);
        DrawPanel(handle, leftLabel, RedOutline, filled: PanelBackground);
        DrawPanel(handle, rightLabel, BlueOutline, filled: PanelBackground);
        DrawPanel(handle, rightCount, BlueOutline, filled: CountFill);

        DrawCenteredText(handle, _countFont, _seekerCount.ToString(), leftCount, TextColor);
        DrawCenteredText(handle, _labelFont, Loc.GetString("wh40k-prop-hunt-team-seekers"), leftLabel, TextColor);
        DrawCenteredText(handle, _labelFont, Loc.GetString("wh40k-prop-hunt-team-hiders"), rightLabel, TextColor);
        DrawCenteredText(handle, _countFont, _hiderCount.ToString(), rightCount, TextColor);
    }

    private static void DrawPanel(DrawingHandleScreen handle, UIBox2 bounds, Color outline, Color filled)
    {
        handle.DrawRect(bounds, filled);
        handle.DrawRect(bounds, outline, filled: false);
    }

    private void DrawCenteredText(DrawingHandleScreen handle, Font font, string text, UIBox2 box, Color color)
    {
        var bounds = MeasureText(font, text);
        var baseline = new Vector2(
            MathF.Round(box.Center.X - (bounds.Left + bounds.Right) * 0.5f),
            MathF.Round(box.Center.Y + (font.GetAscent(UIScale) - font.GetDescent(UIScale)) * 0.5f));

        foreach (var rune in text.EnumerateRunes())
        {
            baseline.X += font.DrawChar(handle, rune, baseline, UIScale, color, fallback: true);
        }
    }

    private (float Left, float Right) MeasureText(Font font, string text)
    {
        var pen = 0f;
        var left = 0f;
        var right = 0f;
        var hasGlyph = false;

        foreach (var rune in text.EnumerateRunes())
        {
            var metrics = font.GetCharMetrics(rune, UIScale, fallback: true);
            if (metrics != null && metrics.Value.Width > 0)
            {
                var glyphLeft = pen + metrics.Value.BearingX;
                var glyphRight = glyphLeft + metrics.Value.Width;

                if (!hasGlyph)
                {
                    left = glyphLeft;
                    right = glyphRight;
                    hasGlyph = true;
                }
                else
                {
                    left = MathF.Min(left, glyphLeft);
                    right = MathF.Max(right, glyphRight);
                }
            }

            pen += metrics?.Advance ?? 0f;
        }

        return hasGlyph ? (left, right) : (0f, pen);
    }
}
