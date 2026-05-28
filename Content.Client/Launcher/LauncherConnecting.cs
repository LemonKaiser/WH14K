using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.CCVar;
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
using Robust.Shared.Timing;

namespace Content.Client.Launcher
{
    public sealed partial class LauncherConnecting : Robust.Client.State.State
    {
        [Dependency] private  IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private  IClientNetManager _clientNetManager = default!;
        [Dependency] private  IGameController _gameController = default!;
        [Dependency] private  IBaseClient _baseClient = default!;
        [Dependency] private  IRobustRandom _random = default!;
        [Dependency] private  IPrototypeManager _prototypeManager = default!;
        [Dependency] private  IConfigurationManager _cfg = default!;
        [Dependency] private  IClipboardManager _clipboard = default!;
        [Dependency] private  ILogManager _logManager = default!;
        [Dependency] private  ConnectingTargetManager _connectingTarget = default!;
        [Dependency] private  IStateManager _stateManager = default!;
        [Dependency] private  IGameTiming _timing = default!;
        [Dependency] private  ExtendedDisconnectInformationManager _extendedDisconnectInformation = default!;

        private LauncherConnectingGui? _control;
        private ISawmill _sawmill = default!;

        private Page _currentPage;
        private string? _connectFailReason;
        private string? _activeConnectAddress;
        private string? _activeConnectHost;
        private ushort? _activeConnectPort;
        private INetStructuredReason? _lastFallbackReason;
        private bool _pendingAutomaticFallback;
        private TimeSpan _automaticFallbackAt;
        private TimeSpan _alternativeFallbackAvailableAt;
        private readonly HashSet<string> _automaticFallbackTriedTargets = new(StringComparer.OrdinalIgnoreCase);

        public string? Address => _activeConnectAddress
                                  ?? _gameController.LaunchState.Ss14Address
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
        public event Action<string?>? AddressChanged;
        public event Action? AlternativeConnectAvailabilityChanged;

        public bool CanUseAlternativeConnection =>
            _cfg.GetCVar(CCVars.WH40KConnectionFallbackButtonEnabled) &&
            IsFallbackEligible(_lastFallbackReason) &&
            TryGetAlternativeConnectTarget(skipAutomaticTriedTargets: false, out _);

        public TimeSpan AlternativeConnectCooldownRemaining
        {
            get
            {
                var remaining = _alternativeFallbackAvailableAt - _timing.RealTime;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

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
            _clientNetManager.Disconnect += OnDisconnected;
            _clientNetManager.ClientConnectStateChanged += OnConnectStateChanged;

            _activeConnectAddress = null;
            _activeConnectHost = null;
            _activeConnectPort = null;
            _lastFallbackReason = null;
            _pendingAutomaticFallback = false;
            _alternativeFallbackAvailableAt = TimeSpan.Zero;
            _automaticFallbackTriedTargets.Clear();

            CurrentPage = Page.Connecting;
            UseCachedConnectionEndIfAlreadyFailed();
        }

        protected override void Shutdown()
        {
            _control?.Orphan();
            _control = null;

            _clientNetManager.ConnectFailed -= OnConnectFailed;
            _clientNetManager.Disconnect -= OnDisconnected;
            _clientNetManager.ClientConnectStateChanged -= OnConnectStateChanged;
            _connectingTarget.Clear();
        }

        private void OnConnectFailed(object? _, NetConnectFailArgs args)
        {
            HandleConnectFailed(args, allowRedial: true);
        }

        private void HandleConnectFailed(NetConnectFailArgs args, bool allowRedial)
        {
            if (args.RedialFlag)
            {
                // We've just *attempted* to connect and we've been told we need to redial, so do it.
                // Result deliberately discarded.
                if (allowRedial)
                    Redial();
            }

            ConnectFailReason = args.Reason;
            CurrentPage = Page.ConnectFailed;
            ConnectFailed?.Invoke(args);
            RememberFallbackReason(args);
        }

        private void OnDisconnected(object? _, NetDisconnectedArgs args)
        {
            if (CurrentPage != Page.Connecting)
                return;

            HandleDisconnected(args);
        }

        private void HandleDisconnected(NetDisconnectedArgs args)
        {
            ConnectFailReason = null;
            CurrentPage = Page.Disconnected;
            RememberFallbackReason(args);
        }

        private void OnConnectStateChanged(ClientConnectionState state)
        {
            ConnectionStateChanged?.Invoke(state);

            if (state == ClientConnectionState.NotConnecting &&
                CurrentPage == Page.Connecting)
            {
                UseCachedConnectionEndIfAlreadyFailed();
            }
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

                ClearFallbackPrompt();
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
                var redialAddress = _activeConnectAddress ?? _gameController.LaunchState.Ss14Address;
                if (redialAddress != null && _gameController.LaunchState.FromLauncher)
                {
                    _gameController.Redial(redialAddress);
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
            RememberFallbackReason(_extendedDisconnectInformation.LastNetDisconnectedArgs);
        }

        public enum Page : byte
        {
            Connecting,
            ConnectFailed,
            Disconnected,
        }

        private bool TryGetConnectTarget(out string host, out ushort port)
        {
            if (!string.IsNullOrWhiteSpace(_activeConnectHost) && _activeConnectPort != null)
            {
                host = _activeConnectHost;
                port = _activeConnectPort.Value;
                return true;
            }

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

        private bool UseCachedConnectionEndIfAlreadyFailed()
        {
            if (_clientNetManager.ClientConnectState != ClientConnectionState.NotConnecting ||
                _baseClient.RunLevel >= ClientRunLevel.Connecting)
            {
                return false;
            }

            if (_extendedDisconnectInformation.LastNetConnectFailedArgs is { } connectFailed)
            {
                HandleConnectFailed(connectFailed, allowRedial: false);
                return true;
            }

            if (_extendedDisconnectInformation.LastNetDisconnectedArgs is { } disconnected)
            {
                HandleDisconnected(disconnected);
                return true;
            }

            return false;
        }

        public bool TryRunPendingAutomaticFallback()
        {
            if (!_pendingAutomaticFallback)
                return false;

            if (_timing.RealTime < _automaticFallbackAt)
                return false;

            if (_clientNetManager.ClientConnectState != ClientConnectionState.NotConnecting ||
                _baseClient.RunLevel >= ClientRunLevel.Connecting)
            {
                return false;
            }

            _pendingAutomaticFallback = false;
            return TryConnectAlternative(automatic: true);
        }

        public bool TryConnectAlternative(bool automatic)
        {
            if (_clientNetManager.ClientConnectState != ClientConnectionState.NotConnecting ||
                _baseClient.RunLevel >= ClientRunLevel.Connecting)
            {
                return false;
            }

            if (!IsFallbackEligible(_lastFallbackReason))
                return false;

            if (AlternativeConnectCooldownRemaining > TimeSpan.Zero)
                return false;

            if (!TryGetAlternativeConnectTarget(skipAutomaticTriedTargets: automatic, out var target))
                return false;

            if (automatic)
                _automaticFallbackTriedTargets.Add(target.Key);

            _activeConnectAddress = target.Address;
            _activeConnectHost = target.Host;
            _activeConnectPort = target.Port;
            AddressChanged?.Invoke(Address);

            ClearFallbackPrompt();
            ConnectFailReason = null;
            _sawmill.Info($"Connecting through alternate address '{target.Address}' (automatic={automatic}).");
            _baseClient.ConnectToServer(target.Host, target.Port);
            CurrentPage = Page.Connecting;
            return true;
        }

        private void RememberFallbackReason(INetStructuredReason? reason)
        {
            _lastFallbackReason = reason;
            _pendingAutomaticFallback = false;
            _alternativeFallbackAvailableAt = TimeSpan.Zero;

            if (reason != null &&
                IsFallbackEligible(reason) &&
                TryGetAlternativeConnectTarget(skipAutomaticTriedTargets: true, out _))
            {
                var reconnectDelay = GetReconnectCleanupDelay(reason);
                _alternativeFallbackAvailableAt = _timing.RealTime + TimeSpan.FromSeconds(reconnectDelay);
                if (reconnectDelay > 0f)
                    _sawmill.Info($"Delaying alternate connection for {reconnectDelay:0.#} seconds after disconnect to let the server release the old session.");

                if (_cfg.GetCVar(CCVars.WH40KConnectionFallbackAutomatic))
                {
                    var autoDelay = MathF.Max(0f, _cfg.GetCVar(CCVars.WH40KConnectionFallbackAutoDelaySeconds));
                    var delay = MathF.Max(autoDelay, reconnectDelay);
                    _automaticFallbackAt = _timing.RealTime + TimeSpan.FromSeconds(delay);
                    _pendingAutomaticFallback = true;
                }
            }

            AlternativeConnectAvailabilityChanged?.Invoke();
        }

        private void ClearFallbackPrompt()
        {
            _lastFallbackReason = null;
            _pendingAutomaticFallback = false;
            _alternativeFallbackAvailableAt = TimeSpan.Zero;
            AlternativeConnectAvailabilityChanged?.Invoke();
        }

        private float GetReconnectCleanupDelay(INetStructuredReason reason)
        {
            if (reason is not NetDisconnectedArgs)
                return 0f;

            return MathF.Max(0f, _cfg.GetCVar(CCVars.WH40KConnectionFallbackDisconnectDelaySeconds));
        }

        private bool IsFallbackEligible(INetStructuredReason? reason)
        {
            return _cfg.GetCVar(CCVars.WH40KConnectionFallbackEnabled) &&
                   reason != null &&
                   ConnectionFallbackHelper.IsNetworkFallbackEligible(reason.Reason, reason.RedialFlag);
        }

        private bool TryGetAlternativeConnectTarget(bool skipAutomaticTriedTargets, out ConnectionFallbackTarget target)
        {
            return ConnectionFallbackHelper.TryPickAlternative(
                Address,
                _cfg.GetCVar(CCVars.WH40KConnectionFallbackPrimaryAddresses),
                _cfg.GetCVar(CCVars.WH40KConnectionFallbackAlternateAddresses),
                _baseClient.DefaultPort,
                skipAutomaticTriedTargets ? _automaticFallbackTriedTargets : null,
                out target);
        }
    }
}
