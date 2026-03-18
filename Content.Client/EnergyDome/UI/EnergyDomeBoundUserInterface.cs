using Content.Shared.EnergyDome;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.EnergyDome.UI;

[UsedImplicitly]
public sealed class EnergyDomeBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private EnergyDomeWindow? _window;
    private EnergyDomeBuiState? _state;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<EnergyDomeWindow>();
        _window.TogglePressed += OnTogglePressed;
        _window.ModeChanged += mode => SendMessage(new EnergyDomeUiSetModeMessage(mode));
        _window.SizeChanged += size => SendMessage(new EnergyDomeUiSetSizeMessage(size));
        _window.ColorChanged += color => SendMessage(new EnergyDomeUiSetColorMessage(color));
        _window.WallSideChanged += side => SendMessage(new EnergyDomeUiSetWallSideMessage(side));
        _window.AutoProfileChanged += profile => SendMessage(new EnergyDomeUiSetAutoResponseProfileMessage(profile));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not EnergyDomeBuiState cast)
            return;

        _state = cast;
        _window?.ApplyState(cast);
    }

    private void OnTogglePressed()
    {
        if (_state == null)
            return;

        SendMessage(new EnergyDomeUiToggleMessage(!_state.GlobalEnabled));
    }
}
