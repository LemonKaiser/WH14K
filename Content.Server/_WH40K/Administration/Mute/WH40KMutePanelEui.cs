using System;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.EUI;
using Content.Server._WH40K.Administration;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Content.Shared._WH40K.Administration.Mute;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Server._WH40K.Administration.Mute;

public sealed partial class WH40KMutePanelEui : BaseEui
{
    [Dependency] private IAdminActionGuard _adminActionGuard = default!;
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerLocator _playerLocator = default!;

    private NetUserId? _playerId;
    private string _playerName = string.Empty;
    private WH40KMuteSystem MuteSystem => _entities.System<WH40KMuteSystem>();

    public WH40KMutePanelEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override EuiStateBase GetNewState()
    {
        var canMute = WH40KStaffProtection.CanUseMuteTools(_admins.GetAdminData(Player));
        return new WH40KMutePanelEuiState(_playerName, canMute);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case WH40KMutePanelEuiStateMsg.CreateMuteRequest create:
                _ = HandleCreateMuteAsync(create.Request);
                break;
            case WH40KMutePanelEuiStateMsg.GetPlayerInfoRequest request:
                _ = ChangePlayerAsync(request.PlayerUsername);
                break;
        }
    }

    public async Task ChangePlayerAsync(string playerNameOrId)
    {
        var located = await _playerLocator.LookupIdByNameOrIdAsync(playerNameOrId);
        ChangePlayer(located?.UserId, located?.Username ?? string.Empty);
    }

    public void ChangePlayer(NetUserId? playerId, string playerName)
    {
        _playerId = playerId;
        _playerName = playerName;
        StateDirty();
    }

    private async Task HandleCreateMuteAsync(WH40KCreateMuteRequest request)
    {
        if (!WH40KStaffProtection.CanUseMuteTools(_admins.GetAdminData(Player)))
            return;

        if (request.Type == WH40KMuteType.None)
        {
            _chat.DispatchServerMessage(Player, Loc.GetString("wh40k-mute-panel-no-type"));
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Target))
        {
            _chat.DispatchServerMessage(Player, Loc.GetString("wh40k-mute-panel-no-player"));
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            _chat.DispatchServerMessage(Player, Loc.GetString("wh40k-mute-panel-no-reason"));
            return;
        }

        var located = await _playerLocator.LookupIdByNameOrIdAsync(request.Target);
        if (located == null)
        {
            _chat.DispatchServerMessage(Player, Loc.GetString("cmd-ban-player"));
            return;
        }

        if (await _adminActionGuard.TryDenyProtectedTargetAsync(
                Player,
                located.UserId,
                Loc.GetString("wh40k-admin-hierarchy-action-mute"),
                located.Username,
                message => _chat.DispatchServerMessage(Player, message)))
        {
            return;
        }

        var duration = request.DurationMinutes == 0
            ? (TimeSpan?) null
            : TimeSpan.FromMinutes(request.DurationMinutes);

        await MuteSystem.ApplyMuteAsync(
            located.UserId,
            located.Username,
            request.Type,
            request.Reason,
            duration,
            Player.UserId,
            request.Erase);

        Close();
    }

    public override void Opened()
    {
        base.Opened();
        _admins.OnPermsChanged += OnPermsChanged;
    }

    public override void Closed()
    {
        base.Closed();
        _admins.OnPermsChanged -= OnPermsChanged;
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player)
            StateDirty();
    }
}
