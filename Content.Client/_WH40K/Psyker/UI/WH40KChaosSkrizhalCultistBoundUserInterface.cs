using System;
using Content.Shared._WH40K.Psyker;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Psyker.UI;

[UsedImplicitly]
public sealed class WH40KChaosSkrizhalCultistBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WH40KChaosSkrizhalCultistWindow? _window;

    protected override void Open()
    {
        base.Open();

        if (_window != null)
            return;

        _window = this.CreateWindow<WH40KChaosSkrizhalCultistWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null)
            return;

        if (state is not WH40KChaosSkrizhalCultistBuiState cast)
            return;

        _window.ApplyState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _window != null)
        {
            _window = null;
        }

        base.Dispose(disposing);
    }
}
