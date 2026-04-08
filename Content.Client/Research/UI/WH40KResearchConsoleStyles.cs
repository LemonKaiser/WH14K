using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.Research.UI;

public readonly record struct WH40KResearchConsoleTheme(
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
    Color PreviewBackground,
    Color PrimaryText,
    Color SecondaryText,
    Color LabelText,
    Color DangerAccent,
    string WindowTitleLocKey,
    string SubtitleLocKey,
    string FooterLocKey)
{
    public static readonly WH40KResearchConsoleTheme Disabled = new(
        false,
        string.Empty,
        Color.FromHex("#8EC5B6".AsSpan()),
        Color.FromHex("#2D343A".AsSpan()),
        Color.FromHex("#435059".AsSpan()),
        Color.FromHex("#10141A".AsSpan()),
        Color.FromHex("#141920".AsSpan()),
        Color.FromHex("#181F28".AsSpan()),
        Color.FromHex("#11161D".AsSpan()),
        Color.FromHex("#1C2530".AsSpan()),
        Color.FromHex("#161C24".AsSpan()),
        Color.FromHex("#10151A".AsSpan()),
        Color.FromHex("#D7E4E6".AsSpan()),
        Color.FromHex("#9CAAB4".AsSpan()),
        Color.FromHex("#7E8C97".AsSpan()),
        Color.FromHex("#C46F6F".AsSpan()),
        "research-console-menu-title",
        "research-console-header-subtitle",
        "research-console-footer");
}

public static class WH40KResearchConsoleStyles
{
    public static WH40KResearchConsoleTheme ResolveTheme(string? prototypeId)
    {
        return prototypeId switch
        {
            "WH40KComputerResearchAndDevelopmentImperium" => new WH40KResearchConsoleTheme(
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
                Color.FromHex("#0A0A0E".AsSpan()),
                Color.FromHex("#D4C8A0".AsSpan()),
                Color.FromHex("#A09880".AsSpan()),
                Color.FromHex("#8A7D5E".AsSpan()),
                Color.FromHex("#8B3030".AsSpan()),
                "wh40k-research-console-title-imperium",
                "wh40k-research-console-subtitle-imperium",
                "wh40k-research-console-footer-imperium"),

            "WH40KComputerResearchAndDevelopmentHeretics" => new WH40KResearchConsoleTheme(
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
                Color.FromHex("#0D0A0C".AsSpan()),
                Color.FromHex("#E4D4D4".AsSpan()),
                Color.FromHex("#B89A9A".AsSpan()),
                Color.FromHex("#9B7474".AsSpan()),
                Color.FromHex("#D06A6A".AsSpan()),
                "wh40k-research-console-title-heretics",
                "wh40k-research-console-subtitle-heretics",
                "wh40k-research-console-footer-heretics"),

            _ => WH40KResearchConsoleTheme.Disabled,
        };
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

    public static StyleBoxFlat CreatePrimaryButtonStyle(WH40KResearchConsoleTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.Accent.WithAlpha(0.82f), 12, 7);
    }

    public static StyleBoxFlat CreateSecondaryButtonStyle(WH40KResearchConsoleTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.BorderStrongColor, 12, 7);
    }

    public static StyleBoxFlat CreateDangerButtonStyle(WH40KResearchConsoleTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.DangerAccent.WithAlpha(0.82f), 12, 7);
    }

    public static void ApplyButtonTheme(Button button, StyleBoxFlat style, Color fontColor)
    {
        button.StyleBoxOverride = style;
        button.Label.FontColorOverride = fontColor;
    }
}
