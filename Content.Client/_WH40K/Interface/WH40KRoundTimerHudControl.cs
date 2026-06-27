using System;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._WH40K.Interface;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Interface;

public sealed class WH40KRoundTimerHudControl : Control
{
    public const float CanvasWidth = 320f;

    private const float CanvasHeight = 90f;
    private const float PanelHeight = 90f;
    private const float HorizontalPadding = 32f;
    private const float MinActiveWidth = 144f;
    private const float MinStoppedWidth = 176f;
    private const float MaxPanelWidth = CanvasWidth - 12f;

    private readonly Font _timeFont;
    private readonly Font _labelFont;
    private readonly IGameTiming _timing;

    private int _roundId;
    private int _durationSeconds;
    private int _elapsedSecondsAtSync;
    private bool _stopped;
    private TimeSpan _syncTime;
    private string _lastDisplay = string.Empty;
    private float _pulse;

    public WH40KRoundTimerHudControl()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _timing = IoCManager.Resolve<IGameTiming>();
        _timeFont = cache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 24);
        _labelFont = cache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 12);

        Visible = false;
        MouseFilter = MouseFilterMode.Ignore;
        MinSize = new Vector2(CanvasWidth, CanvasHeight);
        SetSize = new Vector2(CanvasWidth, CanvasHeight);
    }

    public void Apply(WH40KRoundTimerEvent ev)
    {
        _roundId = Math.Max(0, ev.RoundId);
        _durationSeconds = Math.Max(0, ev.DurationSeconds);
        _elapsedSecondsAtSync = Math.Max(0, ev.ElapsedSeconds);
        _stopped = ev.Stopped;
        _syncTime = _timing.CurTime;
        _pulse = 1f;
        _lastDisplay = string.Empty;
        Visible = ev.Visible;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!Visible)
            return;

        _pulse = MathF.Max(0f, _pulse - args.DeltaSeconds * 2.6f);

        var display = GetDisplayText();
        if (!string.Equals(display, _lastDisplay, StringComparison.Ordinal))
        {
            _lastDisplay = display;
            _pulse = 1f;
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (!Visible)
            return;

        var pulse = EaseOutCubic(_pulse);
        var titleText = GetTitleText();
        var timeText = GetDisplayText();
        var panelWidth = GetPanelWidth(titleText, timeText);
        var panelHeight = PanelHeight;
        var left = MathF.Round((CanvasWidth - panelWidth) * 0.5f);
        var top = 10f;
        var panel = UIBox2.FromDimensions(new Vector2(left, top), new Vector2(panelWidth, panelHeight));
        var inner = new UIBox2(panel.Left + 1f, panel.Top + 1f, panel.Right - 1f, panel.Bottom - 1f);

        handle.DrawRect(panel, Color.Black.WithAlpha(0.74f));
        handle.DrawRect(inner, Color.Black.WithAlpha(0.58f));
        handle.DrawRect(panel, Color.White.WithAlpha(0.10f), filled: false);

        var titleBox = new UIBox2(panel.Left + HorizontalPadding, panel.Top + 10f, panel.Right - HorizontalPadding, panel.Top + 28f);
        DrawCenteredText(handle, _labelFont, titleText, titleBox, Color.White.WithAlpha(0.72f));

        var timeBox = new UIBox2(panel.Left + HorizontalPadding, panel.Top + 34f - pulse, panel.Right - HorizontalPadding, panel.Top + 64f - pulse);
        DrawCenteredText(handle, _timeFont, timeText, timeBox, Color.White.WithAlpha(0.98f));
    }

    private string GetTitleText()
    {
        return Loc.GetString("wh40k-round-timer-title", ("id", _roundId));
    }

    private string GetDisplayText()
    {
        if (_stopped || _durationSeconds <= 0)
            return Loc.GetString("wh40k-round-timer-stopped");

        var elapsed = _elapsedSecondsAtSync + Math.Max(0, (int) Math.Floor((_timing.CurTime - _syncTime).TotalSeconds));
        var remainingSeconds = Math.Max(0, _durationSeconds - elapsed);
        var minutes = remainingSeconds / 60;
        var seconds = remainingSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    private static float MeasureTextWidth(Font font, string text, float uiScale)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            var metrics = font.GetCharMetrics(rune, uiScale, fallback: true);
            width += metrics?.Advance ?? 0f;
        }

        return width;
    }

    private float GetPanelWidth(string titleText, string timeText)
    {
        var contentWidth = MathF.Max(
            MeasureTextHorizontalBounds(_labelFont, titleText).Width,
            MeasureTextHorizontalBounds(_timeFont, timeText).Width);

        var minWidth = _stopped ? MinStoppedWidth : MinActiveWidth;
        var width = MathF.Max(minWidth, contentWidth + HorizontalPadding * 2f);
        width = MathF.Min(width, MaxPanelWidth);

        var rounded = MathF.Ceiling(width);
        if (((int) rounded & 1) != 0)
            rounded += 1f;

        return rounded;
    }

    private void DrawCenteredText(DrawingHandleScreen handle, Font font, string text, UIBox2 box, Color color)
    {
        if (text.Length == 0)
            return;

        var bounds = MeasureTextHorizontalBounds(font, text);
        var baselineX = MathF.Round(box.Center.X - (bounds.Left + bounds.Right) * 0.5f);
        var baselineY = MathF.Round(box.Center.Y + (font.GetAscent(UIScale) - font.GetDescent(UIScale)) * 0.5f);
        var baseline = new Vector2(baselineX, baselineY);

        foreach (var rune in text.EnumerateRunes())
        {
            baseline.X += font.DrawChar(handle, rune, baseline, UIScale, color, fallback: true);
        }
    }

    private (float Left, float Right, float Width) MeasureTextHorizontalBounds(Font font, string text)
    {
        var penX = 0f;
        var hasGlyph = false;
        var left = 0f;
        var right = 0f;

        foreach (var rune in text.EnumerateRunes())
        {
            var metrics = font.GetCharMetrics(rune, UIScale, fallback: true);
            if (metrics != null && metrics.Value.Width > 0)
            {
                var glyphLeft = penX + metrics.Value.BearingX;
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

            penX += metrics?.Advance ?? 0f;
        }

        if (!hasGlyph)
            return (0f, penX, penX);

        return (left, right, right - left);
    }

    private static float EaseOutCubic(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        var inverse = 1f - value;
        return 1f - inverse * inverse * inverse;
    }
}
