using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.GameTicking;
using Content.Shared.GameWindow;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Server.Preferences;
using JetBrains.Annotations;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.GameTicking
{
    [UsedImplicitly]
    public sealed partial class GameTicker
    {
        [Dependency] private  IPlayerManager _playerManager = default!;
        [Dependency] private  IEntitySystemManager _entitySystems = default!;

        private void InitializePlayer()
        {
            _playerManager.PlayerStatusChanged += PlayerStatusChanged;
        }

        private async void PlayerStatusChanged(object? sender, SessionStatusEventArgs args)
        {
            var session = args.Session;

            if (_mind.TryGetMind(session.UserId, out var mindId, out var mind))
            {
                if (args.NewStatus != SessionStatus.Disconnected)
                {
                    _pvsOverride.AddSessionOverride(mindId.Value, session);
                }
            }

            DebugTools.Assert(session.GetMind() == mindId);

            switch (args.NewStatus)
            {
                case SessionStatus.Connected:
                {
                    AddPlayerToDb(args.Session.UserId.UserId);

                    // Always make sure the client has player data.
                    if (session.Data.ContentDataUncast == null)
                    {
                        var data = new ContentPlayerData(session.UserId, args.Session.Name);
                        data.Mind = mindId;
                        session.Data.ContentDataUncast = data;
                    }

                    // Make the player actually join the game.
                    // timer time must be > tick length
                    Timer.Spawn(0, () => _playerManager.JoinGame(args.Session));

                    var record = await _db.GetPlayerRecordByUserId(args.Session.UserId);
                    var firstConnection = record != null &&
                                          Math.Abs((record.FirstSeenTime - record.LastSeenTime).TotalMinutes) < 1;

                    _chatManager.SendAdminAnnouncement(firstConnection
                        ? Loc.GetString("player-first-join-message", ("name", args.Session.Name))
                        : Loc.GetString("player-join-message", ("name", args.Session.Name)));

                    RaiseNetworkEvent(GetConnectionStatusMsg(), session.Channel);

                    if (firstConnection && _cfg.GetCVar(CCVars.AdminNewPlayerJoinSound))
                        _audio.PlayGlobal(new SoundPathSpecifier("/Audio/Effects/newplayerping.ogg"),
                            Filter.Empty().AddPlayers(_adminManager.ActiveAdmins), false,
                            audioParams: new AudioParams { Volume = -5f });

                    if (LobbyEnabled && _roundStartCountdownHasNotStartedYetDueToNoPlayers)
                    {
                        _roundStartCountdownHasNotStartedYetDueToNoPlayers = false;
                        _roundStartTime = _gameTiming.CurTime + LobbyDuration;
                    }

                    break;
                }

                case SessionStatus.InGame:
                {
                    _userDb.ClientConnected(session);
                    FinalizeJoinAfterUserDbLoad();
                    break;
                }

                case SessionStatus.Disconnected:
                {
                    _lobbyInfoCultures.Remove(session.UserId);
                    _chatManager.SendAdminAnnouncement(Loc.GetString("player-leave-message", ("name", args.Session.Name)));
                    if (mindId != null)
                    {
                        _pvsOverride.RemoveSessionOverride(mindId.Value, session);
                    }

                    _userDb.ClientDisconnected(session);

                    _adminLogger.Add(LogType.Connection, LogImpact.Low, $"User {args.Session:Player} attached to {(args.Session.AttachedEntity != null ? ToPrettyString(args.Session.AttachedEntity) : "nothing"):entity} disconnected from the game.");
                    break;
                }
            }
            //When the status of a player changes, update the server info text
            UpdateInfoText();

            async void FinalizeJoinAfterUserDbLoad()
            {
                try
                {
                    await _userDb.WaitLoadComplete(session);
                }
                catch (OperationCanceledException)
                {
                    // Bail, user must've disconnected or something.
                    Log.Debug($"Database load cancelled while waiting to spawn {session}");
                    return;
                }

                if (session.Status != SessionStatus.InGame)
                    return;

                if (!_mind.TryGetMind(session.UserId, out var loadedMindId, out var loadedMind))
                {
                    if (LobbyEnabled)
                    {
                        PlayerJoinLobby(session);
                    }
                    else
                    {
                        SpawnPlayer(session, EntityUid.Invalid);
                    }

                    LogConnectedToGame(session);
                    UpdateInfoText();
                    return;
                }

                if (loadedMind.CurrentEntity == null || Deleted(loadedMind.CurrentEntity))
                {
                    DebugTools.Assert(loadedMind.CurrentEntity == null, "a mind's current entity was deleted without updating the mind");
                    JoinAsObserver(session);
                    LogConnectedToGame(session);
                    UpdateInfoText();
                    return;
                }

                if (_playerManager.SetAttachedEntity(session, loadedMind.CurrentEntity))
                {
                    PlayerJoinGame(session);
                    LogConnectedToGame(session);
                    UpdateInfoText();
                    return;
                }

                Log.Error(
                    $"Failed to attach player {session} with mind {ToPrettyString(loadedMindId)} to its current entity {ToPrettyString(loadedMind.CurrentEntity)}");
                JoinAsObserver(session);
                LogConnectedToGame(session);
                UpdateInfoText();
            }

            async void AddPlayerToDb(Guid id)
            {
                if (RoundId != 0 && _runLevel != GameRunLevel.PreRoundLobby)
                {
                    await _db.AddRoundPlayers(RoundId, id);
                }
            }

            void LogConnectedToGame(ICommonSession playerSession)
            {
                _adminLogger.Add(LogType.Connection, LogImpact.Low, $"User {playerSession:Player} attached to {(playerSession.AttachedEntity != null ? ToPrettyString(playerSession.AttachedEntity) : "nothing"):entity} connected to the game.");
            }
        }

        public HumanoidCharacterProfile GetPlayerProfile(ICommonSession p)
        {
            var profile = (HumanoidCharacterProfile) _prefsManager.GetPreferences(p.UserId).SelectedCharacter;
            return SpeciesSelectionValidator.EnsureUnlocked(profile, p, _prototypeManager, _adminManager, _entitySystems);
        }

        public void PlayerJoinGame(ICommonSession session, bool silent = false)
        {
            if (!_userDb.IsLoadComplete(session))
            {
                _sawmill.Warning($"Blocked early JoinGame for {session}: user DB load is not complete yet.");
                return;
            }

            if (!silent)
                _chatManager.DispatchServerMessage(session, Loc.GetString("game-ticker-player-join-game-message"));

            ConsumeRoundParticipationBypass(session);
            _playerGameStatuses[session.UserId] = PlayerGameStatus.JoinedGame;
            _roundJoinedUsers.Add(session.UserId);
            _db.AddRoundPlayers(RoundId, session.UserId);

            if (_adminManager.HasAdminFlag(session, AdminFlags.Admin))
            {
                if (_allPreviousGameRules.Count > 0)
                {
                    var rulesMessage = GetGameRulesListMessage(true);
                    _chatManager.SendAdminAnnouncementMessage(session, Loc.GetString("starting-rule-selected-preset", ("preset", rulesMessage)));
                }
            }

            RaiseNetworkEvent(new TickerJoinGameEvent(), session.Channel);
        }

        private void PlayerJoinLobby(ICommonSession session)
        {
            if (!_userDb.IsLoadComplete(session))
            {
                _sawmill.Warning($"Blocked early JoinLobby for {session}: user DB load is not complete yet.");
                return;
            }

            _playerGameStatuses[session.UserId] = LobbyEnabled ? PlayerGameStatus.NotReadyToPlay : PlayerGameStatus.ReadyToPlay;
            _db.AddRoundPlayers(RoundId, session.UserId);

            var client = session.Channel;
            RaiseNetworkEvent(new TickerJoinLobbyEvent(), client);
            RaiseNetworkEvent(GetStatusMsg(session), client);
            RaiseNetworkEvent(GetInfoMsg(session), client);
            RaiseNetworkEvent(new TickerLateJoinStatusEvent(IsLateJoinDisallowedFor(session)), client);
            RaiseLocalEvent(new PlayerJoinedLobbyEvent(session));
        }

        public bool ReturnGhostToLobby(ICommonSession session)
        {
            if (RunLevel == GameRunLevel.PreRoundLobby)
                return false;

            if (session.AttachedEntity is not { Valid: true } attached ||
                !HasComp<GhostComponent>(attached))
            {
                return false;
            }

            _mind.WipeMind(session);

            if (session.AttachedEntity != null)
                _playerManager.SetAttachedEntity(session, null);

            PlayerJoinLobby(session);
            UpdateLateJoinStatus();
            return true;
        }

        private void ReqWindowAttentionAll()
        {
            RaiseNetworkEvent(new RequestWindowAttentionEvent());
        }
    }

    public sealed class PlayerJoinedLobbyEvent : EntityEventArgs
    {
        public readonly ICommonSession PlayerSession;

        public PlayerJoinedLobbyEvent(ICommonSession playerSession)
        {
            PlayerSession = playerSession;
        }
    }
}
