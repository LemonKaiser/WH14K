using System;
using Content.Shared._WH40K.Psyker;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Log;

namespace Content.Client._WH40K.Psyker.UI;

[UsedImplicitly]
public sealed class WH40KChaosSkrizhalBranchBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("wh40k.psyker.skrizhal.client");
    private WH40KChaosSkrizhalBranchWindow? _window;

    protected override void Open()
    {
        base.Open();

        if (_window != null)
        {
            Sawmill.Warning($"[trace] Branch BUI duplicate open suppressed: owner={Owner}, key={UiKey}.");
            return;
        }

        try
        {
            _window = this.CreateWindow<WH40KChaosSkrizhalBranchWindow>();
            _window.SelectPrimaryRequested += OnSelectPrimaryRequested;
            _window.UnlockRequested += OnUnlockRequested;
            _window.UpgradeTierRequested += OnUpgradeTierRequested;
            _window.UnlockExRequested += OnUnlockExRequested;
            Sawmill.Info($"[trace] Branch BUI open: owner={Owner}, key={UiKey}.");
        }
        catch (Exception e)
        {
            Sawmill.Error($"[trace] Branch BUI open failed: owner={Owner}, key={UiKey}, error={e}");
            throw;
        }
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null)
        {
            Sawmill.Warning($"[trace] Branch BUI UpdateState skipped: owner={Owner}, key={UiKey}, reason=window-null, state={state.GetType().Name}.");
            return;
        }

        if (state is not WH40KChaosSkrizhalPatronBranchBuiState cast)
        {
            Sawmill.Warning($"[trace] Branch BUI UpdateState skipped: owner={Owner}, key={UiKey}, stateType={state.GetType().Name}.");
            return;
        }

        Sawmill.Info(
            $"[trace] Branch BUI UpdateState: owner={Owner}, key={UiKey}, patron={cast.Patron}, level={cast.Level}, points={cast.DevelopmentPoints}, primary={cast.PrimaryGiftSlot}.");
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
            Sawmill.Info($"[trace] Branch BUI dispose: owner={Owner}, key={UiKey}.");
            _window = null;
        }

        base.Dispose(disposing);
    }

    private void OnSelectPrimaryRequested(int slot)
    {
        Sawmill.Info($"[trace] Branch BUI click primary: owner={Owner}, key={UiKey}, slot={slot}.");
        SendMessage(new WH40KChaosSkrizhalSelectPrimaryGiftMessage(slot));
    }

    private void OnUnlockRequested(int slot)
    {
        Sawmill.Info($"[trace] Branch BUI click unlock: owner={Owner}, key={UiKey}, slot={slot}.");
        SendMessage(new WH40KChaosSkrizhalUnlockGiftMessage(slot));
    }

    private void OnUpgradeTierRequested(int slot, WH40KChaosGiftUpgradePath path, int tier)
    {
        Sawmill.Info($"[trace] Branch BUI click upgrade-tier: owner={Owner}, key={UiKey}, slot={slot}, path={path}, tier={tier}.");
        SendMessage(new WH40KChaosSkrizhalUpgradeTierMessage(slot, path, tier));
    }

    private void OnUnlockExRequested(int slot)
    {
        Sawmill.Info($"[trace] Branch BUI click unlock-ex: owner={Owner}, key={UiKey}, slot={slot}.");
        SendMessage(new WH40KChaosSkrizhalUnlockExMessage(slot));
    }
}
