using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Localization;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeTeamCompositionWindow : FancyWindow, ILocalizedControl
{
    private readonly StyleBoxFlat _headerStyle;
    private readonly Label _headerTitleLabel;
    private readonly Label _teamLine;
    private readonly Label _summaryLine;
    private readonly PanelContainer _teamBadge;
    private readonly Label _teamBadgeLabel;
    private readonly Label _staffingSectionTitleLabel;
    private readonly Label _rolesSectionTitleLabel;
    private readonly Label _membersSectionTitleLabel;
    private readonly BoxContainer _staffingRows;
    private readonly BoxContainer _roleRows;
    private readonly BoxContainer _memberRows;

    private Color _accent = WH40KCommandUiStyles.DefaultAccent;
    private WH40KCommandNodeBoundUserInterfaceState? _latestState;

    public WH40KCommandNodeTeamCompositionWindow()
    {
        Title = Loc.GetString("w40k-cmd-team-composition-window-title");
        MinSize = SetSize = new Vector2(960, 620);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(8)
        };
        ContentsContainer.AddChild(root);

        var header = new PanelContainer
        {
            PanelOverride = _headerStyle = WH40KCommandUiStyles.CreateBorderPanelStyle(
                WH40KCommandUiStyles.HeaderBackground,
                _accent,
                2)
        };
        root.AddChild(header);

        var headerBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(10, 8),
            VerticalAlignment = VAlignment.Center
        };
        header.AddChild(headerBox);

        var headerInfo = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            HorizontalExpand = true
        };
        headerBox.AddChild(headerInfo);

        _headerTitleLabel = new Label
        {
            Text = Loc.GetString("w40k-cmd-team-composition-window-title"),
            StyleClasses = { "LabelHeading" },
            ClipText = true
        };
        _teamLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        _summaryLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        headerInfo.AddChild(_headerTitleLabel);
        headerInfo.AddChild(_teamLine);
        headerInfo.AddChild(_summaryLine);

        _teamBadge = new PanelContainer();
        _teamBadgeLabel = new Label
        {
            Align = Label.AlignMode.Center,
            ClipText = true
        };
        _teamBadge.AddChild(_teamBadgeLabel);
        headerBox.AddChild(_teamBadge);

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            VerticalExpand = true
        };
        root.AddChild(body);

        var left = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
            VerticalExpand = true,
            SizeFlagsStretchRatio = 0.92f
        };
        body.AddChild(left);

        var staffingSection = CreateSection(
            Loc.GetString("w40k-cmd-team-composition-section-staffing"),
            out var staffingContent,
            out _staffingSectionTitleLabel,
            verticalExpand: false);
        left.AddChild(staffingSection);

        _staffingRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6
        };
        staffingContent.AddChild(_staffingRows);

        var rolesSection = CreateSection(
            Loc.GetString("w40k-cmd-team-composition-section-roles"),
            out var rolesContent,
            out _rolesSectionTitleLabel,
            verticalExpand: true);
        rolesSection.VerticalExpand = true;
        left.AddChild(rolesSection);

        var rolesScroll = new ScrollContainer
        {
            VerticalExpand = true
        };
        rolesContent.AddChild(rolesScroll);

        _roleRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true
        };
        rolesScroll.AddChild(_roleRows);

        var membersSection = CreateSection(
            Loc.GetString("w40k-cmd-team-composition-section-members"),
            out var membersContent,
            out _membersSectionTitleLabel,
            verticalExpand: true);
        membersSection.MinWidth = 300;
        membersSection.VerticalExpand = true;
        membersSection.SizeFlagsStretchRatio = 1.05f;
        body.AddChild(membersSection);

        var membersScroll = new ScrollContainer
        {
            VerticalExpand = true
        };
        membersContent.AddChild(membersScroll);

        _memberRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true
        };
        membersScroll.AddChild(_memberRows);

        Relocalize();
    }

    public void Relocalize()
    {
        Title = Loc.GetString("w40k-cmd-team-composition-window-title");
        _headerTitleLabel.Text = Loc.GetString("w40k-cmd-team-composition-window-title");
        _staffingSectionTitleLabel.Text = Loc.GetString("w40k-cmd-team-composition-section-staffing");
        _rolesSectionTitleLabel.Text = Loc.GetString("w40k-cmd-team-composition-section-roles");
        _membersSectionTitleLabel.Text = Loc.GetString("w40k-cmd-team-composition-section-members");

        if (_latestState != null)
            UpdateState(_latestState);
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state)
    {
        _latestState = state;
        _accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, WH40KCommandUiStyles.DefaultAccent);

        _headerStyle.BorderColor = _accent;
        _headerTitleLabel.ModulateSelfOverride = _accent;

        var resolvedTeamName = WH40KCommandUiStyles.ResolveLocalizedOrRaw(state.TeamName);

        Title = Loc.GetString("w40k-cmd-team-composition-window-title-team", ("team", resolvedTeamName));

        _teamLine.Text = CompactLine(Loc.GetString("w40k-cmd-team", ("team", resolvedTeamName)));
        var staffing = state.StaffingData;
        if (staffing != null)
        {
            _summaryLine.Text = CompactLine(Loc.GetString("w40k-cmd-team-composition-summary",
                ("members", staffing.MemberCount),
                ("roles", staffing.RoleCount)));
        }
        else
        {
            _summaryLine.Text = CompactLine(WH40KCommandUiStyles.ResolveLocalizedOrRaw(state.TeamCompositionSummary));
        }

        _teamBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(Color.FromHex("#203227".AsSpan()), _accent);
        _teamBadgeLabel.Text = string.IsNullOrWhiteSpace(resolvedTeamName) ? "?" : resolvedTeamName.ToUpperInvariant();

        var officerRoles = state.TeamCompositionOfficerRoles ?? Array.Empty<WH40KTeamCompositionRoleEntry>();
        var coreRoles = state.TeamCompositionCoreRoles ?? Array.Empty<WH40KTeamCompositionRoleEntry>();
        var mechanicusRoles = state.TeamCompositionMechanicusRoles ?? Array.Empty<WH40KTeamCompositionRoleEntry>();
        var members = state.TeamCompositionMembers ?? Array.Empty<WH40KTeamCompositionMemberEntry>();

        RebuildStaffing(state.StaffingData, state.TeamCompositionStaffingLines);
        RebuildRoles(officerRoles, coreRoles, mechanicusRoles);
        RebuildMembers(members);
    }

    private PanelContainer CreateSection(string title, out BoxContainer content, out Label titleLabel, bool verticalExpand)
    {
        var section = new PanelContainer
        {
            VerticalExpand = verticalExpand,
            HorizontalExpand = true,
            PanelOverride = WH40KCommandUiStyles.CreateBorderPanelStyle(
                WH40KCommandUiStyles.PanelBackgroundAlt,
                WH40KCommandUiStyles.StrongBorder,
                2)
        };

        var sectionRoot = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 0,
            VerticalExpand = verticalExpand
        };
        section.AddChild(sectionRoot);

        var titleBar = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateHeaderStripStyle(WH40KCommandUiStyles.MutedBorder)
        };
        titleLabel = new Label
        {
            Text = title,
            StyleClasses = { "LabelHeading" },
            ClipText = true
        };
        titleBar.AddChild(titleLabel);
        sectionRoot.AddChild(titleBar);

        content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(10),
            VerticalExpand = verticalExpand
        };
        sectionRoot.AddChild(content);

        return section;
    }

    private void RebuildStaffing(WH40KTeamCompositionStaffingData? staffingData, IReadOnlyCollection<string> staffingLines)
    {
        _staffingRows.RemoveAllChildren();

        if (staffingData != null)
        {
            _staffingRows.AddChild(CreateSingleLineRow(
                Loc.GetString("w40k-cmd-team-composition-command-staff-line",
                    ("current", staffingData.CommandCurrent),
                    ("max", staffingData.CommandMax)),
                highlight: true));
            _staffingRows.AddChild(CreateSingleLineRow(
                Loc.GetString("w40k-cmd-team-composition-line-staff-line",
                    ("current", staffingData.LineCurrent),
                    ("max", staffingData.LineMax)),
                highlight: true));
            return;
        }

        if (staffingLines.Count == 0)
        {
            _staffingRows.AddChild(CreateSingleLineRow(Loc.GetString("w40k-cmd-team-composition-empty")));
            return;
        }

        foreach (var line in staffingLines)
        {
            _staffingRows.AddChild(CreateSingleLineRow(line, highlight: true));
        }
    }

    private void RebuildRoles(
        IReadOnlyCollection<WH40KTeamCompositionRoleEntry> officerRoles,
        IReadOnlyCollection<WH40KTeamCompositionRoleEntry> coreRoles,
        IReadOnlyCollection<WH40KTeamCompositionRoleEntry> mechanicusRoles)
    {
        _roleRows.RemoveAllChildren();

        var hasAnyRoles = false;
        hasAnyRoles |= AddRoleGroupRows(
            Loc.GetString("w40k-cmd-team-composition-role-group-officers"),
            officerRoles);
        hasAnyRoles |= AddRoleGroupRows(
            Loc.GetString("w40k-cmd-team-composition-role-group-core"),
            coreRoles);
        hasAnyRoles |= AddRoleGroupRows(
            Loc.GetString("w40k-cmd-team-composition-role-group-mechanicus"),
            mechanicusRoles);

        if (hasAnyRoles)
            return;

        _roleRows.AddChild(CreateSingleLineRow(Loc.GetString("w40k-cmd-team-composition-empty")));
    }

    private bool AddRoleGroupRows(string groupName, IReadOnlyCollection<WH40KTeamCompositionRoleEntry> roles)
    {
        var safeRoles = roles
            .Where(role => role != null)
            .ToList();

        var card = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                WH40KCommandUiStyles.CardBackground,
                safeRoles.Count > 0 ? _accent : WH40KCommandUiStyles.MutedBorder)
        };
        _roleRows.AddChild(card);

        var cardBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4
        };
        card.AddChild(cardBox);

        cardBox.AddChild(new Label
        {
            Text = groupName,
            StyleClasses = { "LabelBig" },
            ModulateSelfOverride = _accent,
            ClipText = true
        });

        if (safeRoles.Count == 0)
        {
            cardBox.AddChild(new Label
            {
                Text = Loc.GetString("w40k-cmd-team-composition-group-empty"),
                StyleClasses = { "LabelSubText" },
                ClipText = true
            });
            return false;
        }

        foreach (var role in safeRoles)
        {
            var roleName = string.IsNullOrWhiteSpace(role.RoleName)
                ? Loc.GetString("w40k-cmd-team-composition-role-unknown")
                : WH40KCommandUiStyles.ResolveLocalizedOrRaw(role.RoleName);
            cardBox.AddChild(CreateSingleLineRow($"{roleName}: {role.Count}", inset: true));
        }

        return true;
    }

    private void RebuildMembers(IReadOnlyCollection<WH40KTeamCompositionMemberEntry> members)
    {
        _memberRows.RemoveAllChildren();

        if (members.Count == 0)
        {
            _memberRows.AddChild(CreateSingleLineRow(Loc.GetString("w40k-cmd-team-composition-empty")));
            return;
        }

        foreach (var member in members)
        {
            var resolvedRole = WH40KCommandUiStyles.ResolveLocalizedOrRaw(member.RoleName);
            _memberRows.AddChild(CreateSingleLineRow($"{member.Name} ({resolvedRole})"));
        }
    }

    private Control CreateSingleLineRow(string text, bool highlight = false, bool inset = false)
    {
        var row = new PanelContainer
        {
            MinHeight = 28f,
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                inset ? WH40KCommandUiStyles.CardBackgroundAlt : WH40KCommandUiStyles.CardBackground,
                highlight ? _accent : WH40KCommandUiStyles.MutedBorder)
        };

        row.AddChild(new Label
        {
            Text = CompactLine(text),
            ClipText = true,
            StyleClasses = { inset ? "LabelSubText" : "LabelBig" }
        });

        return row;
    }

    private static string CompactLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var compact = text
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        return string.Join(' ', compact.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
