using System;
using System.Linq;
using Robust.Client;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Client.MainMenu;
using Robust.Client.State;

namespace Content.Client.Launcher
{
    public sealed class LauncherConnecting : Robust.Client.State.State
    {
        [Dependency] private readonly IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private readonly IClientNetManager _clientNetManager = default!;
        [Dependency] private readonly IGameController _gameController = default!;
        [Dependency] private readonly IBaseClient _baseClient = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IClipboardManager _clipboard = default!;
        [Dependency] private readonly ILogManager _logManager = default!;
        [Dependency] private readonly ConnectingTargetManager _connectingTarget = default!;
        [Dependency] private readonly IStateManager _stateManager = default!;

        private LauncherConnectingGui? _control;
        private ISawmill _sawmill = default!;

        private Page _currentPage;
        private string? _connectFailReason;

        public string? Address => _gameController.LaunchState.Ss14Address
                                  ?? _gameController.LaunchState.ConnectAddress
                                  ?? _connectingTarget.Address;

        public bool UsesManualConnectTarget => !_gameController.LaunchState.FromLauncher && _connectingTarget.HasManualTarget;

        public string? ConnectFailReason
        {
            get => _connectFailReason;
            private set
            {
                _connectFailReason = value;
                ConnectFailReasonChanged?.Invoke(value);
            }
        }

        public string? LastDisconnectReason => _baseClient.LastDisconnectReason;

        public Page CurrentPage
        {
            get => _currentPage;
            private set
            {
                _currentPage = value;
                PageChanged?.Invoke(value);
            }
        }

        public ClientConnectionState ConnectionState => _clientNetManager.ClientConnectState;

        public event Action<Page>? PageChanged;
        public event Action<string?>? ConnectFailReasonChanged;
        public event Action<ClientConnectionState>? ConnectionStateChanged;
        public event Action<NetConnectFailArgs>? ConnectFailed;

        protected override void Startup()
        {
            foreach (var staleControl in _userInterfaceManager.StateRoot.Children.OfType<LauncherConnectingGui>().ToArray())
            {
                staleControl.Orphan();
            }

            _control = new LauncherConnectingGui(this, _random, _prototypeManager, _cfg, _clipboard);

            _sawmill = _logManager.GetSawmill("launcher-ui");

            _userInterfaceManager.StateRoot.AddChild(_control);

            _clientNetManager.ConnectFailed += OnConnectFailed;
            _clientNetManager.ClientConnectStateChanged += OnConnectStateChanged;

            CurrentPage = Page.Connecting;
        }

        protected override void Shutdown()
        {
            _control?.Orphan();
            _control = null;

            _clientNetManager.ConnectFailed -= OnConnectFailed;
            _clientNetManager.ClientConnectStateChanged -= OnConnectStateChanged;
            _connectingTarget.Clear();
        }

        private void OnConnectFailed(object? _, NetConnectFailArgs args)
        {
            if (args.RedialFlag)
            {
                // We've just *attempted* to connect and we've been told we need to redial, so do it.
                // Result deliberately discarded.
                Redial();
            }
            ConnectFailReason = args.Reason;
            CurrentPage = Page.ConnectFailed;
            ConnectFailed?.Invoke(args);
        }

        private void OnConnectStateChanged(ClientConnectionState state)
        {
            ConnectionStateChanged?.Invoke(state);
        }

        public void RetryConnect()
        {
            if (TryGetConnectTarget(out var host, out var port))
            {
                if (_clientNetManager.ClientConnectState != ClientConnectionState.NotConnecting ||
                    _baseClient.RunLevel == ClientRunLevel.Connecting)
                {
                    _baseClient.DisconnectFromServer("Retrying failed connection");
                }

                ConnectFailReason = null;
                _baseClient.ConnectToServer(host, port);
                CurrentPage = Page.Connecting;
                return;
            }

            _sawmill.Warning("RetryConnect requested, but no reconnect target could be resolved.");
        }

        public bool Redial()
        {
            try
            {
                if (_gameController.LaunchState.Ss14Address != null)
                {
                    _gameController.Redial(_gameController.LaunchState.Ss14Address);
                    return true;
                }
                else if (TryGetConnectTarget(out var host, out var port))
                {
                    _baseClient.ConnectToServer(host, port);
                    return true;
                }
                else
                {
                    _sawmill.Info($"Redial not possible, no Ss14Address");
                }
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Redial exception: {ex}");
            }
            return false;
        }

        public void Leave()
        {
            if (UsesManualConnectTarget)
            {
                if (_baseClient.RunLevel == ClientRunLevel.Connecting)
                    _baseClient.DisconnectFromServer("Manual direct-connect cancelled");

                _stateManager.RequestStateChange<MainScreen>();
                return;
            }

            _gameController.Shutdown("Exit button pressed");
        }

        public void SetDisconnected()
        {
            CurrentPage = Page.Disconnected;
        }

        public enum Page : byte
        {
            Connecting,
            ConnectFailed,
            Disconnected,
        }

        private bool TryGetConnectTarget(out string host, out ushort port)
        {
            if (_connectingTarget.HasManualTarget && _connectingTarget.Host != null && _connectingTarget.Port != null)
            {
                host = _connectingTarget.Host;
                port = _connectingTarget.Port.Value;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(_gameController.LaunchState.Ss14Address))
            {
                try
                {
                    ConnectingAddressParser.ParseAddress(_gameController.LaunchState.Ss14Address, _baseClient.DefaultPort, out host, out port);
                    return true;
                }
                catch (ArgumentException ex)
                {
                    _sawmill.Warning($"Unable to parse SS14 address '{_gameController.LaunchState.Ss14Address}' for reconnect: {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(_gameController.LaunchState.ConnectAddress))
            {
                try
                {
                    ConnectingAddressParser.ParseAddress(_gameController.LaunchState.ConnectAddress, _baseClient.DefaultPort, out host, out port);
                    return true;
                }
                catch (ArgumentException ex)
                {
                    _sawmill.Warning($"Unable to parse connect address '{_gameController.LaunchState.ConnectAddress}' for reconnect: {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(Address))
            {
                try
                {
                    ConnectingAddressParser.ParseAddress(Address, _baseClient.DefaultPort, out host, out port);
                    return true;
                }
                catch (ArgumentException ex)
                {
                    _sawmill.Warning($"Unable to parse fallback address '{Address}' for reconnect: {ex.Message}");
                }
            }

            host = string.Empty;
            port = 0;
            return false;
        }
    }
}
