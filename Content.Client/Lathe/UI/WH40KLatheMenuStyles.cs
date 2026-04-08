using System;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.Lathe.UI;

public readonly record struct WH40KLatheMenuTheme(
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
    Color InputBackground,
    Color PrimaryText,
    Color SecondaryText,
    Color LabelText,
    Color SuccessAccent,
    Color DangerAccent,
    string ProfileLocKey,
    string FooterLocKey)
{
    public static readonly WH40KLatheMenuTheme Disabled = new(
        false,
        string.Empty,
        Color.FromHex("#8FB6C7".AsSpan()),
        Color.FromHex("#2F3842".AsSpan()),
        Color.FromHex("#45535E".AsSpan()),
        Color.FromHex("#10141A".AsSpan()),
        Color.FromHex("#141A22".AsSpan()),
        Color.FromHex("#18202A".AsSpan()),
        Color.FromHex("#111820".AsSpan()),
        Color.FromHex("#1C2731".AsSpan()),
        Color.FromHex("#151B23".AsSpan()),
        Color.FromHex("#0E1319".AsSpan()),
        Color.FromHex("#121821".AsSpan()),
        Color.FromHex("#D9E3E8".AsSpan()),
        Color.FromHex("#A1B0BA".AsSpan()),
        Color.FromHex("#7E919E".AsSpan()),
        Color.FromHex("#88C49A".AsSpan()),
        Color.FromHex("#C46F6F".AsSpan()),
        "lathe-menu-profile-general",
        "lathe-menu-footer");
}

public static class WH40KLatheMenuStyles
{
    public static WH40KLatheMenuTheme ResolveTheme(string? prototypeId)
    {
        var id = prototypeId ?? string.Empty;
        var profileLocKey = ResolveProfileLocKey(id);

        if (id.Contains("Imperium", StringComparison.OrdinalIgnoreCase))
        {
            return new WH40KLatheMenuTheme(
                true,
                "Imperium",
                Color.FromHex("#C9A94C".AsSpan()),
                Color.FromHex("#2A2418".AsSpan()),
                Color.FromHex("#3D3422".AsSpan()),
                Color.FromHex("#0B0C10".AsSpan()),
                Color.FromHex("#0D0E12".AsSpan()),
                Color.FromHex("#11141A".AsSpan()),
                Color.FromHex("#0E1117".AsSpan()),
                Color.FromHex("#1C1E27".AsSpan()),
                Color.FromHex("#0F1014".AsSpan()),
                Color.FromHex("#0A0A0E".AsSpan()),
                Color.FromHex("#12151B".AsSpan()),
                Color.FromHex("#E6DEC7".AsSpan()),
                Color.FromHex("#B4A88A".AsSpan()),
                Color.FromHex("#8D7E5A".AsSpan()),
                Color.FromHex("#86C095".AsSpan()),
                Color.FromHex("#A94D4D".AsSpan()),
                profileLocKey,
                "lathe-menu-footer-imperium");
        }

        if (id.Contains("Heretics", StringComparison.OrdinalIgnoreCase))
        {
            return new WH40KLatheMenuTheme(
                true,
                "Heretics",
                Color.FromHex("#C7483F".AsSpan()),
                Color.FromHex("#311A1A".AsSpan()),
                Color.FromHex("#512727".AsSpan()),
                Color.FromHex("#0B0C10".AsSpan()),
                Color.FromHex("#110D10".AsSpan()),
                Color.FromHex("#160F14".AsSpan()),
                Color.FromHex("#130E12".AsSpan()),
                Color.FromHex("#25161C".AsSpan()),
                Color.FromHex("#150F14".AsSpan()),
                Color.FromHex("#0D0A0C".AsSpan()),
                Color.FromHex("#131016".AsSpan()),
                Color.FromHex("#E7D9D9".AsSpan()),
                Color.FromHex("#BEA0A0".AsSpan()),
                Color.FromHex("#9B7474".AsSpan()),
                Color.FromHex("#92C27D".AsSpan()),
                Color.FromHex("#D06A6A".AsSpan()),
                profileLocKey,
                "lathe-menu-footer-heretics");
        }

        if (id.Contains("CircuitImprinter", StringComparison.OrdinalIgnoreCase))
        {
            return new WH40KLatheMenuTheme(
                true,
                string.Empty,
                Color.FromHex("#69B7BE".AsSpan()),
                Color.FromHex("#24363A".AsSpan()),
                Color.FromHex("#335157".AsSpan()),
                Color.FromHex("#10141A".AsSpan()),
                Color.FromHex("#131B1F".AsSpan()),
                Color.FromHex("#182328".AsSpan()),
                Color.FromHex("#10171B".AsSpan()),
                Color.FromHex("#183038".AsSpan()),
                Color.FromHex("#11181C".AsSpan()),
                Color.FromHex("#0D1418".AsSpan()),
                Color.FromHex("#11191F".AsSpan()),
                Color.FromHex("#D7E6E7".AsSpan()),
                Color.FromHex("#9EB1B4".AsSpan()),
                Color.FromHex("#7A9095".AsSpan()),
                Color.FromHex("#82C6A4".AsSpan()),
                Color.FromHex("#C46F6F".AsSpan()),
                profileLocKey,
                "lathe-menu-footer");
        }

        if (id.Contains("Security", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Ammo", StringComparison.OrdinalIgnoreCase))
        {
            return new WH40KLatheMenuTheme(
                true,
                string.Empty,
                Color.FromHex("#C36E5A".AsSpan()),
                Color.FromHex("#3A2722".AsSpan()),
                Color.FromHex("#53372E".AsSpan()),
                Color.FromHex("#10141A".AsSpan()),
                Color.FromHex("#1A1518".AsSpan()),
                Color.FromHex("#211A1D".AsSpan()),
                Color.FromHex("#161214".AsSpan()),
                Color.FromHex("#312224".AsSpan()),
                Color.FromHex("#171315".AsSpan()),
                Color.FromHex("#100D0F".AsSpan()),
                Color.FromHex("#161315".AsSpan()),
                Color.FromHex("#E6DEDA".AsSpan()),
                Color.FromHex("#B5A49E".AsSpan()),
                Color.FromHex("#907D76".AsSpan()),
                Color.FromHex("#93C08D".AsSpan()),
                Color.FromHex("#CD6B6B".AsSpan()),
                profileLocKey,
                "lathe-menu-footer");
        }

        if (id.Contains("Bio", StringComparison.OrdinalIgnoreCase))
        {
            return new WH40KLatheMenuTheme(
                true,
                string.Empty,
                Color.FromHex("#84BC7F".AsSpan()),
                Color.FromHex("#223325".AsSpan()),
                Color.FromHex("#35503A".AsSpan()),
                Color.FromHex("#10141A".AsSpan()),
                Color.FromHex("#141B18".AsSpan()),
                Color.FromHex("#19231E".AsSpan()),
                Color.FromHex("#111A15".AsSpan()),
                Color.FromHex("#213126".AsSpan()),
                Color.FromHex("#121A16".AsSpan()),
                Color.FromHex("#0D1410".AsSpan()),
                Color.FromHex("#111A16".AsSpan()),
                Color.FromHex("#DCE8DA".AsSpan()),
                Color.FromHex("#A7B7A3".AsSpan()),
                Color.FromHex("#829680".AsSpan()),
                Color.FromHex("#A0D18D".AsSpan()),
                Color.FromHex("#C97878".AsSpan()),
                profileLocKey,
                "lathe-menu-footer");
        }

        if (id.Contains("Exosuit", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Mech", StringComparison.OrdinalIgnoreCase))
        {
            return new WH40KLatheMenuTheme(
                true,
                string.Empty,
                Color.FromHex("#C38A4F".AsSpan()),
                Color.FromHex("#392C1F".AsSpan()),
                Color.FromHex("#54402C".AsSpan()),
                Color.FromHex("#10141A".AsSpan()),
                Color.FromHex("#1A1714".AsSpan()),
                Color.FromHex("#231D18".AsSpan()),
                Color.FromHex("#181411".AsSpan()),
                Color.FromHex("#30261B".AsSpan()),
                Color.FromHex("#171310".AsSpan()),
                Color.FromHex("#110D0A".AsSpan()),
                Color.FromHex("#171411".AsSpan()),
                Color.FromHex("#E8E0D9".AsSpan()),
                Color.FromHex("#B6A99D".AsSpan()),
                Color.FromHex("#957F68".AsSpan()),
                Color.FromHex("#8DC7A5".AsSpan()),
                Color.FromHex("#C97878".AsSpan()),
                profileLocKey,
                "lathe-menu-footer");
        }

        if (id.Contains("Autolathe", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("OreProcessor", StringComparison.OrdinalIgnoreCase))
        {
            return new WH40KLatheMenuTheme(
                true,
                string.Empty,
                Color.FromHex("#C3A25A".AsSpan()),
                Color.FromHex("#342D21".AsSpan()),
                Color.FromHex("#4E4330".AsSpan()),
                Color.FromHex("#10141A".AsSpan()),
                Color.FromHex("#181814".AsSpan()),
                Color.FromHex("#201E18".AsSpan()),
                Color.FromHex("#14140F".AsSpan()),
                Color.FromHex("#2A261C".AsSpan()),
                Color.FromHex("#15140F".AsSpan()),
                Color.FromHex("#100E0A".AsSpan()),
                Color.FromHex("#16150F".AsSpan()),
                Color.FromHex("#E9E2D5".AsSpan()),
                Color.FromHex("#B5AA95".AsSpan()),
                Color.FromHex("#8F856D".AsSpan()),
                Color.FromHex("#8EC7A1".AsSpan()),
                Color.FromHex("#C97676".AsSpan()),
                profileLocKey,
                "lathe-menu-footer");
        }

        if (id.Contains("Protolathe", StringComparison.OrdinalIgnoreCase))
        {
            return new WH40KLatheMenuTheme(
                true,
                string.Empty,
                Color.FromHex("#6EA9C8".AsSpan()),
                Color.FromHex("#21303A".AsSpan()),
                Color.FromHex("#334A59".AsSpan()),
                Color.FromHex("#10141A".AsSpan()),
                Color.FromHex("#141920".AsSpan()),
                Color.FromHex("#1A212A".AsSpan()),
                Color.FromHex("#12171D".AsSpan()),
                Color.FromHex("#1F2E39".AsSpan()),
                Color.FromHex("#13181E".AsSpan()),
                Color.FromHex("#0E1318".AsSpan()),
                Color.FromHex("#12171E".AsSpan()),
                Color.FromHex("#DCE6EB".AsSpan()),
                Color.FromHex("#A7B4BD".AsSpan()),
                Color.FromHex("#80909B".AsSpan()),
                Color.FromHex("#91C7A7".AsSpan()),
                Color.FromHex("#C66F6F".AsSpan()),
                profileLocKey,
                "lathe-menu-footer");
        }

        return WH40KLatheMenuTheme.Disabled with
        {
            ProfileLocKey = profileLocKey,
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

    public static StyleBoxFlat CreatePrimaryButtonStyle(WH40KLatheMenuTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.Accent.WithAlpha(0.82f), 12, 7);
    }

    public static StyleBoxFlat CreateSecondaryButtonStyle(WH40KLatheMenuTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.BorderStrongColor, 12, 7);
    }

    public static StyleBoxFlat CreateDangerButtonStyle(WH40KLatheMenuTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.DangerAccent.WithAlpha(0.82f), 12, 7);
    }

    public static StyleBoxFlat CreateInputStyle(WH40KLatheMenuTheme theme)
    {
        return CreatePanelStyle(theme.InputBackground, theme.BorderColor, 8);
    }

    public static StyleBoxFlat CreateProgressBackgroundStyle(WH40KLatheMenuTheme theme)
    {
        return CreateBadgeStyle(theme.InputBackground, theme.BorderColor, 6, 5);
    }

    public static StyleBoxFlat CreateProgressForegroundStyle(WH40KLatheMenuTheme theme)
    {
        return CreateBadgeStyle(theme.Accent.WithAlpha(0.68f), theme.Accent.WithAlpha(0.88f), 6, 5);
    }

    public static void ApplyButtonTheme(Button button, StyleBoxFlat style, Color fontColor)
    {
        button.StyleBoxOverride = style;
        button.Label.FontColorOverride = fontColor;
    }

    private static string ResolveProfileLocKey(string prototypeId)
    {
        if (prototypeId.Contains("OreProcessor", StringComparison.OrdinalIgnoreCase))
            return "lathe-menu-profile-ore";

        if (prototypeId.Contains("CircuitImprinter", StringComparison.OrdinalIgnoreCase))
            return "lathe-menu-profile-circuit";

        if (prototypeId.Contains("Protolathe", StringComparison.OrdinalIgnoreCase))
            return "lathe-menu-profile-research";

        if (prototypeId.Contains("Autolathe", StringComparison.OrdinalIgnoreCase))
            return "lathe-menu-profile-industrial";

        if (prototypeId.Contains("Security", StringComparison.OrdinalIgnoreCase) ||
            prototypeId.Contains("Ammo", StringComparison.OrdinalIgnoreCase))
            return "lathe-menu-profile-armory";

        if (prototypeId.Contains("Bio", StringComparison.OrdinalIgnoreCase))
            return "lathe-menu-profile-biotic";

        if (prototypeId.Contains("Exosuit", StringComparison.OrdinalIgnoreCase) ||
            prototypeId.Contains("Mech", StringComparison.OrdinalIgnoreCase))
            return "lathe-menu-profile-robotics";

        return "lathe-menu-profile-general";
    }
}
