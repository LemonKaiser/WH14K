using System.Linq;
using Content.Client.Stylesheets.Palette;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Tools;
using Content.Shared.Tools;
using Content.Shared.Tools.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Tools;

[UsedImplicitly]
public sealed partial class WH40KMultipleToolRadialBoundUserInterface : BoundUserInterface
{
    private static readonly Color SelectedOptionColor = Palettes.Green.Element.WithAlpha(128);
    private static readonly Color SelectedOptionHoverColor = Palettes.Green.HoveredElement.WithAlpha(128);

    [Dependency] private  IPrototypeManager _prototypeManager = default!;

    private SimpleRadialMenu? _menu;

    public WH40KMultipleToolRadialBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        if (!EntMan.TryGetComponent<MultipleToolComponent>(Owner, out var multiple))
        {
            Close();
            return;
        }

        _menu = this.CreateWindow<SimpleRadialMenu>();
        _menu.Track(Owner);
        _menu.SetButtons(CreateButtons(multiple));
        _menu.OpenOverMouseScreenPosition();
    }

    private IEnumerable<RadialMenuActionOptionBase> CreateButtons(MultipleToolComponent multiple)
    {
        var models = new RadialMenuActionOptionBase[multiple.Entries.Length];

        for (var i = 0; i < multiple.Entries.Length; i++)
        {
            var entry = multiple.Entries[i];
            var index = (uint) i;
            var selected = multiple.CurrentEntry == index;

            models[i] = new RadialMenuActionOption<uint>(HandleRadialMenuClick, index)
            {
                IconSpecifier = GetIcon(entry),
                ToolTip = GetTooltip(entry),
                BackgroundColor = selected ? SelectedOptionColor : null,
                HoverBackgroundColor = selected ? SelectedOptionHoverColor : null
            };
        }

        return models;
    }

    private RadialMenuIconSpecifier? GetIcon(MultipleToolComponent.ToolEntry entry)
    {
        if (entry.Sprite != null)
            return RadialMenuIconSpecifier.With(entry.Sprite);

        var qualityId = entry.Behavior.FirstOrDefault();
        if (qualityId == null || !_prototypeManager.TryIndex<ToolQualityPrototype>(qualityId, out var quality))
            return null;

        return RadialMenuIconSpecifier.With(quality.Icon);
    }

    private string GetTooltip(MultipleToolComponent.ToolEntry entry)
    {
        var qualityId = entry.Behavior.FirstOrDefault();
        if (qualityId == null || !_prototypeManager.TryIndex<ToolQualityPrototype>(qualityId, out var quality))
            return Loc.GetString("multiple-tool-component-no-behavior");

        return Loc.GetString(quality.Name);
    }

    private void HandleRadialMenuClick(uint entry)
    {
        SendMessage(new WH40KMultipleToolRadialSelectMessage(entry));
    }
}
