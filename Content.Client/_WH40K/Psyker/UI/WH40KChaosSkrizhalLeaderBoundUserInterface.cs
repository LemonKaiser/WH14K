using System;
using Content.Shared._WH40K.Psyker;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Psyker.UI;

[UsedImplicitly]
public sealed class WH40KChaosSkrizhalLeaderBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WH40KChaosSkrizhalLeaderWindow? _window;

    protected override void Open()
    {
        base.Open();

        if (_window != null)
            return;

        _window = this.CreateWindow<WH40KChaosSkrizhalLeaderWindow>();
        _window.SelectPrimaryRequested += OnSelectPrimaryRequested;
        _window.UnlockRequested += OnUnlockRequested;
        _window.UpgradeTierRequested += OnUpgradeTierRequested;
        _window.UnlockExRequested += OnUnlockExRequested;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null)
            return;

        if (state is not WH40KChaosSkrizhalPatronBranchBuiState cast)
            return;

        _window.ApplyState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _window != null)
        {
            _window.SelectPrimaryRequested -= OnSelectPrimaryRequested;
            _window.UnlockRequested -= OnUnlockRequested;
            _window.UpgradeTierRequested -= OnUpgradeTierRequested;
            _window.UnlockExRequested -= OnUnlockExRequested;
            _window = null;
        }

        base.Dispose(disposing);
    }

    private void OnSelectPrimaryRequested(int slot)
    {
        SendMessage(new WH40KChaosSkrizhalSelectPrimaryGiftMessage(slot));
    }

    private void OnUnlockRequested(int slot)
    {
        SendMessage(new WH40KChaosSkrizhalUnlockGiftMessage(slot));
    }

    private void OnUpgradeTierRequested(int slot, WH40KChaosGiftUpgradePath path, int tier)
    {
        SendMessage(new WH40KChaosSkrizhalUpgradeTierMessage(slot, path, tier));
    }

    private void OnUnlockExRequested(int slot)
    {
        SendMessage(new WH40KChaosSkrizhalUnlockExMessage(slot));
    }
}
