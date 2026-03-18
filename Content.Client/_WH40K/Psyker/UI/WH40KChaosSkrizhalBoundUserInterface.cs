using System;
using Content.Shared._WH40K.Psyker;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Log;

namespace Content.Client._WH40K.Psyker.UI;

[UsedImplicitly]
public sealed class WH40KChaosSkrizhalBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("wh40k.psyker.skrizhal.client");
    private WH40KChaosSkrizhalPatronSelectorWindow? _window;

    protected override void Open()
    {
        base.Open();

        if (_window != null)
        {
            Sawmill.Warning($"[trace] Selector BUI duplicate open suppressed: owner={Owner}, key={UiKey}.");
            return;
        }

        try
        {
            _window = this.CreateWindow<WH40KChaosSkrizhalPatronSelectorWindow>();
            _window.PatronSelected += OnPatronSelected;
            Sawmill.Info($"[trace] Selector BUI open: owner={Owner}, key={UiKey}.");
        }
        catch (Exception e)
        {
            Sawmill.Error($"[trace] Selector BUI open failed: owner={Owner}, key={UiKey}, error={e}");
            throw;
        }
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null)
        {
            Sawmill.Warning($"[trace] Selector BUI UpdateState skipped: owner={Owner}, key={UiKey}, reason=window-null, state={state.GetType().Name}.");
            return;
        }

        if (state is not WH40KChaosSkrizhalPatronSelectorBuiState cast)
        {
            Sawmill.Warning($"[trace] Selector BUI UpdateState skipped: owner={Owner}, key={UiKey}, stateType={state.GetType().Name}.");
            return;
        }

        Sawmill.Info($"[trace] Selector BUI UpdateState: owner={Owner}, key={UiKey}, locked={cast.SelectionLocked}, patron={cast.CurrentPatron}.");
        _window.ApplyState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _window != null)
        {
            _window.PatronSelected -= OnPatronSelected;
            Sawmill.Info($"[trace] Selector BUI dispose: owner={Owner}, key={UiKey}.");
            _window = null;
        }

        base.Dispose(disposing);
    }

    private void OnPatronSelected(WH40KChaosPatron patron)
    {
        Sawmill.Info($"[trace] Selector BUI click: owner={Owner}, key={UiKey}, patron={patron}.");
        SendMessage(new WH40KChaosSkrizhalSelectPatronMessage(patron));
    }
}
