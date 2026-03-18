using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeTeamCompositionWindow : FancyWindow
{
    private static readonly Color ImperiumColor = Color.FromHex("#F3C548");

    private readonly Label _teamLine;
    private readonly Label _summaryLine;
    private readonly StyleBoxFlat _headerStyle;
    private readonly Label _staffingHeaderLine;
    private readonly Label _rolesHeaderLine;
    private readonly Label _membersHeaderLine;
    private readonly BoxContainer _staffingRows;
    private readonly BoxContainer _roleRows;
    private readonly BoxContainer _memberRows;
    private Color _accent = ImperiumColor;

    public WH40KCommandNodeTeamCompositionWindow()
    {
        Title = Loc.GetString("wh40k-command-node-team-composition-window-title");
        MinSize = SetSize = new Vector2(980, 640);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8
        };
        ContentsContainer.AddChild(root);

        var header = new PanelContainer
        {
            PanelOverride = _headerStyle = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#2B3246"),
                BorderColor = ImperiumColor,
                BorderThickness = new Thickness(1)
            }
        };
        root.AddChild(header);

        var headerBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            Margin = new Thickness(8)
        };
        header.AddChild(headerBox);

        _teamLine = new Label();
        _summaryLine = new Label();
        headerBox.AddChild(_teamLine);
        headerBox.AddChild(_summaryLine);

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            VerticalExpand = true
        };
        root.AddChild(body);

        var left = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            HorizontalExpand = true,
            VerticalExpand = true,
            SizeFlagsStretchRatio = 0.95f
        };
        body.AddChild(left);

        var staffingSection = CreateSection(
            Loc.GetString("wh40k-command-node-team-composition-section-staffing"),
            out var staffingBox,
            out _staffingHeaderLine,
            verticalExpand: false);
        left.AddChild(staffingSection);

        _staffingRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5
        };
        staffingBox.AddChild(_staffingRows);

        var rolesSection = CreateSection(
            Loc.GetString("wh40k-command-node-team-composition-section-roles"),
            out var rolesContent,
            out _rolesHeaderLine,
            verticalExpand: true);
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
            Loc.GetString("wh40k-command-node-team-composition-section-members"),
            out var membersBox,
            out _membersHeaderLine,
            verticalExpand: true);
        membersSection.MinWidth = 360;
        membersSection.VerticalExpand = true;
        membersSection.SizeFlagsStretchRatio = 1.05f;
        body.AddChild(membersSection);

        var membersScroll = new ScrollContainer
        {
            VerticalExpand = true
        };
        membersBox.AddChild(membersScroll);

        _memberRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
            VerticalExpand = true
        };
        membersScroll.AddChild(_memberRows);
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state)
    {
        _accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, ImperiumColor);

        _headerStyle.BorderColor = _accent;
        Title = Loc.GetString("wh40k-command-node-team-composition-window-title-team", ("team", state.TeamName));
        _teamLine.Text = Loc.GetString("wh40k-command-node-team", ("team", state.TeamName));
        _summaryLine.Text = state.TeamCompositionSummary;
        _staffingHeaderLine.ModulateSelfOverride = _accent;
        _rolesHeaderLine.ModulateSelfOverride = _accent;
        _membersHeaderLine.ModulateSelfOverride = _accent;

        var officerRoles = state.TeamCompositionOfficerRoles ?? Array.Empty<WH40KTeamCompositionRoleEntry>();
        var coreRoles = state.TeamCompositionCoreRoles ?? Array.Empty<WH40KTeamCompositionRoleEntry>();
        var mechanicusRoles = state.TeamCompositionMechanicusRoles ?? Array.Empty<WH40KTeamCompositionRoleEntry>();
        var members = state.TeamCompositionMembers ?? Array.Empty<WH40KTeamCompositionMemberEntry>();

        RebuildStaffing(state.TeamCompositionStaffingLines);

        try
        {
            RebuildRoles(
                officerRoles,
                coreRoles,
                mechanicusRoles);
        }
        catch (Exception)
        {
            _roleRows.RemoveAllChildren();
            _roleRows.AddChild(new Label
            {
                Text = Loc.GetString("wh40k-command-node-team-composition-group-empty")
            });
        }

        RebuildMembers(members);
    }

    private PanelContainer CreateSection(
        string title,
        out BoxContainer content,
        out Label titleLabel,
        bool verticalExpand)
    {
        var section = new PanelContainer
        {
            VerticalExpand = verticalExpand,
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#1F2433"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
        };

        var sectionRoot = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = verticalExpand
        };
        section.AddChild(sectionRoot);

        var titleBar = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#2B3246"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            }
        };
        sectionRoot.AddChild(titleBar);

        titleLabel = new Label
        {
            Text = title,
            Margin = new Thickness(6, 4)
        };
        titleBar.AddChild(titleLabel);

        content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            VerticalExpand = verticalExpand
        };
        sectionRoot.AddChild(content);

        return section;
    }

    private void RebuildStaffing(IReadOnlyCollection<string> staffingLines)
    {
        _staffingRows.RemoveAllChildren();

        if (staffingLines.Count == 0)
        {
            _staffingRows.AddChild(new Label
            {
                Text = Loc.GetString("wh40k-command-node-team-composition-empty")
            });
            return;
        }

        foreach (var line in staffingLines)
        {
            _staffingRows.AddChild(CreateSingleLineRow(line));
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
            Loc.GetString("wh40k-command-node-team-composition-role-group-officers"),
            officerRoles);
        hasAnyRoles |= AddRoleGroupRows(
            Loc.GetString("wh40k-command-node-team-composition-role-group-core"),
            coreRoles);
        hasAnyRoles |= AddRoleGroupRows(
            Loc.GetString("wh40k-command-node-team-composition-role-group-mechanicus"),
            mechanicusRoles);

        if (hasAnyRoles)
            return;

        _roleRows.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-command-node-team-composition-empty")
        });
    }

    private bool AddRoleGroupRows(string groupName, IReadOnlyCollection<WH40KTeamCompositionRoleEntry> roles)
    {
        var safeRoles = roles
            .Where(role => role != null)
            .ToList();

        _roleRows.AddChild(new Label
        {
            Text = groupName,
            Margin = new Thickness(2, 2, 2, 0),
            ModulateSelfOverride = _accent
        });

        if (safeRoles.Count == 0)
        {
            _roleRows.AddChild(new Label
            {
                Text = Loc.GetString("wh40k-command-node-team-composition-group-empty"),
                Margin = new Thickness(10, 0, 2, 4)
            });
            _roleRows.AddChild(new HSeparator { Margin = new Thickness(2, 0, 2, 4) });
            return false;
        }

        foreach (var role in safeRoles)
        {
            var roleName = string.IsNullOrWhiteSpace(role.RoleName)
                ? Loc.GetString("wh40k-command-node-team-composition-role-unknown")
                : role.RoleName;

            _roleRows.AddChild(CreateSingleLineRow($"{roleName}: {role.Count}"));
        }

        _roleRows.AddChild(new HSeparator { Margin = new Thickness(2, 0, 2, 4) });
        return true;
    }

    private void RebuildMembers(IReadOnlyCollection<WH40KTeamCompositionMemberEntry> members)
    {
        _memberRows.RemoveAllChildren();

        if (members.Count == 0)
        {
            _memberRows.AddChild(new Label
            {
                Text = Loc.GetString("wh40k-command-node-team-composition-empty")
            });
            return;
        }

        foreach (var member in members)
        {
            _memberRows.AddChild(CreateSingleLineRow($"{member.Name} ({member.RoleName})"));
        }
    }

    private Control CreateSingleLineRow(string text)
    {
        var row = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#232A3B"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
        };

        row.AddChild(new Label
        {
            Text = text,
            Margin = new Thickness(6, 4)
        });

        return row;
    }

}
