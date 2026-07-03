using System;
using System.Globalization;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Command;

public static class WH40KCommandUiStyles
{
    public const string ThroneGeltSymbol = "\u20AE";
    public const string InfluenceSymbol = "\u2182";
    public const string ResearchSymbol = "\u03A9";
    public const string ExperienceSymbol = "XP";
    public const string ImperiumArtifactSymbol = "\u25A0";
    public const string ChaosArtifactSymbol = "\u25B2";

    public static readonly Color DefaultAccent = Color.FromHex("#C9A94C".AsSpan());
    public static readonly Color HeaderBackground = Color.FromHex("#0B0C10".AsSpan());
    public static readonly Color PanelBackground = Color.FromHex("#0C0D10".AsSpan());
    public static readonly Color PanelBackgroundAlt = Color.FromHex("#0E0F14".AsSpan());
    public static readonly Color HeaderStripBackground = Color.FromHex("#101218".AsSpan());
    public static readonly Color CardBackground = Color.FromHex("#0D0F13".AsSpan());
    public static readonly Color CardBackgroundAlt = Color.FromHex("#11131A".AsSpan());
    public static readonly Color CardBackgroundMuted = Color.FromHex("#141821".AsSpan());
    public static readonly Color FooterBackground = Color.FromHex("#0A0B0E".AsSpan());
    public static readonly Color BadgeBackground = Color.FromHex("#14110C".AsSpan());
    public static readonly Color MutedBorder = Color.FromHex("#4B3E25".AsSpan());
    public static readonly Color StrongBorder = Color.FromHex("#6A5530".AsSpan());
    public static readonly Color ReadyBadge = Color.FromHex("#5FA27E".AsSpan());
    public static readonly Color WarningBadge = Color.FromHex("#D5A356".AsSpan());
    public static readonly Color DangerBadge = Color.FromHex("#C97070".AsSpan());
    public static readonly Color InfoBadge = Color.FromHex("#B68E46".AsSpan());
    public static readonly Color MutedText = Color.FromHex("#9D8C64".AsSpan());
    public static readonly Color SoftText = Color.FromHex("#D4C8A0".AsSpan());
    public static readonly Color ButtonBackground = Color.FromHex("#12130F".AsSpan());
    public static readonly Color ButtonBackgroundAlt = Color.FromHex("#161813".AsSpan());
    public static readonly Color InputBackground = Color.FromHex("#0A0B0E".AsSpan());

    public static readonly Color ChaosHeaderBackground = Color.FromHex("#100B0E".AsSpan());
    public static readonly Color ChaosPanelBackground = Color.FromHex("#100B0E".AsSpan());
    public static readonly Color ChaosPanelBackgroundAlt = Color.FromHex("#120D10".AsSpan());
    public static readonly Color ChaosHeaderStripBackground = Color.FromHex("#140D11".AsSpan());
    public static readonly Color ChaosCardBackground = Color.FromHex("#100B0E".AsSpan());
    public static readonly Color ChaosCardBackgroundAlt = Color.FromHex("#120D10".AsSpan());
    public static readonly Color ChaosCardBackgroundMuted = Color.FromHex("#090608".AsSpan());
    public static readonly Color ChaosFooterBackground = Color.FromHex("#090608".AsSpan());
    public static readonly Color ChaosBadgeBackground = Color.FromHex("#170C0E".AsSpan());
    public static readonly Color ChaosMutedBorder = Color.FromHex("#351416".AsSpan());
    public static readonly Color ChaosStrongBorder = Color.FromHex("#672126".AsSpan());
    public static readonly Color ChaosMutedText = Color.FromHex("#A98A8D".AsSpan());
    public static readonly Color ChaosSoftText = Color.FromHex("#D8C1C1".AsSpan());
    public static readonly Color ChaosButtonBackground = Color.FromHex("#120A0E".AsSpan());
    public static readonly Color ChaosButtonBackgroundAlt = Color.FromHex("#160D11".AsSpan());
    public static readonly Color ChaosInputBackground = Color.FromHex("#090608".AsSpan());

    public static Color ResolveHeaderBackground(bool chaos)
    {
        return chaos ? ChaosHeaderBackground : HeaderBackground;
    }

    public static Color ResolvePanelBackground(bool chaos)
    {
        return chaos ? ChaosPanelBackground : PanelBackground;
    }

    public static Color ResolvePanelBackgroundAlt(bool chaos)
    {
        return chaos ? ChaosPanelBackgroundAlt : PanelBackgroundAlt;
    }

    public static Color ResolveCardBackground(bool chaos)
    {
        return chaos ? ChaosCardBackground : CardBackground;
    }

    public static Color ResolveCardBackgroundAlt(bool chaos)
    {
        return chaos ? ChaosCardBackgroundAlt : CardBackgroundAlt;
    }

    public static Color ResolveCardBackgroundMuted(bool chaos)
    {
        return chaos ? ChaosCardBackgroundMuted : CardBackgroundMuted;
    }

    public static Color ResolveFooterBackground(bool chaos)
    {
        return chaos ? ChaosFooterBackground : FooterBackground;
    }

    public static Color ResolveBadgeBackground(bool chaos)
    {
        return chaos ? ChaosBadgeBackground : BadgeBackground;
    }

    public static Color ResolveMutedBorder(bool chaos)
    {
        return chaos ? ChaosMutedBorder : MutedBorder;
    }

    public static Color ResolveStrongBorder(bool chaos)
    {
        return chaos ? ChaosStrongBorder : StrongBorder;
    }

    public static Color ResolveMutedText(bool chaos)
    {
        return chaos ? ChaosMutedText : MutedText;
    }

    public static Color ResolveSoftText(bool chaos)
    {
        return chaos ? ChaosSoftText : SoftText;
    }

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

    public static StyleBoxFlat CreateHeaderStripStyle(Color border, bool chaos = false)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = chaos ? ChaosHeaderStripBackground : HeaderStripBackground,
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

    public static StyleBoxFlat CreateProgressBackgroundStyle(bool chaos = false)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = ResolveCardBackgroundMuted(chaos)
        };
    }

    public static StyleBoxFlat CreateProgressForegroundStyle(Color accent)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = accent
        };
    }

    public static StyleBoxFlat CreatePrimaryButtonStyle(Color accent, bool disabled = false, bool chaos = false)
    {
        var border = disabled ? ResolveMutedBorder(chaos) : accent;
        var background = disabled
            ? ResolveCardBackgroundMuted(chaos)
            : Blend(chaos ? ChaosButtonBackground : ButtonBackground, accent, chaos ? 0.24f : 0.18f);

        return CreateButtonStyle(background, border);
    }

    public static StyleBoxFlat CreateSecondaryButtonStyle(Color accent, bool disabled = false, bool chaos = false)
    {
        var border = disabled ? ResolveMutedBorder(chaos) : accent.WithAlpha(0.78f);
        var background = disabled
            ? ResolveCardBackgroundMuted(chaos)
            : Blend(chaos ? ChaosButtonBackgroundAlt : ButtonBackgroundAlt, accent, chaos ? 0.12f : 0.08f);

        return CreateButtonStyle(background, border);
    }

    public static StyleBoxFlat CreateDangerButtonStyle(bool disabled = false, bool chaos = false)
    {
        var border = disabled ? ResolveMutedBorder(chaos) : DangerBadge;
        var background = disabled
            ? ResolveCardBackgroundMuted(chaos)
            : Blend(chaos ? ChaosButtonBackground : ButtonBackground, DangerBadge, chaos ? 0.22f : 0.16f);

        return CreateButtonStyle(background, border);
    }

    public static StyleBoxFlat CreateInputStyle(Color border, bool chaos = false)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = chaos ? ChaosInputBackground : InputBackground,
            BorderColor = border,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 8,
            ContentMarginTopOverride = 6,
            ContentMarginRightOverride = 8,
            ContentMarginBottomOverride = 6,
        };
    }

    public static StyleBoxFlat CreatePopupListButtonStyle(Color accent, bool chaos = false)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = ResolveCardBackground(chaos),
            BorderColor = ResolveMutedBorder(chaos).WithAlpha(0.88f),
            BorderThickness = new Thickness(0f, 0f, 0f, 1f),
            ContentMarginLeftOverride = 12,
            ContentMarginTopOverride = 7,
            ContentMarginRightOverride = 12,
            ContentMarginBottomOverride = 7,
        };
    }

    public static StyleBoxFlat CreatePopupListSelectedButtonStyle(Color accent, bool chaos = false)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = ResolveCardBackgroundAlt(chaos),
            BorderColor = accent.WithAlpha(0.72f),
            BorderThickness = new Thickness(0f, 0f, 0f, 1f),
            ContentMarginLeftOverride = 12,
            ContentMarginTopOverride = 7,
            ContentMarginRightOverride = 12,
            ContentMarginBottomOverride = 7,
        };
    }

    public static void ApplyPrimaryButtonTheme(Button button, Color accent, bool disabled = false, bool chaos = false)
    {
        ApplyButtonTheme(button, CreatePrimaryButtonStyle(accent, disabled, chaos), disabled ? ResolveMutedText(chaos) : ResolveSoftText(chaos));
    }

    public static void ApplySecondaryButtonTheme(Button button, Color accent, bool disabled = false, bool chaos = false)
    {
        ApplyButtonTheme(button, CreateSecondaryButtonStyle(accent, disabled, chaos), disabled ? ResolveMutedText(chaos) : ResolveSoftText(chaos));
    }

    public static void ApplyDangerButtonTheme(Button button, bool disabled = false, bool chaos = false)
    {
        ApplyButtonTheme(button, CreateDangerButtonStyle(disabled, chaos), disabled ? ResolveMutedText(chaos) : ResolveSoftText(chaos));
    }

    public static void ApplyButtonTheme(Button button, StyleBoxFlat style, Color fontColor)
    {
        button.StyleBoxOverride = style;
        button.Label.FontColorOverride = fontColor;
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

    public static string FormatThroneGelt(int amount)
    {
        return $"{Math.Max(0, amount)}{ThroneGeltSymbol}";
    }

    public static string FormatInfluence(int amount)
    {
        return $"{Math.Max(0, amount)}{InfluenceSymbol}";
    }

    public static string FormatResearch(int amount)
    {
        return $"{Math.Max(0, amount)}{ResearchSymbol}";
    }

    public static string FormatExperience(int amount)
    {
        return $"{Math.Max(0, amount)}{ExperienceSymbol}";
    }

    public static string FormatArtifacts(int amount, string teamId)
    {
        var value = Math.Max(0, amount) / 10f;
        return $"{value.ToString("0.0", CultureInfo.CurrentCulture)}{ResolveArtifactSymbol(teamId)}";
    }

    public static string ResolveArtifactSymbol(string teamId)
    {
        return string.Equals(teamId, "Heretics", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(teamId, "Chaos", StringComparison.OrdinalIgnoreCase)
            ? ChaosArtifactSymbol
            : ImperiumArtifactSymbol;
    }

    public static string FormatRate(float amount)
    {
        var clamped = Math.Max(0f, amount);
        if (clamped > 0f && clamped < 0.1f)
            return clamped.ToString("0.##", CultureInfo.CurrentCulture);

        var rounded = MathF.Round(clamped * 10f) / 10f;
        if (Math.Abs(rounded - MathF.Round(rounded)) < 0.01f)
            return MathF.Round(rounded).ToString(CultureInfo.CurrentCulture);

        return rounded.ToString("0.#", CultureInfo.CurrentCulture);
    }

    private static StyleBoxFlat CreateButtonStyle(Color background, Color border)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 12,
            ContentMarginTopOverride = 7,
            ContentMarginRightOverride = 12,
            ContentMarginBottomOverride = 7,
        };
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
