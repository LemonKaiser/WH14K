using System;
using Content.Client.UserInterface.Controls.FancyTree;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets;

[CommonSheetlet]
public sealed class FancyTreeSheetlet : Sheetlet<PalettedStylesheet>
{
    public override StyleRule[] GetRules(PalettedStylesheet sheet, object config)
    {
        var evenRow = new StyleBoxFlat(sheet.PrimaryPalette.BackgroundDark.WithAlpha(0.92f));
        var oddRow = new StyleBoxFlat(sheet.PrimaryPalette.Background.WithAlpha(0.92f));
        var selectedRow = new StyleBoxFlat(Blend(sheet.PrimaryPalette.BackgroundLight, sheet.HighlightPalette.Text, 0.14f));
        var hoveredRow = new StyleBoxFlat(Blend(sheet.PrimaryPalette.BackgroundLight, sheet.HighlightPalette.Text, 0.08f));

        return
        [
            E<ContainerButton>()
                .Identifier(TreeItem.StyleIdentifierTreeButton)
                .Class(TreeItem.StyleClassEvenRow)
                .Prop(ContainerButton.StylePropertyStyleBox, evenRow),
            E<ContainerButton>()
                .Identifier(TreeItem.StyleIdentifierTreeButton)
                .Class(TreeItem.StyleClassOddRow)
                .Prop(ContainerButton.StylePropertyStyleBox, oddRow),

            E<ContainerButton>()
                .Identifier(TreeItem.StyleIdentifierTreeButton)
                .Class(TreeItem.StyleClassSelected)
                .Prop(ContainerButton.StylePropertyStyleBox, selectedRow),

            E<ContainerButton>()
                .Identifier(TreeItem.StyleIdentifierTreeButton)
                .Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(ContainerButton.StylePropertyStyleBox, hoveredRow),

            E<FancyTree>()
                .Prop(FancyTree.StylePropertyLineColor, sheet.HighlightPalette.TextDark.WithAlpha(0.58f))
                .Prop(FancyTree.StylePropertyIconColor, sheet.HighlightPalette.TextDark.WithAlpha(0.9f)),
        ];
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
