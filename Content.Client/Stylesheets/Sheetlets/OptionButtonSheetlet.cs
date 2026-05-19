using Content.Client.Stylesheets.SheetletConfigs;
using Content.Client.Stylesheets.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets;

[CommonSheetlet]
public sealed class OptionButtonSheetlet<T> : Sheetlet<T> where T : PalettedStylesheet, IIconConfig, IButtonConfig
{
    public override StyleRule[] GetRules(T sheet, object config)
    {
        IIconConfig iconCfg = sheet;

        var invertedTriangleTex =
            sheet.GetTextureOr(iconCfg.InvertedTriangleIconPath, NanotrasenStylesheet.TextureRoot);

        return
        [
            E<OptionButton>()
                .PseudoNormal()
                .Box(StyleBoxHelpers.ImperialButtonStyleBox(sheet, ButtonShape.Default, ButtonVisualState.Normal)),
            E<OptionButton>()
                .PseudoHovered()
                .Box(StyleBoxHelpers.ImperialButtonStyleBox(sheet, ButtonShape.Default, ButtonVisualState.Hovered)),
            E<OptionButton>()
                .PseudoPressed()
                .Box(StyleBoxHelpers.ImperialButtonStyleBox(sheet, ButtonShape.Default, ButtonVisualState.Pressed)),
            E<OptionButton>()
                .PseudoDisabled()
                .Box(StyleBoxHelpers.ImperialButtonStyleBox(sheet, ButtonShape.Default, ButtonVisualState.Disabled)),
            E<TextureRect>()
                .Class(OptionButton.StyleClassOptionTriangle)
                .Prop(TextureRect.StylePropertyTexture, invertedTriangleTex)
                .Prop(Control.StylePropertyModulateSelf, sheet.HighlightPalette.Text),
            E<Label>()
                .Class(OptionButton.StyleClassOptionButton)
                .AlignMode(Label.AlignMode.Center)
                .FontColor(sheet.HighlightPalette.Text),
            E<PanelContainer>()
                .Class(OptionButton.StyleClassOptionsBackground)
                .Panel(new StyleBoxFlat(sheet.PrimaryPalette.BackgroundDark.WithAlpha(0.97f))),
        ];
    }
}
