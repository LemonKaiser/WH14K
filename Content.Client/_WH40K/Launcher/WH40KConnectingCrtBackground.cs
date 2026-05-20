using System;
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

    private readonly IGameTiming _timing;
    private readonly Font _terminalFont;
    private readonly Font _smallFont;

    public WH40KConnectingCrtBackground()
    {
        IoCManager.InjectDependencies(this);

        _timing = IoCManager.Resolve<IGameTiming>();
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
        DrawTerminalText(handle, size, time, flicker);
    }

    private void DrawTerminalText(DrawingHandleScreen handle, Vector2 size, float time, float flicker)
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

        var pulse = MathF.Sin(time * 3.4f) > -0.35f ? "_" : " ";
        DrawGlowString(
            handle,
            _terminalFont,
            new Vector2(24f, MathF.Max(size.Y - 38f, 110f)),
            $"[guard@battlefleet uplink]$ awaiting handshake{pulse}",
            color,
            0.22f);

        var rightText = $"SIGNAL NOISE {Hash01((int) (time * 7f)) * 100f:00.0}%";
        var rightPos = new Vector2(MathF.Max(24f, size.X - 265f), 18f);
        DrawGlowString(handle, _smallFont, rightPos, rightText, dim, 0.12f);
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
