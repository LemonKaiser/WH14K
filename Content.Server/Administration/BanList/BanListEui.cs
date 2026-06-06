using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.Administration.BanList;
using Content.Shared.Database;
using Content.Shared.Eui;
using Content.Shared._WH40K.Administration.Mute;
using Robust.Shared.Network;

namespace Content.Server.Administration.BanList;

public sealed partial class BanListEui : BaseEui
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IPlayerLocator _playerLocator = default!;
    [Dependency] private IServerDbManager _db = default!;

    public BanListEui()
    {
        IoCManager.InjectDependencies(this);
    }

    private Guid BanListPlayer { get; set; }
    private string BanListPlayerName { get; set; } = string.Empty;
    private List<SharedBan> Bans { get; } = new();
    private List<SharedBan> RoleBans { get; } = new();
    private List<WH40KSharedMute> Mutes { get; } = new();

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

    public override EuiStateBase GetNewState()
    {
        return new BanListEuiState(BanListPlayerName, Bans, RoleBans, Mutes);
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player && !_admins.HasAdminFlag(Player, AdminFlags.Ban))
        {
            Close();
        }
    }

    private async Task LoadBans(NetUserId userId)
    {
        await LoadBansCore(userId, BanType.Server, Bans);
        await LoadBansCore(userId, BanType.Role, RoleBans);
        await LoadMutesCore(userId);
    }

    private async Task LoadBansCore(NetUserId userId, BanType banType, List<SharedBan> list)
    {
        foreach (var ban in await _db.GetBansAsync(null, userId, null, null, type: banType))
        {
            SharedUnban? unban = null;
            if (ban.Unban is { } unbanDef)
            {
                var unbanningAdmin = unbanDef.UnbanningAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(unbanDef.UnbanningAdmin.Value))?.Username;
                unban = new SharedUnban(unbanningAdmin, ban.Unban.UnbanTime.UtcDateTime);
            }

            ImmutableArray<(string, int cidrMask)> ips = [("*Hidden*", 0)];
            ImmutableArray<string> hwids = ["*Hidden*"];

            if (_admins.HasAdminFlag(Player, AdminFlags.Pii))
            {
                ips = [..ban.Addresses.Select(a => (a.address.ToString(), a.cidrMask))];
                hwids = [..ban.HWIds.Select(h => h.ToString())];
            }

            list.Add(new SharedBan(
                ban.Id,
                ban.Type,
                ban.UserIds,
                ips,
                hwids,
                ban.BanTime.UtcDateTime,
                ban.ExpirationTime?.UtcDateTime,
                ban.Reason,
                ban.BanningAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(ban.BanningAdmin.Value))?.Username,
                unban,
                ban.Roles
            ));
        }
    }

    private async Task LoadFromDb()
    {
        Bans.Clear();
        RoleBans.Clear();
        Mutes.Clear();

        var userId = new NetUserId(BanListPlayer);
        BanListPlayerName = (await _playerLocator.LookupIdAsync(userId))?.Username ??
                            string.Empty;

        await LoadBans(userId);

        StateDirty();
    }

    public async Task ChangeBanListPlayer(Guid banListPlayer)
    {
        BanListPlayer = banListPlayer;
        await LoadFromDb();
    }

    private async Task LoadMutesCore(NetUserId userId)
    {
        foreach (var mute in await _db.GetMutesAsync(userId))
        {
            SharedUnban? unmute = null;
            if (mute.Unmute is { } unmuteDef)
            {
                var unmutingAdmin = unmuteDef.UnmutingAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(unmuteDef.UnmutingAdmin.Value))?.Username;
                unmute = new SharedUnban(unmutingAdmin, unmuteDef.UnmuteTime.UtcDateTime);
            }

            Mutes.Add(new WH40KSharedMute(
                mute.Id ?? 0,
                mute.Type,
                mute.MuteTime.UtcDateTime,
                mute.ExpirationTime?.UtcDateTime,
                mute.Reason,
                mute.MutingAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(mute.MutingAdmin.Value))?.Username,
                unmute));
        }
    }
}
