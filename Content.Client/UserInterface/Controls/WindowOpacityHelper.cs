using System;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Controls;

public static class WindowOpacityHelper
{
    private static readonly Color FallbackPanelColor = Color.FromHex("#25252ADD");
    public const float MinUiWindowOpacity = 0.75f;
    public const float MaxUiWindowOpacity = 1f;
    private const float MaxReadableBackdropOpacity = 0.58f;

    public static void ApplyPanelOpacity(PanelContainer panel, float opacity, Color? fallbackColor = null)
    {
        StyleBox? source = panel.PanelOverride;
        if (source == null
            && panel.TryGetStyleProperty<StyleBox>(PanelContainer.StylePropertyPanel, out var styleProperty))
        {
            source = styleProperty;
        }

        panel.PanelOverride = CreateOpacityStyle(source, opacity, fallbackColor);
    }

    public static void ApplySelfModulateOpacity(Control control, float opacity, Color? fallbackColor = null)
    {
        var clampedOpacity = NormalizeWindowOpacity(opacity);

        Color source;
        if (control.ModulateSelfOverride is { } overrideColor)
            source = overrideColor;
        else if (control.TryGetStyleProperty(Control.StylePropertyModulateSelf, out Color styleColor))
            source = styleColor;
        else
            source = fallbackColor ?? Color.White;

        control.ModulateSelfOverride = source.WithAlpha(clampedOpacity);
    }

    public static float ResolveReadableBackdropOpacity(float opacity)
    {
        var clampedOpacity = NormalizeWindowOpacity(opacity);
        return Math.Clamp((1f - clampedOpacity) * 0.72f, 0f, MaxReadableBackdropOpacity);
    }

    public static StyleBox CreateOpacityStyle(StyleBox? source, float opacity, Color? fallbackColor = null)
    {
        var clampedOpacity = NormalizeWindowOpacity(opacity);

        if (TryCreateOpacityStyle(source, clampedOpacity, out var style))
            return style;

        return new StyleBoxFlat
        {
            BackgroundColor = (fallbackColor ?? FallbackPanelColor).WithAlpha(clampedOpacity),
        };
    }

    public static float NormalizeWindowOpacity(float opacity)
    {
        return Math.Clamp(opacity, MinUiWindowOpacity, MaxUiWindowOpacity);
    }

    private static bool TryCreateOpacityStyle(StyleBox? source, float opacity, out StyleBox style)
    {
        switch (source)
        {
            case StyleBoxFlat flat:
                style = new StyleBoxFlat(flat)
                {
                    BackgroundColor = flat.BackgroundColor.WithAlpha(opacity),
                    BorderColor = flat.BorderColor.WithAlpha(opacity),
                };
                return true;
            case StyleBoxTexture texture:
                style = new StyleBoxTexture(texture)
                {
                    Modulate = texture.Modulate.WithAlpha(opacity),
                };
                return true;
            default:
                style = default!;
                return false;
        }
    }
}
