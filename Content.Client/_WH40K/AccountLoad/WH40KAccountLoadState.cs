using System;
using Content.Shared._WH40K.AccountLoad;
using Robust.Client.State;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.AccountLoad;

public sealed partial class WH40KAccountLoadState : State
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    private WH40KAccountLoadControl? _control;

    protected override void Startup()
    {
        _control = new WH40KAccountLoadControl();
        _ui.StateRoot.AddChild(_control);
    }

    protected override void Shutdown()
    {
        _control?.Parent?.RemoveChild(_control);
        _control = null;
    }

    public void UpdateStatus(WH40KAccountLoadStatusEvent message)
    {
        if (_control == null)
            return;

        _control.Header.Text = Loc.GetString(message.TitleLocKey);
        _control.Stage.Text = Loc.GetString(message.StageLocKey);
        _control.ProgressBar.MinValue = 0f;
        _control.ProgressBar.MaxValue = 1f;
        _control.ProgressBar.Value = Math.Clamp(message.Progress, 0f, 1f);
        _control.ProgressText.Text = message.TotalSteps > 0
            ? Loc.GetString(
                "wh40k-account-load-progress",
                ("completed", message.CompletedSteps),
                ("total", message.TotalSteps))
            : Loc.GetString("wh40k-account-load-progress-indeterminate");
        _control.Detail.Text = message.DetailLocKey == null
            ? string.Empty
            : Loc.GetString(message.DetailLocKey);
    }
}
