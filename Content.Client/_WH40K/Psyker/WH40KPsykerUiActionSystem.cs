using Content.Client._WH40K.Psyker.UI;
using Content.Shared._WH40K.Psyker;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Psyker;

/// <summary>
/// Client-side bridge from action button to psyker progression window toggle.
/// </summary>
public sealed partial class WH40KPsykerUiActionSystem : EntitySystem
{
    [Dependency] private  IPlayerManager _player = default!;
    [Dependency] private  IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerRoleComponent, WH40KPsykerToggleProgressionUiActionEvent>(OnTogglePsykerUi);
    }

    private void OnTogglePsykerUi(
        Entity<WH40KPsykerRoleComponent> ent,
        ref WH40KPsykerToggleProgressionUiActionEvent args)
    {
        if (_player.LocalEntity != ent.Owner)
            return;

        args.Handled = true;
        _ui.GetUIController<WH40KWarpUiController>().TogglePsykerWindow();
    }
}
