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

public sealed class WH40KPropHuntSeekerCountdownHudControl : Control
{
    private readonly Font _titleFont;
    private readonly Font _timerFont;
    private int _remainingSeconds;

    public WH40KPropHuntSeekerCountdownHudControl()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _titleFont = cache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 20);
        _timerFont = cache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 72);

        Visible = false;
        MouseFilter = MouseFilterMode.Ignore;
    }

    public void Apply(WH40KPropHuntSeekerCountdownEvent ev)
    {
        Visible = ev.Active;
        _remainingSeconds = Math.Max(0, ev.RemainingSeconds);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (!Visible)
            return;

        var bounds = new UIBox2(Vector2.Zero, PixelSize);
        handle.DrawRect(bounds, Color.Black);

        var title = Loc.GetString("wh40k-prop-hunt-seeker-countdown-title");
        var timer = _remainingSeconds.ToString("00");

        DrawCentered(handle, _titleFont, title, bounds.Center + new Vector2(0f, -72f), Color.White.WithAlpha(0.88f));
        DrawCentered(handle, _timerFont, timer, bounds.Center + new Vector2(0f, -6f), Color.White);
    }

    private void DrawCentered(DrawingHandleScreen handle, Font font, string text, Vector2 center, Color color)
    {
        var bounds = MeasureText(font, text);
        var baseline = new Vector2(
            MathF.Round(center.X - (bounds.Left + bounds.Right) * 0.5f),
            MathF.Round(center.Y + (font.GetAscent(UIScale) - font.GetDescent(UIScale)) * 0.5f));

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
