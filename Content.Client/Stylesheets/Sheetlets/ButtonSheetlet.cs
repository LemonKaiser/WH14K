using System;
using System.Numerics;
using Content.Client.Stylesheets.Palette;
using Content.Client.Stylesheets.SheetletConfigs;
using Content.Client.Stylesheets.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets;

[CommonSheetlet]
public sealed class ButtonSheetlet<T> : Sheetlet<T> where T : PalettedStylesheet, IButtonConfig, IIconConfig
{
    public override StyleRule[] GetRules(T sheet, object config)
    {
        IButtonConfig buttonCfg = sheet;
        IIconConfig iconCfg = sheet;

        var crossTex = sheet.GetTextureOr(iconCfg.CrossIconPath, NanotrasenStylesheet.TextureRoot);
        var refreshTex = sheet.GetTextureOr(iconCfg.RefreshIconPath, NanotrasenStylesheet.TextureRoot);
        var helpTex = sheet.GetTextureOr(iconCfg.HelpIconPath, NanotrasenStylesheet.TextureRoot);
        var neutralVisuals = CreateButtonVisuals(sheet, buttonCfg.ButtonPalette);
        var positiveVisuals = CreateButtonVisuals(sheet, buttonCfg.PositiveButtonPalette);
        var negativeVisuals = CreateButtonVisuals(sheet, buttonCfg.NegativeButtonPalette);

        var rules = new List<StyleRule>
        {
            CButton()
                .Class(StyleClass.ButtonSmall)
                .ParentOf(E<Label>())
                .Font(sheet.BaseFont.GetFont(8)),
            CButton().Class(StyleClass.ButtonBig).ParentOf(E<Label>()).Font(sheet.BaseFont.GetFont(16)),

            // Cross Button (Red)
            E<TextureButton>()
                .Class(StyleClass.CrossButtonRed)
                .Prop(TextureButton.StylePropertyTexture, crossTex),

            // Refresh Button
            E<TextureButton>()
                .Class(StyleClass.RefreshButton)
                .Prop(TextureButton.StylePropertyTexture, refreshTex),

            // Help button
            E<TextureButton>()
                .Class(StyleClass.HelpButton)
                .Prop(TextureButton.StylePropertyTexture, helpTex),
        };

        AddButtonStateRules(rules, sheet, neutralVisuals, null);
        AddButtonStateRules(rules, sheet, positiveVisuals, StyleClass.Positive);
        AddButtonStateRules(rules, sheet, negativeVisuals, StyleClass.Negative);

        AddLabelRules(rules, neutralVisuals, null);
        AddLabelRules(rules, positiveVisuals, StyleClass.Positive);
        AddLabelRules(rules, negativeVisuals, StyleClass.Negative);

        // Texture button modulation
        MakeButtonRules<TextureButton>(rules, Palettes.AlphaModulate, null);
        MakeButtonRules<TextureButton>(rules, sheet.NegativePalette, StyleClass.CrossButtonRed);

        return rules.ToArray();
    }

    private static void AddButtonStateRules(List<StyleRule> rules, T sheet, ButtonVisuals visuals, string? toneClass)
    {
        AddButtonShapeRules(rules, sheet, visuals, ButtonShape.Default, null, toneClass);
        AddButtonShapeRules(rules, sheet, visuals, ButtonShape.OpenLeft, StyleClass.ButtonOpenLeft, toneClass);
        AddButtonShapeRules(rules, sheet, visuals, ButtonShape.OpenRight, StyleClass.ButtonOpenRight, toneClass);
        AddButtonShapeRules(rules, sheet, visuals, ButtonShape.OpenBoth, StyleClass.ButtonOpenBoth, toneClass);
        AddButtonShapeRules(rules, sheet, visuals, ButtonShape.Square, StyleClass.ButtonSquare, toneClass);
        AddButtonShapeRules(rules, sheet, visuals, ButtonShape.Small, StyleClass.ButtonSmall, toneClass);
    }

    private static void AddButtonShapeRules(
        List<StyleRule> rules,
        T sheet,
        ButtonVisuals visuals,
        ButtonShape shape,
        string? shapeClass,
        string? toneClass)
    {
        StyleBox normal = StyleBoxHelpers.ImperialButtonStyleBox(sheet, shape, ButtonVisualState.Normal);
        StyleBox hovered = StyleBoxHelpers.ImperialButtonStyleBox(sheet, shape, ButtonVisualState.Hovered);
        StyleBox pressed = StyleBoxHelpers.ImperialButtonStyleBox(sheet, shape, ButtonVisualState.Pressed);
        StyleBox disabled = StyleBoxHelpers.ImperialButtonStyleBox(sheet, shape, ButtonVisualState.Disabled);

        rules.AddRange([
            CButton(shapeClass, toneClass)
                .PseudoNormal()
                .Box(normal),
            CButton(shapeClass, toneClass)
                .PseudoHovered()
                .Box(hovered),
            CButton(shapeClass, toneClass)
                .PseudoPressed()
                .Box(pressed),
            CButton(shapeClass, toneClass)
                .PseudoDisabled()
                .Box(disabled),
        ]);
    }

    private static void AddLabelRules(List<StyleRule> rules, ButtonVisuals visuals, string? toneClass)
    {
        rules.AddRange([
            CButton(null, toneClass)
                .ParentOf(E<Label>())
                .AlignMode(Label.AlignMode.Center)
                .FontColor(visuals.Text),
            CButton(null, toneClass)
                .PseudoDisabled()
                .ParentOf(E<Label>())
                .FontColor(visuals.DisabledText),
            CButton(null, toneClass)
                .PseudoDisabled()
                .ParentOf(E())
                .ParentOf(E<Label>())
                .FontColor(visuals.DisabledText),
        ]);
    }

    private static ButtonVisuals CreateButtonVisuals(PalettedStylesheet sheet, ColorPalette palette)
    {
        var accent = palette.Text;

        return new ButtonVisuals(
            NormalBackground: Blend(sheet.PrimaryPalette.BackgroundDark, accent, 0.05f),
            HoveredBackground: Blend(sheet.PrimaryPalette.Background, accent, 0.08f),
            PressedBackground: Blend(sheet.PrimaryPalette.BackgroundDark, accent, 0.12f),
            DisabledBackground: Blend(sheet.PrimaryPalette.BackgroundDark, accent, 0.02f),
            NormalBorder: Blend(sheet.SecondaryPalette.TextDark, palette.TextDark, 0.55f).WithAlpha(0.96f),
            HoveredBorder: palette.Text.WithAlpha(0.96f),
            PressedBorder: Blend(palette.TextDark, palette.Text, 0.24f).WithAlpha(0.96f),
            DisabledBorder: Blend(sheet.SecondaryPalette.TextDark, palette.TextDark, 0.25f).WithAlpha(0.55f),
            Text: palette.Text,
            DisabledText: palette.TextDark.WithAlpha(0.62f));
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

    public static void MakeButtonRules<TC>(
        List<StyleRule> rules,
        ColorPalette palette,
        string? styleclass)
        where TC : Control
    {
        rules.AddRange([
            E<TC>().MaybeClass(styleclass).PseudoNormal().Modulate(palette.Element),
            E<TC>().MaybeClass(styleclass).PseudoHovered().Modulate(palette.HoveredElement),
            E<TC>().MaybeClass(styleclass).PseudoPressed().Modulate(palette.PressedElement),
            E<TC>().MaybeClass(styleclass).PseudoDisabled().Modulate(palette.DisabledElement),
        ]);
    }

    public static void MakeButtonRules(
        List<StyleRule> rules,
        ColorPalette palette,
        string? styleclass)
    {
        rules.AddRange([
            CButton()
                .MaybeClass(styleclass)
                .PseudoNormal()
                .Prop(Control.StylePropertyModulateSelf, palette.Element),
            CButton()
                .MaybeClass(styleclass)
                .PseudoHovered()
                .Prop(Control.StylePropertyModulateSelf, palette.HoveredElement),
            CButton()
                .MaybeClass(styleclass)
                .PseudoPressed()
                .Prop(Control.StylePropertyModulateSelf, palette.PressedElement),
            CButton()
                .MaybeClass(styleclass)
                .PseudoDisabled()
                .Prop(Control.StylePropertyModulateSelf, palette.DisabledElement),
        ]);
    }

    private static MutableSelectorElement CButton(string? shapeClass = null, string? toneClass = null)
    {
        var selector = E<ContainerButton>().Class(ContainerButton.StyleClassButton);

        if (!string.IsNullOrEmpty(shapeClass))
            selector = selector.Class(shapeClass);

        if (!string.IsNullOrEmpty(toneClass))
            selector = selector.Class(toneClass);

        return selector;
    }
}

internal readonly record struct ButtonVisuals(
    Color NormalBackground,
    Color HoveredBackground,
    Color PressedBackground,
    Color DisabledBackground,
    Color NormalBorder,
    Color HoveredBorder,
    Color PressedBorder,
    Color DisabledBorder,
    Color Text,
    Color DisabledText);

internal enum ButtonShape
{
    Default,
    OpenLeft,
    OpenRight,
    OpenBoth,
    Square,
    Small,
}

internal enum ButtonVisualState
{
    Normal,
    Hovered,
    Pressed,
    Disabled,
}

// this is currently the only other "helper" type class, if any more crop up consider making a specific directory for them
public static class StyleBoxHelpers
{
    // TODO: Figure out a nicer way to store/represent these hardcoded margins. This is icky.
    public static StyleBoxTexture BaseStyleBox<T>(T sheet) where T : PalettedStylesheet, IButtonConfig
    {
        var baseBox = new StyleBoxTexture
        {
            Texture = sheet.GetTextureOr(sheet.BaseButtonPath, NanotrasenStylesheet.TextureRoot),
        };
        baseBox.SetPatchMargin(StyleBox.Margin.All, 10);
        baseBox.SetPadding(StyleBox.Margin.All, 1);
        baseBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        baseBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 14);
        return baseBox;
    }

    public static StyleBoxTexture OpenLeftStyleBox<T>(T sheet) where T : PalettedStylesheet, IButtonConfig
    {
        var openLeftBox = new StyleBoxTexture(BaseStyleBox(sheet))
        {
            Texture = new AtlasTexture(sheet.GetTextureOr(sheet.OpenLeftButtonPath, NanotrasenStylesheet.TextureRoot),
                UIBox2.FromDimensions(new Vector2(10, 0), new Vector2(14, 24))),
        };
        openLeftBox.SetPatchMargin(StyleBox.Margin.Left, 0);
        openLeftBox.SetContentMarginOverride(StyleBox.Margin.Left, 8);
        // openLeftBox.SetPadding(StyleBox.Margin.Left, 1);
        return openLeftBox;
    }

    public static StyleBoxTexture OpenRightStyleBox<T>(T sheet) where T : PalettedStylesheet, IButtonConfig
    {
        var openRightBox = new StyleBoxTexture(BaseStyleBox(sheet))
        {
            Texture = new AtlasTexture(sheet.GetTextureOr(sheet.OpenRightButtonPath, NanotrasenStylesheet.TextureRoot),
                UIBox2.FromDimensions(new Vector2(0, 0), new Vector2(14, 24))),
        };
        openRightBox.SetPatchMargin(StyleBox.Margin.Right, 0);
        openRightBox.SetContentMarginOverride(StyleBox.Margin.Right, 8);
        openRightBox.SetPadding(StyleBox.Margin.Right, 1);
        return openRightBox;
    }

    public static StyleBoxTexture SquareStyleBox<T>(T sheet) where T : PalettedStylesheet, IButtonConfig
    {
        var openBothBox = new StyleBoxTexture(BaseStyleBox(sheet))
        {
            Texture = new AtlasTexture(sheet.GetTextureOr(sheet.OpenBothButtonPath, NanotrasenStylesheet.TextureRoot),
                UIBox2.FromDimensions(new Vector2(10, 0), new Vector2(3, 24))),
        };
        openBothBox.SetPatchMargin(StyleBox.Margin.Horizontal, 0);
        openBothBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);
        openBothBox.SetPadding(StyleBox.Margin.Horizontal, 1);
        return openBothBox;
    }

    public static StyleBoxTexture SmallStyleBox<T>(T sheet) where T : PalettedStylesheet, IButtonConfig
    {
        var smallBox = new StyleBoxTexture
        {
            Texture = sheet.GetTextureOr(sheet.SmallButtonPath, NanotrasenStylesheet.TextureRoot),
        };
        smallBox.SetPatchMargin(StyleBox.Margin.Left, 8);
        smallBox.SetPatchMargin(StyleBox.Margin.Right, 8);
        smallBox.SetPatchMargin(StyleBox.Margin.Top, 4);
        smallBox.SetPatchMargin(StyleBox.Margin.Bottom, 4);
        smallBox.SetContentMarginOverride(StyleBox.Margin.Left, 10);
        smallBox.SetContentMarginOverride(StyleBox.Margin.Right, 10);
        smallBox.SetContentMarginOverride(StyleBox.Margin.Top, 4);
        smallBox.SetContentMarginOverride(StyleBox.Margin.Bottom, 4);
        return smallBox;
    }

    internal static StyleBoxTexture ImperialButtonStyleBox<T>(T sheet, ButtonShape shape, ButtonVisualState state)
        where T : PalettedStylesheet, IButtonConfig
    {
        StyleBoxTexture style = shape switch
        {
            ButtonShape.OpenLeft => OpenLeftStyleBox(sheet),
            ButtonShape.OpenRight => OpenRightStyleBox(sheet),
            ButtonShape.OpenBoth => SquareStyleBox(sheet),
            ButtonShape.Square => SquareStyleBox(sheet),
            ButtonShape.Small => SmallStyleBox(sheet),
            _ => BaseStyleBox(sheet),
        };

        style.Modulate = state switch
        {
            ButtonVisualState.Hovered => Color.FromHex("#FFF1C6"),
            ButtonVisualState.Pressed => Color.FromHex("#C9AF70"),
            ButtonVisualState.Disabled => Color.FromHex("#5B5751").WithAlpha(0.75f),
            _ => Color.White,
        };

        return style;
    }

    internal static StyleBoxFlat FlatButtonStyleBox(ButtonVisuals visuals, ButtonShape shape, ButtonVisualState state)
    {
        var (background, border) = state switch
        {
            ButtonVisualState.Hovered => (visuals.HoveredBackground, visuals.HoveredBorder),
            ButtonVisualState.Pressed => (visuals.PressedBackground, visuals.PressedBorder),
            ButtonVisualState.Disabled => (visuals.DisabledBackground, visuals.DisabledBorder),
            _ => (visuals.NormalBackground, visuals.NormalBorder),
        };

        var style = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
        };

        switch (shape)
        {
            case ButtonShape.OpenLeft:
                style.BorderThickness = new Thickness(0f, 1f, 1f, 2f);
                style.ContentMarginLeftOverride = 12;
                style.ContentMarginTopOverride = 6;
                style.ContentMarginRightOverride = 16;
                style.ContentMarginBottomOverride = 6;
                break;
            case ButtonShape.OpenRight:
                style.BorderThickness = new Thickness(1f, 1f, 0f, 2f);
                style.ContentMarginLeftOverride = 16;
                style.ContentMarginTopOverride = 6;
                style.ContentMarginRightOverride = 12;
                style.ContentMarginBottomOverride = 6;
                break;
            case ButtonShape.OpenBoth:
                style.BorderThickness = new Thickness(1f, 1f, 1f, 2f);
                style.ContentMarginLeftOverride = 12;
                style.ContentMarginTopOverride = 6;
                style.ContentMarginRightOverride = 12;
                style.ContentMarginBottomOverride = 6;
                break;
            case ButtonShape.Square:
                style.BorderThickness = new Thickness(1f, 1f, 1f, 2f);
                style.ContentMarginLeftOverride = 10;
                style.ContentMarginTopOverride = 6;
                style.ContentMarginRightOverride = 10;
                style.ContentMarginBottomOverride = 6;
                break;
            case ButtonShape.Small:
                style.BorderThickness = new Thickness(1f);
                style.ContentMarginLeftOverride = 9;
                style.ContentMarginTopOverride = 4;
                style.ContentMarginRightOverride = 9;
                style.ContentMarginBottomOverride = 4;
                break;
            default:
                style.BorderThickness = new Thickness(1f, 1f, 1f, 2f);
                style.ContentMarginLeftOverride = 16;
                style.ContentMarginTopOverride = 6;
                style.ContentMarginRightOverride = 16;
                style.ContentMarginBottomOverride = 6;
                break;
        }

        return style;
    }
}
