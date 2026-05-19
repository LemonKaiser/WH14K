using Content.Client.ContextMenu.UI;
using Content.Client.Resources;
using Content.Client.Stylesheets.Fonts;
using Content.Client.Stylesheets.Palette;
using Content.Client.Stylesheets.SheetletConfigs;
using Content.Client.Stylesheets.Stylesheets;
using Content.Client._WH40K.Command;
using Content.Client.Verbs.UI;
using Content.Shared.Verbs;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets.Hud;

[CommonSheetlet]
public sealed class ContextMenuSheetlet<T> : Sheetlet<T>
    where T : PalettedStylesheet, IWindowConfig, IButtonConfig, IIconConfig
{
    public override StyleRule[] GetRules(T sheet, object config)
    {
        IWindowConfig windowCfg = sheet;

        var popupBackground = new StyleBoxFlat
        {
            BackgroundColor = WH40KCommandUiStyles.PanelBackground,
            BorderColor = WH40KCommandUiStyles.StrongBorder,
            BorderThickness = new Thickness(1f),
        };
        var buttonContextNormal = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#12151C"),
            BorderColor = WH40KCommandUiStyles.MutedBorder.WithAlpha(0.82f),
            BorderThickness = new Thickness(1f),
            ContentMarginLeftOverride = 6,
            ContentMarginTopOverride = 1,
            ContentMarginRightOverride = 6,
            ContentMarginBottomOverride = 1,
        };
        var buttonContextHover = new StyleBoxFlat(buttonContextNormal)
        {
            BackgroundColor = Color.FromHex("#18202A"),
            BorderColor = WH40KCommandUiStyles.StrongBorder.WithAlpha(0.92f),
        };
        var buttonContextPressed = new StyleBoxFlat(buttonContextNormal)
        {
            BackgroundColor = Color.FromHex("#201A13"),
            BorderColor = sheet.HighlightPalette.Text.WithAlpha(0.95f),
        };
        var buttonContextDisabled = new StyleBoxFlat(buttonContextNormal)
        {
            BackgroundColor = Color.FromHex("#111319"),
            BorderColor = WH40KCommandUiStyles.MutedBorder.WithAlpha(0.45f),
        };
        var buttonConfirmNormal = new StyleBoxFlat(buttonContextNormal)
        {
            BorderColor = sheet.NegativePalette.TextDark.WithAlpha(0.75f),
        };
        var buttonConfirmHover = new StyleBoxFlat(buttonContextHover)
        {
            BorderColor = sheet.NegativePalette.Text.WithAlpha(0.9f),
        };
        var buttonConfirmPressed = new StyleBoxFlat(buttonContextPressed)
        {
            BorderColor = sheet.NegativePalette.Text.WithAlpha(0.95f),
        };
        var buttonConfirmDisabled = new StyleBoxFlat(buttonContextDisabled)
        {
            BorderColor = sheet.NegativePalette.TextDark.WithAlpha(0.35f),
        };
        var contextMenuExpansionTexture = ResCache.GetTexture("/Textures/Interface/VerbIcons/group.svg.192dpi.png");
        var verbMenuConfirmationTexture = ResCache.GetTexture("/Textures/Interface/VerbIcons/group.svg.192dpi.png");

        var rules = new List<StyleRule>
        {
            // Context Menu window
            E<PanelContainer>()
                .Class(ContextMenuPopup.StyleClassContextMenuPopup)
                .Panel(popupBackground),

            // Context menu buttons
            E<ContextMenuElement>()
                .Class(ContextMenuElement.StyleClassContextMenuButton)
                .PseudoNormal()
                .Prop(ContainerButton.StylePropertyStyleBox, buttonContextNormal),
            E<ContextMenuElement>()
                .Class(ContextMenuElement.StyleClassContextMenuButton)
                .PseudoHovered()
                .Prop(ContainerButton.StylePropertyStyleBox, buttonContextHover),
            E<ContextMenuElement>()
                .Class(ContextMenuElement.StyleClassContextMenuButton)
                .PseudoPressed()
                .Prop(ContainerButton.StylePropertyStyleBox, buttonContextPressed),
            E<ContextMenuElement>()
                .Class(ContextMenuElement.StyleClassContextMenuButton)
                .PseudoDisabled()
                .Prop(ContainerButton.StylePropertyStyleBox, buttonContextDisabled),

            // Context Menu Labels
            E<RichTextLabel>()
                .Class(InteractionVerb.DefaultTextStyleClass)
                .Font(sheet.BaseFont.GetFont(12, FontKind.BoldItalic))
                .FontColor(sheet.HighlightPalette.Text),
            E<RichTextLabel>()
                .Class(ActivationVerb.DefaultTextStyleClass)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(sheet.HighlightPalette.Text),
            E<RichTextLabel>()
                .Class(AlternativeVerb.DefaultTextStyleClass)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Italic))
                .FontColor(sheet.HighlightPalette.Text),
            E<RichTextLabel>()
                .Class(Verb.DefaultTextStyleClass)
                .Font(sheet.BaseFont.GetFont(12))
                .FontColor(sheet.HighlightPalette.Text),
            E<TextureRect>()
                .Class(ContextMenuElement.StyleClassContextMenuExpansionTexture)
                .Prop(TextureRect.StylePropertyTexture, contextMenuExpansionTexture)
                .Prop(Control.StylePropertyModulateSelf, sheet.HighlightPalette.Text),
            E<TextureRect>()
                .Class(VerbMenuElement.StyleClassVerbMenuConfirmationTexture)
                .Prop(TextureRect.StylePropertyTexture, verbMenuConfirmationTexture)
                .Prop(Control.StylePropertyModulateSelf, sheet.HighlightPalette.Text),

            // Context menu confirm buttons
            E<ContextMenuElement>()
                .Class(ConfirmationMenuElement.StyleClassConfirmationContextMenuButton)
                .PseudoNormal()
                .Prop(ContainerButton.StylePropertyStyleBox, buttonConfirmNormal),
            E<ContextMenuElement>()
                .Class(ConfirmationMenuElement.StyleClassConfirmationContextMenuButton)
                .PseudoHovered()
                .Prop(ContainerButton.StylePropertyStyleBox, buttonConfirmHover),
            E<ContextMenuElement>()
                .Class(ConfirmationMenuElement.StyleClassConfirmationContextMenuButton)
                .PseudoPressed()
                .Prop(ContainerButton.StylePropertyStyleBox, buttonConfirmPressed),
            E<ContextMenuElement>()
                .Class(ConfirmationMenuElement.StyleClassConfirmationContextMenuButton)
                .PseudoDisabled()
                .Prop(ContainerButton.StylePropertyStyleBox, buttonConfirmDisabled),
        };

        return rules.ToArray();
    }
}
