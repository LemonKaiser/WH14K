using System;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.SurveillanceCamera.UI;

public readonly record struct SurveillanceCameraMonitorTheme(
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
    Color FeedBackground,
    Color PrimaryText,
    Color SecondaryText,
    Color LabelText,
    Color SuccessAccent,
    Color WarningAccent,
    Color DangerAccent)
{
    public static readonly SurveillanceCameraMonitorTheme Default = new(
        "Noosphere",
        Color.FromHex("#D3AE72".AsSpan()),
        Color.FromHex("#26374A".AsSpan()),
        Color.FromHex("#354B63".AsSpan()),
        Color.FromHex("#080B10".AsSpan()),
        Color.FromHex("#0C0D10".AsSpan()),
        Color.FromHex("#0F1014".AsSpan()),
        Color.FromHex("#11151D".AsSpan()),
        Color.FromHex("#0E0F14".AsSpan()),
        Color.FromHex("#101218".AsSpan()),
        Color.FromHex("#05070A".AsSpan()),
        Color.FromHex("#E0D6BE".AsSpan()),
        Color.FromHex("#A5B0BC".AsSpan()),
        Color.FromHex("#7E8FA1".AsSpan()),
        Color.FromHex("#6EB19A".AsSpan()),
        Color.FromHex("#D0A66A".AsSpan()),
        Color.FromHex("#B85757".AsSpan()));
}

public static class SurveillanceCameraMonitorStyles
{
    public static SurveillanceCameraMonitorTheme ResolveTheme(string? prototypeId)
    {
        var id = prototypeId ?? string.Empty;

        if (id.Contains("Imperium", StringComparison.OrdinalIgnoreCase))
        {
            return new SurveillanceCameraMonitorTheme(
                "Imperium",
                Color.FromHex("#C9A94C".AsSpan()),
                Color.FromHex("#2A2418".AsSpan()),
                Color.FromHex("#3D3422".AsSpan()),
                Color.FromHex("#08090C".AsSpan()),
                Color.FromHex("#0B0C10".AsSpan()),
                Color.FromHex("#0F1014".AsSpan()),
                Color.FromHex("#12151B".AsSpan()),
                Color.FromHex("#0D0E12".AsSpan()),
                Color.FromHex("#101218".AsSpan()),
                Color.FromHex("#050608".AsSpan()),
                Color.FromHex("#E6DEC7".AsSpan()),
                Color.FromHex("#B4A88A".AsSpan()),
                Color.FromHex("#8D7E5A".AsSpan()),
                Color.FromHex("#86C095".AsSpan()),
                Color.FromHex("#D3A456".AsSpan()),
                Color.FromHex("#B85A5A".AsSpan()));
        }

        if (id.Contains("Heretics", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Syndicate", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Bug", StringComparison.OrdinalIgnoreCase))
        {
            return new SurveillanceCameraMonitorTheme(
                "Hostile",
                Color.FromHex("#C7483F".AsSpan()),
                Color.FromHex("#311A1A".AsSpan()),
                Color.FromHex("#512727".AsSpan()),
                Color.FromHex("#09090D".AsSpan()),
                Color.FromHex("#110D10".AsSpan()),
                Color.FromHex("#171116".AsSpan()),
                Color.FromHex("#150F14".AsSpan()),
                Color.FromHex("#131016".AsSpan()),
                Color.FromHex("#0E0C10".AsSpan()),
                Color.FromHex("#040305".AsSpan()),
                Color.FromHex("#E7D9D9".AsSpan()),
                Color.FromHex("#BEA0A0".AsSpan()),
                Color.FromHex("#9B7474".AsSpan()),
                Color.FromHex("#92C27D".AsSpan()),
                Color.FromHex("#D79A5C".AsSpan()),
                Color.FromHex("#D06A6A".AsSpan()));
        }

        if (id.Contains("Xeno", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Mothership", StringComparison.OrdinalIgnoreCase))
        {
            return new SurveillanceCameraMonitorTheme(
                "Xeno",
                Color.FromHex("#93C26E".AsSpan()),
                Color.FromHex("#213325".AsSpan()),
                Color.FromHex("#35503B".AsSpan()),
                Color.FromHex("#080B0A".AsSpan()),
                Color.FromHex("#0C110E".AsSpan()),
                Color.FromHex("#101712".AsSpan()),
                Color.FromHex("#121A15".AsSpan()),
                Color.FromHex("#0D120F".AsSpan()),
                Color.FromHex("#101510".AsSpan()),
                Color.FromHex("#040705".AsSpan()),
                Color.FromHex("#D8E6CF".AsSpan()),
                Color.FromHex("#A3B694".AsSpan()),
                Color.FromHex("#7E9776".AsSpan()),
                Color.FromHex("#7BCB90".AsSpan()),
                Color.FromHex("#D0B36A".AsSpan()),
                Color.FromHex("#C96A6A".AsSpan()));
        }

        return SurveillanceCameraMonitorTheme.Default;
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

    public static StyleBoxFlat CreateInputStyle(SurveillanceCameraMonitorTheme theme)
    {
        return CreatePanelStyle(theme.InputBackground, theme.BorderColor, 8);
    }

    public static StyleBoxFlat CreatePrimaryButtonStyle(SurveillanceCameraMonitorTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.Accent.WithAlpha(0.82f), 12, 7);
    }

    public static StyleBoxFlat CreateSecondaryButtonStyle(SurveillanceCameraMonitorTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.BorderStrongColor, 12, 7);
    }

    public static StyleBoxFlat CreateDangerButtonStyle(SurveillanceCameraMonitorTheme theme)
    {
        return CreateBadgeStyle(theme.SurfaceBackground.WithAlpha(0.96f), theme.DangerAccent.WithAlpha(0.82f), 12, 7);
    }

    public static void ApplyButtonTheme(Button button, StyleBoxFlat style, Color fontColor)
    {
        button.StyleBoxOverride = style;
        button.Label.FontColorOverride = fontColor;
    }

    public static void ApplyOptionButtonTheme(ThemedOptionButton button, SurveillanceCameraMonitorTheme theme)
    {
        button.StyleBoxOverride = CreateSecondaryButtonStyle(theme);
        button.PopupButtonStyleOverride = CreateSecondaryButtonStyle(theme);
        button.PopupSelectedButtonStyleOverride = CreatePrimaryButtonStyle(theme);
        button.PopupButtonFontColorOverride = theme.PrimaryText;
        button.PopupSelectedButtonFontColorOverride = theme.PrimaryText;
        button.RefreshPopupItemTheme();
    }
}
