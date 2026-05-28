using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Administration.Notes;
using Content.Server._WH40K.Administration.ScreenCheck;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.Administration.Systems;
using Content.Shared._WH40K.Administration.ScreenCheck;
using Content.Shared.Database;
using Content.Shared.Eui;
using Content.Shared.Follower;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Administration;

public sealed partial class PlayerPanelEui : BaseEui
{
    [Dependency] private  IAdminManager _admins = default!;
    [Dependency] private  IAdminHierarchyManager _adminHierarchy = default!;
    [Dependency] private  IServerDbManager _db = default!;
    [Dependency] private  IAdminNotesManager _notesMan = default!;
    [Dependency] private  IEntityManager _entity = default!;
    [Dependency] private  IPlayerManager _player = default!;
    [Dependency] private  EuiManager _eui = default!;
    [Dependency] private  IAdminLogManager _adminLog = default!;
    [Dependency] private  IChatManager _chat = default!;
    [Dependency] private  ScreenCheckManager _screenChecks = default!;

    private readonly LocatedPlayerData _targetPlayer;
    private int? _notes;
    private int? _bans;
    private int? _roleBans;
    private int _sharedConnections;
    private bool? _whitelisted;
    private TimeSpan _playtime;
    private bool _frozen;
    private bool _canFreeze;
    private bool _canAhelp;
    private bool _canScreenCheck;
    private ScreenCheckTargetSnapshot _screenCheckSnapshot;
    private FollowerSystem _follower;

    public PlayerPanelEui(LocatedPlayerData player)
    {
        IoCManager.InjectDependencies(this);
        _targetPlayer = player;
        _follower = _entity.System<FollowerSystem>();
    }

    public override void Opened()
    {
        base.Opened();
        _admins.OnPermsChanged += OnPermsChanged;
        _screenChecks.TargetStateChanged += OnScreenCheckStateChanged;
    }

    public override void Closed()
    {
        base.Closed();
        _admins.OnPermsChanged -= OnPermsChanged;
        _screenChecks.TargetStateChanged -= OnScreenCheckStateChanged;
    }

    public override EuiStateBase GetNewState()
    {
        return new PlayerPanelEuiState(_targetPlayer.UserId,
            _targetPlayer.Username,
            _playtime,
            _notes,
            _bans,
            _roleBans,
            _sharedConnections,
            _whitelisted,
            _canFreeze,
            _frozen,
            _canAhelp,
            _canScreenCheck,
            _screenCheckSnapshot.HasActiveRequest,
            _screenCheckSnapshot.ActiveAdminName ?? string.Empty,
            _screenCheckSnapshot.ActiveSinceUtc,
            _screenCheckSnapshot.HasLastResult,
            _screenCheckSnapshot.LastAdminName ?? string.Empty,
            _screenCheckSnapshot.LastUpdatedUtc,
            _screenCheckSnapshot.LastStatus);
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player != Player)
            return;

        SetPlayerState();
    }

    private void OnScreenCheckStateChanged(NetUserId targetUserId)
    {
        if (targetUserId != _targetPlayer.UserId)
            return;

        _screenCheckSnapshot = _screenChecks.GetTargetSnapshot(_targetPlayer.UserId);
        StateDirty();
    }

    public override async void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is PlayerPanelFreezeMessage
            or PlayerPanelLogsMessage
            or PlayerPanelDeleteMessage
            or PlayerPanelRejuvenationMessage
            or PlayerPanelFollowMessage
            or PlayerPanelScreenCheckMessage)
        {
            var decision = await _adminHierarchy.CanManageAdminAsync(Player, _targetPlayer.UserId);
            if (!decision.Allowed)
            {
                Close();
                return;
            }
        }

        ICommonSession? session;

        switch (msg)
        {
            case PlayerPanelFreezeMessage freezeMsg:
                if (!_admins.IsAdmin(Player) ||
                    !_entity.TrySystem<AdminFrozenSystem>(out var frozenSystem) ||
                    !_player.TryGetSessionById(_targetPlayer.UserId, out session) ||
                    session.AttachedEntity == null)
                    return;

                if (_entity.HasComponent<AdminFrozenComponent>(session.AttachedEntity))
                {
                    _adminLog.Add(LogType.Action,$"{Player:actor} unfroze {_entity.ToPrettyString(session.AttachedEntity):subject}");
                    _entity.RemoveComponent<AdminFrozenComponent>(session.AttachedEntity.Value);
                    SetPlayerState();
                    return;
                }

                if (freezeMsg.Mute)
                {
                    _adminLog.Add(LogType.Action,$"{Player:actor} froze and muted {_entity.ToPrettyString(session.AttachedEntity):subject}");
                    frozenSystem.FreezeAndMute(session.AttachedEntity.Value);
                }
                else
                {
                    _adminLog.Add(LogType.Action,$"{Player:actor} froze {_entity.ToPrettyString(session.AttachedEntity):subject}");
                    _entity.EnsureComponent<AdminFrozenComponent>(session.AttachedEntity.Value);
                }
                SetPlayerState();
                break;

            case PlayerPanelLogsMessage:
                if (!_admins.HasAdminFlag(Player, AdminFlags.Logs))
                    return;

                _adminLog.Add(LogType.Action, $"{Player:actor} opened logs on {_targetPlayer.Username:subject}");
                var ui = new AdminLogsEui();
                _eui.OpenEui(ui, Player);
                ui.SetLogFilter(search: _targetPlayer.Username);
                break;
            case PlayerPanelDeleteMessage:
            case PlayerPanelRejuvenationMessage:
                if (!_admins.HasAdminFlag(Player, AdminFlags.Debug) ||
                    !_player.TryGetSessionById(_targetPlayer.UserId, out session) ||
                    session.AttachedEntity == null)
                    return;

                if (msg is PlayerPanelRejuvenationMessage)
                {
                    _adminLog.Add(LogType.Action,$"{Player:actor} rejuvenated {_entity.ToPrettyString(session.AttachedEntity):subject}");
                    if (!_entity.TrySystem<RejuvenateSystem>(out var rejuvenate))
                        return;

                    rejuvenate.PerformRejuvenate(session.AttachedEntity.Value);
                }
                else
                {
                    _adminLog.Add(LogType.Action,$"{Player:actor} deleted {_entity.ToPrettyString(session.AttachedEntity):subject}");
                    _entity.DeleteEntity(session.AttachedEntity);
                }
                break;
            case PlayerPanelFollowMessage:
                if (!_admins.HasAdminFlag(Player, AdminFlags.Admin) ||
                    !_player.TryGetSessionById(_targetPlayer.UserId, out session) ||
                    session.AttachedEntity == null ||
                    Player.AttachedEntity is null ||
                    session.AttachedEntity == Player.AttachedEntity)
                    return;

                _follower.StartFollowingEntity(Player.AttachedEntity.Value, session.AttachedEntity.Value);
                break;
            case PlayerPanelScreenCheckMessage:
                if (!_admins.HasAdminFlag(Player, AdminFlags.Moderator))
                    return;

                if (!_player.TryGetSessionById(_targetPlayer.UserId, out var targetSession))
                {
                    _chat.DispatchServerMessage(Player, Loc.GetString("screen-check-player-offline", ("player", _targetPlayer.Username)));
                    return;
                }

                var result = _screenChecks.StartScreenCheck(Player, targetSession);
                switch (result)
                {
                    case ScreenCheckStartResult.Success:
                        _chat.DispatchServerMessage(Player, Loc.GetString("screen-check-request-sent", ("player", targetSession.Name)));
                        break;

                    case ScreenCheckStartResult.AdminAlreadyHasPending:
                        _chat.DispatchServerMessage(Player, Loc.GetString("screen-check-request-active-admin"));
                        break;

                    case ScreenCheckStartResult.TargetAlreadyHasPending:
                        _chat.DispatchServerMessage(Player, Loc.GetString("screen-check-request-active-target", ("player", targetSession.Name)));
                        break;

                    case ScreenCheckStartResult.TooManyPending:
                        _chat.DispatchServerMessage(Player, Loc.GetString("screen-check-request-limit-reached"));
                        break;
                }

                SetPlayerState();
                break;
        }
    }

    public async void SetPlayerState()
    {
        if (!_admins.IsAdmin(Player))
        {
            Close();
            return;
        }

        var hierarchyDecision = await _adminHierarchy.CanManageAdminAsync(Player, _targetPlayer.UserId);
        if (!hierarchyDecision.Allowed)
        {
            Close();
            return;
        }

        _playtime = (await _db.GetPlayTimes(_targetPlayer.UserId))
            .Where(p => p.Tracker == "Overall")
            .Select(p => p.TimeSpent)
            .FirstOrDefault();

        if (_notesMan.CanView(Player))
        {
            _notes = (await _notesMan.GetAllAdminRemarks(_targetPlayer.UserId)).Count;
        }
        else
        {
            _notes = null;
        }

        _sharedConnections = _player.Sessions.Count(s => s.Channel.RemoteEndPoint.Address.Equals(_targetPlayer.LastAddress) && s.UserId != _targetPlayer.UserId);

    // Apparently the Bans flag is also used for whitelists
    if (_admins.HasAdminFlag(Player, AdminFlags.Ban))
        {
            _whitelisted = await _db.GetWhitelistStatusAsync(_targetPlayer.UserId);
            // This won't get associated ip or hwid bans but they were not placed on this account anyways
            _bans = (await _db.GetBansAsync(null, _targetPlayer.UserId, null, null)).Count;
            _roleBans = (await _db.GetBansAsync(null, _targetPlayer.UserId, null, null, type: BanType.Role)).Count();
        }
        else
        {
            _whitelisted = null;
            _bans = null;
            _roleBans = null;
        }

        var targetOnline = _player.TryGetSessionById(_targetPlayer.UserId, out var session);
        if (targetOnline && session?.AttachedEntity is { } attachedEntity)
        {
            _canFreeze = true;
            _frozen = _entity.HasComponent<AdminFrozenComponent>(attachedEntity);
        }
        else
        {
            _canFreeze = false;
            _frozen = false;
        }

        if (_admins.HasAdminFlag(Player, AdminFlags.Adminhelp))
        {
            _canAhelp = true;
        }
        else
        {
            _canAhelp = false;
        }

        _canScreenCheck = _admins.HasAdminFlag(Player, AdminFlags.Moderator) && targetOnline;
        _screenCheckSnapshot = _screenChecks.GetTargetSnapshot(_targetPlayer.UserId);

        StateDirty();
    }
}
