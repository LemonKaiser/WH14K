using Content.Client.Stylesheets.Palette;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Oskvernitel;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;
using System.Linq;

namespace Content.Client._WH40K.Oskvernitel;

[UsedImplicitly]
public sealed class WH40KOskvernitelWeaponBoundUserInterface(EntityUid owner, Enum uiKey)
    : BoundUserInterface(owner, uiKey)
{
    private static readonly Color SelectedOptionColor = Palettes.Green.Element.WithAlpha(128);
    private static readonly Color SelectedOptionHoverColor = Palettes.Green.HoveredElement.WithAlpha(128);

    private SimpleRadialMenu? _menu;
    private WH40KOskvernitelWeaponBuiState? _state;

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SimpleRadialMenu>();

        if (_state != null)
            RefreshMenu(_state);

        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not WH40KOskvernitelWeaponBuiState cast)
            return;

        _state = cast;

        if (_menu != null)
            RefreshMenu(cast);
    }

    private void RefreshMenu(WH40KOskvernitelWeaponBuiState state)
    {
        _menu?.SetButtons(CreateButtons(state));
    }

    private IEnumerable<RadialMenuOptionBase> CreateButtons(WH40KOskvernitelWeaponBuiState state)
    {
        foreach (var entry in state.Entries.OrderBy(static entry => entry.Id))
        {
            var localizedName = Loc.TryGetString($"ent-{entry.PrototypeId}", out var prototypeName) &&
                                !string.IsNullOrWhiteSpace(prototypeName)
                ? prototypeName
                : Loc.GetString(entry.NameLocKey);

            yield return new RadialMenuActionOption<WH40KOskvernitelWeaponUiEntryId>(HandleRadialMenuClick, entry.Id)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(new SpriteSpecifier.EntityPrototype(entry.PrototypeId)),
                ToolTip = Loc.GetString(
                    "wh40k-oskvernitel-weapon-tooltip",
                    ("weapon", localizedName),
                    ("current", entry.CurrentAmmo),
                    ("max", entry.MaxAmmo)),
                BackgroundColor = entry.Selected ? SelectedOptionColor : null,
                HoverBackgroundColor = entry.Selected ? SelectedOptionHoverColor : null,
            };
        }
    }

    private void HandleRadialMenuClick(WH40KOskvernitelWeaponUiEntryId entry)
    {
        SendMessage(new WH40KOskvernitelWeaponSelectMessage(entry));
        Close();
    }
}
