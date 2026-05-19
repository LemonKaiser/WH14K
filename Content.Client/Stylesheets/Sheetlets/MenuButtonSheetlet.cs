using System.Numerics;
using Content.Client.Stylesheets.Fonts;
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
    private static MutableSelectorElement CButton()
    {
        return E<MenuButton>();
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
            CButton().Class(StyleClass.ButtonSquare).Box(topButtonSquare),
            CButton().Class(StyleClass.ButtonOpenLeft).Box(topButtonOpenLeft),
            CButton().Class(StyleClass.ButtonOpenRight).Box(topButtonOpenRight),
            CButton()
                .Class(StyleClass.ButtonOpenLeft)
                .PseudoNormal()
                .Box(MenuButtonStateBox(topButtonOpenLeft, ButtonVisualState.Normal)),
            CButton()
                .Class(StyleClass.ButtonOpenRight)
                .PseudoNormal()
                .Box(MenuButtonStateBox(topButtonOpenRight, ButtonVisualState.Normal)),
            CButton()
                .Class(StyleClass.ButtonOpenBoth)
                .PseudoNormal()
                .Box(MenuButtonStateBox(topButtonSquare, ButtonVisualState.Normal)),
            CButton()
                .Class(StyleClass.ButtonSquare)
                .PseudoNormal()
                .Box(MenuButtonStateBox(topButtonSquare, ButtonVisualState.Normal)),
            CButton()
                .PseudoNormal()
                .Box(MenuButtonStateBox(topButtonBase, ButtonVisualState.Normal)),
            E<Label>()
                .Class(MenuButton.StyleClassLabelTopButton)
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(14, FontKind.Bold)),
            // new StyleProperty(Label.StylePropertyFont, notoSansDisplayBold14),
        };

        AddMenuButtonStateRules(rules, topButtonBase, null);
        AddMenuButtonStateRules(rules, topButtonOpenLeft, StyleClass.ButtonOpenLeft);
        AddMenuButtonStateRules(rules, topButtonOpenRight, StyleClass.ButtonOpenRight);
        AddMenuButtonStateRules(rules, topButtonSquare, StyleClass.ButtonSquare);
        AddMenuButtonStateRules(rules, topButtonSquare, StyleClass.ButtonOpenBoth);

        return rules.ToArray();
    }

    private static void AddMenuButtonStateRules(
        List<StyleRule> rules,
        StyleBoxTexture baseBox,
        string? shapeClass)
    {
        rules.AddRange([
            CButton().MaybeClass(shapeClass).PseudoNormal()
                .Box(MenuButtonStateBox(baseBox, ButtonVisualState.Normal)),
            CButton().MaybeClass(shapeClass).PseudoHovered()
                .Box(MenuButtonStateBox(baseBox, ButtonVisualState.Hovered)),
            CButton().MaybeClass(shapeClass).PseudoPressed()
                .Box(MenuButtonStateBox(baseBox, ButtonVisualState.Pressed)),
            CButton().MaybeClass(shapeClass).PseudoDisabled()
                .Box(MenuButtonStateBox(baseBox, ButtonVisualState.Disabled)),
        ]);
    }

    private static StyleBoxTexture MenuButtonStateBox(StyleBoxTexture baseBox, ButtonVisualState state)
    {
        var box = new StyleBoxTexture(baseBox);
        box.Modulate = state switch
        {
            ButtonVisualState.Hovered => Color.FromHex("#F3E6BF"),
            ButtonVisualState.Pressed => Color.FromHex("#D5BE86"),
            ButtonVisualState.Disabled => Color.FromHex("#6A6458").WithAlpha(0.75f),
            _ => Color.FromHex("#E3D2A5"),
        };

        return box;
    }
}
