using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client._WH40K.Command.Controls;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeUpgradeSketchWindow : FancyWindow
{
    private static readonly Color ImperiumColor = Color.FromHex("#F3C548");
    public event Action<string>? OnTreeNodePurchaseRequested;

    private readonly Label _teamLine;
    private readonly Label _upgradePointsLine;
    private readonly Label _doctrineLine;
    private readonly StyleBoxFlat _headerStyle;
    private readonly BoxContainer _domainHeaderRow;
    private readonly WH40KCommandTreeSketchControl _treeSketch;
    private readonly Label _hoverTitleLine;
    private readonly RichTextLabel _hoverDescriptionLine;

    public WH40KCommandNodeUpgradeSketchWindow()
    {
        Title = Loc.GetString("wh40k-command-node-upgrade-sketch-window-title");
        MinSize = SetSize = new Vector2(1060, 760);

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
        _upgradePointsLine = new Label();
        _doctrineLine = new Label();
        headerBox.AddChild(_teamLine);
        headerBox.AddChild(_upgradePointsLine);
        headerBox.AddChild(_doctrineLine);
        headerBox.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-command-node-upgrade-sketch-window-draft-note")
        });

        var hoverPanel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#232A3B"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
        };
        root.AddChild(hoverPanel);

        var hoverBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            Margin = new Thickness(8)
        };
        hoverPanel.AddChild(hoverBox);

        _hoverTitleLine = new Label();
        _hoverDescriptionLine = new RichTextLabel
        {
            HorizontalExpand = true,
            SetHeight = 74f
        };
        hoverBox.AddChild(_hoverTitleLine);
        hoverBox.AddChild(_hoverDescriptionLine);

        var canvasFrame = new PanelContainer
        {
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#1C2029"),
                BorderColor = Color.FromHex("#4D5670"),
                BorderThickness = new Thickness(1)
            }
        };
        root.AddChild(canvasFrame);

        var treeArea = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            VerticalExpand = true
        };
        canvasFrame.AddChild(treeArea);

        var domainsPanel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#232A3B"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
        };
        treeArea.AddChild(domainsPanel);

        _domainHeaderRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(6, 4)
        };
        domainsPanel.AddChild(_domainHeaderRow);

        var treeScroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
            HScrollEnabled = true
        };
        treeArea.AddChild(treeScroll);

        _treeSketch = new WH40KCommandTreeSketchControl
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            MinSize = new Vector2(760f, 680f)
        };
        _treeSketch.OnNodeInfoChanged += OnNodeInfoChanged;
        _treeSketch.OnPurchaseRequested += OnTreeNodePurchaseRequestedInternal;
        treeScroll.AddChild(_treeSketch);
        RebuildDomainHeaders(_treeSketch.ActiveDomainIds);

        OnNodeInfoChanged(
            Loc.GetString("wh40k-command-node-upgrade-tree-info-default-title"),
            Loc.GetString("wh40k-command-node-upgrade-tree-info-default-description"));
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state, string activeDoctrineId)
    {
        var accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, ImperiumColor);
        _headerStyle.BorderColor = accent;
        _treeSketch.AccentColor = accent;

        Title = Loc.GetString("wh40k-command-node-upgrade-sketch-window-title-team", ("team", state.TeamName));
        _teamLine.Text = Loc.GetString("wh40k-command-node-team", ("team", state.TeamName));
        _upgradePointsLine.Text = Loc.GetString("wh40k-command-node-upgrade-sketch-window-points",
            ("points", state.CommandPoints));
        _doctrineLine.Text = string.IsNullOrWhiteSpace(activeDoctrineId)
            ? Loc.GetString("wh40k-command-node-upgrade-sketch-window-doctrine-none")
            : Loc.GetString("wh40k-command-node-upgrade-sketch-window-doctrine-active",
                ("doctrine", WH40KCommandNodeDoctrineWindow.ResolveDoctrineDisplay(activeDoctrineId, state.TeamId).Name));

        _treeSketch.UpdateState(state, activeDoctrineId);
        RebuildDomainHeaders(_treeSketch.ActiveDomainIds);
    }

    private void OnNodeInfoChanged(string title, string description)
    {
        _hoverTitleLine.Text = title;
        var normalizedDescription = NormalizeMultiline(description);
        _hoverDescriptionLine.SetMessage(
            FormattedMessage.FromMarkupPermissive(FormattedMessage.EscapeText(normalizedDescription)),
            tagsAllowed: null,
            defaultColor: Color.White);
    }

    private static string NormalizeMultiline(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Replace("\\n", "\n", StringComparison.Ordinal);
    }

    private void RebuildDomainHeaders(IReadOnlyList<string> domainIds)
    {
        _domainHeaderRow.RemoveAllChildren();

        foreach (var domainId in domainIds)
        {
            _domainHeaderRow.AddChild(new Label
            {
                HorizontalExpand = true,
                HorizontalAlignment = HAlignment.Center,
                Text = ResolveDomainLabel(domainId)
            });
        }
    }

    private static string ResolveDomainLabel(string domainId)
    {
        return Loc.GetString($"wh40k-command-node-upgrade-sketch-domain-{domainId}");
    }

    private void OnTreeNodePurchaseRequestedInternal(string nodeId)
    {
        if (!string.IsNullOrWhiteSpace(nodeId))
            OnTreeNodePurchaseRequested?.Invoke(nodeId);
    }

}
