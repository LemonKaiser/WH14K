using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets;

[CommonSheetlet]
public sealed class ProgressBarSheetlet : Sheetlet<PalettedStylesheet>
{
    public override StyleRule[] GetRules(PalettedStylesheet sheet, object config)
    {
        var progressBarBackground = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#3A362E"),
            BorderColor = Color.FromHex("#5C4A26"),
            BorderThickness = new Thickness(1),
        };
        progressBarBackground.SetContentMarginOverride(StyleBox.Margin.Vertical, 14.5f);
        var progressBarForeground = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#B69A4A"),
        };
        progressBarForeground.SetContentMarginOverride(StyleBox.Margin.Vertical, 14.5f);

        return
        [
            E<ProgressBar>()
                .Prop(ProgressBar.StylePropertyBackground, progressBarBackground)
                .Prop(ProgressBar.StylePropertyForeground, progressBarForeground),
        ];
    }
}
