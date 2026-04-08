using System;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.VendingMachines.UI;

public readonly record struct VendingMachineMenuTheme(
    bool Enabled,
    string ThemeId,
    Color Accent,
    Color BorderColor,
    Color BorderStrongColor,
    Color WindowBackground,
    Color PanelBackground,
    Color PanelAltBackground,
    Color SurfaceBackground,
    Color HeaderBackground,
    Color InputBackground,
    Color PrimaryText,
    Color SecondaryText,
    Color LabelText,
    Color SuccessAccent,
    Color WarningAccent,
    Color DangerAccent)
{
    public static readonly VendingMachineMenuTheme Default = new(
        false,
        "Default",
        Color.FromHex("#8FB6C7".AsSpan()),
        Color.FromHex("#2F3842".AsSpan()),
        Color.FromHex("#45535E".AsSpan()),
        Color.FromHex("#10141A".AsSpan()),
        Color.FromHex("#141A22".AsSpan()),
        Color.FromHex("#18202A".AsSpan()),
        Color.FromHex("#121A22".AsSpan()),
        Color.FromHex("#0E141A".AsSpan()),
        Color.FromHex("#111922".AsSpan()),
        Color.FromHex("#D9E3E8".AsSpan()),
        Color.FromHex("#A1B0BA".AsSpan()),
        Color.FromHex("#7E919E".AsSpan()),
        Color.FromHex("#88C49A".AsSpan()),
        Color.FromHex("#D0A66A".AsSpan()),
        Color.FromHex("#C46F6F".AsSpan()));
}

public static class VendingMachineMenuStyles
{
    public static VendingMachineMenuTheme ResolveTheme(string? prototypeId)
    {
        var id = prototypeId ?? string.Empty;

        if (id.Contains("Imperium", StringComparison.OrdinalIgnoreCase))
        {
            return new VendingMachineMenuTheme(
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
                Color.FromHex("#11151B".AsSpan()),
                Color.FromHex("#E6DEC7".AsSpan()),
                Color.FromHex("#B4A88A".AsSpan()),
                Color.FromHex("#8D7E5A".AsSpan()),
                Color.FromHex("#86C095".AsSpan()),
                Color.FromHex("#D3A456".AsSpan()),
                Color.FromHex("#B85A5A".AsSpan()));
        }

        if (id.Contains("Heretics", StringComparison.OrdinalIgnoreCase))
        {
            return new VendingMachineMenuTheme(
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
                Color.FromHex("#131016".AsSpan()),
                Color.FromHex("#E7D9D9".AsSpan()),
                Color.FromHex("#BEA0A0".AsSpan()),
                Color.FromHex("#9B7474".AsSpan()),
                Color.FromHex("#92C27D".AsSpan()),
                Color.FromHex("#D79A5C".AsSpan()),
                Color.FromHex("#D06A6A".AsSpan()));
        }

        if (id.Contains("Med", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Chem", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Wallmed", StringComparison.OrdinalIgnoreCase))
        {
            return CreateTheme(
                "Medical",
                "#80BA82",
                "#213327",
                "#35503C",
                "#141B18",
                "#19231E");
        }

        if (id.Contains("Sec", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("PTech", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Ammo", StringComparison.OrdinalIgnoreCase))
        {
            return CreateTheme(
                "Security",
                "#C36E5A",
                "#3A2722",
                "#53372E",
                "#1A1518",
                "#211A1D");
        }

        if (id.Contains("Booze", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Coffee", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Cigs", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("HotDrinks", StringComparison.OrdinalIgnoreCase))
        {
            return CreateTheme(
                "Hospitality",
                "#B67A5B",
                "#38271F",
                "#4E372C",
                "#1A1512",
                "#211914");
        }

        if (id.Contains("Snack", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Soda", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Dinnerware", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Sustenance", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("FieldKitchen", StringComparison.OrdinalIgnoreCase))
        {
            return CreateTheme(
                "Supply",
                "#C3A25A",
                "#342D21",
                "#4E4330",
                "#181814",
                "#201E18");
        }

        if (id.Contains("Engi", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Tool", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Robotics", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Vendomat", StringComparison.OrdinalIgnoreCase))
        {
            return CreateTheme(
                "Engineering",
                "#D0B458",
                "#3B361F",
                "#544C2E",
                "#181812",
                "#211F18");
        }

        return VendingMachineMenuTheme.Default;
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

    public static StyleBoxFlat CreateInputStyle(VendingMachineMenuTheme theme)
    {
        return CreatePanelStyle(theme.InputBackground, theme.BorderColor, 8);
    }

    public static StyleBoxFlat CreatePrimaryButtonStyle(VendingMachineMenuTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.Accent.WithAlpha(0.84f), 12, 7);
    }

    public static StyleBoxFlat CreateSecondaryButtonStyle(VendingMachineMenuTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.BorderStrongColor, 12, 7);
    }

    public static StyleBoxFlat CreateRowStyle(Color background, Color border)
    {
        return CreatePanelStyle(background, border, 0);
    }

    public static void ApplyButtonTheme(Button button, StyleBoxFlat style, Color fontColor)
    {
        button.StyleBoxOverride = style;
        button.Label.FontColorOverride = fontColor;
    }

    private static VendingMachineMenuTheme CreateTheme(
        string id,
        string accent,
        string border,
        string strongBorder,
        string panel,
        string panelAlt)
    {
        return new VendingMachineMenuTheme(
            true,
            id,
            Color.FromHex(accent.AsSpan()),
            Color.FromHex(border.AsSpan()),
            Color.FromHex(strongBorder.AsSpan()),
            Color.FromHex("#10141A".AsSpan()),
            Color.FromHex(panel.AsSpan()),
            Color.FromHex(panelAlt.AsSpan()),
            Color.FromHex("#121A22".AsSpan()),
            Color.FromHex("#0E141A".AsSpan()),
            Color.FromHex("#111922".AsSpan()),
            Color.FromHex("#E2E7E9".AsSpan()),
            Color.FromHex("#ADB7BC".AsSpan()),
            Color.FromHex("#89979E".AsSpan()),
            Color.FromHex("#88C49A".AsSpan()),
            Color.FromHex("#D0A66A".AsSpan()),
            Color.FromHex("#C46F6F".AsSpan()));
    }
}
