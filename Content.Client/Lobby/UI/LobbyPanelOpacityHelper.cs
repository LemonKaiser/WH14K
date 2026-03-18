using System;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

internal static class LobbyPanelOpacityHelper
{
    private static readonly Color FallbackColor = Color.FromHex("#25252ADD");

    public static void ApplyPanelOpacity(PanelContainer panel, float opacity)
    {
        StyleBox? source = panel.PanelOverride;
        if (source == null
            && panel.TryGetStyleProperty<StyleBox>(PanelContainer.StylePropertyPanel, out var styleProperty))
        {
            source = styleProperty;
        }

        panel.PanelOverride = CreateOpacityStyle(source, opacity);
    }

    public static StyleBox CreateOpacityStyle(StyleBox? source, float opacity, Color? fallbackColor = null)
    {
        opacity = Math.Clamp(opacity, 0f, 1f);

        if (TryCreateOpacityStyle(source, opacity, out var style))
            return style;

        return new StyleBoxFlat
        {
            BackgroundColor = (fallbackColor ?? FallbackColor).WithAlpha(opacity),
        };
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
