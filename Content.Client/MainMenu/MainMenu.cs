using Content.Client.Launcher;
using Content.Client.MainMenu.UI;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Robust.Client;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;
using UsernameHelpers = Robust.Shared.AuthLib.UsernameHelpers;

namespace Content.Client.MainMenu
{
    /// <summary>
    ///     Main menu screen that is the first screen to be displayed when the game starts.
    /// </summary>
    // Instantiated dynamically through the StateManager, Dependencies will be resolved.
    public sealed class MainScreen : Robust.Client.State.State
    {
        [Dependency] private readonly IBaseClient _client = default!;
        [Dependency] private readonly IConfigurationManager _configurationManager = default!;
        [Dependency] private readonly IGameController _controllerProxy = default!;
        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private readonly ILogManager _logManager = default!;
        [Dependency] private readonly IStateManager _stateManager = default!;
        [Dependency] private readonly ConnectingTargetManager _connectingTarget = default!;

        private ISawmill _sawmill = default!;

        private MainMenuControl _mainMenuControl = default!;
        private bool _isConnecting;
        /// <inheritdoc />
        protected override void Startup()
        {
            _sawmill = _logManager.GetSawmill("mainmenu");

            _mainMenuControl = new MainMenuControl(_resourceCache, _configurationManager);
            _userInterfaceManager.StateRoot.AddChild(_mainMenuControl);

            _mainMenuControl.QuitButton.OnPressed += QuitButtonPressed;
            _mainMenuControl.OptionsButton.OnPressed += OptionsButtonPressed;
            _mainMenuControl.DirectConnectButton.OnPressed += DirectConnectButtonPressed;
            _mainMenuControl.AddressBox.OnTextEntered += AddressBoxEntered;
            _mainMenuControl.ChangelogButton.OnPressed += ChangelogButtonPressed;

            _client.RunLevelChanged += RunLevelChanged;
        }

        /// <inheritdoc />
        protected override void Shutdown()
        {
            _client.RunLevelChanged -= RunLevelChanged;

            _mainMenuControl.Dispose();
        }

        private void ChangelogButtonPressed(BaseButton.ButtonEventArgs args)
        {
            _userInterfaceManager.GetUIController<ChangelogUIController>().ToggleWindow();
        }

        private void OptionsButtonPressed(BaseButton.ButtonEventArgs args)
        {
            _userInterfaceManager.GetUIController<OptionsUIController>().ToggleWindow();
        }

        private void QuitButtonPressed(BaseButton.ButtonEventArgs args)
        {
            _controllerProxy.Shutdown();
        }

        private void DirectConnectButtonPressed(BaseButton.ButtonEventArgs args)
        {
            var input = _mainMenuControl.AddressBox;
            TryConnect(input.Text);
        }

        private void AddressBoxEntered(LineEdit.LineEditEventArgs args)
        {
            if (_isConnecting)
            {
                return;
            }

            TryConnect(args.Text);
        }

        private void TryConnect(string address)
        {
            var inputName = _mainMenuControl.UsernameBox.Text.Trim();
            if (!UsernameHelpers.IsNameValid(inputName, out var reason))
            {
                var invalidReason = Loc.GetString(reason.ToText());
                _userInterfaceManager.Popup(
                    Loc.GetString("main-menu-invalid-username-with-reason", ("invalidReason", invalidReason)),
                    Loc.GetString("main-menu-invalid-username"));
                return;
            }

            var configName = _configurationManager.GetCVar(CVars.PlayerName);
            if (_mainMenuControl.UsernameBox.Text != configName)
            {
                _configurationManager.SetCVar(CVars.PlayerName, inputName);
                _configurationManager.SaveToFile();
            }

            _setConnectingState(true);
            try
            {
                ConnectingAddressParser.ParseAddress(address, _client.DefaultPort, out var host, out var port);
                _connectingTarget.SetManualTarget(address, host, port);
                _stateManager.RequestStateChange<LauncherConnecting>();
                if (_stateManager.CurrentState is LauncherConnecting state)
                    state.RetryConnect();
            }
            catch (ArgumentException e)
            {
                _userInterfaceManager.Popup($"Unable to connect: {e.Message}", "Connection error.");
                _sawmill.Warning(e.ToString());
                _connectingTarget.Clear();
                _setConnectingState(false);
            }
        }

        private void RunLevelChanged(object? obj, RunLevelChangedEventArgs args)
        {
            switch (args.NewLevel)
            {
                case ClientRunLevel.Connecting:
                    _setConnectingState(true);
                    break;
                case ClientRunLevel.Initialize:
                    _setConnectingState(false);
                    break;
            }
        }

        private void _setConnectingState(bool state)
        {
            _isConnecting = state;
            _mainMenuControl.DirectConnectButton.Disabled = state;
        }
    }
}
