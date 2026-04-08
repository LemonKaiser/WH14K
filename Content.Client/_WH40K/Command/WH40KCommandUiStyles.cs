using System;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Command;

public static class WH40KCommandUiStyles
{
    public static readonly Color DefaultAccent = Color.FromHex("#5F8FB8".AsSpan());
    public static readonly Color HeaderBackground = Color.FromHex("#0D1520".AsSpan());
    public static readonly Color PanelBackground = Color.FromHex("#111924".AsSpan());
    public static readonly Color PanelBackgroundAlt = Color.FromHex("#0D141D".AsSpan());
    public static readonly Color HeaderStripBackground = Color.FromHex("#162231".AsSpan());
    public static readonly Color CardBackground = Color.FromHex("#121C28".AsSpan());
    public static readonly Color CardBackgroundAlt = Color.FromHex("#0F1822".AsSpan());
    public static readonly Color CardBackgroundMuted = Color.FromHex("#101821".AsSpan());
    public static readonly Color FooterBackground = Color.FromHex("#0E141C".AsSpan());
    public static readonly Color MutedBorder = Color.FromHex("#324459".AsSpan());
    public static readonly Color StrongBorder = Color.FromHex("#3A4E66".AsSpan());
    public static readonly Color ReadyBadge = Color.FromHex("#5FA27E".AsSpan());
    public static readonly Color WarningBadge = Color.FromHex("#D5A356".AsSpan());
    public static readonly Color DangerBadge = Color.FromHex("#C97070".AsSpan());
    public static readonly Color InfoBadge = Color.FromHex("#7FA0D8".AsSpan());
    public static readonly Color MutedText = Color.FromHex("#8FA1B6".AsSpan());
    public static readonly Color SoftText = Color.FromHex("#BFCBDA".AsSpan());

    public static StyleBoxFlat CreateBorderPanelStyle(Color background, Color border, int thickness)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(thickness),
            ContentMarginLeftOverride = thickness == 1 ? 6 : 0,
            ContentMarginTopOverride = thickness == 1 ? 6 : 0,
            ContentMarginRightOverride = thickness == 1 ? 6 : 0,
            ContentMarginBottomOverride = thickness == 1 ? 6 : 0,
        };
    }

    public static StyleBoxFlat CreateCardStyle(Color background, Color border)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 8,
            ContentMarginTopOverride = 8,
            ContentMarginRightOverride = 8,
            ContentMarginBottomOverride = 8,
        };
    }

    public static StyleBoxFlat CreateHeaderStripStyle(Color border)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = HeaderStripBackground,
            BorderColor = border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            ContentMarginLeftOverride = 10,
            ContentMarginTopOverride = 8,
            ContentMarginRightOverride = 10,
            ContentMarginBottomOverride = 8,
        };
    }

    public static StyleBoxFlat CreateBadgeStyle(Color background, Color border)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 8,
            ContentMarginTopOverride = 3,
            ContentMarginRightOverride = 8,
            ContentMarginBottomOverride = 3,
        };
    }

    public static StyleBoxFlat CreateProgressBackgroundStyle()
    {
        return new StyleBoxFlat
        {
            BackgroundColor = CardBackgroundMuted
        };
    }

    public static StyleBoxFlat CreateProgressForegroundStyle(Color accent)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = accent
        };
    }

    public static void SetWrappedText(RichTextLabel label, string text, Color? color = null)
    {
        var normalized = string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Replace("\\n", "\n", StringComparison.Ordinal);

        label.SetMessage(
            FormattedMessage.FromMarkupPermissive(FormattedMessage.EscapeText(normalized)),
            tagsAllowed: null,
            defaultColor: color ?? Color.White);
    }

    public static string ResolveLocalizedOrRaw(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (IoCManager.Resolve<ILocalizationManager>().TryGetString(value, out var localized) && !string.IsNullOrWhiteSpace(localized))
            return localized!;

        return value;
    }
}
