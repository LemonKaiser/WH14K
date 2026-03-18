using System.Linq;
using Content.Shared.GameTicking;
using Content.Shared.Localizations;
using Content.Server.Station.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using System.Text;

namespace Content.Server.GameTicking
{
    public sealed partial class GameTicker
    {
        [ViewVariables]
        private readonly Dictionary<NetUserId, PlayerGameStatus> _playerGameStatuses = new();
        [ViewVariables]
        private readonly HashSet<NetUserId> _roundJoinedUsers = new();
        [ViewVariables]
        private readonly HashSet<NetUserId> _roundParticipationBypassUsers = new();
        [ViewVariables]
        private readonly Dictionary<NetUserId, string> _lobbyInfoCultures = new();

        [ViewVariables]
        private TimeSpan _roundStartTime;

        /// <summary>
        /// How long before RoundStartTime do we load maps.
        /// </summary>
        [ViewVariables]
        public TimeSpan RoundPreloadTime { get; } = TimeSpan.FromSeconds(15);

        [ViewVariables]
        private TimeSpan _pauseTime;

        [ViewVariables]
        public new bool Paused { get; set; }

        [ViewVariables]
        private bool _roundStartCountdownHasNotStartedYetDueToNoPlayers;

        /// <summary>
        /// The game status of a players user Id. May contain disconnected players
        /// </summary>
        public IReadOnlyDictionary<NetUserId, PlayerGameStatus> PlayerGameStatuses => _playerGameStatuses;

        public void UpdateInfoText()
        {
            foreach (var session in _playerManager.NetworkedSessions)
            {
                RaiseNetworkEvent(GetInfoMsg(session), session.Channel);
            }
        }

        private void OnLobbyInfoRefreshRequested(RequestLobbyInfoRefreshEvent msg, EntitySessionEventArgs args)
        {
            if (args.SenderSession is not { } session)
                return;

            if (string.IsNullOrWhiteSpace(msg.CultureName))
                _lobbyInfoCultures.Remove(session.UserId);
            else
                _lobbyInfoCultures[session.UserId] = msg.CultureName;

            RaiseNetworkEvent(GetInfoMsg(session), session.Channel);
        }

        private string GetInfoText()
        {
            var preset = CurrentPreset ?? Preset;
            if (preset == null)
            {
                return string.Empty;
            }

            var playerCount = $"{_playerManager.PlayerCount}";
            var readyCount = _playerGameStatuses.Values.Count(x => x == PlayerGameStatus.ReadyToPlay);

            var stationNames = new StringBuilder();
            var query =
                EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent, MetaDataComponent>();

            var foundOne = false;

            while (query.MoveNext(out _, out _, out var meta))
            {
                foundOne = true;
                if (stationNames.Length > 0)
                    stationNames.Append('\n');

                stationNames.Append(meta.EntityName);
            }

            if (!foundOne)
            {
                stationNames.Append(_gameMapManager.GetSelectedMap()?.MapName ??
                                    Loc.GetString("game-ticker-no-map-selected"));
            }

            var gmTitle = (Decoy == null) ? Loc.GetString(preset.ModeTitle) : Loc.GetString(Decoy.ModeTitle);
            var desc = (Decoy == null) ? Loc.GetString(preset.Description) : Loc.GetString(Decoy.Description);
            return Loc.GetString(
                RunLevel == GameRunLevel.PreRoundLobby
                    ? "game-ticker-get-info-preround-text"
                    : "game-ticker-get-info-text",
                ("roundId", RoundId),
                ("playerCount", playerCount),
                ("readyCount", readyCount),
                ("mapName", stationNames.ToString()),
                ("gmTitle", gmTitle),
                ("desc", desc));
        }

        private TickerConnectionStatusEvent GetConnectionStatusMsg()
        {
            return new TickerConnectionStatusEvent(RoundStartTimeSpan);
        }

        private TickerLobbyStatusEvent GetStatusMsg(ICommonSession session)
        {
            _playerGameStatuses.TryGetValue(session.UserId, out var status);
            return new TickerLobbyStatusEvent(RunLevel != GameRunLevel.PreRoundLobby, LobbyBackground, status == PlayerGameStatus.ReadyToPlay, _roundStartTime, RoundPreloadTime, RoundStartTimeSpan, Paused);
        }

        private void SendStatusToAll()
        {
            foreach (var player in _playerManager.Sessions)
            {
                RaiseNetworkEvent(GetStatusMsg(player), player.Channel);
            }
        }

        private TickerLobbyInfoEvent GetInfoMsg(ICommonSession? session = null)
        {
            var cultureName = session != null && _lobbyInfoCultures.TryGetValue(session.UserId, out var storedCulture)
                ? storedCulture
                : null;

            using var cultureScope = new LocalizationCultureScope(_localizationManager, cultureName);
            return new(GetInfoText());
        }

        private void UpdateLateJoinStatus()
        {
            foreach (var player in _playerManager.Sessions)
            {
                RaiseNetworkEvent(new TickerLateJoinStatusEvent(IsLateJoinDisallowedFor(player)), player.Channel);
            }
        }

        public bool PauseStart(bool pause = true)
        {
            if (Paused == pause)
            {
                return false;
            }

            Paused = pause;

            if (pause)
            {
                _pauseTime = _gameTiming.CurTime;
            }
            else if (_pauseTime != default)
            {
                _roundStartTime += _gameTiming.CurTime - _pauseTime;
            }

            RaiseNetworkEvent(new TickerLobbyCountdownEvent(_roundStartTime, Paused));

            _chatManager.DispatchServerAnnouncement(Loc.GetString(Paused
                ? "game-ticker-pause-start"
                : "game-ticker-pause-start-resumed"));

            return true;
        }

        public bool TogglePause()
        {
            PauseStart(!Paused);
            return Paused;
        }

        public void ToggleReadyAll(bool ready)
        {
            var status = ready ? PlayerGameStatus.ReadyToPlay : PlayerGameStatus.NotReadyToPlay;
            foreach (var playerUserId in _playerGameStatuses.Keys)
            {
                _playerGameStatuses[playerUserId] = status;
                if (!_playerManager.TryGetSessionById(playerUserId, out var playerSession))
                    continue;
                RaiseNetworkEvent(GetStatusMsg(playerSession), playerSession.Channel);
            }
        }

        public void ToggleReady(ICommonSession player, bool ready)
        {
            if (!_playerGameStatuses.ContainsKey(player.UserId))
                return;

            if (!_userDb.IsLoadComplete(player))
                return;

            if (RunLevel != GameRunLevel.PreRoundLobby)
            {
                return;
            }

            _playerGameStatuses[player.UserId] = ready ? PlayerGameStatus.ReadyToPlay : PlayerGameStatus.NotReadyToPlay;
            RaiseNetworkEvent(GetStatusMsg(player), player.Channel);
            // update server info to reflect new ready count
            UpdateInfoText();
        }

        public bool UserHasJoinedGame(ICommonSession session)
            => UserHasJoinedGame(session.UserId);

        public bool UserHasJoinedGame(NetUserId userId)
            => PlayerGameStatuses.TryGetValue(userId, out var status) && status == PlayerGameStatus.JoinedGame;

        public bool HasJoinedRoundThisSession(ICommonSession session)
            => HasJoinedRoundThisSession(session.UserId);

        public bool HasJoinedRoundThisSession(NetUserId userId)
            => _roundJoinedUsers.Contains(userId);

        public bool IsRoundParticipationLocked(ICommonSession session)
            => RunLevel == GameRunLevel.InRound
               && HasJoinedRoundThisSession(session.UserId)
               && !_roundParticipationBypassUsers.Contains(session.UserId);

        public void GrantRoundParticipationBypass(ICommonSession session)
            => _roundParticipationBypassUsers.Add(session.UserId);

        private void ConsumeRoundParticipationBypass(ICommonSession session)
            => _roundParticipationBypassUsers.Remove(session.UserId);

        private bool IsLateJoinDisallowedFor(ICommonSession session)
        {
            if (RunLevel != GameRunLevel.InRound)
                return false;

            return DisallowLateJoin || IsRoundParticipationLocked(session);
        }
    }
}
