using Content.Client.Audio;
using Content.Client.GameTicking.Managers;
using Content.Client.LateJoin;
using Content.Client._WH40K.LateJoin;
using Content.Client._WH40K.DiscordAuth;
using Content.Client.Ghost;
using Content.Client.Lobby.UI;
using Content.Client.Message;
using Content.Client.Playtime;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.Localization;
using Content.Client.Voting;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Prototypes;
using Content.Shared.Roles;
using Robust.Client;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Lobby
{
    public sealed class LobbyState : Robust.Client.State.State
    {
        [Dependency] private readonly IBaseClient _baseClient = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IClientConsoleHost _consoleHost = default!;
        [Dependency] private readonly IClyde _clyde = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly IVoteManager _voteManager = default!;
        [Dependency] private readonly ClientsidePlaytimeTrackingManager _playtimeTracking = default!;
        [Dependency] private readonly IPrototypeManager _protoMan = default!;

        private ClientGameTicker _gameTicker = default!;
        private ContentAudioSystem _contentAudioSystem = default!;
        private WH40KDiscordAuthSystem _discordAuth = default!;
        private GhostSystem _ghostSystem = default!;
        private WH40KFactionSystem _wh40kFactions = default!;
        private bool _pendingJoinOpen;
        private LateJoinGui? _lateJoinWindow;
        private WH40KFactionJoinGui? _factionJoinWindow;
        private LobbyBackgroundController? _backgroundController;

        protected override Type? LinkedScreenType { get; } = typeof(LobbyGui);
        public LobbyGui? Lobby;

        protected override void Startup()
        {
            if (_userInterfaceManager.ActiveScreen == null)
            {
                return;
            }

            Lobby = (LobbyGui) _userInterfaceManager.ActiveScreen;

            var chatController = _userInterfaceManager.GetUIController<ChatUIController>();
            _gameTicker = _entityManager.System<ClientGameTicker>();
            _contentAudioSystem = _entityManager.System<ContentAudioSystem>();
            _discordAuth = _entityManager.System<WH40KDiscordAuthSystem>();
            _ghostSystem = _entityManager.System<GhostSystem>();
            _wh40kFactions = _entityManager.System<WH40KFactionSystem>();
            _discordAuth.EnsureSnapshot();
            _contentAudioSystem.LobbySoundtrackChanged += UpdateLobbySoundtrackInfo;
            _ghostSystem.GhostRoleCountUpdated += OnGhostRoleCountUpdated;
            _wh40kFactions.FactionsUpdated += OnWh40kFactionsUpdated;
            _pendingJoinOpen = false;

            chatController.SetMainChat(true);

            _voteManager.SetPopupContainer(Lobby.VoteContainer);
            LayoutContainer.SetAnchorPreset(Lobby, LayoutContainer.LayoutPreset.Wide);

            var lobbyNameCvar = _cfg.GetCVar(CCVars.ServerLobbyName);
            var serverName = _baseClient.GameInfo?.ServerName ?? string.Empty;

            Lobby.ServerName.Text = string.IsNullOrEmpty(lobbyNameCvar)
                ? Loc.GetString("ui-lobby-title", ("serverName", serverName))
                : lobbyNameCvar;

            var width = _cfg.GetCVar(CCVars.ServerLobbyRightPanelWidth);
            Lobby.RightSide.SetWidth = width;

            UpdateLobbyUi();
            _backgroundController = new LobbyBackgroundController(
                _cfg,
                _protoMan,
                _resourceCache,
                _clyde,
                _gameTiming,
                () => _gameTicker.LobbyBackground ?? string.Empty);
            _backgroundController.Startup(Lobby);

            Lobby.CharacterPreview.CharacterSetupButton.OnPressed += OnSetupPressed;
            Lobby.ReadyButton.OnPressed += OnReadyPressed;
            Lobby.ReadyButton.OnToggled += OnReadyToggled;
            Lobby.GhostRolesButton.OnPressed += OnGhostRolesPressed;

            _gameTicker.InfoBlobUpdated += UpdateLobbyUi;
            _gameTicker.LobbyStatusUpdated += LobbyStatusUpdated;
            _gameTicker.LobbyLateJoinStatusUpdated += LobbyLateJoinStatusUpdated;

            _userInterfaceManager.GetUIController<LocalizationUIController>().RefreshCurrentCulture();
        }

        protected override void Shutdown()
        {
            var chatController = _userInterfaceManager.GetUIController<ChatUIController>();
            chatController.SetMainChat(false);
            _gameTicker.InfoBlobUpdated -= UpdateLobbyUi;
            _gameTicker.LobbyStatusUpdated -= LobbyStatusUpdated;
            _gameTicker.LobbyLateJoinStatusUpdated -= LobbyLateJoinStatusUpdated;
            _contentAudioSystem.LobbySoundtrackChanged -= UpdateLobbySoundtrackInfo;
            _ghostSystem.GhostRoleCountUpdated -= OnGhostRoleCountUpdated;
            _wh40kFactions.FactionsUpdated -= OnWh40kFactionsUpdated;
            _pendingJoinOpen = false;
            CloseJoinWindows();
            _backgroundController?.Shutdown();
            _backgroundController = null;

            _voteManager.ClearPopupContainer();

            Lobby!.CharacterPreview.CharacterSetupButton.OnPressed -= OnSetupPressed;
            Lobby!.ReadyButton.OnPressed -= OnReadyPressed;
            Lobby!.ReadyButton.OnToggled -= OnReadyToggled;
            Lobby!.GhostRolesButton.OnPressed -= OnGhostRolesPressed;

            Lobby = null;
        }

        public void SwitchState(LobbyGui.LobbyGuiState state)
        {
            // Yeah I hate this but LobbyState contains all the badness for now.
            Lobby?.SwitchState(state);
        }

        private void OnSetupPressed(BaseButton.ButtonEventArgs args)
        {
            SetReady(false);
            Lobby?.SwitchState(LobbyGui.LobbyGuiState.CharacterSetup);
        }

        private void OnReadyPressed(BaseButton.ButtonEventArgs args)
        {
            if (!_gameTicker.IsGameStarted)
            {
                return;
            }

            if (_factionJoinWindow?.IsOpen == true)
            {
                _factionJoinWindow.MoveToFront();
                return;
            }

            if (_lateJoinWindow?.IsOpen == true)
            {
                _lateJoinWindow.MoveToFront();
                return;
            }

            if (_pendingJoinOpen)
                return;

            if (_wh40kFactions.TryGetCachedFactions(out var factions))
            {
                OpenJoinWindow(factions);
                return;
            }

            _pendingJoinOpen = true;
            _wh40kFactions.RequestFactions(force: true);
        }

        private void OnGhostRolesPressed(BaseButton.ButtonEventArgs args)
        {
            if (!_gameTicker.IsGameStarted)
                return;

            _ghostSystem.OpenGhostRoles();
        }

        private void OnGhostRoleCountUpdated(Content.Shared.Ghost.GhostUpdateGhostRoleCountEvent ev)
        {
            UpdateLobbyUi();
        }

        private void OnReadyToggled(BaseButton.ButtonToggledEventArgs args)
        {
            SetReady(args.Pressed);
        }

        public override void FrameUpdate(FrameEventArgs e)
        {
            _backgroundController?.FrameUpdate(e.DeltaSeconds);
            RefreshTimingText();
        }

        private void RefreshTimingText()
        {
            if (_gameTicker.IsGameStarted)
            {
                Lobby!.StartTime.Text = string.Empty;
                var roundTime = _gameTiming.CurTime.Subtract(_gameTicker.RoundStartTimeSpan);
                Lobby!.StationTime.Text = Loc.GetString("lobby-state-player-status-round-time", ("hours", roundTime.Hours), ("minutes", roundTime.Minutes));
                return;
            }

            Lobby!.StationTime.Text = Loc.GetString("lobby-state-player-status-round-not-started");
            string text;

            if (_gameTicker.Paused)
            {
                text = Loc.GetString("lobby-state-paused");
            }
            else if (_gameTicker.StartTime < _gameTiming.CurTime)
            {
                Lobby!.StartTime.Text = Loc.GetString("lobby-state-soon");
                return;
            }
            else
            {
                var difference = _gameTicker.StartTime - _gameTiming.CurTime;
                var seconds = difference.TotalSeconds;
                if (seconds < 0)
                {
                    text = Loc.GetString(seconds < -5 ? "lobby-state-right-now-question" : "lobby-state-right-now-confirmation");
                }
                else if (difference.TotalHours >= 1)
                {
                    text = $"{Math.Floor(difference.TotalHours)}:{difference.Minutes:D2}:{difference.Seconds:D2}";
                }
                else
                {
                    text = $"{difference.Minutes}:{difference.Seconds:D2}";
                }
            }

            Lobby!.StartTime.Text = Loc.GetString("lobby-state-round-start-countdown-text", ("timeLeft", text));
        }

        private void LobbyStatusUpdated()
        {
            _backgroundController?.RefreshBackground();
            UpdateLobbyUi();

            if (_gameTicker.IsGameStarted)
            {
                _wh40kFactions.RequestFactions(force: true);
            }
            else
            {
                _pendingJoinOpen = false;
            }
        }

        private void LobbyLateJoinStatusUpdated()
        {
            Lobby!.ReadyButton.Disabled = _gameTicker.DisallowedLateJoin;
        }

        public void RefreshLocalization()
        {
            if (Lobby == null)
                return;

            var lobbyNameCvar = _cfg.GetCVar(CCVars.ServerLobbyName);
            var serverName = _baseClient.GameInfo?.ServerName ?? string.Empty;

            Lobby.ServerName.Text = string.IsNullOrEmpty(lobbyNameCvar)
                ? Loc.GetString("ui-lobby-title", ("serverName", serverName))
                : lobbyNameCvar;

            Lobby.ObserveButton.Text = Loc.GetString("ui-lobby-observe-button");
            Lobby.OptionsButton.Text = Loc.GetString("ui-lobby-options-button");
            Lobby.LeaveButton.Text = Loc.GetString("ui-lobby-leave-button");
            Lobby.LobbySong.SetMarkup(Loc.GetString("lobby-state-song-no-song-text"));

            _userInterfaceManager.GetUIController<LobbyUIController>().RefreshLocalization();
            _backgroundController?.RefreshBackground();
            UpdateLobbyUi();
            RefreshTimingText();
            _gameTicker.RequestLobbyInfoRefresh();
            LobbyLateJoinStatusUpdated();
            void RefreshTrack(LobbySoundtrackChangedEvent ev) => UpdateLobbySoundtrackInfo(ev);
            _contentAudioSystem.LobbySoundtrackChanged += RefreshTrack;
            _contentAudioSystem.LobbySoundtrackChanged -= RefreshTrack;
        }

        private void UpdateLobbyUi()
        {
            var availableGhostRoles = _ghostSystem.AvailableGhostRoleCount;
            Lobby!.GhostRolesButton.Text = Loc.GetString("ghost-gui-ghost-roles-button", ("count", availableGhostRoles));
            Lobby.GhostRolesButton.Visible = _gameTicker.IsGameStarted;
            Lobby.GhostRolesButton.Disabled = !_gameTicker.IsGameStarted || availableGhostRoles <= 0;

            if (_gameTicker.IsGameStarted)
            {
                Lobby!.ReadyButton.Text = Loc.GetString("lobby-state-ready-button-join-state");
                Lobby!.ReadyButton.ToggleMode = false;
                Lobby!.ReadyButton.Pressed = false;
                Lobby!.ReadyButton.Disabled = _gameTicker.DisallowedLateJoin;
                Lobby!.ObserveButton.Disabled = false;
            }
            else
            {
                Lobby!.StartTime.Text = string.Empty;
                Lobby!.ReadyButton.Pressed = _gameTicker.AreWeReady;
                Lobby!.ReadyButton.Text = Loc.GetString(Lobby!.ReadyButton.Pressed ? "lobby-state-player-status-ready": "lobby-state-player-status-not-ready");
                Lobby!.ReadyButton.ToggleMode = true;
                Lobby!.ReadyButton.Disabled = false;
                Lobby!.ObserveButton.Disabled = true;
            }

            if (_gameTicker.ServerInfoBlob != null)
            {
                Lobby!.ServerInfo.SetInfoBlob(_gameTicker.ServerInfoBlob);
            }

            var minutesToday = _playtimeTracking.PlaytimeMinutesToday;
            if (minutesToday > 60)
            {
                Lobby!.PlaytimeComment.Visible = true;

                var hoursToday = Math.Round(minutesToday / 60f, 1);

                var chosenString = minutesToday switch
                {
                    < 180 => "lobby-state-playtime-comment-normal",
                    < 360 => "lobby-state-playtime-comment-concerning",
                    < 720 => "lobby-state-playtime-comment-grasstouchless",
                    _ => "lobby-state-playtime-comment-selfdestructive"
                };

                Lobby.PlaytimeComment.SetMarkup(Loc.GetString(chosenString, ("hours", hoursToday)));
            }
            else
                Lobby!.PlaytimeComment.Visible = false;
        }

        private void UpdateLobbySoundtrackInfo(LobbySoundtrackChangedEvent ev)
        {
            if (ev.SoundtrackFilename == null)
            {
                Lobby!.LobbySong.SetMarkup(Loc.GetString("lobby-state-song-no-song-text"));
            }
            else if (
                ev.SoundtrackFilename != null
                && _resourceCache.TryGetResource<AudioResource>(ev.SoundtrackFilename, out var lobbySongResource)
                )
            {
                var lobbyStream = lobbySongResource.AudioStream;

                var title = string.IsNullOrEmpty(lobbyStream.Title)
                    ? Loc.GetString("lobby-state-song-unknown-title")
                    : lobbyStream.Title;

                var artist = string.IsNullOrEmpty(lobbyStream.Artist)
                    ? Loc.GetString("lobby-state-song-unknown-artist")
                    : lobbyStream.Artist;

                var markup = Loc.GetString("lobby-state-song-text",
                    ("songTitle", title),
                    ("songArtist", artist));

                Lobby!.LobbySong.SetMarkup(markup);
            }
        }

        private void SetReady(bool newReady)
        {
            if (_gameTicker.IsGameStarted)
            {
                return;
            }

            _consoleHost.ExecuteCommand($"toggleready {newReady}");
        }

        private void OnWh40kFactionsUpdated(IReadOnlyList<Content.Shared._WH40K.LateJoin.WH40KFactionInfo> factions)
        {
            if (!_pendingJoinOpen)
                return;

            _pendingJoinOpen = false;
            OpenJoinWindow(factions);
        }

        private void OpenJoinWindow(IReadOnlyList<Content.Shared._WH40K.LateJoin.WH40KFactionInfo> factions)
        {
            if (factions.Count == 0)
            {
                OpenLateJoinWindow();
                return;
            }

            OpenFactionJoinWindow(factions);
        }

        private void OpenLateJoinWindow(IReadOnlyList<ProtoId<DepartmentPrototype>>? departments = null)
        {
            if (_factionJoinWindow?.IsOpen == true)
                _factionJoinWindow.Close();

            if (_lateJoinWindow?.IsOpen == true)
            {
                _lateJoinWindow.MoveToFront();
                return;
            }

            _lateJoinWindow = departments == null ? new LateJoinGui() : new LateJoinGui(departments);
            _lateJoinWindow.OnClose += () => _lateJoinWindow = null;
            _lateJoinWindow.OpenCentered();
        }

        private void OpenFactionJoinWindow(IReadOnlyList<Content.Shared._WH40K.LateJoin.WH40KFactionInfo> factions)
        {
            if (_lateJoinWindow?.IsOpen == true)
                _lateJoinWindow.Close();

            if (_factionJoinWindow?.IsOpen == true)
            {
                _factionJoinWindow.MoveToFront();
                return;
            }

            _factionJoinWindow = new WH40KFactionJoinGui(factions);
            _factionJoinWindow.OnClose += () => _factionJoinWindow = null;
            _factionJoinWindow.OpenCentered();
        }

        private void CloseJoinWindows()
        {
            if (_lateJoinWindow?.IsOpen == true)
                _lateJoinWindow.Close();
            _lateJoinWindow = null;

            if (_factionJoinWindow?.IsOpen == true)
                _factionJoinWindow.Close();
            _factionJoinWindow = null;
        }
    }
}
