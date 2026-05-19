using Content.Client.Stylesheets.SheetletConfigs;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets;

[CommonSheetlet]
public sealed class TabContainerSheetlet<T> : Sheetlet<T> where T : PalettedStylesheet, ITabContainerConfig
{
    public override StyleRule[] GetRules(T sheet, object config)
    {
        var tabContainerPanel = new StyleBoxFlat
        {
            BackgroundColor = sheet.PrimaryPalette.BackgroundDark.WithAlpha(0.82f),
            BorderColor = sheet.HighlightPalette.TextDark.WithAlpha(0.72f),
            BorderThickness = new Thickness(1),
        };

        var tabContainerBoxActive = new StyleBoxFlat
        {
            BackgroundColor = sheet.PrimaryPalette.Background.WithAlpha(0.95f),
            BorderColor = sheet.HighlightPalette.Text.WithAlpha(0.86f),
            BorderThickness = new Thickness(1, 1, 1, 0),
            PaddingLeft = 1,
            PaddingRight = 1,
            ContentMarginBottomOverride = 4,
            ContentMarginLeftOverride = 8,
            ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 4,
        };

        var tabContainerBoxInactive = new StyleBoxFlat
        {
            BackgroundColor = sheet.PrimaryPalette.BackgroundDark.WithAlpha(0.72f),
            BorderColor = sheet.SecondaryPalette.TextDark.WithAlpha(0.55f),
            BorderThickness = new Thickness(1, 1, 1, 0),
            PaddingLeft = 1,
            PaddingRight = 1,
            ContentMarginBottomOverride = 4,
            ContentMarginLeftOverride = 8,
            ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 4,
        };

        return
        [
            E<TabContainer>()
                .Prop(TabContainer.StylePropertyPanelStyleBox, tabContainerPanel)
                .Prop(TabContainer.StylePropertyTabStyleBox, tabContainerBoxActive)
                .Prop(TabContainer.StylePropertyTabStyleBoxInactive, tabContainerBoxInactive)
                .Prop(TabContainer.stylePropertyTabFontColor, sheet.HighlightPalette.Text)
                .Prop(TabContainer.StylePropertyTabFontColorInactive, sheet.HighlightPalette.TextDark.WithAlpha(0.92f)),
        ];
    }
}
