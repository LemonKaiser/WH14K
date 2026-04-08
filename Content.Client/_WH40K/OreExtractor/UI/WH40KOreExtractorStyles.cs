using System;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.OreExtractor.UI;

public readonly record struct WH40KOreExtractorTheme(
    bool Enabled,
    string TeamId,
    Color Accent,
    Color BorderColor,
    Color BorderStrongColor,
    Color WindowBackground,
    Color PanelBackground,
    Color PanelAltBackground,
    Color SurfaceBackground,
    Color HeaderBackground,
    Color PrimaryText,
    Color SecondaryText,
    Color LabelText,
    Color SuccessAccent,
    Color WarningAccent,
    Color DangerAccent,
    string WindowTitleLocKey,
    string SubtitleLocKey,
    string FooterLocKey)
{
    public static readonly WH40KOreExtractorTheme Disabled = new(
        false,
        string.Empty,
        Color.FromHex("#8FB6C7".AsSpan()),
        Color.FromHex("#2F3842".AsSpan()),
        Color.FromHex("#45535E".AsSpan()),
        Color.FromHex("#10141A".AsSpan()),
        Color.FromHex("#141A22".AsSpan()),
        Color.FromHex("#18202A".AsSpan()),
        Color.FromHex("#121A22".AsSpan()),
        Color.FromHex("#0E141A".AsSpan()),
        Color.FromHex("#D9E3E8".AsSpan()),
        Color.FromHex("#A1B0BA".AsSpan()),
        Color.FromHex("#7E919E".AsSpan()),
        Color.FromHex("#88C49A".AsSpan()),
        Color.FromHex("#D0A66A".AsSpan()),
        Color.FromHex("#C46F6F".AsSpan()),
        "wh40k-ore-extractor-ui-window-title",
        "wh40k-ore-extractor-ui-subtitle",
        "wh40k-ore-extractor-ui-footer");
}

public static class WH40KOreExtractorStyles
{
    public static WH40KOreExtractorTheme ResolveTheme(string? teamId)
    {
        if (string.Equals(teamId, "Imperium", StringComparison.OrdinalIgnoreCase))
        {
            return new WH40KOreExtractorTheme(
                true,
                "Imperium",
                Color.FromHex("#C9A94C".AsSpan()),
                Color.FromHex("#2A2418".AsSpan()),
                Color.FromHex("#3D3422".AsSpan()),
                Color.FromHex("#0A0B0F".AsSpan()),
                Color.FromHex("#0D0E12".AsSpan()),
                Color.FromHex("#12151B".AsSpan()),
                Color.FromHex("#11151C".AsSpan()),
                Color.FromHex("#0A0A0E".AsSpan()),
                Color.FromHex("#E6DEC7".AsSpan()),
                Color.FromHex("#B4A88A".AsSpan()),
                Color.FromHex("#8D7E5A".AsSpan()),
                Color.FromHex("#86C095".AsSpan()),
                Color.FromHex("#D3A456".AsSpan()),
                Color.FromHex("#B85A5A".AsSpan()),
                "wh40k-ore-extractor-ui-window-title-imperium",
                "wh40k-ore-extractor-ui-subtitle-imperium",
                "wh40k-ore-extractor-ui-footer-imperium");
        }

        if (string.Equals(teamId, "Heretics", StringComparison.OrdinalIgnoreCase))
        {
            return new WH40KOreExtractorTheme(
                true,
                "Heretics",
                Color.FromHex("#C7483F".AsSpan()),
                Color.FromHex("#311A1A".AsSpan()),
                Color.FromHex("#512727".AsSpan()),
                Color.FromHex("#0A0A0F".AsSpan()),
                Color.FromHex("#110D10".AsSpan()),
                Color.FromHex("#171116".AsSpan()),
                Color.FromHex("#150F14".AsSpan()),
                Color.FromHex("#0D0A0C".AsSpan()),
                Color.FromHex("#E7D9D9".AsSpan()),
                Color.FromHex("#BEA0A0".AsSpan()),
                Color.FromHex("#9B7474".AsSpan()),
                Color.FromHex("#92C27D".AsSpan()),
                Color.FromHex("#D79A5C".AsSpan()),
                Color.FromHex("#D06A6A".AsSpan()),
                "wh40k-ore-extractor-ui-window-title-heretics",
                "wh40k-ore-extractor-ui-subtitle-heretics",
                "wh40k-ore-extractor-ui-footer-heretics");
        }

        return WH40KOreExtractorTheme.Disabled;
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

    public static StyleBoxFlat CreateBadgeStyle(Color background, Color border, int horizontalPadding = 10, int verticalPadding = 5)
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

    public static StyleBoxFlat CreatePrimaryButtonStyle(WH40KOreExtractorTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.Accent.WithAlpha(0.82f), 12, 7);
    }

    public static StyleBoxFlat CreateSecondaryButtonStyle(WH40KOreExtractorTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.BorderStrongColor, 12, 7);
    }

    public static StyleBoxFlat CreateDangerButtonStyle(WH40KOreExtractorTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.DangerAccent.WithAlpha(0.82f), 12, 7);
    }

    public static StyleBoxFlat CreateLockedButtonStyle(WH40KOreExtractorTheme theme)
    {
        return CreateBadgeStyle(
            theme.SurfaceBackground.WithAlpha(0.82f),
            theme.BorderColor.WithAlpha(0.72f),
            12,
            7);
    }

    public static void ApplyButtonTheme(Button button, StyleBoxFlat style, Color fontColor)
    {
        button.StyleBoxOverride = style;
        button.Label.FontColorOverride = fontColor;
    }
}
