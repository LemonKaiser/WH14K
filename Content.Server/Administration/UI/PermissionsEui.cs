using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Robust.Server.Player;
using Robust.Shared.Network;
using DbAdminRank = Content.Server.Database.AdminRank;
using static Content.Shared.Administration.PermissionsEuiMsg;

namespace Content.Server.Administration.UI
{
    public sealed partial class PermissionsEui : BaseEui
    {
        [Dependency] private  IPlayerManager _playerManager = default!;
        [Dependency] private  IServerDbManager _db = default!;
        [Dependency] private  IAdminManager _adminManager = default!;
        [Dependency] private  IAdminHierarchyManager _adminHierarchyManager = default!;
        [Dependency] private  ILogManager _logManager = default!;

        private readonly ISawmill _sawmill;
        private bool _isLoading;
        private PermissionsNoticeCode _noticeCode;
        private string? _noticeSubject;

        private readonly List<(Admin a, string? lastUserName, DateTime? lastSeenTime)> _admins = new();
        private readonly List<DbAdminRank> _adminRanks = new();

        public PermissionsEui()
        {
            IoCManager.InjectDependencies(this);
            _sawmill = _logManager.GetSawmill("admin.perms");
        }

        public override void Opened()
        {
            base.Opened();

            StateDirty();
            LoadFromDb();
            _adminManager.OnPermsChanged += AdminManagerOnPermsChanged;
        }

        public override void Closed()
        {
            base.Closed();

            _adminManager.OnPermsChanged -= AdminManagerOnPermsChanged;
        }

        private void AdminManagerOnPermsChanged(AdminPermsChangedEventArgs obj)
        {
            if (obj.Player == Player && !UserAdminFlagCheck(AdminFlags.Permissions))
            {
                Close();
            }
        }

        public override EuiStateBase GetNewState()
        {
            if (_isLoading)
            {
                return new PermissionsEuiState
                {
                    IsLoading = true
                };
            }

            AdminHierarchyInfo GetHierarchy(Admin admin)
            {
                if (_playerManager.TryGetSessionById(new NetUserId(admin.UserId), out var session))
                    return _adminHierarchyManager.GetAdminHierarchy(session, includeDeAdmin: true);

                return _adminHierarchyManager.GetAdminHierarchy(admin);
            }

            return new PermissionsEuiState
            {
                NoticeCode = _noticeCode,
                NoticeSubject = _noticeSubject,
                ServerTimeUtc = DateTime.UtcNow,
                Admins = _admins.Select(p =>
                {
                    var hierarchy = GetHierarchy(p.a);
                    var isOnline = _playerManager.TryGetSessionById(new NetUserId(p.a.UserId), out var session);

                    return new PermissionsEuiState.AdminData
                    {
                        CanModify = CanTouchAdmin(p.a),
                        Deadminned = p.a.Deadminned,
                        EffectiveHierarchyLevel = hierarchy.EffectiveHierarchyLevel,
                        IsHost = hierarchy.IsHost,
                        IsOnline = isOnline,
                        LastSeenTimeUtc = p.lastSeenTime,
                        NegFlags = AdminFlagsHelper.NamesToFlags(p.a.Flags.Where(f => f.Negative).Select(f => f.Flag)),
                        OnlineSinceUtc = isOnline ? session!.ConnectedTime : null,
                        PosFlags = AdminFlagsHelper.NamesToFlags(p.a.Flags.Where(f => !f.Negative).Select(f => f.Flag)),
                        RankId = p.a.AdminRankId,
                        Revision = ComputeAdminRevision(p.a),
                        Suspended = p.a.Suspended,
                        Title = p.a.Title,
                        UserId = new NetUserId(p.a.UserId),
                        UserName = p.lastUserName,
                    };
                }).ToArray(),
                AdminRanks = _adminRanks.ToDictionary(a => a.Id, a => new PermissionsEuiState.AdminRankData
                {
                    AssignedAdminCount = _admins.Count(admin => admin.a.AdminRankId == a.Id),
                    CanAssign = CanAssignRank(a),
                    CanModify = CanTouchRank(a),
                    Flags = AdminFlagsHelper.NamesToFlags(a.Flags.Select(p => p.Flag)),
                    HierarchyLevel = a.HierarchyLevel,
                    Name = a.Name,
                    Revision = ComputeRankRevision(a),
                })
            };
        }

        public override async void HandleMessage(EuiMessageBase msg)
        {
            base.HandleMessage(msg);

            switch (msg)
            {
                case AddAdmin ca:
                    await HandleCreateAdmin(ca);
                    break;

                case UpdateAdmin ua:
                    await HandleUpdateAdmin(ua);
                    break;

                case RemoveAdmin ra:
                    await HandleRemoveAdmin(ra);
                    break;

                case AddAdminRank ar:
                    await HandleAddAdminRank(ar);
                    break;

                case UpdateAdminRank ur:
                    await HandleUpdateAdminRank(ur);
                    break;

                case RemoveAdminRank rr:
                    await HandleRemoveAdminRank(rr);
                    break;
            }

            if (!IsShutDown)
            {
                LoadFromDb();
            }
        }

        private async Task HandleRemoveAdminRank(RemoveAdminRank rr)
        {
            var rank = await _db.GetAdminRankAsync(rr.Id);
            if (rank == null)
                return;

            if (!ValidateRankRevision(rank, rr.ExpectedRevision))
                return;

            if (!CanTouchRank(rank))
            {
                SetNotice(PermissionsNoticeCode.ProtectedRank, rank.Name);
                _sawmill.Warning($"{Player} tried to remove protected admin rank {rank.Name}.");
                return;
            }

            await _db.RemoveAdminRankAsync(rr.Id);
            ClearNotice();

            _adminManager.ReloadAdminsWithRank(rr.Id);
        }

        private async Task HandleUpdateAdminRank(UpdateAdminRank ur)
        {
            var rank = await _db.GetAdminRankAsync(ur.Id);
            if (rank == null)
                return;

            if (!ValidateRankRevision(rank, ur.ExpectedRevision))
                return;

            if (!CanTouchRank(rank))
            {
                SetNotice(PermissionsNoticeCode.ProtectedRank, rank.Name);
                _sawmill.Warning($"{Player} tried to update protected admin rank {rank.Name}.");
                return;
            }

            var hierarchyDecision = _adminHierarchyManager.CanUseHierarchyLevel(Player, ur.HierarchyLevel);
            if (!hierarchyDecision.Allowed)
            {
                SetNotice(PermissionsNoticeCode.InvalidHierarchy, rank.Name);
                _sawmill.Warning($"{Player} tried to set admin rank {rank.Name} to invalid/protected hierarchy level {ur.HierarchyLevel}: {hierarchyDecision.Reason}");
                return;
            }

            if (!UserAdminFlagCheck(ur.Flags))
            {
                SetNotice(PermissionsNoticeCode.ProtectedRank, rank.Name);
                _sawmill.Warning($"{Player} tried to give a rank permissions above their authorization.");
                return;
            }

            rank.Flags = GenRankFlagList(ur.Flags);
            rank.Name = ur.Name;
            rank.HierarchyLevel = ur.HierarchyLevel;

            await _db.UpdateAdminRankAsync(rank);
            ClearNotice();

            var flagText = string.Join(' ', AdminFlagsHelper.FlagsToNames(ur.Flags).Select(f => $"+{f}"));
            _sawmill.Info($"{Player} updated admin rank {rank.Name}/H{rank.HierarchyLevel}/{flagText}.");

            _adminManager.ReloadAdminsWithRank(ur.Id);
        }

        private async Task HandleAddAdminRank(AddAdminRank ar)
        {
            var hierarchyDecision = _adminHierarchyManager.CanUseHierarchyLevel(Player, ar.HierarchyLevel);
            if (!hierarchyDecision.Allowed)
            {
                SetNotice(PermissionsNoticeCode.InvalidHierarchy, ar.Name);
                _sawmill.Warning($"{Player} tried to create admin rank with invalid/protected hierarchy level {ar.HierarchyLevel}: {hierarchyDecision.Reason}");
                return;
            }

            if (!UserAdminFlagCheck(ar.Flags))
            {
                SetNotice(PermissionsNoticeCode.ProtectedRank, ar.Name);
                _sawmill.Warning($"{Player} tried to give a rank permissions above their authorization.");
                return;
            }

            var rank = new DbAdminRank
            {
                Name = ar.Name,
                HierarchyLevel = ar.HierarchyLevel,
                Flags = GenRankFlagList(ar.Flags)
            };

            await _db.AddAdminRankAsync(rank);
            ClearNotice();

            var flagText = string.Join(' ', AdminFlagsHelper.FlagsToNames(ar.Flags).Select(f => $"+{f}"));
            _sawmill.Info($"{Player} added admin rank {rank.Name}/H{rank.HierarchyLevel}/{flagText}.");
        }

        private async Task HandleRemoveAdmin(RemoveAdmin ra)
        {
            var admin = await _db.GetAdminDataForAsync(ra.UserId);
            if (admin == null)
                return;

            if (!ValidateAdminRevision(admin, ra.ExpectedRevision))
                return;

            if (!CanTouchAdmin(admin))
            {
                SetNotice(PermissionsNoticeCode.ProtectedAdmin, admin.Title ?? admin.AdminRank?.Name ?? ra.UserId.ToString());
                _sawmill.Warning($"{Player} tried to remove protected admin {ra.UserId}");
                return;
            }

            await _db.RemoveAdminAsync(ra.UserId);
            ClearNotice();

            var record = await _db.GetPlayerRecordByUserId(ra.UserId);
            _sawmill.Info($"{Player} removed admin {record?.LastSeenUserName ?? ra.UserId.ToString()}");

            if (_playerManager.TryGetSessionById(ra.UserId, out var player))
            {
                _adminManager.ReloadAdmin(player);
            }
        }

        private async Task HandleUpdateAdmin(UpdateAdmin ua)
        {
            if (!CheckCreatePerms(ua.PosFlags, ua.NegFlags))
                return;

            var admin = await _db.GetAdminDataForAsync(ua.UserId);
            if (admin == null)
                return;

            if (!ValidateAdminRevision(admin, ua.ExpectedRevision))
                return;

            if (!CanTouchAdmin(admin))
            {
                SetNotice(PermissionsNoticeCode.ProtectedAdmin, admin.Title ?? admin.AdminRank?.Name ?? ua.UserId.ToString());
                _sawmill.Warning($"{Player} tried to modify protected admin {ua.UserId}");
                return;
            }

            var (bad, rankName) = await FetchAndCheckRank(ua.RankId);
            if (bad)
                return;

            admin.Title = ua.Title;
            admin.AdminRankId = ua.RankId;
            admin.Flags = GenAdminFlagList(ua.PosFlags, ua.NegFlags);
            admin.Suspended = ua.Suspended;

            await _db.UpdateAdminAsync(admin);
            ClearNotice();

            var playerRecord = await _db.GetPlayerRecordByUserId(ua.UserId);
            var name = playerRecord?.LastSeenUserName ?? ua.UserId.ToString();
            var title = ua.Title ?? "<no title>";
            var flags = AdminFlagsHelper.PosNegFlagsText(ua.PosFlags, ua.NegFlags);

            _sawmill.Info($"{Player} updated admin {name} to {title}/{rankName}/{flags}");

            if (_playerManager.TryGetSessionById(ua.UserId, out var player))
            {
                _adminManager.ReloadAdmin(player);
            }
        }

        private async Task HandleCreateAdmin(AddAdmin ca)
        {
            if (!CheckCreatePerms(ca.PosFlags, ca.NegFlags))
                return;

            string name;
            NetUserId userId;
            if (Guid.TryParse(ca.UserNameOrId, out var guid))
            {
                userId = new NetUserId(guid);
                var playerRecord = await _db.GetPlayerRecordByUserId(userId);
                name = playerRecord?.LastSeenUserName ?? userId.ToString();
            }
            else
            {
                var dbPlayer = await _db.GetPlayerRecordByUserName(ca.UserNameOrId);
                if (dbPlayer == null)
                {
                    SetNotice(PermissionsNoticeCode.UnknownUser, ca.UserNameOrId);
                    _sawmill.Warning($"{Player} tried to add admin with unknown username {ca.UserNameOrId}.");
                    return;
                }

                userId = dbPlayer.UserId;
                name = ca.UserNameOrId;
            }

            var existing = await _db.GetAdminDataForAsync(userId);
            if (existing != null)
            {
                SetNotice(PermissionsNoticeCode.AlreadyExists, name);
                return;
            }

            var (bad, rankName) = await FetchAndCheckRank(ca.RankId);
            if (bad)
                return;

            rankName ??= "<no rank>";

            var admin = new Admin
            {
                Flags = GenAdminFlagList(ca.PosFlags, ca.NegFlags),
                AdminRankId = ca.RankId,
                UserId = userId.UserId,
                Title = ca.Title,
                Suspended = ca.Suspended,
            };

            await _db.AddAdminAsync(admin);
            ClearNotice();

            var title = ca.Title ?? "<no title>";
            var flags = AdminFlagsHelper.PosNegFlagsText(ca.PosFlags, ca.NegFlags);

            _sawmill.Info($"{Player} added admin {name} as {title}/{rankName}/{flags}");

            if (_playerManager.TryGetSessionById(userId, out var player))
            {
                _adminManager.ReloadAdmin(player);
            }
        }

        private bool CheckCreatePerms(AdminFlags posFlags, AdminFlags negFlags)
        {
            if ((posFlags & negFlags) != 0)
                return false;

            if (!UserAdminFlagCheck(posFlags))
            {
                SetNotice(PermissionsNoticeCode.ProtectedAdmin, null);
                _sawmill.Warning($"{Player} tried to grant admin powers above their authorization.");
                return false;
            }

            return true;
        }

        private async Task<(bool bad, string?)> FetchAndCheckRank(int? rankId)
        {
            string? ret = null;
            if (rankId is not { } r)
                return (false, ret);

            var rank = await _db.GetAdminRankAsync(r);
            if (rank == null)
            {
                SetNotice(PermissionsNoticeCode.RankNotAssignable, null);
                _sawmill.Warning($"{Player} tried to assign nonexistent admin rank.");
                return (true, null);
            }

            ret = rank.Name;

            var rankFlags = AdminFlagsHelper.NamesToFlags(rank.Flags.Select(p => p.Flag));
            if (!UserAdminFlagCheck(rankFlags))
            {
                SetNotice(PermissionsNoticeCode.ProtectedRank, rank.Name);
                _sawmill.Warning($"{Player} tried to assign admin rank above their authorization.");
                return (true, null);
            }

            var hierarchyDecision = await _adminHierarchyManager.CanAssignRankAsync(Player, r);
            if (!hierarchyDecision.Allowed)
            {
                SetNotice(PermissionsNoticeCode.RankNotAssignable, rank.Name);
                _sawmill.Warning($"{Player} tried to assign protected admin rank {rank.Name}: {hierarchyDecision.Reason}");
                return (true, null);
            }

            return (false, ret);
        }

        private async void LoadFromDb()
        {
            StateDirty();
            _isLoading = true;
            var (admins, ranks) = await _db.GetAllAdminAndRanksAsync();

            _admins.Clear();
            _admins.AddRange(admins);
            _adminRanks.Clear();
            _adminRanks.AddRange(ranks);

            _isLoading = false;
            StateDirty();
        }

        private static List<AdminFlag> GenAdminFlagList(AdminFlags posFlags, AdminFlags negFlags)
        {
            var posFlagList = AdminFlagsHelper.FlagsToNames(posFlags);
            var negFlagList = AdminFlagsHelper.FlagsToNames(negFlags);

            return posFlagList
                .Select(f => new AdminFlag { Negative = false, Flag = f })
                .Concat(negFlagList.Select(f => new AdminFlag { Negative = true, Flag = f }))
                .ToList();
        }

        private static List<AdminRankFlag> GenRankFlagList(AdminFlags flags)
        {
            return AdminFlagsHelper.FlagsToNames(flags).Select(f => new AdminRankFlag { Flag = f }).ToList();
        }

        private bool UserAdminFlagCheck(AdminFlags flags)
        {
            return _adminManager.HasAdminFlag(Player, flags);
        }

        private bool CanTouchAdmin(Admin admin)
        {
            if (_playerManager.TryGetSessionById(new NetUserId(admin.UserId), out var targetSession))
            {
                if (!_adminHierarchyManager.CanManageAdmin(Player, targetSession, includeDeAdmin: true).Allowed)
                    return false;
            }
            else if (!_adminHierarchyManager.CanManageAdmin(Player, admin).Allowed)
            {
                return false;
            }

            var totalFlags = AdminHierarchyManager.ResolveFlags(admin);
            return UserAdminFlagCheck(totalFlags);
        }

        private bool CanTouchRank(DbAdminRank rank)
        {
            var rankFlags = AdminFlagsHelper.NamesToFlags(rank.Flags.Select(f => f.Flag));
            if (!UserAdminFlagCheck(rankFlags))
                return false;

            return _adminHierarchyManager.CanManageRank(Player, rank).Allowed;
        }

        private bool CanAssignRank(DbAdminRank rank)
        {
            var rankFlags = AdminFlagsHelper.NamesToFlags(rank.Flags.Select(flag => flag.Flag));
            if (!UserAdminFlagCheck(rankFlags))
                return false;

            return _adminHierarchyManager.CanManageRank(Player, rank).Allowed;
        }

        private bool ValidateAdminRevision(Admin admin, uint expectedRevision)
        {
            var currentRevision = ComputeAdminRevision(admin);
            if (currentRevision == expectedRevision)
                return true;

            SetNotice(PermissionsNoticeCode.StaleAdmin, admin.Title ?? admin.AdminRank?.Name ?? admin.UserId.ToString());
            _sawmill.Warning($"{Player} tried to modify stale admin data for {admin.UserId}.");
            return false;
        }

        private bool ValidateRankRevision(DbAdminRank rank, uint expectedRevision)
        {
            var currentRevision = ComputeRankRevision(rank);
            if (currentRevision == expectedRevision)
                return true;

            SetNotice(PermissionsNoticeCode.StaleRank, rank.Name);
            _sawmill.Warning($"{Player} tried to modify stale rank data for {rank.Name}.");
            return false;
        }

        private void SetNotice(PermissionsNoticeCode code, string? subject)
        {
            _noticeCode = code;
            _noticeSubject = subject;
        }

        private void ClearNotice()
        {
            SetNotice(PermissionsNoticeCode.None, null);
        }

        private static uint ComputeAdminRevision(Admin admin)
        {
            var hash = 17u;
            hash = Mix(hash, admin.UserId.GetHashCode());
            hash = Mix(hash, admin.AdminRankId ?? -1);
            hash = Mix(hash, admin.Title?.GetHashCode() ?? 0);
            hash = Mix(hash, admin.Suspended ? 1 : 0);
            hash = Mix(hash, admin.Deadminned ? 1 : 0);

            foreach (var flag in (admin.Flags ?? new List<AdminFlag>())
                         .OrderBy(flag => flag.Flag)
                         .ThenBy(flag => flag.Negative))
            {
                hash = Mix(hash, flag.Flag.GetHashCode());
                hash = Mix(hash, flag.Negative ? 1 : 0);
            }

            return hash;
        }

        private static uint ComputeRankRevision(DbAdminRank rank)
        {
            var hash = 17u;
            hash = Mix(hash, rank.Id);
            hash = Mix(hash, rank.Name.GetHashCode());
            hash = Mix(hash, rank.HierarchyLevel);

            foreach (var flag in rank.Flags.OrderBy(flag => flag.Flag))
            {
                hash = Mix(hash, flag.Flag.GetHashCode());
            }

            return hash;
        }

        private static uint Mix(uint current, int next)
        {
            unchecked
            {
                return (current * 31u) + (uint) next;
            }
        }
    }
}
