using System;
using Content.Client._WH40K.Squads.UI;
using Content.Shared._WH40K.Squads;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Squads;

[UsedImplicitly]
public sealed class WH40KSquadBoundUserInterface : BoundUserInterface
{
    private WH40KSquadWindow? _window;

    public WH40KSquadBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WH40KSquadWindow>();
        _window.CreatePressed += () => SendMessage(new WH40KSquadCreateMessage());
        _window.DisbandPressed += () => SendMessage(new WH40KSquadDisbandMessage());
        _window.AssignPressed += target => SendMessage(new WH40KSquadAssignMessage(target));
        _window.RemovePressed += slot => SendMessage(new WH40KSquadRemoveMessage(slot));
        _window.RefreshPressed += () => SendMessage(new WH40KSquadRefreshMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not WH40KSquadBuiState squadState)
            return;

        _window.UpdateState(squadState);
    }
}
