using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Content.Client.Resources;
using Content.Shared._WH40K.Notifications;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Notifications;

public sealed class WH40KNotificationHudControl : LayoutContainer
{
    public const float StandardWidth = 460f;
    public const float CanvasWidth = 560f;
    public const float DefaultCanvasHeight = 96f;

    private const float CompactWidth = 360f;
    private const float WideWidth = CanvasWidth;
    private const float CompactHeight = 70f;
    private const float StandardHeight = 112f;
    private const float WideHeight = DefaultCanvasHeight;
    private const float OpenSeconds = 0.38f;
    private const float CloseSeconds = 0.22f;
    private const float MarqueeGap = 48f;
    private const float MarqueePixelsPerSecond = 46f;
    private const float StripePixelsPerSecond = 30f;
    private const float MaxPanelHeight = 220f;
    private const float ContentTopPadding = 27f;
    private const float ContentBottomPadding = 31f;
    private const float TitleHeight = 22f;
    private const float TitleTextGap = 10f;
    private const float BodyLineGap = 4f;
    private const float MinimumTextHeight = 24f;
    private const float TextClipSafetyPadding = 4f;
    private const float ContentLeftPadding = 76f;
    private const float ContentRightPadding = 20f;

    private enum NotificationVisualState : byte
    {
        Hidden,
        Opening,
        Showing,
        Closing
    }

    private readonly Label _titleLabel;
    private readonly NotificationTextControl _textLabel;
    private readonly LayoutContainer _textClip;
    private readonly Font _titleFont;
    private readonly Font _textFont;

    private NotificationVisualState _state = NotificationVisualState.Hidden;
    private Color _accentColor = Color.White;
    private string _title = string.Empty;
    private string _text = string.Empty;
    private readonly List<string> _wrappedLines = new();
    private WH40KNotificationIcon _icon = WH40KNotificationIcon.Vox;
    private bool _marquee;
    private float _durationSeconds;
    private float _visibleSeconds;
    private float _progress;
    private float _marqueeTime;
    private float _stripeTime;
    private float _contentTextWidth;
    private float _measuredTextWidth;
    private float _currentWidth = StandardWidth;
    private float _currentHeight = StandardHeight;
    private float _canvasHeight = DefaultCanvasHeight;

    public event Action? NotificationClosed;

    public bool IsBusy => _state != NotificationVisualState.Hidden;

    public WH40KNotificationHudControl()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _titleFont = cache.GetFont("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf", 14);
        _textFont = cache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 13);

        Visible = false;
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = false;
        MinSize = new Vector2(CanvasWidth, DefaultCanvasHeight);
        SetSize = new Vector2(CanvasWidth, DefaultCanvasHeight);

        _titleLabel = new Label
        {
            FontOverride = _titleFont,
            ClipText = true,
            Align = Label.AlignMode.Left,
            VAlign = Label.VAlignMode.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };

        _textLabel = new NotificationTextControl(_textFont)
        {
            MouseFilter = MouseFilterMode.Ignore,
        };

        _textClip = new LayoutContainer
        {
            RectClipContent = true,
            MouseFilter = MouseFilterMode.Ignore,
        };

        _textClip.AddChild(_textLabel);
        AddChild(_titleLabel);
        AddChild(_textClip);
        _titleLabel.Visible = false;
        _textClip.Visible = false;

        ApplySize(WH40KNotificationSize.Standard);
        ApplyChildLayout();
    }

    public void Show(WH40KNotificationEvent ev)
    {
        _accentColor = ev.AccentColor;
        _text = NormalizeBodyText(ev.Text);
        _icon = ev.Icon == WH40KNotificationIcon.Auto
            ? WH40KNotificationMetadata.DefaultIcon(ev.Category, ev.AccentColor)
            : ev.Icon;
        _marquee = ev.Marquee && !_text.Contains('\n');
        _durationSeconds = Math.Max(0f, ev.DurationSeconds);
        MouseFilter = MouseFilterMode.Stop;
        _visibleSeconds = 0f;
        _marqueeTime = 0f;
        _stripeTime = 0f;
        _progress = 0f;
        _state = NotificationVisualState.Opening;

        ApplySize(ev.Size);
        _title = SingleLine(ev.Title);
        _titleLabel.Text = _title;
        _textLabel.Text = _text;
        _textLabel.Marquee = _marquee;
        _textLabel.Multiline = _text.Contains('\n');
        Visible = true;

        UpdateTextAlpha(0f);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var delta = args.DeltaSeconds;
        switch (_state)
        {
            case NotificationVisualState.Opening:
                _progress = Math.Clamp(_progress + delta / OpenSeconds, 0f, 1f);
                if (_progress >= 1f)
                {
                    _state = NotificationVisualState.Showing;
                    _visibleSeconds = 0f;
                }
                break;

            case NotificationVisualState.Showing:
                _visibleSeconds += delta;
                if (_durationSeconds > 0f && _visibleSeconds >= _durationSeconds)
                    StartClosing();
                break;

            case NotificationVisualState.Closing:
                _progress = Math.Clamp(_progress - delta / CloseSeconds, 0f, 1f);
                if (_progress <= 0f)
                {
                    _state = NotificationVisualState.Hidden;
                    Visible = false;
                    MouseFilter = MouseFilterMode.Ignore;
                    NotificationClosed?.Invoke();
                    return;
                }
                break;
        }

        if (_marquee && _state != NotificationVisualState.Hidden)
            _marqueeTime += delta;

        if (_state != NotificationVisualState.Hidden)
            _stripeTime += delta;

        UpdateTextAlpha(SmoothStep(Math.Clamp((_progress - 0.44f) / 0.56f, 0f, 1f)));
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        if (_state is NotificationVisualState.Opening or NotificationVisualState.Showing)
        {
            StartClosing();
            args.Handle();
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (_state == NotificationVisualState.Hidden || _progress <= 0f)
            return;

        var open = EaseOutCubic(_progress);
        var alpha = SmoothStep(_progress);
        var panelLeft = (CanvasWidth - _currentWidth) * 0.5f;
        var panelTop = (_canvasHeight - _currentHeight) * 0.5f;
        var panelCenter = new Vector2(panelLeft + _currentWidth * 0.5f, panelTop + _currentHeight * 0.5f);
        var width = Lerp(72f, _currentWidth, open);
        var height = Lerp(26f, _currentHeight, open);
        var left = panelCenter.X - width * 0.5f;
        var top = panelCenter.Y - height * 0.5f;
        var rect = new UIBox2(left, top, left + width, top + height);
        var accent = _accentColor.WithAlpha(0.92f * alpha);
        var faintAccent = _accentColor.WithAlpha(0.32f * alpha);

        handle.DrawRect(rect, Color.FromHex("#06070A").WithAlpha(0.86f * alpha));
        DrawStripeBand(handle, rect, topBand: true, alpha);
        DrawStripeBand(handle, rect, topBand: false, alpha);
        handle.DrawRect(rect, Color.FromHex("#151821").WithAlpha(0.78f * alpha), filled: false);

        var inner = new UIBox2(rect.Left + 5f, rect.Top + 5f, rect.Right - 5f, rect.Bottom - 5f);
        handle.DrawRect(inner, Color.Black.WithAlpha(0.18f * alpha), filled: false);
        DrawCornerBrackets(handle, rect, accent, faintAccent, open);
        DrawNotificationIcon(handle, rect.TopLeft + new Vector2(34f, (rect.Bottom - rect.Top) * 0.5f), accent, alpha, _icon);
        DrawContent(handle, rect, alpha);

        base.Draw(handle);
    }

    private void StartClosing()
    {
        if (_state == NotificationVisualState.Closing || _state == NotificationVisualState.Hidden)
            return;

        _state = NotificationVisualState.Closing;
    }

    private void ApplySize(WH40KNotificationSize size)
    {
        var baseSize = size switch
        {
            WH40KNotificationSize.Compact => (CompactWidth, CompactHeight),
            WH40KNotificationSize.Wide => (WideWidth, WideHeight),
            _ => (StandardWidth, StandardHeight),
        };
        _currentWidth = baseSize.Item1;
        _contentTextWidth = GetContentTextWidth(_currentWidth, _marquee);
        _measuredTextWidth = MeasureTextWidth(_textFont, _text, UIScale);
        RebuildWrappedLines(_contentTextWidth);

        var desiredTextHeight = GetDesiredTextHeight(_marquee ? 1 : _wrappedLines.Count);
        var desiredPanelHeight = ContentTopPadding + TitleHeight + TitleTextGap + desiredTextHeight + ContentBottomPadding;

        _currentHeight = Math.Clamp(Math.Max(baseSize.Item2, desiredPanelHeight), baseSize.Item2, MaxPanelHeight);
        _canvasHeight = Math.Max(DefaultCanvasHeight, _currentHeight);

        MinSize = new Vector2(CanvasWidth, _canvasHeight);
        SetSize = new Vector2(CanvasWidth, _canvasHeight);
    }

    private void ApplyChildLayout()
    {
        var textAlpha = SmoothStep(Math.Clamp((_progress - 0.44f) / 0.56f, 0f, 1f));
        var panelLeft = (CanvasWidth - _currentWidth) * 0.5f;
        var panelTop = (_canvasHeight - _currentHeight) * 0.5f;
        var left = panelLeft + ContentLeftPadding;
        var titleTop = panelTop + ContentTopPadding;
        var textTop = titleTop + TitleHeight + TitleTextGap;
        var textWidth = _contentTextWidth;
        var availableTextHeight = panelTop + _currentHeight - ContentBottomPadding - textTop;
        var textHeight = Math.Max(MinimumTextHeight, availableTextHeight);

        LayoutContainer.SetPosition(_titleLabel, new Vector2(left, titleTop));
        _titleLabel.MinSize = new Vector2(textWidth, TitleHeight);
        _titleLabel.SetSize = new Vector2(textWidth, TitleHeight);

        LayoutContainer.SetPosition(_textClip, new Vector2(left, textTop));
        _textClip.MinSize = new Vector2(textWidth, textHeight);
        _textClip.SetSize = new Vector2(textWidth, textHeight);

        if (_marquee && _measuredTextWidth > textWidth)
        {
            var cycle = _measuredTextWidth + textWidth + MarqueeGap;
            var offset = textWidth - ((_marqueeTime * MarqueePixelsPerSecond) % cycle);
            LayoutContainer.SetPosition(_textLabel, Vector2.Zero);
            _textLabel.MarqueeOffset = offset;
            _textLabel.ViewWidth = textWidth;
            _textLabel.MinSize = new Vector2(textWidth, textHeight);
            _textLabel.SetSize = new Vector2(textWidth, textHeight);
        }
        else
        {
            LayoutContainer.SetPosition(_textLabel, Vector2.Zero);
            _textLabel.MarqueeOffset = 0f;
            _textLabel.ViewWidth = textWidth;
            _textLabel.MinSize = new Vector2(textWidth, textHeight);
            _textLabel.SetSize = new Vector2(textWidth, textHeight);
        }

        UpdateTextAlpha(textAlpha);
    }

    private void UpdateTextAlpha(float alpha)
    {
        _titleLabel.FontColorOverride = Color.White.WithAlpha(0.94f * alpha);
        _textLabel.FontColor = Color.FromHex("#F1F1F1").WithAlpha(0.9f * alpha);
    }

    private void DrawStripeBand(DrawingHandleScreen handle, UIBox2 rect, bool topBand, float alpha)
    {
        var y = topBand ? rect.Top + 6f : rect.Bottom - 22f;
        var band = new UIBox2(rect.Left + 7f, y, rect.Right - 7f, y + 16f);
        handle.DrawRect(band, Color.Black.WithAlpha(0.42f * alpha));

        const float spacing = 9f;
        const float stripeWidth = 15f;
        var direction = topBand ? 1f : -1f;
        var phase = PositiveModulo(_stripeTime * StripePixelsPerSecond * direction, spacing);

        for (var x = band.Left - stripeWidth - spacing + phase; x < band.Right + stripeWidth + spacing; x += spacing)
        {
            var rawFrom = topBand
                ? new Vector2(x, band.Bottom)
                : new Vector2(x + stripeWidth, band.Bottom);
            var rawTo = topBand
                ? new Vector2(x + stripeWidth, band.Top)
                : new Vector2(x, band.Top);

            if (!TryClipLineToBox(rawFrom, rawTo, band, out var from, out var to))
                continue;

            handle.DrawLine(from, to, Color.FromHex("#2A2D34").WithAlpha(0.64f * alpha));
        }
    }

    private static void DrawCornerBrackets(
        DrawingHandleScreen handle,
        UIBox2 rect,
        Color accent,
        Color faintAccent,
        float open)
    {
        var outer = new UIBox2(rect.Left - 5f, rect.Top - 5f, rect.Right + 5f, rect.Bottom + 5f);
        var length = Lerp(12f, 34f, open);

        DrawCorner(handle, outer.TopLeft, new Vector2(1f, 0f), new Vector2(0f, 1f), length, accent, faintAccent);
        DrawCorner(handle, outer.TopRight, new Vector2(-1f, 0f), new Vector2(0f, 1f), length, accent, faintAccent);
        DrawCorner(handle, outer.BottomLeft, new Vector2(1f, 0f), new Vector2(0f, -1f), length, accent, faintAccent);
        DrawCorner(handle, outer.BottomRight, new Vector2(-1f, 0f), new Vector2(0f, -1f), length, accent, faintAccent);
    }

    private static void DrawCorner(
        DrawingHandleScreen handle,
        Vector2 origin,
        Vector2 horizontal,
        Vector2 vertical,
        float length,
        Color accent,
        Color faintAccent)
    {
        handle.DrawLine(origin, origin + horizontal * length, accent);
        handle.DrawLine(origin, origin + vertical * length, accent);
        handle.DrawLine(origin + horizontal * 4f, origin + horizontal * (length + 8f), faintAccent);
        handle.DrawLine(origin + vertical * 4f, origin + vertical * (length + 8f), faintAccent);
    }

    private static void DrawNotificationIcon(
        DrawingHandleScreen handle,
        Vector2 center,
        Color accent,
        float alpha,
        WH40KNotificationIcon icon)
    {
        var white = Color.White.WithAlpha(0.92f * alpha);
        var muted = Color.FromHex("#2D3038").WithAlpha(0.82f * alpha);
        var glyphBox = UIBox2.FromDimensions(center - new Vector2(20f, 20f), new Vector2(40f, 40f));
        handle.DrawRect(glyphBox, muted);
        handle.DrawRect(glyphBox, accent.WithAlpha(0.76f * alpha), filled: false);

        switch (icon)
        {
            case WH40KNotificationIcon.Aquila:
                DrawAquilaIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Chaos:
                DrawChaosIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Weather:
                DrawWeatherIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Event:
                DrawEventIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Objective:
                DrawObjectiveIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Mission:
                DrawMissionIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Point:
                DrawPointIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Warning:
                DrawWarningIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Supply:
                DrawSupplyIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Cog:
                DrawCogIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Skull:
                DrawSkullIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Admin:
                DrawAdminIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Tau:
                DrawTauIcon(handle, center, white, accent);
                return;
            case WH40KNotificationIcon.Warp:
                DrawWarpIcon(handle, center, white, accent);
                return;
        }

        DrawVoxIcon(handle, center, white, accent);
    }

    private static void DrawVoxIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        var top = center + new Vector2(0f, -12f);
        var left = center + new Vector2(-9f, -1f);
        var right = center + new Vector2(9f, -1f);
        var bottom = center + new Vector2(0f, 12f);
        handle.DrawLine(top, left, white);
        handle.DrawLine(left, bottom, white);
        handle.DrawLine(bottom, right, white);
        handle.DrawLine(right, top, white);

        handle.DrawLine(center + new Vector2(-6f, -3f), center + new Vector2(0f, 8f), white);
        handle.DrawLine(center + new Vector2(6f, -3f), center + new Vector2(0f, 8f), white);
        handle.DrawLine(center + new Vector2(-5f, -8f), center + new Vector2(5f, -8f), accent);
    }

    private static void DrawAquilaIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        handle.DrawCircle(center + new Vector2(0f, -6f), 3.5f, accent);
        handle.DrawLine(center + new Vector2(0f, -2f), center + new Vector2(0f, 10f), white);
        for (var i = 0; i < 4; i++)
        {
            var y = -5f + i * 4f;
            handle.DrawLine(center + new Vector2(-2f, y), center + new Vector2(-16f, y + 5f), white);
            handle.DrawLine(center + new Vector2(2f, y), center + new Vector2(16f, y + 5f), white);
        }
        handle.DrawLine(center + new Vector2(-4f, 11f), center + new Vector2(4f, 11f), accent);
    }

    private static void DrawChaosIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        handle.DrawCircle(center, 7f, white, filled: false);
        for (var i = 0; i < 8; i++)
        {
            var angle = MathF.PI * 2f * i / 8f;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            handle.DrawLine(center + dir * 4f, center + dir * 15f, i % 2 == 0 ? accent : white);
            var side = new Vector2(-dir.Y, dir.X);
            var tip = center + dir * 15f;
            handle.DrawLine(tip, tip - dir * 4f + side * 3f, accent);
            handle.DrawLine(tip, tip - dir * 4f - side * 3f, accent);
        }
    }

    private static void DrawWeatherIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        handle.DrawCircle(center + new Vector2(-7f, -2f), 6f, white.WithAlpha(0.38f), filled: false);
        handle.DrawCircle(center + new Vector2(1f, -5f), 8f, white.WithAlpha(0.5f), filled: false);
        handle.DrawLine(center + new Vector2(-14f, 3f), center + new Vector2(10f, 3f), white);
        handle.DrawLine(center + new Vector2(3f, 4f), center + new Vector2(-3f, 13f), accent);
        handle.DrawLine(center + new Vector2(-3f, 13f), center + new Vector2(5f, 10f), accent);
        handle.DrawLine(center + new Vector2(5f, 10f), center + new Vector2(0f, 18f), accent);
    }

    private static void DrawEventIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        for (var i = 0; i < 8; i++)
        {
            var angle = MathF.PI * 2f * i / 8f;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            handle.DrawLine(center + dir * 5f, center + dir * 15f, i % 2 == 0 ? accent : white);
        }
        handle.DrawCircle(center, 4f, white);
    }

    private static void DrawObjectiveIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        handle.DrawCircle(center, 13f, accent, filled: false);
        handle.DrawCircle(center, 6f, white, filled: false);
        handle.DrawLine(center + new Vector2(-16f, 0f), center + new Vector2(16f, 0f), white);
        handle.DrawLine(center + new Vector2(0f, -16f), center + new Vector2(0f, 16f), white);
    }

    private static void DrawMissionIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        handle.DrawLine(center + new Vector2(-9f, 15f), center + new Vector2(-9f, -14f), white);
        handle.DrawLine(center + new Vector2(-8f, -13f), center + new Vector2(12f, -8f), accent);
        handle.DrawLine(center + new Vector2(12f, -8f), center + new Vector2(-8f, -2f), accent);
        handle.DrawLine(center + new Vector2(-8f, -2f), center + new Vector2(-8f, -13f), accent);
        handle.DrawLine(center + new Vector2(-14f, 15f), center + new Vector2(4f, 15f), white);
    }

    private static void DrawPointIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        handle.DrawRect(UIBox2.FromDimensions(center - new Vector2(10f, 10f), new Vector2(20f, 20f)), accent, filled: false);
        handle.DrawLine(center + new Vector2(-16f, 0f), center + new Vector2(-5f, 0f), white);
        handle.DrawLine(center + new Vector2(5f, 0f), center + new Vector2(16f, 0f), white);
        handle.DrawLine(center + new Vector2(0f, -16f), center + new Vector2(0f, -5f), white);
        handle.DrawLine(center + new Vector2(0f, 5f), center + new Vector2(0f, 16f), white);
    }

    private static void DrawWarningIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        var top = center + new Vector2(0f, -15f);
        var left = center + new Vector2(-14f, 12f);
        var right = center + new Vector2(14f, 12f);
        handle.DrawLine(top, left, accent);
        handle.DrawLine(left, right, accent);
        handle.DrawLine(right, top, accent);
        handle.DrawLine(center + new Vector2(0f, -6f), center + new Vector2(0f, 5f), white);
        handle.DrawCircle(center + new Vector2(0f, 10f), 1.8f, white);
    }

    private static void DrawSupplyIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        var box = UIBox2.FromDimensions(center - new Vector2(13f, 9f), new Vector2(26f, 18f));
        handle.DrawRect(box, accent, filled: false);
        handle.DrawLine(center + new Vector2(-13f, -3f), center + new Vector2(13f, -3f), white);
        handle.DrawLine(center + new Vector2(0f, -9f), center + new Vector2(0f, 9f), white);
        handle.DrawLine(center + new Vector2(-7f, 12f), center + new Vector2(7f, 12f), accent);
    }

    private static void DrawCogIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        handle.DrawCircle(center, 9f, white, filled: false);
        handle.DrawCircle(center, 3.5f, accent, filled: false);
        for (var i = 0; i < 8; i++)
        {
            var angle = MathF.PI * 2f * i / 8f;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            handle.DrawLine(center + dir * 10f, center + dir * 15f, accent);
        }
    }

    private static void DrawSkullIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        handle.DrawCircle(center + new Vector2(0f, -3f), 10f, white.WithAlpha(0.25f), filled: false);
        handle.DrawCircle(center + new Vector2(-4f, -4f), 2.5f, accent);
        handle.DrawCircle(center + new Vector2(4f, -4f), 2.5f, accent);
        handle.DrawLine(center + new Vector2(-6f, 7f), center + new Vector2(6f, 7f), white);
        handle.DrawLine(center + new Vector2(-3f, 7f), center + new Vector2(-3f, 12f), white);
        handle.DrawLine(center + new Vector2(3f, 7f), center + new Vector2(3f, 12f), white);
    }

    private static void DrawAdminIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        var top = center + new Vector2(0f, -15f);
        var left = center + new Vector2(-12f, -7f);
        var right = center + new Vector2(12f, -7f);
        var bottom = center + new Vector2(0f, 15f);
        handle.DrawLine(top, left, accent);
        handle.DrawLine(top, right, accent);
        handle.DrawLine(left, center + new Vector2(-9f, 6f), white);
        handle.DrawLine(right, center + new Vector2(9f, 6f), white);
        handle.DrawLine(center + new Vector2(-9f, 6f), bottom, accent);
        handle.DrawLine(center + new Vector2(9f, 6f), bottom, accent);
        handle.DrawLine(center + new Vector2(-5f, 1f), center + new Vector2(5f, 1f), white);
    }

    private static void DrawTauIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        handle.DrawCircle(center, 12f, accent, filled: false);
        handle.DrawCircle(center, 4f, white);
        handle.DrawLine(center + new Vector2(0f, -16f), center + new Vector2(0f, -7f), white);
        handle.DrawLine(center + new Vector2(-13f, 9f), center + new Vector2(-6f, 4f), white);
        handle.DrawLine(center + new Vector2(13f, 9f), center + new Vector2(6f, 4f), white);
    }

    private static void DrawWarpIcon(DrawingHandleScreen handle, Vector2 center, Color white, Color accent)
    {
        handle.DrawCircle(center, 13f, accent.WithAlpha(0.75f), filled: false);
        handle.DrawLine(center + new Vector2(-12f, -8f), center + new Vector2(8f, -12f), white);
        handle.DrawLine(center + new Vector2(8f, -12f), center + new Vector2(13f, 2f), accent);
        handle.DrawLine(center + new Vector2(13f, 2f), center + new Vector2(-4f, 13f), white);
        handle.DrawLine(center + new Vector2(-4f, 13f), center + new Vector2(-12f, -8f), accent);
        handle.DrawCircle(center, 3f, white);
    }

    private void DrawContent(DrawingHandleScreen handle, UIBox2 rect, float alpha)
    {
        var contentLeft = rect.Left + ContentLeftPadding;
        var contentRight = rect.Right - ContentRightPadding;
        var titleTop = rect.Top + ContentTopPadding;
        var titleBaseline = titleTop + _titleFont.GetAscent(UIScale);
        var textTop = titleTop + TitleHeight + TitleTextGap;
        var textBaseline = textTop + (_textFont.GetHeight(UIScale) * 0.5f) + (_textFont.GetAscent(UIScale) * 0.5f);
        var textRight = contentRight - (_marquee ? TextClipSafetyPadding : 0f);
        var textBottom = rect.Bottom - ContentBottomPadding;

        if (textRight <= contentLeft + 32f)
            return;

        var titleColor = Color.White.WithAlpha(0.94f * alpha);
        var textColor = Color.FromHex("#F1F1F1").WithAlpha(0.9f * alpha);

        DrawBoundedLine(handle, _titleFont, _title, contentLeft, contentRight, titleBaseline, titleColor, fadeEdges: false);

        if (!_marquee)
        {
            DrawWrappedBounded(handle, contentLeft, textRight, textTop, textBottom, textColor);
            return;
        }

        if (_marquee && _measuredTextWidth > textRight - contentLeft)
        {
            var textWidth = textRight - contentLeft;
            var cycle = _measuredTextWidth + textWidth + MarqueeGap;
            var offset = textWidth - ((_marqueeTime * MarqueePixelsPerSecond) % cycle);
            DrawBoundedLine(handle, _textFont, _text, contentLeft + offset, textRight, textBaseline, textColor, fadeEdges: true, fadeLeft: contentLeft);
        }
        else
        {
            DrawBoundedLine(handle, _textFont, _text, contentLeft, textRight, textBaseline, textColor, fadeEdges: false);
        }
    }

    private void DrawWrappedBounded(DrawingHandleScreen handle, float left, float right, float top, float bottom, Color color)
    {
        var lineHeight = GetBodyLineHeight();
        var baseline = top + _textFont.GetAscent(UIScale);

        foreach (var line in _wrappedLines)
        {
            if (baseline > bottom)
                return;

            DrawBoundedLine(handle, _textFont, line, left, right, baseline, color, fadeEdges: false);
            baseline += lineHeight;
        }
    }

    private void DrawBoundedLine(
        DrawingHandleScreen handle,
        Font font,
        string text,
        float x,
        float right,
        float baseline,
        Color color,
        bool fadeEdges,
        float? fadeLeft = null)
    {
        var left = fadeLeft ?? x;

        foreach (var rune in text.EnumerateRunes())
        {
            if (rune == new Rune('\n'))
                continue;

            var metrics = font.GetCharMetrics(rune, UIScale);
            if (metrics == null)
                continue;

            var advance = metrics.Value.Advance;
            var charLeft = x;
            var charRight = x + advance;
            if (charLeft > right)
                break;

            if (charLeft >= left && charRight <= right)
            {
                var drawColor = fadeEdges
                    ? color.WithAlpha(color.A * GetEdgeFade((charLeft + charRight) * 0.5f, left, right))
                    : color;

                if (drawColor.A > 0.01f)
                    font.DrawChar(handle, rune, new Vector2(x, baseline), UIScale, drawColor);
            }

            x += advance;
        }
    }

    private static float GetEdgeFade(float x, float left, float right)
    {
        const float fadeWidth = 14f;
        var fromLeft = Math.Clamp((x - left) / fadeWidth, 0f, 1f);
        var fromRight = Math.Clamp((right - x) / fadeWidth, 0f, 1f);
        return SmoothStep(Math.Min(fromLeft, fromRight));
    }

    private static string SingleLine(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static string NormalizeBodyText(string value)
    {
        return value
            .Replace("\\n", "\n")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();
    }

    private static float GetContentTextWidth(float panelWidth, bool marquee)
    {
        var safety = marquee ? TextClipSafetyPadding : 0f;
        return Math.Max(80f, panelWidth - ContentLeftPadding - ContentRightPadding - safety);
    }

    private float GetDesiredTextHeight(int lineCount)
    {
        var lineHeight = GetBodyLineHeight();

        return Math.Max(MinimumTextHeight, lineCount * lineHeight + 6f);
    }

    private float GetBodyLineHeight()
    {
        return _textFont.GetLineHeight(UIScale) + BodyLineGap;
    }

    private void RebuildWrappedLines(float maxWidth)
    {
        _wrappedLines.Clear();

        if (_marquee)
        {
            _wrappedLines.Add(_text);
            return;
        }

        _wrappedLines.AddRange(BuildWrappedLines(_text, maxWidth));
    }

    private List<string> BuildWrappedLines(string value, float maxWidth)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        var currentWidth = 0f;
        var spaceWidth = MeasureRuneWidth(_textFont, new Rune(' '), UIScale);
        maxWidth = Math.Max(24f, maxWidth);

        void PushLine()
        {
            lines.Add(current.ToString().TrimEnd());
            current.Clear();
            currentWidth = 0f;
        }

        void AppendLongWord(string word)
        {
            foreach (var rune in word.EnumerateRunes())
            {
                var runeWidth = MeasureRuneWidth(_textFont, rune, UIScale);
                if (current.Length > 0 && currentWidth + runeWidth > maxWidth)
                    PushLine();

                current.Append(rune.ToString());
                currentWidth += runeWidth;
            }
        }

        foreach (var paragraph in value.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                PushLine();
                continue;
            }

            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var wordWidth = MeasureTextWidth(_textFont, word, UIScale);
                var separatorWidth = current.Length == 0 ? 0f : spaceWidth;

                if (current.Length > 0 && currentWidth + separatorWidth + wordWidth > maxWidth)
                    PushLine();

                if (wordWidth > maxWidth)
                {
                    AppendLongWord(word);
                    continue;
                }

                if (current.Length > 0)
                {
                    current.Append(' ');
                    currentWidth += spaceWidth;
                }

                current.Append(word);
                currentWidth += wordWidth;
            }

            if (current.Length > 0)
                PushLine();
        }

        if (lines.Count == 0)
            lines.Add(current.ToString());

        return lines;
    }

    private static float MeasureTextWidth(Font font, string text, float scale)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            var metrics = font.GetCharMetrics(rune, scale);
            if (metrics != null)
                width += metrics.Value.Advance;
        }

        return width;
    }

    private static float MeasureRuneWidth(Font font, Rune rune, float scale)
    {
        var metrics = font.GetCharMetrics(rune, scale);
        return metrics?.Advance ?? 0f;
    }

    private static float EaseOutCubic(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        var inverse = 1f - value;
        return 1f - inverse * inverse * inverse;
    }

    private static float SmoothStep(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
    }

    private static float Lerp(float from, float to, float value)
    {
        return from + (to - from) * Math.Clamp(value, 0f, 1f);
    }

    private static float PositiveModulo(float value, float divisor)
    {
        return ((value % divisor) + divisor) % divisor;
    }

    private static bool TryClipLineToBox(Vector2 start, Vector2 end, UIBox2 box, out Vector2 clippedStart, out Vector2 clippedEnd)
    {
        var delta = end - start;
        var min = 0f;
        var max = 1f;

        if (!ClipTest(-delta.X, start.X - box.Left, ref min, ref max) ||
            !ClipTest(delta.X, box.Right - start.X, ref min, ref max) ||
            !ClipTest(-delta.Y, start.Y - box.Top, ref min, ref max) ||
            !ClipTest(delta.Y, box.Bottom - start.Y, ref min, ref max))
        {
            clippedStart = default;
            clippedEnd = default;
            return false;
        }

        clippedStart = start + delta * min;
        clippedEnd = start + delta * max;
        return true;
    }

    private static bool ClipTest(float edge, float distance, ref float min, ref float max)
    {
        if (Math.Abs(edge) < 0.0001f)
            return distance >= 0f;

        var value = distance / edge;
        if (edge < 0f)
        {
            if (value > max)
                return false;
            if (value > min)
                min = value;
        }
        else
        {
            if (value < min)
                return false;
            if (value < max)
                max = value;
        }

        return true;
    }

    private sealed class NotificationTextControl : Control
    {
        private const float EdgeFadeWidth = 34f;

        private readonly Font _font;

        public string Text = string.Empty;
        public Color FontColor = Color.White;
        public bool Marquee;
        public bool Multiline;
        public float MarqueeOffset;
        public float ViewWidth;

        public NotificationTextControl(Font font)
        {
            _font = font;
            RectClipContent = true;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            if (Text.Length == 0 || PixelSize.X <= 0 || PixelSize.Y <= 0)
                return;

            if (Multiline)
            {
                DrawMultiline(handle);
                return;
            }

            var textHeight = _font.GetHeight(UIScale);
            var baselineY = (PixelSize.Y - textHeight) * 0.5f + _font.GetAscent(UIScale);
            if (Marquee)
                DrawMarquee(handle, baselineY);
            else
                DrawLine(handle, Text, 0f, baselineY);
        }

        private void DrawMultiline(DrawingHandleScreen handle)
        {
            var lineHeight = _font.GetLineHeight(UIScale);
            var baselineY = _font.GetAscent(UIScale);
            var start = 0;

            for (var i = 0; i <= Text.Length; i++)
            {
                if (i < Text.Length && Text[i] != '\n')
                    continue;

                if (baselineY - lineHeight > PixelSize.Y)
                    return;

                DrawLine(handle, Text[start..i], 0f, baselineY);
                baselineY += lineHeight;
                start = i + 1;
            }
        }

        private void DrawMarquee(DrawingHandleScreen handle, float baselineY)
        {
            var x = MarqueeOffset;
            foreach (var rune in Text.EnumerateRunes())
            {
                var metrics = _font.GetCharMetrics(rune, UIScale);
                if (metrics == null)
                    continue;

                var advance = metrics.Value.Advance;
                var charLeft = x;
                var charRight = x + advance;
                if (charLeft >= 0f && charRight <= ViewWidth)
                {
                    var fade = GetEdgeFade((charLeft + charRight) * 0.5f);
                    if (fade > 0.01f)
                        _font.DrawChar(handle, rune, new Vector2(x, baselineY), UIScale, FontColor.WithAlpha(FontColor.A * fade));
                }

                x += advance;
            }
        }

        private void DrawLine(DrawingHandleScreen handle, string line, float x, float baselineY)
        {
            foreach (var rune in line.EnumerateRunes())
            {
                if (rune == new Rune('\n'))
                    continue;

                var metrics = _font.GetCharMetrics(rune, UIScale);
                if (metrics == null)
                    continue;

                var advance = metrics.Value.Advance;
                var charLeft = x;
                var charRight = x + advance;
                if (charRight >= 0f && charLeft <= PixelSize.X)
                    _font.DrawChar(handle, rune, new Vector2(x, baselineY), UIScale, FontColor);

                x += advance;
            }
        }

        private float GetEdgeFade(float x)
        {
            var left = Math.Clamp(x / EdgeFadeWidth, 0f, 1f);
            var right = Math.Clamp((ViewWidth - x) / EdgeFadeWidth, 0f, 1f);
            var value = Math.Min(left, right);
            return value * value * (3f - 2f * value);
        }
    }
}
