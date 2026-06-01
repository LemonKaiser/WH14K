using System.Numerics;
using Content.Client.Stylesheets.Fonts;
using Content.Client.Stylesheets.Palette;
using Content.Client.Stylesheets.SheetletConfigs;
using Content.Client.Stylesheets.Stylesheets;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets;

[CommonSheetlet]
public sealed class MenuButtonSheetlet<T> : Sheetlet<T> where T : PalettedStylesheet, IButtonConfig, IIconConfig
{
    private static MutableSelectorElement CButton(string? shapeClass = null, string? toneClass = null)
    {
        var selector = E<MenuButton>();

        if (!string.IsNullOrEmpty(shapeClass))
            selector = selector.Class(shapeClass);

        if (!string.IsNullOrEmpty(toneClass))
            selector = selector.Class(toneClass);

        return selector;
    }

    public override StyleRule[] GetRules(T sheet, object config)
    {
        IButtonConfig cfg = sheet;

        var buttonTex = sheet.GetTextureOr(cfg.BaseButtonPath, NanotrasenStylesheet.TextureRoot);
        var topButtonBase = new StyleBoxTexture
        {
            Texture = buttonTex,
        };
        topButtonBase.SetPatchMargin(StyleBox.Margin.All, 10);
        topButtonBase.SetPadding(StyleBox.Margin.All, 0);
        topButtonBase.SetContentMarginOverride(StyleBox.Margin.All, 0);

        var topButtonOpenRight = new StyleBoxTexture(topButtonBase)
        {
            Texture = new AtlasTexture(buttonTex, UIBox2.FromDimensions(new Vector2(0, 0), new Vector2(14, 24))),
        };
        topButtonOpenRight.SetPatchMargin(StyleBox.Margin.Right, 0);

        var topButtonOpenLeft = new StyleBoxTexture(topButtonBase)
        {
            Texture = new AtlasTexture(buttonTex, UIBox2.FromDimensions(new Vector2(10, 0), new Vector2(14, 24))),
        };
        topButtonOpenLeft.SetPatchMargin(StyleBox.Margin.Left, 0);

        var topButtonSquare = new StyleBoxTexture(topButtonBase)
        {
            Texture = new AtlasTexture(buttonTex, UIBox2.FromDimensions(new Vector2(10, 0), new Vector2(3, 24))),
        };
        topButtonSquare.SetPatchMargin(StyleBox.Margin.Horizontal, 0);

        var rules = new List<StyleRule>
        {
            CButton().Box(topButtonBase),
            CButton(StyleClass.ButtonSquare).Box(topButtonSquare),
            CButton(StyleClass.ButtonOpenLeft).Box(topButtonOpenLeft),
            CButton(StyleClass.ButtonOpenRight).Box(topButtonOpenRight),
            CButton(StyleClass.ButtonOpenLeft)
                .PseudoNormal()
                .Box(MenuButtonStateBox(topButtonOpenLeft, ButtonVisualState.Normal, cfg.ButtonPalette)),
            CButton(StyleClass.ButtonOpenRight)
                .PseudoNormal()
                .Box(MenuButtonStateBox(topButtonOpenRight, ButtonVisualState.Normal, cfg.ButtonPalette)),
            CButton(StyleClass.ButtonOpenBoth)
                .PseudoNormal()
                .Box(MenuButtonStateBox(topButtonSquare, ButtonVisualState.Normal, cfg.ButtonPalette)),
            CButton(StyleClass.ButtonSquare)
                .PseudoNormal()
                .Box(MenuButtonStateBox(topButtonSquare, ButtonVisualState.Normal, cfg.ButtonPalette)),
            CButton()
                .PseudoNormal()
                .Box(MenuButtonStateBox(topButtonBase, ButtonVisualState.Normal, cfg.ButtonPalette)),
            E<Label>()
                .Class(MenuButton.StyleClassLabelTopButton)
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(14, FontKind.Bold)),
        };

        AddMenuButtonStateRules(rules, topButtonBase, null, cfg.ButtonPalette);
        AddMenuButtonStateRules(rules, topButtonOpenLeft, StyleClass.ButtonOpenLeft, cfg.ButtonPalette);
        AddMenuButtonStateRules(rules, topButtonOpenRight, StyleClass.ButtonOpenRight, cfg.ButtonPalette);
        AddMenuButtonStateRules(rules, topButtonSquare, StyleClass.ButtonSquare, cfg.ButtonPalette);
        AddMenuButtonStateRules(rules, topButtonSquare, StyleClass.ButtonOpenBoth, cfg.ButtonPalette);

        AddMenuButtonToneRules(rules, topButtonBase, null, cfg.PositiveButtonPalette, StyleClass.Positive);
        AddMenuButtonToneRules(rules, topButtonBase, null, cfg.NegativeButtonPalette, StyleClass.Negative);

        return rules.ToArray();
    }

    private static void AddMenuButtonToneRules(
        List<StyleRule> rules,
        StyleBoxTexture baseBox,
        string? shapeClass,
        ColorPalette palette,
        string toneClass)
    {
        AddMenuButtonStateRules(rules, baseBox, shapeClass, palette, toneClass);
    }

    private static void AddMenuButtonStateRules(
        List<StyleRule> rules,
        StyleBoxTexture baseBox,
        string? shapeClass,
        ColorPalette palette,
        string? toneClass = null)
    {
        rules.AddRange([
            CButton(shapeClass, toneClass).PseudoNormal()
                .Box(MenuButtonStateBox(baseBox, ButtonVisualState.Normal, palette)),
            CButton(shapeClass, toneClass).PseudoHovered()
                .Box(MenuButtonStateBox(baseBox, ButtonVisualState.Hovered, palette)),
            CButton(shapeClass, toneClass).PseudoPressed()
                .Box(MenuButtonStateBox(baseBox, ButtonVisualState.Pressed, palette)),
            CButton(shapeClass, toneClass).PseudoDisabled()
                .Box(MenuButtonStateBox(baseBox, ButtonVisualState.Disabled, palette)),
        ]);
    }

    private static StyleBoxTexture MenuButtonStateBox(StyleBoxTexture baseBox, ButtonVisualState state, ColorPalette palette)
    {
        var box = new StyleBoxTexture(baseBox);
        box.Modulate = state switch
        {
            ButtonVisualState.Hovered => palette.HoveredElement,
            ButtonVisualState.Pressed => palette.PressedElement,
            ButtonVisualState.Disabled => palette.DisabledElement.WithAlpha(0.75f),
            _ => palette.Element,
        };

        return box;
    }
}
