using System;
using Content.Client.Stylesheets.SheetletConfigs;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets;

[CommonSheetlet]
public sealed class ListContainerSheetlet<T> : Sheetlet<T> where T : PalettedStylesheet, IButtonConfig, IIconConfig
{
    public override StyleRule[] GetRules(T sheet, object config)
    {
        var normalBox = CreateRowBox(
            sheet.PrimaryPalette.BackgroundDark.WithAlpha(0.72f),
            sheet.HighlightPalette.TextDark.WithAlpha(0.24f));
        var hoveredBox = CreateRowBox(
            Blend(sheet.PrimaryPalette.BackgroundDark, sheet.HighlightPalette.Text, 0.06f).WithAlpha(0.86f),
            sheet.HighlightPalette.TextDark.WithAlpha(0.52f));
        var pressedBox = CreateRowBox(
            Blend(sheet.PrimaryPalette.BackgroundDark, sheet.HighlightPalette.Text, 0.11f).WithAlpha(0.96f),
            sheet.HighlightPalette.Text.WithAlpha(0.78f));
        var disabledBox = CreateRowBox(
            sheet.PrimaryPalette.BackgroundDark.WithAlpha(0.46f),
            sheet.SecondaryPalette.TextDark.WithAlpha(0.28f));

        var rules = new List<StyleRule>(
        [
            E<ContainerButton>()
                .Class(ListContainer.StyleClassListContainerButton)
                .PseudoNormal()
                .Box(normalBox),
            E<ContainerButton>()
                .Class(ListContainer.StyleClassListContainerButton)
                .PseudoHovered()
                .Box(hoveredBox),
            E<ContainerButton>()
                .Class(ListContainer.StyleClassListContainerButton)
                .PseudoPressed()
                .Box(pressedBox),
            E<ContainerButton>()
                .Class(ListContainer.StyleClassListContainerButton)
                .PseudoDisabled()
                .Box(disabledBox),

            E<ContainerButton>()
                .Class(ListContainer.StyleClassListContainerButton)
                .ParentOf(E<Label>())
                .FontColor(sheet.HighlightPalette.Text),
            E<ContainerButton>()
                .Class(ListContainer.StyleClassListContainerButton)
                .PseudoDisabled()
                .ParentOf(E<Label>())
                .FontColor(sheet.HighlightPalette.TextDark.WithAlpha(0.62f)),
        ]);

        return rules.ToArray();
    }

    private static StyleBoxFlat CreateRowBox(Color background, Color border)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 8,
            ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 4,
            ContentMarginBottomOverride = 4,
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
