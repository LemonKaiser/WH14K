using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Launcher;

/// <summary>
/// Draws the WH40K launcher connection background as an amber CRT terminal.
/// </summary>
public sealed class WH40KConnectingCrtBackground : Control
{
    private const string UplinkPrompt = "[guard@battlefleet uplink]$ ";
    private const string HandshakeText = "awaiting handshake";
    private const string GratitudeMessage =
        "Спасибо всем разработчиками Heretec, lemon_kaiser, gadosex, the_walking_rad, fizzoghoster, wickerler2, hos7719, suslik2331, comradlie, londorondondon, litoryionn, endr_animet, myr04ka, .car_bon, igornoopi и всем другим за работу и помощь проекту!";
    private const float HandshakeHoldSeconds = 30f;
    private const float BackspaceCharsPerSecond = 8f;
    private const float TypeCharsPerSecond = 18f;
    private const float HeartRevealDelaySeconds = 2f;
    private const int HeartScale = 2;
    private const float HeartLineHeight = 18f;

    private static readonly Color Background = Color.FromHex("#050402");
    private static readonly Color TerminalGold = Color.FromHex("#d7b65a");
    private static readonly Color TerminalGoldSoft = Color.FromHex("#8d7440");

    private static readonly string[] BootLines =
    {
        "[guard@battlefleet Documents]$ ./wh14k_connect.sh",
        "Starting Program...",
        "Noospheric uplink engaged.",
        "Authenticating astropathic route...",
    };

    private static readonly string[] SigilLines =
    {
        "############################################################################",
        "###                                                                      ###",
        "###                    Heretek Warhammer 40k                             ###",
        "###                                                                      ###",
        "############################################################################",
        "###        NOOSPHERIC COMMAND CHANNEL // DARK FORGE TELEMETRY            ###",
        "############################################################################",
    };

    private static readonly string[] HeartLines =
    {
        "     ##     ##     ",
        "   ###### ######   ",
        "  ###############  ",
        "   #############   ",
        "     #########     ",
        "       #####       ",
        "        ###        ",
        "         #         ",
    };

    private readonly IGameTiming _timing;
    private readonly Font _terminalFont;
    private readonly Font _smallFont;
    private readonly float _startTime;

    public WH40KConnectingCrtBackground()
    {
        IoCManager.InjectDependencies(this);

        _timing = IoCManager.Resolve<IGameTiming>();
        _startTime = (float) _timing.RealTime.TotalSeconds;
        var cache = IoCManager.Resolve<IResourceCache>();
        _terminalFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 15);
        _smallFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 12);

        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = true;
        HorizontalExpand = true;
        VerticalExpand = true;
        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var size = PixelSize;
        if (size.X <= 0 || size.Y <= 0)
            return;

        var time = (float) _timing.RealTime.TotalSeconds;
        var flicker = 0.92f + MathF.Sin(time * 23.0f) * 0.025f + MathF.Sin(time * 41.0f) * 0.012f;

        handle.DrawRect(PixelSizeBox, Background);
        DrawTerminalRaster(handle, size, time);
        DrawTerminalText(handle, size, time, time - _startTime, flicker);
    }

    private static void DrawTerminalRaster(DrawingHandleScreen handle, Vector2 size, float time)
    {
        const float spacing = 6f;
        const float sweepRadius = 42f;
        var sweepY = (time * 78f) % (size.Y + sweepRadius * 2f) - sweepRadius;

        for (var y = 0f; y <= size.Y; y += spacing)
        {
            var distance = MathF.Abs(y - sweepY);
            var sweep = 1f - Math.Clamp(distance / sweepRadius, 0f, 1f);
            sweep *= sweep;

            var baseAlpha = 0.105f;
            var lineColor = TerminalGoldSoft.WithAlpha(baseAlpha + sweep * 0.30f);
            handle.DrawLine(new Vector2(0f, y), new Vector2(size.X, y), lineColor);

            if (sweep <= 0.01f)
                continue;

            var glow = TerminalGold.WithAlpha(sweep * 0.14f);
            handle.DrawLine(new Vector2(0f, y - 1f), new Vector2(size.X, y - 1f), glow);
            handle.DrawLine(new Vector2(0f, y + 1f), new Vector2(size.X, y + 1f), glow);
        }
    }

    private void DrawTerminalText(DrawingHandleScreen handle, Vector2 size, float time, float elapsedTime, float flicker)
    {
        var origin = new Vector2(24f, 8f);
        var color = TerminalGold.WithAlpha(Math.Clamp(flicker, 0.72f, 1.0f));
        var dim = TerminalGoldSoft.WithAlpha(0.5f * flicker);

        for (var i = 0; i < BootLines.Length; i++)
        {
            DrawGlowString(handle, _terminalFont, origin + new Vector2(0f, i * 24f), BootLines[i], color, 0.2f);
        }

        var sigilY = MathF.Max(90f, size.Y * 0.13f);
        for (var i = 0; i < SigilLines.Length; i++)
        {
            DrawGlowString(handle, _terminalFont, new Vector2(20f, sigilY + i * 26f), SigilLines[i], color, 0.16f);
        }

        DrawUplinkMessage(handle, size, time, elapsedTime, color);

        var rightText = $"SIGNAL NOISE {Hash01((int) (time * 7f)) * 100f:00.0}%";
        var rightPos = new Vector2(MathF.Max(24f, size.X - 265f), 18f);
        DrawGlowString(handle, _smallFont, rightPos, rightText, dim, 0.12f);
    }

    private void DrawUplinkMessage(DrawingHandleScreen handle, Vector2 size, float time, float elapsedTime, Color color)
    {
        const float lineHeight = 22f;
        var messageTime = elapsedTime - HandshakeHoldSeconds;
        var backspaceDuration = HandshakeText.Length / BackspaceCharsPerSecond;
        var typeStart = backspaceDuration + 0.45f;

        if (messageTime < 0f)
        {
            var pulse = MathF.Sin(time * 3.4f) > -0.35f ? "_" : " ";
            DrawGlowString(
                handle,
                _terminalFont,
                new Vector2(24f, MathF.Max(size.Y - 38f, 110f)),
                $"{UplinkPrompt}{HandshakeText}{pulse}",
                color,
                0.22f);
            return;
        }

        if (messageTime < backspaceDuration)
        {
            var visibleChars = Math.Max(0, HandshakeText.Length - (int) MathF.Floor(messageTime * BackspaceCharsPerSecond));
            DrawGlowString(
                handle,
                _terminalFont,
                new Vector2(24f, MathF.Max(size.Y - 38f, 110f)),
                $"{UplinkPrompt}{HandshakeText[..visibleChars]}_",
                color,
                0.22f);
            return;
        }

        if (messageTime < typeStart)
        {
            DrawGlowString(
                handle,
                _terminalFont,
                new Vector2(24f, MathF.Max(size.Y - 38f, 110f)),
                $"{UplinkPrompt}_",
                color,
                0.22f);
            return;
        }

        var typedChars = Math.Min(
            GratitudeMessage.Length,
            (int) MathF.Floor((messageTime - typeStart) * TypeCharsPerSecond));
        var maxChars = Math.Clamp((int) ((size.X - 80f) / 10.5f), 72, 150);
        var typingComplete = typedChars >= GratitudeMessage.Length;
        var heartVisible = messageTime >= typeStart + GratitudeMessage.Length / TypeCharsPerSecond + HeartRevealDelaySeconds;
        var cursor = !typingComplete || MathF.Sin(time * 5.8f) > -0.25f ? "_" : " ";
        var typedMessage = GratitudeMessage[..typedChars];
        var lines = WrapTerminalText(typedMessage, maxChars);
        var displayedLineCount = lines.Length;
        var heartHeight = heartVisible ? HeartLines.Length * HeartScale * HeartLineHeight : 0f;
        var heartOffset = heartVisible ? heartHeight + 34f : 0f;
        var baselineY = MathF.Max(size.Y - 38f, 110f) - heartOffset;
        var startY = baselineY - (displayedLineCount - 1) * lineHeight;

        for (var i = 0; i < lines.Length; i++)
        {
            var prefix = i == 0 ? UplinkPrompt : new string(' ', UplinkPrompt.Length);
            var lineText = i == lines.Length - 1 ? $"{lines[i]}{cursor}" : lines[i];
            DrawGlowString(
                handle,
                _terminalFont,
                new Vector2(24f, startY + i * lineHeight),
                $"{prefix}{lineText}",
                color,
                0.22f);
        }

        if (!heartVisible)
            return;

        var heartX = 24f + UplinkPrompt.Length * 9.5f;
        var heartY = startY + displayedLineCount * lineHeight + 12f;
        for (var i = 0; i < HeartLines.Length; i++)
        {
            var line = ScaleAsciiLine(HeartLines[i], HeartScale);
            for (var yScale = 0; yScale < HeartScale; yScale++)
            {
                DrawGlowString(
                    handle,
                    _terminalFont,
                    new Vector2(heartX, heartY + (i * HeartScale + yScale) * HeartLineHeight),
                    line,
                    color,
                    0.18f);
            }
        }
    }

    private static string ScaleAsciiLine(string line, int scale)
    {
        var scaled = new char[line.Length * scale];
        var index = 0;
        foreach (var c in line)
        {
            for (var i = 0; i < scale; i++)
            {
                scaled[index++] = c;
            }
        }

        return new string(scaled);
    }

    private static string[] WrapTerminalText(string text, int maxChars)
    {
        var lines = new List<string>();
        var remaining = text;

        while (remaining.Length > maxChars)
        {
            var split = remaining.LastIndexOf(' ', maxChars);
            if (split <= 0)
                split = maxChars;

            lines.Add(remaining[..split]);
            remaining = remaining[split..].TrimStart();
        }

        lines.Add(remaining);
        return lines.ToArray();
    }

    private static void DrawGlowString(
        DrawingHandleScreen handle,
        Font font,
        Vector2 position,
        string text,
        Color color,
        float glowAlpha)
    {
        var glow = color.WithAlpha(glowAlpha);
        handle.DrawString(font, position + new Vector2(-1f, 0f), text, glow);
        handle.DrawString(font, position + new Vector2(1f, 0f), text, glow);
        handle.DrawString(font, position + new Vector2(0f, -1f), text, glow);
        handle.DrawString(font, position + new Vector2(0f, 1f), text, glow);
        handle.DrawString(font, position, text, color);
    }

    private static int Hash(int seed)
    {
        var value = unchecked((uint) seed);
        value ^= value >> 16;
        value *= 0x7feb352d;
        value ^= value >> 15;
        value *= 0x846ca68b;
        value ^= value >> 16;
        return unchecked((int) value & 0x7fffffff);
    }

    private static float Hash01(int seed)
    {
        return (Hash(seed) & 0x00ffffff) / 16777215f;
    }
}
