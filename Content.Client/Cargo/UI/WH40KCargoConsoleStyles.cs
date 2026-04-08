using System;
using Content.Shared.Cargo.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Cargo.UI
{
    public readonly record struct WH40KCargoConsoleTheme(
        bool Enabled,
        string TeamId,
        Color Accent,
        Color BorderColor,
        Color BorderStrongColor,
        Color WindowBackground,
        Color PanelBackground,
        Color PanelAltBackground,
        Color SurfaceBackground,
        Color SurfaceHoverBackground,
        Color HeaderBackground,
        Color InputBackground,
        Color PreviewBackground,
        Color PrimaryText,
        Color SecondaryText,
        Color LabelText,
        Color DangerAccent,
        string FooterLocKey,
        string OrderMenuTitleLocKey)
    {
        public static readonly WH40KCargoConsoleTheme Disabled = new(
            false,
            string.Empty,
            Color.White,
            Color.FromHex("#404040".AsSpan()),
            Color.FromHex("#606060".AsSpan()),
            Color.FromHex("#202025".AsSpan()),
            Color.FromHex("#26262D".AsSpan()),
            Color.FromHex("#1E1E24".AsSpan()),
            Color.FromHex("#2A2A33".AsSpan()),
            Color.FromHex("#353540".AsSpan()),
            Color.FromHex("#30303A".AsSpan()),
            Color.FromHex("#1C1C20".AsSpan()),
            Color.FromHex("#141416".AsSpan()),
            Color.White,
            Color.FromHex("#C0C0C8".AsSpan()),
            Color.FromHex("#8C8C94".AsSpan()),
            Color.FromHex("#B04848".AsSpan()),
            string.Empty,
            string.Empty);

        public bool IsHeretics => TeamId == "Heretics";
    }

    public static class WH40KCargoConsoleStyles
    {
        public static WH40KCargoConsoleTheme ResolveTheme(ProtoId<CargoAccountPrototype> accountId)
        {
            if (accountId == "WH40KImperium")
            {
                return new WH40KCargoConsoleTheme(
                    true,
                    "Imperium",
                    Color.FromHex("#C9A94C".AsSpan()),
                    Color.FromHex("#2A2418".AsSpan()),
                    Color.FromHex("#3D3422".AsSpan()),
                    Color.FromHex("#0B0C10".AsSpan()),
                    Color.FromHex("#0C0D10".AsSpan()),
                    Color.FromHex("#0E0F14".AsSpan()),
                    Color.FromHex("#0C0D12".AsSpan()),
                    Color.FromHex("#1A1A24".AsSpan()),
                    Color.FromHex("#0E0F14".AsSpan()),
                    Color.FromHex("#101218".AsSpan()),
                    Color.FromHex("#0A0A0E".AsSpan()),
                    Color.FromHex("#D4C8A0".AsSpan()),
                    Color.FromHex("#A09880".AsSpan()),
                    Color.FromHex("#8A7D5E".AsSpan()),
                    Color.FromHex("#8B3030".AsSpan()),
                    "wh40k-cargo-console-footer-left-imperium",
                    "wh40k-cargo-console-order-menu-title-imperium");
            }

            if (accountId == "WH40KHeretics")
            {
                return new WH40KCargoConsoleTheme(
                    true,
                    "Heretics",
                    Color.FromHex("#C7483F".AsSpan()),
                    Color.FromHex("#311A1A".AsSpan()),
                    Color.FromHex("#512727".AsSpan()),
                    Color.FromHex("#0B0C10".AsSpan()),
                    Color.FromHex("#110D10".AsSpan()),
                    Color.FromHex("#150F14".AsSpan()),
                    Color.FromHex("#130E12".AsSpan()),
                    Color.FromHex("#24161C".AsSpan()),
                    Color.FromHex("#150F14".AsSpan()),
                    Color.FromHex("#141015".AsSpan()),
                    Color.FromHex("#0D0A0C".AsSpan()),
                    Color.FromHex("#E4D4D4".AsSpan()),
                    Color.FromHex("#B89A9A".AsSpan()),
                    Color.FromHex("#9B7474".AsSpan()),
                    Color.FromHex("#D06A6A".AsSpan()),
                    "wh40k-cargo-console-footer-left-heretics",
                    "wh40k-cargo-console-order-menu-title-heretics");
            }

            return WH40KCargoConsoleTheme.Disabled;
        }

        public static StyleBoxFlat CreatePanelStyle(Color background, Color border, int padding = 8, int borderThickness = 1)
        {
            return new StyleBoxFlat
            {
                BackgroundColor = background,
                BorderColor = border,
                BorderThickness = new Thickness(borderThickness),
                ContentMarginLeftOverride = padding,
                ContentMarginTopOverride = padding,
                ContentMarginRightOverride = padding,
                ContentMarginBottomOverride = padding,
            };
        }

        public static StyleBoxFlat CreateEdgePanelStyle(Color background, Color border, Thickness borderThickness, int padding = 8)
        {
            return new StyleBoxFlat
            {
                BackgroundColor = background,
                BorderColor = border,
                BorderThickness = borderThickness,
                ContentMarginLeftOverride = padding,
                ContentMarginTopOverride = padding,
                ContentMarginRightOverride = padding,
                ContentMarginBottomOverride = padding,
            };
        }

        public static StyleBoxFlat CreateBadgeStyle(Color background, Color border, int horizontalPadding = 10, int verticalPadding = 4)
        {
            return new StyleBoxFlat
            {
                BackgroundColor = background,
                BorderColor = border,
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = horizontalPadding,
                ContentMarginTopOverride = verticalPadding,
                ContentMarginRightOverride = horizontalPadding,
                ContentMarginBottomOverride = verticalPadding,
            };
        }

        public static StyleBoxFlat CreateTransparentButtonStyle()
        {
            return new StyleBoxFlat
            {
                BackgroundColor = Color.Transparent,
                BorderColor = Color.Transparent,
                BorderThickness = new Thickness(0),
                ContentMarginLeftOverride = 0,
                ContentMarginTopOverride = 0,
                ContentMarginRightOverride = 0,
                ContentMarginBottomOverride = 0,
            };
        }

        public static StyleBoxFlat CreatePrimaryButtonStyle(WH40KCargoConsoleTheme theme)
        {
            return CreateBadgeStyle(Blend(theme.SurfaceBackground, theme.Accent, 0.22f), theme.Accent.WithAlpha(0.82f), 12, 7);
        }

        public static StyleBoxFlat CreateSecondaryButtonStyle(WH40KCargoConsoleTheme theme)
        {
            return CreateBadgeStyle(Blend(theme.SurfaceBackground, theme.BorderStrongColor, 0.42f), theme.BorderStrongColor, 12, 7);
        }

        public static StyleBoxFlat CreateDangerButtonStyle(WH40KCargoConsoleTheme theme)
        {
            return CreateBadgeStyle(Blend(theme.SurfaceBackground, theme.DangerAccent, 0.2f), theme.DangerAccent.WithAlpha(0.82f), 12, 7);
        }

        public static StyleBoxFlat CreatePopupListButtonStyle(WH40KCargoConsoleTheme theme)
        {
            return CreateEdgePanelStyle(
                theme.PanelBackground,
                theme.BorderColor.WithAlpha(0.88f),
                new Thickness(0f, 0f, 0f, 1f),
                12);
        }

        public static StyleBoxFlat CreatePopupListSelectedButtonStyle(WH40KCargoConsoleTheme theme)
        {
            return CreateEdgePanelStyle(
                Blend(theme.PanelBackground, theme.Accent, 0.18f),
                theme.Accent.WithAlpha(0.78f),
                new Thickness(0f, 0f, 0f, 1f),
                12);
        }

        public static StyleBoxFlat CreateInputStyle(WH40KCargoConsoleTheme theme)
        {
            return CreatePanelStyle(theme.InputBackground, theme.BorderColor, 8);
        }

        public static void ApplyButtonTheme(Button button, StyleBoxFlat style, Color fontColor)
        {
            button.StyleBoxOverride = style;
            button.Label.FontColorOverride = fontColor;
        }

        private static Color ButtonBackground(this WH40KCargoConsoleTheme theme)
        {
            return theme.SurfaceBackground.WithAlpha(0.96f);
        }

        private static Color Blend(Color baseColor, Color accent, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return new Color(
                baseColor.R + (accent.R - baseColor.R) * amount,
                baseColor.G + (accent.G - baseColor.G) * amount,
                baseColor.B + (accent.B - baseColor.B) * amount,
                MathF.Max(baseColor.A, accent.A));
        }
    }
}
