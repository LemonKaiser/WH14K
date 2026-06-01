using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Client.Administration.Managers;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.Eui;
using Content.Client.Stylesheets;
using Content.Shared.Administration;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using static Content.Shared.Administration.PermissionsEuiMsg;

namespace Content.Client.Administration.UI
{
    [UsedImplicitly]
    public sealed partial class PermissionsEui : BaseEui
    {
        private const int NoRank = -1;

        private static readonly Color NoticeInfo = Color.FromHex("#D8A95B");
        private static readonly Color NoticeWarning = Color.FromHex("#D97B66");
        private static readonly Color MetaTextColor = Color.FromHex("#C7D0E2");

        private static readonly StyleBoxFlat SectionPanelStyle = CreatePanelStyle(Color.FromHex("#1E2230"), Color.FromHex("#49526A"));
        private static readonly StyleBoxFlat ListPanelStyle = CreatePanelStyle(Color.FromHex("#1A1D28"), Color.FromHex("#3A4053"));
        private static readonly StyleBoxFlat RowPanelStyle = CreatePanelStyle(Color.FromHex("#232734"), Color.FromHex("#3F465A"));
        private static readonly StyleBoxFlat SelectedRowPanelStyle = CreatePanelStyle(Color.FromHex("#2C3448"), Color.FromHex("#7B91C1"));
        private static readonly StyleBoxFlat LockedRowPanelStyle = CreatePanelStyle(Color.FromHex("#2A2832"), Color.FromHex("#675D72"));
        private static readonly StyleBoxFlat NoticePanelStyle = CreatePanelStyle(Color.FromHex("#2B2834"), Color.FromHex("#6F5A47"));
        private static readonly StyleBoxFlat BadgeNeutralStyle = CreateBadgeStyle(Color.FromHex("#2E3441"), Color.FromHex("#596175"));
        private static readonly StyleBoxFlat BadgeInfoStyle = CreateBadgeStyle(Color.FromHex("#283345"), Color.FromHex("#54739B"));
        private static readonly StyleBoxFlat BadgeSuccessStyle = CreateBadgeStyle(Color.FromHex("#21352A"), Color.FromHex("#4E8E63"));
        private static readonly StyleBoxFlat BadgeWarningStyle = CreateBadgeStyle(Color.FromHex("#453126"), Color.FromHex("#9C6B42"));
        private static readonly StyleBoxFlat BadgeDangerStyle = CreateBadgeStyle(Color.FromHex("#452B2B"), Color.FromHex("#A55A5A"));
        private static readonly StyleBoxFlat BadgeHostStyle = CreateBadgeStyle(Color.FromHex("#40292D"), Color.FromHex("#B3606A"));
        private static readonly StyleBoxFlat FlagModePanelStyle = CreatePanelStyle(Color.FromHex("#1B202B"), Color.FromHex("#48546E"));
        private static readonly StyleBoxFlat FlagModeIdleStyle = CreateBadgeStyle(Color.FromHex("#2A303C"), Color.FromHex("#5A6378"));
        private static readonly StyleBoxFlat FlagModeNeutralStyle = CreateBadgeStyle(Color.FromHex("#334051"), Color.FromHex("#7385A5"));
        private static readonly StyleBoxFlat FlagModePositiveStyle = CreateBadgeStyle(Color.FromHex("#254032"), Color.FromHex("#5D9E74"));
        private static readonly StyleBoxFlat FlagModeNegativeStyle = CreateBadgeStyle(Color.FromHex("#462C2C"), Color.FromHex("#B56C6C"));

        [Dependency] private IClientAdminManager _adminManager = default!;

        private readonly PermissionsWindow _window;
        private readonly List<BaseWindow> _dialogs = new();

        private PermissionsEuiState _state = new();
        private AdminSortMode _adminSortMode = AdminSortMode.Hierarchy;
        private RankSortMode _rankSortMode = RankSortMode.Hierarchy;

        private NetUserId? _selectedAdminId;
        private bool _creatingAdmin;
        private int? _selectedRankId;
        private bool _creatingRank;
        private string? _pendingAdminLookup;
        private string? _pendingRankName;

        public PermissionsEui()
        {
            IoCManager.InjectDependencies(this);

            _window = new PermissionsWindow();
            _window.OnClose += () => SendMessage(new CloseEuiMessage());

            InitializeSortButtons();
            HookEvents();
        }

        public override void Opened()
        {
            _window.OpenCentered();
        }

        public override void Closed()
        {
            base.Closed();

            CloseDialogs();
            _window.ClearTimers();
            _window.Close();
        }

        public override void HandleState(EuiStateBase state)
        {
            _state = (PermissionsEuiState) state;

            _window.SetLoading(_state.IsLoading);
            _window.SetNotice(FormatNotice(_state.NoticeCode, _state.NoticeSubject), GetNoticeColor(_state.NoticeCode));

            if (_state.IsLoading)
                return;

            ResolvePendingSelections();
            EnsureSelections();
            RefreshAdminList();
            RefreshRankList();
            SyncAdminEditor();
            SyncRankEditor();
        }

        private void HookEvents()
        {
            _window.AdminSearch.OnTextChanged += _ => RefreshAdminList();
            _window.AdminOnlineOnly.OnToggled += _ => RefreshAdminList();
            _window.AdminEditableOnly.OnToggled += _ => RefreshAdminList();
            _window.AdminSort.OnItemSelected += args =>
            {
                _adminSortMode = (AdminSortMode) args.Id;
                _window.AdminSort.SelectId(args.Id);
                RefreshAdminList();
            };
            _window.AddAdminButton.OnPressed += _ => RequestAdminContext(null, true);

            _window.RankSearch.OnTextChanged += _ => RefreshRankList();
            _window.RankEditableOnly.OnToggled += _ => RefreshRankList();
            _window.RankAssignableOnly.OnToggled += _ => RefreshRankList();
            _window.RankSort.OnItemSelected += args =>
            {
                _rankSortMode = (RankSortMode) args.Id;
                _window.RankSort.SelectId(args.Id);
                RefreshRankList();
            };
            _window.AddRankButton.OnPressed += _ => RequestRankContext(null, true);

            _window.AdminEditor.SavePressed += SaveAdmin;
            _window.AdminEditor.ResetPressed += ResetAdminEditor;
            _window.AdminEditor.RemovePressed += RemoveAdmin;

            _window.RankEditor.SavePressed += SaveRank;
            _window.RankEditor.ResetPressed += ResetRankEditor;
            _window.RankEditor.RemovePressed += RemoveRank;
        }

        private void InitializeSortButtons()
        {
            _window.AdminSort.AddItem(Loc.GetString("permissions-eui-admin-sort-hierarchy"), (int) AdminSortMode.Hierarchy);
            _window.AdminSort.AddItem(Loc.GetString("permissions-eui-admin-sort-name"), (int) AdminSortMode.Name);
            _window.AdminSort.AddItem(Loc.GetString("permissions-eui-admin-sort-online"), (int) AdminSortMode.Online);
            _window.AdminSort.AddItem(Loc.GetString("permissions-eui-admin-sort-last-seen"), (int) AdminSortMode.LastSeen);
            _window.AdminSort.SelectId((int) _adminSortMode);

            _window.RankSort.AddItem(Loc.GetString("permissions-eui-rank-sort-hierarchy"), (int) RankSortMode.Hierarchy);
            _window.RankSort.AddItem(Loc.GetString("permissions-eui-rank-sort-name"), (int) RankSortMode.Name);
            _window.RankSort.AddItem(Loc.GetString("permissions-eui-rank-sort-usage"), (int) RankSortMode.Usage);
            _window.RankSort.SelectId((int) _rankSortMode);
        }

        private void ResolvePendingSelections()
        {
            if (_pendingAdminLookup != null)
            {
                var pending = _pendingAdminLookup;
                var match = _state.Admins.FirstOrDefault(admin =>
                    string.Equals(GetAdminName(admin), pending, StringComparison.CurrentCultureIgnoreCase)
                    || string.Equals(admin.UserId.ToString(), pending, StringComparison.CurrentCultureIgnoreCase));

                if (!match.Equals(default(PermissionsEuiState.AdminData)))
                {
                    _pendingAdminLookup = null;
                    _creatingAdmin = false;
                    _selectedAdminId = match.UserId;
                }
            }

            if (_pendingRankName != null)
            {
                var pending = _pendingRankName;
                var match = _state.AdminRanks.FirstOrDefault(rank =>
                    string.Equals(rank.Value.Name, pending, StringComparison.CurrentCultureIgnoreCase));

                if (!match.Equals(default(KeyValuePair<int, PermissionsEuiState.AdminRankData>)))
                {
                    _pendingRankName = null;
                    _creatingRank = false;
                    _selectedRankId = match.Key;
                }
            }
        }

        private void EnsureSelections()
        {
            if (!_creatingAdmin)
            {
                if (_selectedAdminId == null || !_state.Admins.Any(admin => admin.UserId == _selectedAdminId))
                {
                    if (_state.Admins.Length > 0)
                    {
                        _selectedAdminId = _state.Admins
                            .OrderBy(admin => admin.EffectiveHierarchyLevel)
                            .ThenBy(GetAdminName, StringComparer.CurrentCultureIgnoreCase)
                            .First().UserId;
                    }
                    else
                    {
                        _creatingAdmin = true;
                        _selectedAdminId = null;
                    }
                }
            }

            if (!_creatingRank)
            {
                if (_selectedRankId == null || !_state.AdminRanks.ContainsKey(_selectedRankId.Value))
                {
                    if (_state.AdminRanks.Count > 0)
                    {
                        _selectedRankId = _state.AdminRanks
                            .OrderBy(rank => rank.Value.HierarchyLevel)
                            .ThenBy(rank => rank.Value.Name, StringComparer.CurrentCultureIgnoreCase)
                            .First().Key;
                    }
                    else
                    {
                        _creatingRank = true;
                        _selectedRankId = null;
                    }
                }
            }
        }

        private void RefreshAdminList()
        {
            _window.AdminList.DisposeAllChildren();

            if (_state.IsLoading)
                return;

            var filtered = GetFilteredAdmins().ToArray();
            var total = _state.Admins.Length;
            var online = _state.Admins.Count(admin => admin.IsOnline);

            _window.AdminCounter.Text = Loc.GetString("permissions-eui-admin-counter",
                ("shown", filtered.Length),
                ("total", total),
                ("online", online));

            if (filtered.Length == 0)
            {
                _window.AdminList.AddChild(CreateEmptyLabel("permissions-eui-empty-admins"));
                return;
            }

            foreach (var admin in filtered)
            {
                var selected = !_creatingAdmin && _selectedAdminId == admin.UserId;
                _window.AdminList.AddChild(CreateAdminRow(admin, selected));
            }
        }

        private void RefreshRankList()
        {
            _window.RankList.DisposeAllChildren();

            if (_state.IsLoading)
                return;

            var filtered = GetFilteredRanks().ToArray();
            var total = _state.AdminRanks.Count;
            var assignable = _state.AdminRanks.Values.Count(rank => rank.CanAssign);

            _window.RankCounter.Text = Loc.GetString("permissions-eui-rank-counter",
                ("shown", filtered.Length),
                ("total", total),
                ("assignable", assignable));

            if (filtered.Length == 0)
            {
                _window.RankList.AddChild(CreateEmptyLabel("permissions-eui-empty-ranks"));
                return;
            }

            foreach (var (rankId, rank) in filtered)
            {
                var selected = !_creatingRank && _selectedRankId == rankId;
                _window.RankList.AddChild(CreateRankRow(rankId, rank, selected));
            }
        }

        private void SyncAdminEditor(bool force = false)
        {
            if (_creatingAdmin)
            {
                if (_window.AdminEditor.ShouldLoad(null, true, force))
                    _window.AdminEditor.Load(this, null, true);

                return;
            }

            if (_selectedAdminId == null || !TryGetAdmin(_selectedAdminId.Value, out var admin))
            {
                _window.AdminEditor.Load(this, null, true);
                return;
            }

            if (_window.AdminEditor.ShouldLoad(admin, false, force))
                _window.AdminEditor.Load(this, admin, false);
        }

        private void SyncRankEditor(bool force = false)
        {
            if (_creatingRank)
            {
                if (_window.RankEditor.ShouldLoad(null, null, true, force))
                    _window.RankEditor.Load(this, null, null, true);

                return;
            }

            if (_selectedRankId == null || !TryGetRank(_selectedRankId.Value, out var rank))
            {
                _window.RankEditor.Load(this, null, null, true);
                return;
            }

            if (_window.RankEditor.ShouldLoad(_selectedRankId, rank, false, force))
                _window.RankEditor.Load(this, _selectedRankId, rank, false);
        }

        private void RequestAdminContext(NetUserId? adminId, bool isNew)
        {
            if (_creatingAdmin == isNew && _selectedAdminId == adminId)
                return;

            if (_window.AdminEditor.IsDirty)
            {
                OpenConfirmation(
                    Loc.GetString("permissions-eui-confirm-discard-admin-title"),
                    Loc.GetString("permissions-eui-confirm-discard-admin-body"),
                    () => SetAdminContext(adminId, isNew));
                return;
            }

            SetAdminContext(adminId, isNew);
        }

        private void RequestRankContext(int? rankId, bool isNew)
        {
            if (_creatingRank == isNew && _selectedRankId == rankId)
                return;

            if (_window.RankEditor.IsDirty)
            {
                OpenConfirmation(
                    Loc.GetString("permissions-eui-confirm-discard-rank-title"),
                    Loc.GetString("permissions-eui-confirm-discard-rank-body"),
                    () => SetRankContext(rankId, isNew));
                return;
            }

            SetRankContext(rankId, isNew);
        }

        private void SetAdminContext(NetUserId? adminId, bool isNew)
        {
            _creatingAdmin = isNew;
            _selectedAdminId = adminId;
            SyncAdminEditor();
            RefreshAdminList();
        }

        private void SetRankContext(int? rankId, bool isNew)
        {
            _creatingRank = isNew;
            _selectedRankId = rankId;
            SyncRankEditor();
            RefreshRankList();
        }

        private void SaveAdmin()
        {
            var draft = _window.AdminEditor.GetSnapshot();
            var rankId = draft.RankId;

            if (_creatingAdmin)
            {
                if (string.IsNullOrWhiteSpace(draft.NameOrId))
                    return;

                var lookup = draft.NameOrId.Trim();
                OpenConfirmation(
                    Loc.GetString("permissions-eui-confirm-add-admin-title"),
                    Loc.GetString("permissions-eui-confirm-add-admin-body", ("admin", lookup)),
                    () =>
                    {
                        _pendingAdminLookup = lookup;
                        SendMessage(new AddAdmin
                        {
                            UserNameOrId = lookup,
                            Title = draft.Title,
                            PosFlags = draft.PosFlags,
                            NegFlags = draft.NegFlags,
                            RankId = rankId,
                            Suspended = draft.Suspended,
                        });
                    });
                return;
            }

            if (_selectedAdminId == null || !TryGetAdmin(_selectedAdminId.Value, out var admin))
                return;

            OpenConfirmation(
                Loc.GetString("permissions-eui-confirm-save-admin-title"),
                Loc.GetString("permissions-eui-confirm-save-admin-body", ("admin", GetAdminName(admin))),
                () => SendMessage(new UpdateAdmin
                {
                    UserId = admin.UserId,
                    Title = draft.Title,
                    PosFlags = draft.PosFlags,
                    NegFlags = draft.NegFlags,
                    RankId = rankId,
                    Suspended = draft.Suspended,
                    ExpectedRevision = admin.Revision,
                }));
        }

        private void ResetAdminEditor()
        {
            if (!_window.AdminEditor.IsDirty)
                return;

            OpenConfirmation(
                Loc.GetString("permissions-eui-confirm-discard-admin-title"),
                Loc.GetString("permissions-eui-confirm-discard-admin-body"),
                () => SyncAdminEditor(force: true));
        }

        private void RemoveAdmin()
        {
            if (_selectedAdminId == null || !TryGetAdmin(_selectedAdminId.Value, out var admin))
                return;

            OpenConfirmation(
                Loc.GetString("permissions-eui-confirm-remove-admin-title"),
                Loc.GetString("permissions-eui-confirm-remove-admin-body", ("admin", GetAdminName(admin))),
                () => SendMessage(new RemoveAdmin
                {
                    UserId = admin.UserId,
                    ExpectedRevision = admin.Revision,
                }));
        }

        private void SaveRank()
        {
            var draft = _window.RankEditor.GetSnapshot();
            if (string.IsNullOrWhiteSpace(draft.Name))
                return;

            var name = draft.Name.Trim();
            if (_creatingRank)
            {
                OpenConfirmation(
                    Loc.GetString("permissions-eui-confirm-add-rank-title"),
                    Loc.GetString("permissions-eui-confirm-add-rank-body", ("rank", name)),
                    () =>
                    {
                        _pendingRankName = name;
                        SendMessage(new AddAdminRank
                        {
                            Name = name,
                            HierarchyLevel = draft.HierarchyLevel,
                            Flags = draft.Flags,
                        });
                    });
                return;
            }

            if (_selectedRankId == null || !TryGetRank(_selectedRankId.Value, out var rank))
                return;

            OpenConfirmation(
                Loc.GetString("permissions-eui-confirm-save-rank-title"),
                Loc.GetString("permissions-eui-confirm-save-rank-body", ("rank", name)),
                () => SendMessage(new UpdateAdminRank
                {
                    Id = _selectedRankId.Value,
                    Name = name,
                    HierarchyLevel = draft.HierarchyLevel,
                    Flags = draft.Flags,
                    ExpectedRevision = rank.Revision,
                }));
        }

        private void ResetRankEditor()
        {
            if (!_window.RankEditor.IsDirty)
                return;

            OpenConfirmation(
                Loc.GetString("permissions-eui-confirm-discard-rank-title"),
                Loc.GetString("permissions-eui-confirm-discard-rank-body"),
                () => SyncRankEditor(force: true));
        }

        private void RemoveRank()
        {
            if (_selectedRankId == null || !TryGetRank(_selectedRankId.Value, out var rank))
                return;

            OpenConfirmation(
                Loc.GetString("permissions-eui-confirm-remove-rank-title"),
                Loc.GetString("permissions-eui-confirm-remove-rank-body", ("rank", rank.Name)),
                () => SendMessage(new RemoveAdminRank
                {
                    Id = _selectedRankId.Value,
                    ExpectedRevision = rank.Revision,
                }));
        }

        private void OpenConfirmation(string title, string body, Action confirmed)
        {
            var dialog = new ConfirmationWindow(title, body);
            OpenDialog(dialog);
            dialog.ConfirmButton.OnPressed += _ =>
            {
                confirmed();
                dialog.Close();
            };
            dialog.CancelButton.OnPressed += _ => dialog.Close();
        }

        private void OpenDialog(BaseWindow dialog)
        {
            dialog.OnClose += () => _dialogs.Remove(dialog);
            _dialogs.Add(dialog);
            dialog.OpenCentered();
        }

        private void CloseDialogs()
        {
            foreach (var dialog in _dialogs.ToArray())
            {
                dialog.Close();
            }
        }

        private IEnumerable<PermissionsEuiState.AdminData> GetFilteredAdmins()
        {
            var query = _state.Admins.AsEnumerable();
            var search = _window.AdminSearch.Text.Trim();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(admin => MatchesAdminSearch(admin, search));

            if (_window.AdminOnlineOnly.Pressed)
                query = query.Where(admin => admin.IsOnline);

            if (_window.AdminEditableOnly.Pressed)
                query = query.Where(admin => admin.CanModify);

            query = _adminSortMode switch
            {
                AdminSortMode.Name => query.OrderBy(GetAdminName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(admin => admin.EffectiveHierarchyLevel),
                AdminSortMode.Online => query.OrderByDescending(admin => admin.IsOnline)
                    .ThenBy(admin => admin.EffectiveHierarchyLevel)
                    .ThenBy(GetAdminName, StringComparer.CurrentCultureIgnoreCase),
                AdminSortMode.LastSeen => query.OrderByDescending(GetAdminActivityTime)
                    .ThenBy(admin => admin.EffectiveHierarchyLevel)
                    .ThenBy(GetAdminName, StringComparer.CurrentCultureIgnoreCase),
                _ => query.OrderBy(admin => admin.EffectiveHierarchyLevel)
                    .ThenByDescending(admin => admin.IsHost)
                    .ThenBy(GetAdminName, StringComparer.CurrentCultureIgnoreCase),
            };

            return query;
        }

        private IEnumerable<KeyValuePair<int, PermissionsEuiState.AdminRankData>> GetFilteredRanks()
        {
            var query = _state.AdminRanks.AsEnumerable();
            var search = _window.RankSearch.Text.Trim();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(rank => MatchesRankSearch(rank.Value, search));

            if (_window.RankEditableOnly.Pressed)
                query = query.Where(rank => rank.Value.CanModify);

            if (_window.RankAssignableOnly.Pressed)
                query = query.Where(rank => rank.Value.CanAssign);

            query = _rankSortMode switch
            {
                RankSortMode.Name => query.OrderBy(rank => rank.Value.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(rank => rank.Value.HierarchyLevel),
                RankSortMode.Usage => query.OrderByDescending(rank => rank.Value.AssignedAdminCount)
                    .ThenBy(rank => rank.Value.HierarchyLevel)
                    .ThenBy(rank => rank.Value.Name, StringComparer.CurrentCultureIgnoreCase),
                _ => query.OrderBy(rank => rank.Value.HierarchyLevel)
                    .ThenBy(rank => rank.Value.Name, StringComparer.CurrentCultureIgnoreCase),
            };

            return query;
        }

        private Control CreateAdminRow(PermissionsEuiState.AdminData admin, bool selected)
        {
            var panel = new PanelContainer
            {
                PanelOverride = selected
                    ? new StyleBoxFlat(SelectedRowPanelStyle)
                    : new StyleBoxFlat(admin.CanModify ? RowPanelStyle : LockedRowPanelStyle),
                Margin = new Thickness(0, 0, 0, 6),
            };

            var button = new ContainerButton
            {
                HorizontalExpand = true,
                Children =
                {
                    new BoxContainer
                    {
                        Orientation = BoxContainer.LayoutOrientation.Vertical,
                        HorizontalExpand = true,
                        Margin = new Thickness(8),
                        SeparationOverride = 4,
                        Children =
                        {
                            BuildAdminRowHeader(admin)
                        }
                    }
                }
            };

            if (!admin.CanModify)
                button.ToolTip = Loc.GetString("permissions-eui-readonly-tooltip");

            button.OnPressed += _ => RequestAdminContext(admin.UserId, false);
            panel.AddChild(button);
            return panel;
        }

        private Control CreateRankRow(int rankId, PermissionsEuiState.AdminRankData rank, bool selected)
        {
            var panel = new PanelContainer
            {
                PanelOverride = selected
                    ? new StyleBoxFlat(SelectedRowPanelStyle)
                    : new StyleBoxFlat(rank.CanModify ? RowPanelStyle : LockedRowPanelStyle),
                Margin = new Thickness(0, 0, 0, 6),
            };

            var button = new ContainerButton
            {
                HorizontalExpand = true,
                Children =
                {
                    new BoxContainer
                    {
                        Orientation = BoxContainer.LayoutOrientation.Vertical,
                        HorizontalExpand = true,
                        Margin = new Thickness(8),
                        SeparationOverride = 4,
                        Children =
                        {
                            BuildRankRowHeader(rank)
                        }
                    }
                }
            };

            if (!rank.CanModify)
                button.ToolTip = Loc.GetString("permissions-eui-readonly-tooltip");

            button.OnPressed += _ => RequestRankContext(rankId, false);
            panel.AddChild(button);
            return panel;
        }

        private Control BuildAdminRowHeader(PermissionsEuiState.AdminData admin)
        {
            var root = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 4,
                HorizontalExpand = true,
            };

            root.AddChild(new Label
            {
                Text = GetAdminName(admin),
                HorizontalExpand = true,
                ClipText = true,
            });

            var badges = new WrapContainer
            {
                HorizontalExpand = true,
                SeparationOverride = 4,
                CrossSeparationOverride = 4,
            };

            foreach (var badge in GetAdminBadges(admin))
            {
                badges.AddChild(badge);
            }

            root.AddChild(badges);
            var statusBadges = new WrapContainer
            {
                HorizontalExpand = true,
                SeparationOverride = 4,
                CrossSeparationOverride = 4,
            };

            foreach (var badge in GetAdminSecondaryBadges(admin))
            {
                statusBadges.AddChild(badge);
            }

            if (statusBadges.ChildCount > 0)
                root.AddChild(statusBadges);

            root.AddChild(CreateWideBadge(GetFlagCountText(GetEffectiveFlags(admin)), BadgeNeutralStyle, GetEffectiveFlagsText(admin)));
            return root;
        }

        private Control BuildRankRowHeader(PermissionsEuiState.AdminRankData rank)
        {
            var root = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 4,
                HorizontalExpand = true,
            };

            root.AddChild(new Label
            {
                Text = rank.Name,
                HorizontalExpand = true,
                ClipText = true,
            });

            var badges = new WrapContainer
            {
                HorizontalExpand = true,
                SeparationOverride = 4,
                CrossSeparationOverride = 4,
            };

            badges.AddChild(CreateBadge(GetHierarchyText(rank.HierarchyLevel), BadgeInfoStyle));
            badges.AddChild(CreateBadge(Loc.GetString("permissions-eui-rank-badge-assigned", ("count", rank.AssignedAdminCount)), BadgeNeutralStyle));

            if (rank.CanAssign)
                badges.AddChild(CreateBadge(Loc.GetString("permissions-eui-rank-badge-assignable"), BadgeSuccessStyle));

            if (!rank.CanModify)
                badges.AddChild(CreateBadge(Loc.GetString("permissions-eui-badge-locked"), BadgeWarningStyle));

            root.AddChild(badges);
            root.AddChild(CreateWideBadge(GetFlagCountText(rank.Flags), BadgeNeutralStyle, GetFlagListText(rank.Flags)));
            return root;
        }

        private IEnumerable<Control> GetAdminBadges(PermissionsEuiState.AdminData admin)
        {
            if (admin.IsHost)
                yield return CreateBadge(Loc.GetString("permissions-eui-badge-host"), BadgeHostStyle);

            yield return CreateBadge(GetHierarchyText(admin.EffectiveHierarchyLevel), BadgeInfoStyle);

            if (admin.IsOnline)
                yield return CreateBadge(Loc.GetString("permissions-eui-badge-online"), BadgeSuccessStyle);

            if (admin.RankId == null && !admin.IsHost)
                yield return CreateBadge(Loc.GetString("permissions-eui-badge-no-rank"), BadgeNeutralStyle);

            if (!admin.CanModify)
                yield return CreateBadge(Loc.GetString("permissions-eui-badge-locked"), BadgeNeutralStyle);
        }

        private IEnumerable<Control> GetAdminSecondaryBadges(PermissionsEuiState.AdminData admin)
        {
            if (admin.Deadminned)
                yield return CreateBadge(Loc.GetString("permissions-eui-badge-deadmin"), BadgeWarningStyle);

            if (admin.Suspended)
                yield return CreateBadge(Loc.GetString("permissions-eui-badge-suspended"), BadgeDangerStyle);
        }

        private bool TryGetAdmin(NetUserId adminId, out PermissionsEuiState.AdminData admin)
        {
            foreach (var candidate in _state.Admins)
            {
                if (candidate.UserId == adminId)
                {
                    admin = candidate;
                    return true;
                }
            }

            admin = default;
            return false;
        }

        internal bool TryGetRank(int rankId, out PermissionsEuiState.AdminRankData rank)
        {
            return _state.AdminRanks.TryGetValue(rankId, out rank);
        }

        internal IEnumerable<KeyValuePair<int, PermissionsEuiState.AdminRankData>> GetRanksSorted()
        {
            return _state.AdminRanks
                .OrderBy(rank => rank.Value.HierarchyLevel)
                .ThenBy(rank => rank.Value.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        internal bool CanGrantFlag(AdminFlags flag)
        {
            return _adminManager.HasFlag(flag);
        }

        internal string GetAdminRankDisplay(PermissionsEuiState.AdminData? admin, int? selectedRankId = null)
        {
            if (admin?.IsHost == true)
                return Loc.GetString("permissions-eui-host-rank-display");

            var rankId = selectedRankId ?? admin?.RankId;
            if (rankId is { } id && TryGetRank(id, out var rank))
                return $"{rank.Name} ({GetHierarchyText(rank.HierarchyLevel)})";

            return Loc.GetString("permissions-eui-edit-no-rank-text");
        }

        internal byte GetEffectiveHierarchy(PermissionsEuiState.AdminData? admin, int? selectedRankId)
        {
            if (admin?.IsHost == true)
                return AdminHierarchy.HostHierarchyLevel;

            if (selectedRankId is { } id && TryGetRank(id, out var rank))
                return rank.HierarchyLevel;

            return AdminHierarchy.DefaultHierarchyLevel;
        }

        internal string GetActivityText(PermissionsEuiState.AdminData? admin)
        {
            if (admin == null)
                return string.Empty;

            if (admin.Value.IsOnline && admin.Value.OnlineSinceUtc != null)
                return Loc.GetString("permissions-eui-meta-online-since", ("time", FormatRelativeTime(admin.Value.OnlineSinceUtc)));

            if (admin.Value.LastSeenTimeUtc == null)
                return string.Empty;

            if (IsRelativeTimeImmediate(admin.Value.LastSeenTimeUtc.Value))
                return Loc.GetString("permissions-eui-meta-last-seen-now");

            return Loc.GetString("permissions-eui-meta-last-seen", ("time", FormatRelativeTime(admin.Value.LastSeenTimeUtc)));
        }

        private bool MatchesAdminSearch(PermissionsEuiState.AdminData admin, string search)
        {
            if (GetAdminName(admin).Contains(search, StringComparison.CurrentCultureIgnoreCase))
                return true;

            if (admin.Title?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true)
                return true;

            if (GetAdminRankDisplay(admin).Contains(search, StringComparison.CurrentCultureIgnoreCase))
                return true;

            return GetEffectiveFlagsText(admin).Contains(search, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool MatchesRankSearch(PermissionsEuiState.AdminRankData rank, string search)
        {
            if (rank.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                return true;

            return GetFlagListText(rank.Flags).Contains(search, StringComparison.CurrentCultureIgnoreCase);
        }

        private static string GetAdminName(PermissionsEuiState.AdminData admin)
        {
            return admin.UserName ?? admin.UserId.ToString();
        }

        private static string GetRankFlagsText(PermissionsEuiState.AdminRankData rank, bool countsOnly = false)
        {
            return countsOnly
                ? GetFlagCountText(rank.Flags)
                : Loc.GetString("permissions-eui-meta-effective", ("value", GetFlagListText(rank.Flags, compact: true)));
        }

        private string GetEffectiveFlagsText(PermissionsEuiState.AdminData admin, bool compact = false)
        {
            return GetFlagListText(GetEffectiveFlags(admin), compact);
        }

        private AdminFlags GetEffectiveFlags(PermissionsEuiState.AdminData admin)
        {
            var flags = admin.PosFlags;

            if (admin.RankId is { } rankId && TryGetRank(rankId, out var rank))
                flags |= rank.Flags;

            return flags & ~admin.NegFlags;
        }

        internal static string GetFlagListText(AdminFlags flags, bool compact = false)
        {
            var names = AdminFlagsHelper.FlagsToNames(flags).Select(flag => $"+{flag}").ToArray();
            if (names.Length == 0)
                return Loc.GetString("permissions-eui-flags-none");

            if (!compact)
                return string.Join(' ', names);

            const int previewCount = 2;
            var preview = string.Join(' ', names.Take(previewCount));
            var remaining = names.Length - previewCount;

            return remaining > 0
                ? Loc.GetString("permissions-eui-flags-preview-more", ("count", names.Length), ("preview", preview), ("remaining", remaining))
                : Loc.GetString("permissions-eui-flags-preview", ("count", names.Length), ("preview", preview));
        }

        internal static string GetFlagCountText(AdminFlags flags)
        {
            var count = GetFlagCount(flags);
            return count == 0
                ? Loc.GetString("permissions-eui-flags-none-count")
                : Loc.GetString("permissions-eui-flags-count", ("count", count));
        }

        internal static int GetFlagCount(AdminFlags flags)
        {
            return AdminFlagsHelper.FlagsToNames(flags).Count();
        }

        internal static string GetHierarchyText(byte level)
        {
            return Loc.GetString("permissions-eui-hierarchy-badge", ("level", level));
        }

        private static DateTime GetAdminActivityTime(PermissionsEuiState.AdminData admin)
        {
            if (admin.IsOnline && admin.OnlineSinceUtc != null)
                return admin.OnlineSinceUtc.Value;

            return admin.LastSeenTimeUtc ?? DateTime.MinValue;
        }

        private string FormatRelativeTime(DateTime? timeUtc)
        {
            if (timeUtc == null)
                return Loc.GetString("permissions-eui-time-unknown");

            var span = GetRelativeTimeSpan(timeUtc.Value);

            if (span < TimeSpan.FromMinutes(1))
                return Loc.GetString("permissions-eui-time-just-now");

            if (span < TimeSpan.FromHours(1))
                return Loc.GetString("permissions-eui-time-minutes", ("count", Math.Max(1, (int) span.TotalMinutes)));

            if (span < TimeSpan.FromDays(1))
                return Loc.GetString("permissions-eui-time-hours", ("count", Math.Max(1, (int) span.TotalHours)));

            if (span < TimeSpan.FromDays(30))
                return Loc.GetString("permissions-eui-time-days", ("count", Math.Max(1, (int) span.TotalDays)));

            if (span < TimeSpan.FromDays(365))
                return Loc.GetString("permissions-eui-time-months", ("count", Math.Max(1, (int) (span.TotalDays / 30))));

            return Loc.GetString("permissions-eui-time-years", ("count", Math.Max(1, (int) (span.TotalDays / 365))));
        }

        private bool IsRelativeTimeImmediate(DateTime timeUtc)
        {
            return GetRelativeTimeSpan(timeUtc) < TimeSpan.FromMinutes(1);
        }

        private TimeSpan GetRelativeTimeSpan(DateTime timeUtc)
        {
            var reference = _state.ServerTimeUtc == default ? DateTime.UtcNow : _state.ServerTimeUtc;
            var span = reference - timeUtc;

            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;

            return span;
        }

        private string? FormatNotice(PermissionsNoticeCode code, string? subject)
        {
            if (code == PermissionsNoticeCode.None)
                return null;

            var value = subject ?? Loc.GetString("permissions-eui-notice-generic-subject");
            return code switch
            {
                PermissionsNoticeCode.StaleAdmin => Loc.GetString("permissions-eui-notice-stale-admin", ("subject", value)),
                PermissionsNoticeCode.StaleRank => Loc.GetString("permissions-eui-notice-stale-rank", ("subject", value)),
                PermissionsNoticeCode.ProtectedAdmin => Loc.GetString("permissions-eui-notice-protected-admin", ("subject", value)),
                PermissionsNoticeCode.ProtectedRank => Loc.GetString("permissions-eui-notice-protected-rank", ("subject", value)),
                PermissionsNoticeCode.InvalidHierarchy => Loc.GetString("permissions-eui-notice-invalid-hierarchy", ("subject", value)),
                PermissionsNoticeCode.RankNotAssignable => Loc.GetString("permissions-eui-notice-rank-not-assignable", ("subject", value)),
                PermissionsNoticeCode.UnknownUser => Loc.GetString("permissions-eui-notice-unknown-user", ("subject", value)),
                PermissionsNoticeCode.AlreadyExists => Loc.GetString("permissions-eui-notice-already-exists", ("subject", value)),
                _ => null
            };
        }

        private static Color GetNoticeColor(PermissionsNoticeCode code)
        {
            return code switch
            {
                PermissionsNoticeCode.StaleAdmin or PermissionsNoticeCode.StaleRank => NoticeInfo,
                PermissionsNoticeCode.None => Color.White,
                _ => NoticeWarning,
            };
        }

        private static Control CreateBadge(string text, StyleBoxFlat style)
        {
            var panel = new PanelContainer
            {
                PanelOverride = new StyleBoxFlat(style),
                VerticalAlignment = Control.VAlignment.Center,
            };

            panel.AddChild(new Label
            {
                Text = text,
                Margin = new Thickness(8, 2),
                FontColorOverride = Color.White,
            });

            return panel;
        }

        private static Control CreateWideBadge(string text, StyleBoxFlat style, string? toolTip = null)
        {
            var panel = new PanelContainer
            {
                PanelOverride = new StyleBoxFlat(style),
                HorizontalExpand = true,
                ToolTip = toolTip,
            };

            panel.AddChild(new Label
            {
                Text = text,
                Margin = new Thickness(8, 3),
                FontColorOverride = Color.White,
                Align = Label.AlignMode.Center,
                HorizontalExpand = true,
            });

            return panel;
        }

        private static Control CreateEmptyLabel(string key)
        {
            return new Label
            {
                Text = Loc.GetString(key),
                FontColorOverride = StyleNano.DisabledFore,
                Margin = new Thickness(4),
            };
        }

        private static RichTextLabel CreateWrappedText(string text, Color? color = null, string? toolTip = null)
        {
            var label = new RichTextLabel
            {
                HorizontalExpand = true,
                VerticalAlignment = Control.VAlignment.Top,
                ToolTip = toolTip,
            };
            label.SetMessage(text, color);
            return label;
        }

        private static StyleBoxFlat CreatePanelStyle(Color background, Color border)
        {
            return new StyleBoxFlat
            {
                BackgroundColor = background,
                BorderColor = border,
                BorderThickness = new Thickness(1),
                ContentMarginBottomOverride = 0,
                ContentMarginTopOverride = 0,
                ContentMarginLeftOverride = 0,
                ContentMarginRightOverride = 0,
            };
        }

        private static StyleBoxFlat CreateBadgeStyle(Color background, Color border)
        {
            return new StyleBoxFlat
            {
                BackgroundColor = background,
                BorderColor = border,
                BorderThickness = new Thickness(1),
                ContentMarginBottomOverride = 0,
                ContentMarginTopOverride = 0,
                ContentMarginLeftOverride = 0,
                ContentMarginRightOverride = 0,
            };
        }

        private enum AdminSortMode
        {
            Hierarchy = 1,
            Name = 2,
            Online = 3,
            LastSeen = 4,
        }

        private enum RankSortMode
        {
            Hierarchy = 1,
            Name = 2,
            Usage = 3,
        }

        private sealed class PermissionsWindow : DefaultWindow
        {
            private const int NoticeAutoHideDelayMs = 5000;

            public readonly LineEdit AdminSearch;
            public readonly OptionButton AdminSort;
            public readonly CheckBox AdminOnlineOnly;
            public readonly CheckBox AdminEditableOnly;
            public readonly Label AdminCounter;
            public readonly BoxContainer AdminList;
            public readonly Button AddAdminButton;
            public readonly AdminEditorView AdminEditor;

            public readonly LineEdit RankSearch;
            public readonly OptionButton RankSort;
            public readonly CheckBox RankEditableOnly;
            public readonly CheckBox RankAssignableOnly;
            public readonly Label RankCounter;
            public readonly BoxContainer RankList;
            public readonly Button AddRankButton;
            public readonly RankEditorView RankEditor;

            private readonly PanelContainer _noticePanel;
            private readonly Label _noticeLabel;
            private readonly Label _loadingLabel;
            private CancellationTokenSource _noticeHideCancel = new();

            public PermissionsWindow()
            {
                Title = Loc.GetString("permissions-eui-menu-title");
                MinSize = new Vector2(700, 560);
                SetSize = new Vector2(860, 680);

                _noticeLabel = new Label();
                _loadingLabel = new Label
                {
                    Text = Loc.GetString("permissions-eui-loading"),
                    Visible = false,
                };
                _noticePanel = new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat(NoticePanelStyle),
                    Visible = false,
                };
                _noticePanel.AddChild(new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    Margin = new Thickness(10, 7),
                    Children =
                    {
                        _noticeLabel,
                        new Control { HorizontalExpand = true },
                        _loadingLabel,
                    }
                });

                AdminSearch = new LineEdit
                {
                    PlaceHolder = Loc.GetString("permissions-eui-admin-search-placeholder"),
                    HorizontalExpand = true,
                };
                AdminSort = new PermissionsOptionButton();
                AdminSort.MinSize = new Vector2(150, 0);
                AdminOnlineOnly = new CheckBox { Text = Loc.GetString("permissions-eui-filter-online-only") };
                AdminEditableOnly = new CheckBox { Text = Loc.GetString("permissions-eui-filter-editable-only") };
                AdminCounter = new Label { FontColorOverride = MetaTextColor, ClipText = true };
                AdminList = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    VerticalExpand = true,
                    HorizontalExpand = true,
                };
                AddAdminButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-menu-add-admin-button"),
                    HorizontalAlignment = HAlignment.Stretch,
                    MinSize = new Vector2(210, 0),
                    ClipText = true,
                };
                AdminEditor = new AdminEditorView();

                RankSearch = new LineEdit
                {
                    PlaceHolder = Loc.GetString("permissions-eui-rank-search-placeholder"),
                    HorizontalExpand = true,
                };
                RankSort = new PermissionsOptionButton();
                RankSort.MinSize = new Vector2(150, 0);
                RankEditableOnly = new CheckBox { Text = Loc.GetString("permissions-eui-filter-editable-only") };
                RankAssignableOnly = new CheckBox { Text = Loc.GetString("permissions-eui-filter-assignable-only") };
                RankCounter = new Label { FontColorOverride = MetaTextColor, ClipText = true };
                RankList = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    VerticalExpand = true,
                    HorizontalExpand = true,
                };
                AddRankButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-menu-add-admin-rank-button"),
                    HorizontalAlignment = HAlignment.Stretch,
                    MinSize = new Vector2(170, 0),
                    ClipText = true,
                };
                RankEditor = new RankEditorView();

                var tabs = new TabContainer
                {
                    HorizontalExpand = true,
                    VerticalExpand = true,
                };
                var adminTab = CreateSplitTab(
                    AdminSearch,
                    AdminSort,
                    AdminOnlineOnly,
                    AdminEditableOnly,
                    null,
                    AdminCounter,
                    AdminList,
                    AddAdminButton,
                    AdminEditor);
                var rankTab = CreateSplitTab(
                    RankSearch,
                    RankSort,
                    RankEditableOnly,
                    RankAssignableOnly,
                    null,
                    RankCounter,
                    RankList,
                    AddRankButton,
                    RankEditor);

                TabContainer.SetTabTitle(adminTab, Loc.GetString("permissions-eui-menu-admins-tab-title"));
                TabContainer.SetTabTitle(rankTab, Loc.GetString("permissions-eui-menu-admin-ranks-tab-title"));

                tabs.AddChild(adminTab);
                tabs.AddChild(rankTab);

                Contents.AddChild(new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    Margin = new Thickness(10),
                    SeparationOverride = 8,
                    HorizontalExpand = true,
                    VerticalExpand = true,
                    Children =
                    {
                        _noticePanel,
                        tabs,
                    }
                });
            }

            public void SetLoading(bool loading)
            {
                _loadingLabel.Visible = loading;
                _noticePanel.Visible = loading || !string.IsNullOrWhiteSpace(_noticeLabel.Text);
            }

            public void SetNotice(string? text, Color color)
            {
                CancelNoticeTimer();
                _noticeLabel.Text = text ?? string.Empty;
                _noticeLabel.FontColorOverride = color;
                _noticePanel.Visible = _loadingLabel.Visible || !string.IsNullOrWhiteSpace(text);

                if (!string.IsNullOrWhiteSpace(text))
                    StartNoticeTimer();
            }

            public void ClearTimers()
            {
                CancelNoticeTimer();
            }

            private void StartNoticeTimer()
            {
                _noticeHideCancel = new CancellationTokenSource();
                Robust.Shared.Timing.Timer.Spawn(NoticeAutoHideDelayMs, HideNotice, _noticeHideCancel.Token);
            }

            private void CancelNoticeTimer()
            {
                _noticeHideCancel.Cancel();
                _noticeHideCancel.Dispose();
                _noticeHideCancel = new CancellationTokenSource();
            }

            private void HideNotice()
            {
                _noticeLabel.Text = string.Empty;
                _noticePanel.Visible = _loadingLabel.Visible;
            }

            private static Control CreateSplitTab(
                LineEdit search,
                OptionButton sort,
                CheckBox firstFilter,
                CheckBox secondFilter,
                CheckBox? thirdFilter,
                Label counter,
                BoxContainer list,
                Button actionButton,
                Control editor)
            {
                var topRow = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 6,
                    Children =
                    {
                        search,
                        sort,
                    }
                };

                var filterRow = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 10,
                    Children =
                    {
                        firstFilter,
                        secondFilter,
                    }
                };

                if (thirdFilter != null)
                    filterRow.AddChild(thirdFilter);

                filterRow.AddChild(new Control { HorizontalExpand = true });
                filterRow.AddChild(counter);
                filterRow.AddChild(actionButton);

                var leftPanel = new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat(ListPanelStyle),
                    MinSize = new Vector2(240, 0),
                    MaxWidth = 320,
                    HorizontalExpand = false,
                    VerticalExpand = true,
                    Children =
                    {
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Vertical,
                            Margin = new Thickness(8),
                            HorizontalExpand = true,
                            VerticalExpand = true,
                            Children =
                            {
                                new ScrollContainer
                                {
                                    HorizontalExpand = true,
                                    VerticalExpand = true,
                                    HScrollEnabled = false,
                                    Children = { list }
                                }
                            }
                        }
                    }
                };

                return new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    SeparationOverride = 8,
                    HorizontalExpand = true,
                    VerticalExpand = true,
                    Children =
                    {
                        topRow,
                        filterRow,
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 10,
                            VerticalExpand = true,
                            Children =
                            {
                                leftPanel,
                                new VSeparator(),
                                editor,
                            }
                        }
                    }
                };
            }
        }

        private sealed class AdminEditorView : PanelContainer
        {
            public event Action? SavePressed;
            public event Action? ResetPressed;
            public event Action? RemovePressed;

            private readonly Label _subjectLabel;
            private readonly WrapContainer _badgeRow;
            private readonly WrapContainer _statusBadgeRow;
            private readonly LineEdit _nameEdit;
            private readonly LineEdit _titleEdit;
            private readonly OptionButton _rankButton;
            private readonly CheckBox _suspendedCheck;
            private readonly RichTextLabel _rankPreview;
            private readonly HSeparator _activitySeparator;
            private readonly HSeparator _activityFooterSeparator;
            private readonly RichTextLabel _effectiveFlags;
            private readonly RichTextLabel _activityLabel;
            private readonly RichTextLabel _positiveFlags;
            private readonly RichTextLabel _negativeFlags;
            private readonly Button _editFlagsButton;
            private readonly Button _resetButton;
            private readonly Button _removeButton;
            private readonly Button _saveButton;

            private PermissionsEui? _owner;
            private PermissionsEuiState.AdminData? _sourceData;
            private AdminEditorSnapshot _originalSnapshot;
            private NetUserId? _contextUserId;
            private bool _isNew;
            private bool _suppressEvents;
            private bool _canModify;
            private AdminFlags _positiveFlagsValue;
            private AdminFlags _negativeFlagsValue;

            public bool IsDirty => GetSnapshot() != _originalSnapshot;

            public AdminEditorView()
            {
                PanelOverride = new StyleBoxFlat(SectionPanelStyle);
                HorizontalExpand = true;
                VerticalExpand = true;

                _subjectLabel = new Label
                {
                    FontColorOverride = Color.White,
                };
                _badgeRow = new WrapContainer
                {
                    HorizontalExpand = true,
                    SeparationOverride = 4,
                    CrossSeparationOverride = 4,
                };
                _statusBadgeRow = new WrapContainer
                {
                    HorizontalExpand = true,
                    SeparationOverride = 4,
                    CrossSeparationOverride = 4,
                    Visible = false,
                };

                _nameEdit = new LineEdit
                {
                    PlaceHolder = Loc.GetString("permissions-eui-edit-admin-window-name-edit-placeholder"),
                };
                _nameEdit.OnTextChanged += _ => OnInputChanged();

                _titleEdit = new LineEdit
                {
                    PlaceHolder = Loc.GetString("permissions-eui-edit-admin-window-title-edit-placeholder"),
                };
                _titleEdit.OnTextChanged += _ => OnInputChanged();

                _rankButton = new PermissionsOptionButton
                {
                    Filterable = true,
                };
                _rankButton.OnItemSelected += args =>
                {
                    _rankButton.SelectId(args.Id);
                    OnInputChanged();
                };

                _suspendedCheck = new CheckBox
                {
                    Text = Loc.GetString("permissions-eui-edit-admin-window-suspended"),
                };
                _suspendedCheck.OnToggled += _ => OnInputChanged();

                _rankPreview = CreateWrappedText(string.Empty, MetaTextColor);
                _activitySeparator = new HSeparator();
                _activityFooterSeparator = new HSeparator();
                _effectiveFlags = CreateWrappedText(string.Empty, MetaTextColor);
                _activityLabel = CreateWrappedText(string.Empty, MetaTextColor);
                _positiveFlags = CreateWrappedText(string.Empty, MetaTextColor);
                _negativeFlags = CreateWrappedText(string.Empty, MetaTextColor);
                _editFlagsButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-edit-admin-flags-button"),
                    HorizontalAlignment = HAlignment.Left,
                };
                _editFlagsButton.OnPressed += _ => OpenFlagEditor();

                _resetButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-reset-button"),
                };
                _resetButton.OnPressed += _ => ResetPressed?.Invoke();

                _removeButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-edit-admin-window-remove-flag-button"),
                };
                _removeButton.OnPressed += _ => RemovePressed?.Invoke();

                _saveButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-edit-admin-window-save-button"),
                };
                _saveButton.OnPressed += _ => SavePressed?.Invoke();

                var body = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    SeparationOverride = 8,
                    HorizontalExpand = true,
                    Children =
                    {
                        _subjectLabel,
                        _badgeRow,
                        _statusBadgeRow,
                        new HSeparator(),
                        CreateSection(
                            Loc.GetString("permissions-eui-section-admin-profile"),
                            new BoxContainer
                            {
                                Orientation = BoxContainer.LayoutOrientation.Vertical,
                                HorizontalExpand = true,
                                VerticalExpand = true,
                                SeparationOverride = 6,
                                Children =
                                {
                                    _nameEdit,
                                    _titleEdit,
                                    _rankButton,
                                    _suspendedCheck,
                                    new BoxContainer
                                    {
                                        Orientation = BoxContainer.LayoutOrientation.Vertical,
                                        HorizontalExpand = true,
                                        SeparationOverride = 4,
                                        Children =
                                        {
                                            _rankPreview,
                                            _activitySeparator,
                                            _activityLabel,
                                            _activityFooterSeparator,
                                            _effectiveFlags,
                                            new HSeparator(),
                                            _positiveFlags,
                                            new HSeparator(),
                                            _negativeFlags,
                                        }
                                    },
                                    _editFlagsButton
                                }
                            },
                            true),
                    }
                };

                AddChild(new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    Margin = new Thickness(10),
                    SeparationOverride = 8,
                    HorizontalExpand = true,
                    VerticalExpand = true,
                    Children =
                    {
                        new ScrollContainer
                        {
                            HorizontalExpand = true,
                            VerticalExpand = true,
                            HScrollEnabled = false,
                            Children = { body }
                        },
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 6,
                            Children =
                            {
                                _removeButton,
                                _resetButton,
                                new Control { HorizontalExpand = true },
                                _saveButton,
                            }
                        }
                    }
                });
            }

            public void Load(PermissionsEui owner, PermissionsEuiState.AdminData? sourceData, bool isNew)
            {
                _owner = owner;
                _sourceData = sourceData;
                _contextUserId = sourceData?.UserId;
                _isNew = isNew;
                _canModify = sourceData?.CanModify ?? true;

                PopulateRankOptions(owner, sourceData);

                var snapshot = CreateSnapshot(sourceData);

                ApplySnapshot(snapshot, sourceData);
                _originalSnapshot = snapshot;

                _subjectLabel.Text = isNew
                    ? Loc.GetString("permissions-eui-new-admin-entry")
                    : GetAdminName(sourceData!.Value);

                _badgeRow.DisposeAllChildren();
                if (sourceData != null)
                {
                    foreach (var badge in owner.GetAdminBadges(sourceData.Value))
                    {
                        _badgeRow.AddChild(badge);
                    }
                }
                _badgeRow.Visible = _badgeRow.ChildCount > 0;

                _statusBadgeRow.DisposeAllChildren();
                if (sourceData != null)
                {
                    foreach (var badge in owner.GetAdminSecondaryBadges(sourceData.Value))
                    {
                        _statusBadgeRow.AddChild(badge);
                    }
                }
                _statusBadgeRow.Visible = _statusBadgeRow.ChildCount > 0;

                UpdateInputAvailability();
                UpdateSummary();
                UpdateActionState();
            }

            public bool IsSameContext(NetUserId? userId, bool isNew)
            {
                return _isNew == isNew && _contextUserId == userId;
            }

            public bool ShouldLoad(PermissionsEuiState.AdminData? sourceData, bool isNew, bool force)
            {
                if (force || !IsSameContext(sourceData?.UserId, isNew))
                    return true;

                if (!IsDirty)
                    return true;

                if (sourceData == null)
                    return false;

                return GetSnapshot() == CreateSnapshot(sourceData);
            }

            public AdminEditorSnapshot GetSnapshot()
            {
                var title = NormalizeOptionalText(_titleEdit.Text);
                var name = _isNew ? _nameEdit.Text.Trim() : string.Empty;
                int? rankId = _rankButton.SelectedId == NoRank ? null : _rankButton.SelectedId;
                return new AdminEditorSnapshot(name, title, rankId, _suspendedCheck.Pressed, _positiveFlagsValue, _negativeFlagsValue);
            }

            private static AdminEditorSnapshot CreateSnapshot(PermissionsEuiState.AdminData? sourceData)
            {
                return sourceData == null
                    ? new AdminEditorSnapshot(string.Empty, null, null, false, AdminFlags.None, AdminFlags.None)
                    : new AdminEditorSnapshot(string.Empty, sourceData.Value.Title, sourceData.Value.RankId, sourceData.Value.Suspended, sourceData.Value.PosFlags, sourceData.Value.NegFlags);
            }

            private void PopulateRankOptions(PermissionsEui owner, PermissionsEuiState.AdminData? sourceData)
            {
                _rankButton.Clear();
                _rankButton.AddItem(Loc.GetString("permissions-eui-edit-admin-window-no-rank-button"), NoRank);

                foreach (var (rankId, rank) in owner.GetRanksSorted())
                {
                    var text = $"{rank.Name} ({GetHierarchyText(rank.HierarchyLevel)})";
                    if (!rank.CanAssign)
                        text += $" {Loc.GetString("permissions-eui-locked-suffix")}";

                    _rankButton.AddItem(text, rankId);
                    var idx = _rankButton.GetIdx(rankId);
                    _rankButton.SetItemDisabled(idx, !rank.CanAssign && sourceData?.RankId != rankId);
                }
            }

            private void ApplySnapshot(AdminEditorSnapshot snapshot, PermissionsEuiState.AdminData? sourceData)
            {
                _suppressEvents = true;

                _nameEdit.Text = sourceData == null ? snapshot.NameOrId : GetAdminName(sourceData.Value);
                _nameEdit.Editable = sourceData == null;
                _titleEdit.Text = snapshot.Title ?? string.Empty;
                _suspendedCheck.Pressed = snapshot.Suspended;
                _positiveFlagsValue = snapshot.PosFlags;
                _negativeFlagsValue = snapshot.NegFlags;

                if (!_rankButton.TrySelectId(snapshot.RankId ?? NoRank))
                    _rankButton.SelectId(NoRank);

                _suppressEvents = false;
            }

            private void UpdateInputAvailability()
            {
                var nameEditable = _isNew && _canModify;
                _nameEdit.Editable = nameEditable;
                _titleEdit.Editable = _canModify;
                _rankButton.Disabled = !_canModify;
                _suspendedCheck.Disabled = !_canModify;
                _editFlagsButton.Disabled = !_canModify;
            }

            private void UpdateSummary()
            {
                if (_owner == null)
                    return;

                var snapshot = GetSnapshot();
                _rankPreview.SetMessage(Loc.GetString("permissions-eui-edit-admin-selected-rank",
                    ("rank", _owner.GetAdminRankDisplay(_sourceData, snapshot.RankId)),
                    ("hierarchy", _owner.GetEffectiveHierarchy(_sourceData, snapshot.RankId))), MetaTextColor);
                var activity = _owner.GetActivityText(_sourceData);
                _activityLabel.Visible = !string.IsNullOrWhiteSpace(activity);
                _activitySeparator.Visible = _activityLabel.Visible;
                _activityFooterSeparator.Visible = _activityLabel.Visible;
                _activityLabel.SetMessage(activity, MetaTextColor);
                var effectiveFlags = snapshot.PosFlags;
                if (snapshot.RankId is { } rankId && _owner.TryGetRank(rankId, out var rank))
                    effectiveFlags |= rank.Flags;

                effectiveFlags &= ~snapshot.NegFlags;
                var effectiveFull = GetFlagListText(effectiveFlags);
                _effectiveFlags.SetMessage(GetFlagCountText(effectiveFlags), MetaTextColor);
                _effectiveFlags.ToolTip = effectiveFull;
                var positiveFull = GetFlagListText(snapshot.PosFlags);
                var negativeFull = GetFlagListText(snapshot.NegFlags);
                _positiveFlags.SetMessage(Loc.GetString("permissions-eui-meta-overrides-positive-count", ("count", GetFlagCount(snapshot.PosFlags))), MetaTextColor);
                _positiveFlags.ToolTip = positiveFull;
                _negativeFlags.SetMessage(Loc.GetString("permissions-eui-meta-overrides-negative-count", ("count", GetFlagCount(snapshot.NegFlags))), MetaTextColor);
                _negativeFlags.ToolTip = negativeFull;
            }

            private void OpenFlagEditor()
            {
                if (_owner == null)
                    return;

                var dialog = new AdminFlagsWindow(_positiveFlagsValue, _negativeFlagsValue, _owner.CanGrantFlag, _canModify);
                dialog.ConfirmButton.OnPressed += _ =>
                {
                    dialog.GetFlags(out _positiveFlagsValue, out _negativeFlagsValue);
                    dialog.Close();
                    OnInputChanged();
                };
                dialog.CancelButton.OnPressed += _ => dialog.Close();
                _owner.OpenDialog(dialog);
            }

            private void UpdateActionState()
            {
                var snapshot = GetSnapshot();
                var canSave = _canModify
                    && (!_isNew || !string.IsNullOrWhiteSpace(snapshot.NameOrId))
                    && snapshot != _originalSnapshot;

                _saveButton.Disabled = !canSave;
                _resetButton.Disabled = !_canModify || snapshot == _originalSnapshot;
                _removeButton.Disabled = !_canModify || _isNew;
                _removeButton.Visible = !_isNew;
            }

            private void OnInputChanged()
            {
                if (_suppressEvents)
                    return;

                UpdateSummary();
                UpdateActionState();
            }
        }

        private sealed class RankEditorView : PanelContainer
        {
            public event Action? SavePressed;
            public event Action? ResetPressed;
            public event Action? RemovePressed;

            private readonly Label _subjectLabel;
            private readonly LineEdit _nameEdit;
            private readonly OptionButton _hierarchyButton;
            private readonly RichTextLabel _assignedCount;
            private readonly RichTextLabel _assignability;
            private readonly RichTextLabel _flagsSummary;
            private readonly Button _editFlagsButton;
            private readonly Button _resetButton;
            private readonly Button _removeButton;
            private readonly Button _saveButton;

            private PermissionsEui? _owner;
            private RankEditorSnapshot _originalSnapshot;
            private int? _contextRankId;
            private bool _isNew;
            private bool _suppressEvents;
            private bool _canModify;
            private PermissionsEuiState.AdminRankData? _sourceData;
            private AdminFlags _flagsValue;

            public bool IsDirty => GetSnapshot() != _originalSnapshot;

            public RankEditorView()
            {
                PanelOverride = new StyleBoxFlat(SectionPanelStyle);
                HorizontalExpand = true;
                VerticalExpand = true;

                _subjectLabel = new Label();
                _nameEdit = new LineEdit
                {
                    PlaceHolder = Loc.GetString("permissions-eui-edit-admin-rank-window-name-edit-placeholder"),
                };
                _nameEdit.OnTextChanged += _ => OnInputChanged();

                _hierarchyButton = new PermissionsOptionButton();
                for (byte level = AdminHierarchy.HighestRankLevel; level <= AdminHierarchy.LowestRankLevel; level++)
                {
                    _hierarchyButton.AddItem(GetHierarchyText(level), level);
                }
                _hierarchyButton.OnItemSelected += args =>
                {
                    _hierarchyButton.SelectId(args.Id);
                    OnInputChanged();
                };

                _assignedCount = CreateWrappedText(string.Empty, MetaTextColor);
                _assignability = CreateWrappedText(string.Empty, MetaTextColor);
                _flagsSummary = CreateWrappedText(string.Empty, MetaTextColor);
                _editFlagsButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-edit-rank-flags-button"),
                    HorizontalAlignment = HAlignment.Left,
                };
                _editFlagsButton.OnPressed += _ => OpenFlagEditor();

                _resetButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-reset-button"),
                };
                _resetButton.OnPressed += _ => ResetPressed?.Invoke();

                _removeButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-edit-admin-window-remove-flag-button"),
                };
                _removeButton.OnPressed += _ => RemovePressed?.Invoke();

                _saveButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-edit-admin-window-save-button"),
                };
                _saveButton.OnPressed += _ => SavePressed?.Invoke();

                var body = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    SeparationOverride = 8,
                    HorizontalExpand = true,
                    Children =
                    {
                        _subjectLabel,
                        new HSeparator(),
                        CreateSection(
                            Loc.GetString("permissions-eui-section-rank-profile"),
                            new BoxContainer
                            {
                                Orientation = BoxContainer.LayoutOrientation.Vertical,
                                HorizontalExpand = true,
                                VerticalExpand = true,
                                SeparationOverride = 6,
                                Children =
                                {
                                    _nameEdit,
                                    _hierarchyButton,
                                    CreateSeparatedTextBlock(
                                        _assignedCount,
                                        _assignability,
                                        _flagsSummary),
                                    _editFlagsButton
                                }
                            },
                            true),
                    }
                };

                AddChild(new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    Margin = new Thickness(10),
                    SeparationOverride = 8,
                    HorizontalExpand = true,
                    VerticalExpand = true,
                    Children =
                    {
                        new ScrollContainer
                        {
                            HorizontalExpand = true,
                            VerticalExpand = true,
                            HScrollEnabled = false,
                            Children = { body }
                        },
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 6,
                            Children =
                            {
                                _removeButton,
                                _resetButton,
                                new Control { HorizontalExpand = true },
                                _saveButton,
                            }
                        }
                    }
                });
            }

            public void Load(PermissionsEui owner, int? rankId, PermissionsEuiState.AdminRankData? sourceData, bool isNew)
            {
                _owner = owner;
                _contextRankId = rankId;
                _sourceData = sourceData;
                _isNew = isNew;
                _canModify = sourceData?.CanModify ?? true;

                var snapshot = CreateSnapshot(sourceData);

                ApplySnapshot(snapshot);
                _originalSnapshot = snapshot;

                _subjectLabel.Text = isNew
                    ? Loc.GetString("permissions-eui-new-rank-entry")
                    : sourceData!.Value.Name;

                UpdateInputAvailability();
                UpdateSummary();
                UpdateActionState();
            }

            public bool IsSameContext(int? rankId, bool isNew)
            {
                return _isNew == isNew && _contextRankId == rankId;
            }

            public bool ShouldLoad(int? rankId, PermissionsEuiState.AdminRankData? sourceData, bool isNew, bool force)
            {
                if (force || !IsSameContext(rankId, isNew))
                    return true;

                if (!IsDirty)
                    return true;

                if (sourceData == null)
                    return false;

                return GetSnapshot() == CreateSnapshot(sourceData);
            }

            public RankEditorSnapshot GetSnapshot()
            {
                return new RankEditorSnapshot(
                    _nameEdit.Text.Trim(),
                    (byte) _hierarchyButton.SelectedId,
                    _flagsValue);
            }

            private static RankEditorSnapshot CreateSnapshot(PermissionsEuiState.AdminRankData? sourceData)
            {
                return sourceData == null
                    ? new RankEditorSnapshot(string.Empty, AdminHierarchy.DefaultHierarchyLevel, AdminFlags.None)
                    : new RankEditorSnapshot(sourceData.Value.Name, sourceData.Value.HierarchyLevel, sourceData.Value.Flags);
            }

            private void ApplySnapshot(RankEditorSnapshot snapshot)
            {
                _suppressEvents = true;

                _nameEdit.Text = snapshot.Name;
                _hierarchyButton.SelectId(snapshot.HierarchyLevel);
                _flagsValue = snapshot.Flags;

                _suppressEvents = false;
            }

            private void UpdateInputAvailability()
            {
                _nameEdit.Editable = _canModify;
                _hierarchyButton.Disabled = !_canModify;
                _editFlagsButton.Disabled = !_canModify;
            }

            private void UpdateSummary()
            {
                if (_sourceData is { } source)
                {
                    _assignedCount.SetMessage(Loc.GetString("permissions-eui-edit-rank-assigned-count", ("count", source.AssignedAdminCount)), MetaTextColor);
                    _assignability.SetMessage(source.CanAssign
                        ? Loc.GetString("permissions-eui-meta-can-assign")
                        : Loc.GetString("permissions-eui-meta-cannot-assign"), MetaTextColor);
                }
                else
                {
                    _assignedCount.SetMessage(Loc.GetString("permissions-eui-edit-rank-assigned-count", ("count", 0)), MetaTextColor);
                    _assignability.SetMessage(Loc.GetString("permissions-eui-meta-can-assign"), MetaTextColor);
                }

                var flagsFull = GetFlagListText(GetSnapshot().Flags);
                _flagsSummary.SetMessage(GetFlagCountText(GetSnapshot().Flags), MetaTextColor);
                _flagsSummary.ToolTip = flagsFull;
            }

            private void OpenFlagEditor()
            {
                if (_owner == null)
                    return;

                var dialog = new RankFlagsWindow(_flagsValue, _owner.CanGrantFlag, _canModify);
                dialog.ConfirmButton.OnPressed += _ =>
                {
                    _flagsValue = dialog.GetFlags();
                    dialog.Close();
                    OnInputChanged();
                };
                dialog.CancelButton.OnPressed += _ => dialog.Close();
                _owner.OpenDialog(dialog);
            }

            private void UpdateActionState()
            {
                var snapshot = GetSnapshot();
                var canSave = _canModify && !string.IsNullOrWhiteSpace(snapshot.Name) && snapshot != _originalSnapshot;

                _saveButton.Disabled = !canSave;
                _resetButton.Disabled = !_canModify || snapshot == _originalSnapshot;
                _removeButton.Disabled = !_canModify || _isNew;
                _removeButton.Visible = !_isNew;
            }

            private void OnInputChanged()
            {
                if (_suppressEvents)
                    return;

                UpdateSummary();
                UpdateActionState();
            }
        }

        private sealed class AdminFlagsWindow : DefaultWindow
        {
            public readonly Button ConfirmButton;
            public readonly Button CancelButton;

            private readonly Dictionary<AdminFlags, FlagOverrideRow> _rows = new();

            public AdminFlagsWindow(AdminFlags posFlags, AdminFlags negFlags, Func<AdminFlags, bool> canGrantFlag, bool canModify)
            {
                Title = Loc.GetString("permissions-eui-edit-admin-flags-window-title");
                MinSize = new Vector2(600, 720);

                var rows = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    SeparationOverride = 6,
                };

                rows.AddChild(new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat(ListPanelStyle),
                    HorizontalExpand = true,
                    Children =
                    {
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            Margin = new Thickness(10, 6),
                            SeparationOverride = 8,
                            Children =
                            {
                                new Label
                                {
                                    Text = Loc.GetString("permissions-eui-flags-column-name"),
                                    HorizontalExpand = true,
                                    FontColorOverride = MetaTextColor,
                                },
                                CreateModePanel(
                                    CreateModeHeaderChip("+", Loc.GetString("permissions-eui-flag-mode-grant")),
                                    CreateModeHeaderChip("/", Loc.GetString("permissions-eui-flag-mode-auto")),
                                    CreateModeHeaderChip("-", Loc.GetString("permissions-eui-flag-mode-deny"))),
                            }
                        }
                    }
                });

                foreach (var flag in AdminFlagsHelper.AllFlags.OrderBy(flag => flag.ToString(), StringComparer.CurrentCultureIgnoreCase))
                {
                    var row = new FlagOverrideRow(flag, () => { });
                    row.SetState((negFlags & flag) != 0
                        ? FlagOverrideState.Deny
                        : (posFlags & flag) != 0
                            ? FlagOverrideState.Grant
                            : FlagOverrideState.Inherit);
                    row.SetDisabled(!canModify || !canGrantFlag(flag));
                    _rows.Add(flag, row);

                    rows.AddChild(new PanelContainer
                    {
                        PanelOverride = new StyleBoxFlat(RowPanelStyle),
                        HorizontalExpand = true,
                        Children =
                        {
                            new BoxContainer
                            {
                                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                                Margin = new Thickness(10, 6),
                                SeparationOverride = 8,
                                Children =
                                {
                                    new Label
                                    {
                                        Text = flag.ToString().ToUpperInvariant(),
                                        HorizontalExpand = true,
                                        VerticalAlignment = VAlignment.Center,
                                    },
                                    row.ModePanel,
                                }
                            }
                        }
                    });
                }

                ConfirmButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-confirm-button"),
                };
                CancelButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-cancel-button"),
                };

                ContentsContainer.AddChild(new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    Margin = new Thickness(10),
                    SeparationOverride = 8,
                    Children =
                    {
                        CreateWrappedText(Loc.GetString("permissions-eui-edit-admin-flags-window-help"), MetaTextColor),
                        new ScrollContainer
                        {
                            HorizontalExpand = true,
                            VerticalExpand = true,
                            HScrollEnabled = false,
                            Children = { rows }
                        },
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 6,
                            Children =
                            {
                                new Control { HorizontalExpand = true },
                                CancelButton,
                                ConfirmButton,
                            }
                        }
                    }
                });
            }

            public void GetFlags(out AdminFlags posFlags, out AdminFlags negFlags)
            {
                posFlags = AdminFlags.None;
                negFlags = AdminFlags.None;

                foreach (var (flag, row) in _rows)
                {
                    switch (row.GetState())
                    {
                        case FlagOverrideState.Grant:
                            posFlags |= flag;
                            break;
                        case FlagOverrideState.Deny:
                            negFlags |= flag;
                            break;
                    }
                }
            }

        }

        private sealed class RankFlagsWindow : DefaultWindow
        {
            public readonly Button ConfirmButton;
            public readonly Button CancelButton;

            private readonly Dictionary<AdminFlags, Button> _checks = new();

            public RankFlagsWindow(AdminFlags flags, Func<AdminFlags, bool> canGrantFlag, bool canModify)
            {
                Title = Loc.GetString("permissions-eui-edit-rank-flags-window-title");
                MinSize = new Vector2(560, 700);

                var rows = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    SeparationOverride = 6,
                };

                rows.AddChild(new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat(ListPanelStyle),
                    HorizontalExpand = true,
                    Children =
                    {
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            Margin = new Thickness(10, 6),
                            SeparationOverride = 8,
                            Children =
                            {
                                new Label
                                {
                                    Text = Loc.GetString("permissions-eui-flags-column-name"),
                                    HorizontalExpand = true,
                                    FontColorOverride = MetaTextColor,
                                },
                                CreateModePanel(
                                    CreateModeHeaderChip("+", Loc.GetString("permissions-eui-flag-mode-grant"))),
                            }
                        }
                    }
                });

                foreach (var flag in AdminFlagsHelper.AllFlags.OrderBy(flag => flag.ToString(), StringComparer.CurrentCultureIgnoreCase))
                {
                    var check = new Button
                    {
                        Text = "+",
                        ToggleMode = true,
                        Pressed = (flags & flag) != 0,
                        Disabled = !canModify || !canGrantFlag(flag),
                        MinSize = new Vector2(54, 0),
                    };
                    ApplyFlagModeButtonStyle(check, FlagModePositiveStyle, check.Pressed);
                    check.ToolTip = flag.ToString().ToUpperInvariant();
                    check.OnPressed += _ => ApplyFlagModeButtonStyle(check, FlagModePositiveStyle, check.Pressed);
                    _checks.Add(flag, check);
                    rows.AddChild(new PanelContainer
                    {
                        PanelOverride = new StyleBoxFlat(RowPanelStyle),
                        HorizontalExpand = true,
                        Children =
                        {
                            new BoxContainer
                            {
                                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                                Margin = new Thickness(10, 6),
                                SeparationOverride = 8,
                                Children =
                                {
                                    new Label
                                    {
                                        Text = flag.ToString().ToUpperInvariant(),
                                        HorizontalExpand = true,
                                        VerticalAlignment = VAlignment.Center,
                                    },
                                    CreateModePanel(check),
                                }
                            }
                        }
                    });
                }

                ConfirmButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-confirm-button"),
                };
                CancelButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-cancel-button"),
                };

                ContentsContainer.AddChild(new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    Margin = new Thickness(10),
                    SeparationOverride = 8,
                    Children =
                    {
                        CreateWrappedText(Loc.GetString("permissions-eui-edit-rank-flags-window-help"), MetaTextColor),
                        new ScrollContainer
                        {
                            HorizontalExpand = true,
                            VerticalExpand = true,
                            HScrollEnabled = false,
                            Children = { rows }
                        },
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 6,
                            Children =
                            {
                                new Control { HorizontalExpand = true },
                                CancelButton,
                                ConfirmButton,
                            }
                        }
                    }
                });
            }

            public AdminFlags GetFlags()
            {
                var flags = AdminFlags.None;
                foreach (var (flag, check) in _checks)
                {
                    if (check.Pressed)
                        flags |= flag;
                }

                return flags;
            }
        }

        private sealed class ConfirmationWindow : DefaultWindow
        {
            public readonly Button ConfirmButton;
            public readonly Button CancelButton;

            public ConfirmationWindow(string title, string body)
            {
                Title = title;
                MinSize = new Vector2(392, 142);
                SetSize = new Vector2(412, 150);

                ConfirmButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-confirm-button"),
                    MinSize = new Vector2(118, 0),
                };
                CancelButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-cancel-button"),
                    MinSize = new Vector2(108, 0),
                };

                ContentsContainer.AddChild(new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    Margin = new Thickness(10),
                    SeparationOverride = 8,
                    Children =
                    {
                        CreateWrappedText(body, Color.White),
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 6,
                            Children =
                            {
                                new Control { HorizontalExpand = true },
                                CancelButton,
                                ConfirmButton,
                            }
                        }
                    }
                });
            }
        }

        private sealed class FlagOverrideRow
        {
            public readonly Control ModePanel;
            public readonly Button InheritButton;
            public readonly Button GrantButton;
            public readonly Button DenyButton;

            public FlagOverrideRow(AdminFlags flag, Action changed)
            {
                var group = new ButtonGroup();
                var flagText = flag.ToString().ToUpperInvariant();

                InheritButton = CreateButton("/", group, changed, $"{Loc.GetString("permissions-eui-flag-mode-auto")}: {flagText}");
                GrantButton = CreateButton("+", group, changed, $"{Loc.GetString("permissions-eui-flag-mode-grant")}: {flagText}");
                DenyButton = CreateButton("-", group, changed, $"{Loc.GetString("permissions-eui-flag-mode-deny")}: {flagText}");
                ModePanel = CreateModePanel(GrantButton, InheritButton, DenyButton);
                RefreshStyles();
            }

            public FlagOverrideState GetState()
            {
                if (GrantButton.Pressed)
                    return FlagOverrideState.Grant;
                if (DenyButton.Pressed)
                    return FlagOverrideState.Deny;
                return FlagOverrideState.Inherit;
            }

            public void SetState(FlagOverrideState state)
            {
                switch (state)
                {
                    case FlagOverrideState.Grant:
                        GrantButton.Pressed = true;
                        break;
                    case FlagOverrideState.Deny:
                        DenyButton.Pressed = true;
                        break;
                    default:
                        InheritButton.Pressed = true;
                        break;
                }

                RefreshStyles();
            }

            public void SetDisabled(bool disabled)
            {
                InheritButton.Disabled = disabled;
                GrantButton.Disabled = disabled;
                DenyButton.Disabled = disabled;
                RefreshStyles();
            }

            private void RefreshStyles()
            {
                ApplyFlagModeButtonStyle(InheritButton, FlagModeNeutralStyle, InheritButton.Pressed);
                ApplyFlagModeButtonStyle(GrantButton, FlagModePositiveStyle, GrantButton.Pressed);
                ApplyFlagModeButtonStyle(DenyButton, FlagModeNegativeStyle, DenyButton.Pressed);
            }

            private Button CreateButton(string text, ButtonGroup group, Action changed, string toolTip)
            {
                var button = new Button
                {
                    Text = text,
                    Group = group,
                    MinSize = new Vector2(54, 0),
                    ToolTip = toolTip,
                };
                button.OnPressed += _ =>
                {
                    RefreshStyles();
                    changed();
                };
                return button;
            }
        }

        private sealed class PermissionsOptionButton : OptionButton
        {
            public PermissionsOptionButton()
            {
                OptionsScroll.MaxHeight = 220;
                OptionsScroll.HScrollEnabled = false;

                OnPressed += _ => ResetPopupScroll();
                OnItemSelected += _ => ResetPopupScroll();
            }

            private void ResetPopupScroll()
            {
                OptionsScroll.SetScrollValue(Vector2.Zero);
            }
        }

        private enum FlagOverrideState : byte
        {
            Inherit,
            Grant,
            Deny,
        }

        private readonly record struct AdminEditorSnapshot(
            string NameOrId,
            string? Title,
            int? RankId,
            bool Suspended,
            AdminFlags PosFlags,
            AdminFlags NegFlags);

        private readonly record struct RankEditorSnapshot(
            string Name,
            byte HierarchyLevel,
            AdminFlags Flags);

        private static string? NormalizeOptionalText(string text)
        {
            var trimmed = text.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }

        private static BoxContainer CreateSeparatedTextBlock(params Control[] controls)
        {
            var box = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 4,
            };

            for (var i = 0; i < controls.Length; i++)
            {
                box.AddChild(controls[i]);

                if (i < controls.Length - 1)
                    box.AddChild(new HSeparator());
            }

            return box;
        }

        private static Control CreateModePanel(params Control[] controls)
        {
            var buttons = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                Margin = new Thickness(4),
                SeparationOverride = 4,
            };

            foreach (var control in controls)
            {
                buttons.AddChild(control);
            }

            return new PanelContainer
            {
                PanelOverride = new StyleBoxFlat(FlagModePanelStyle),
                Children =
                {
                    buttons
                }
            };
        }

        private static Control CreateModeHeaderChip(string text, string toolTip)
        {
            return new PanelContainer
            {
                PanelOverride = new StyleBoxFlat(BadgeNeutralStyle),
                MinSize = new Vector2(54, 0),
                Children =
                {
                    new Label
                    {
                        Text = text,
                        Align = Label.AlignMode.Center,
                        HorizontalExpand = true,
                        Margin = new Thickness(6, 2),
                        FontColorOverride = Color.White,
                        ToolTip = toolTip,
                    }
                }
            };
        }

        private static void ApplyFlagModeButtonStyle(Button button, StyleBoxFlat activeStyle, bool active)
        {
            button.StyleBoxOverride = new StyleBoxFlat(active ? activeStyle : FlagModeIdleStyle);
            button.Label.FontColorOverride = button.Disabled
                ? StyleNano.DisabledFore
                : active
                    ? Color.White
                    : MetaTextColor;
        }

        private static Control CreateSection(string? title, Control content, bool expand)
        {
            content.HorizontalExpand = true;
            content.VerticalExpand = expand;

            var sectionBody = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                Margin = new Thickness(8),
                SeparationOverride = 6,
                VerticalExpand = expand,
            };

            if (!string.IsNullOrWhiteSpace(title))
            {
                sectionBody.AddChild(new Label
                {
                    Text = title,
                    FontColorOverride = Color.White,
                });
            }

            sectionBody.AddChild(content);

            return new PanelContainer
            {
                PanelOverride = new StyleBoxFlat(ListPanelStyle),
                HorizontalExpand = true,
                VerticalExpand = expand,
                Children =
                {
                    sectionBody
                }
            };
        }
    }
}
