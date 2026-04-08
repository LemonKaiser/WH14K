using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Localization;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.GameMode;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeReinforcementWindow : FancyWindow, ILocalizedControl
{
    public event Action<string, int>? OnCallRequested;

    private readonly StyleBoxFlat _headerStyle;
    private readonly Label _headerTitleLabel;
    private readonly Label _statusLine;
    private readonly PanelContainer _teamBadge;
    private readonly Label _teamBadgeLabel;
    private readonly PanelContainer _statusBadge;
    private readonly Label _statusBadgeLabel;
    private readonly ScrollContainer _cardsScroll;
    private readonly BoxContainer _cardsRoot;
    private readonly Dictionary<string, int> _selectedCounts = new();

    private Color _accent = WH40KCommandUiStyles.DefaultAccent;
    private WH40KCommandNodeBoundUserInterfaceState? _latestState;

    public WH40KCommandNodeReinforcementWindow()
    {
        Title = Loc.GetString("w40k-cmd-reinforcement-window-title");
        MinSize = SetSize = new Vector2(960, 580);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(6)
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
            SeparationOverride = 10,
            Margin = new Thickness(10, 8),
            VerticalAlignment = VAlignment.Center
        };
        header.AddChild(headerBox);

        var headerInfo = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            HorizontalExpand = true
        };
        headerBox.AddChild(headerInfo);

        _headerTitleLabel = new Label
        {
            Text = Loc.GetString("w40k-cmd-reinforcement-window-title"),
            StyleClasses = { "LabelHeading" },
            ClipText = true
        };
        headerInfo.AddChild(_headerTitleLabel);

        _statusLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        headerInfo.AddChild(_statusLine);

        var badgeRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            VerticalAlignment = VAlignment.Center
        };
        headerBox.AddChild(badgeRow);

        _teamBadge = new PanelContainer();
        _teamBadgeLabel = new Label { Align = Label.AlignMode.Center, ClipText = true };
        _teamBadge.AddChild(_teamBadgeLabel);
        badgeRow.AddChild(_teamBadge);

        _statusBadge = new PanelContainer();
        _statusBadgeLabel = new Label { Align = Label.AlignMode.Center, ClipText = true };
        _statusBadge.AddChild(_statusBadgeLabel);
        badgeRow.AddChild(_statusBadge);

        var body = new PanelContainer
        {
            VerticalExpand = true,
            PanelOverride = WH40KCommandUiStyles.CreateBorderPanelStyle(
                WH40KCommandUiStyles.PanelBackgroundAlt,
                WH40KCommandUiStyles.StrongBorder,
                2)
        };
        root.AddChild(body);

        _cardsScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        body.AddChild(_cardsScroll);

        _cardsRoot = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(8),
            HorizontalExpand = true,
            VerticalExpand = true
        };
        _cardsScroll.AddChild(_cardsRoot);

        Relocalize();
    }

    public void Relocalize()
    {
        Title = Loc.GetString("w40k-cmd-reinforcement-window-title");
        _headerTitleLabel.Text = Loc.GetString("w40k-cmd-reinforcement-window-title");

        if (_latestState != null)
            UpdateState(_latestState);
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state)
    {
        _latestState = state;
        _accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, WH40KCommandUiStyles.DefaultAccent);
        _headerStyle.BorderColor = _accent;
        _headerTitleLabel.ModulateSelfOverride = _accent;

        var resolvedTeam = WH40KCommandUiStyles.ResolveLocalizedOrRaw(state.TeamName);
        Title = Loc.GetString("w40k-cmd-reinforcement-window-title-team", ("team", resolvedTeam));
        _statusLine.Text = BuildStatusLine(state);

        _teamBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(Color.FromHex("#203227".AsSpan()), _accent);
        _teamBadgeLabel.Text = string.IsNullOrWhiteSpace(state.TeamName) ? "?" : resolvedTeam.ToUpperInvariant();

        ApplyStatusBadge(state);
        RebuildCards(state);
    }

    private void ApplyStatusBadge(WH40KCommandNodeBoundUserInterfaceState state)
    {
        if (state.ReinforcementCooldownSeconds > 0)
        {
            _statusBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#3A2E1D".AsSpan()),
                WH40KCommandUiStyles.WarningBadge);
            _statusBadgeLabel.Text = $"{state.ReinforcementCooldownSeconds:000}s";
            return;
        }

        if (state.Phase == WH40KBattlePhase.Assault)
        {
            _statusBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#223B2F".AsSpan()),
                WH40KCommandUiStyles.ReadyBadge);
            _statusBadgeLabel.Text = Loc.GetString("w40k-cmd-status-badge-ready");
            return;
        }

        _statusBadge.PanelOverride = state.Phase >= WH40KBattlePhase.Apocalypse
            ? WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#3A2A2A".AsSpan()),
                WH40KCommandUiStyles.DangerBadge)
            : WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#22313B".AsSpan()),
                WH40KCommandUiStyles.InfoBadge);
        _statusBadgeLabel.Text = Loc.GetString(state.Phase >= WH40KBattlePhase.Apocalypse
            ? "wh40k-phase-apocalypse-name"
            : "wh40k-phase-preparation-name");
    }

    private string BuildStatusLine(WH40KCommandNodeBoundUserInterfaceState state)
    {
        if (state.ReinforcementCooldownSeconds > 0)
        {
            return Loc.GetString(
                "w40k-cmd-reinforcement-readiness-cooldown",
                ("seconds", state.ReinforcementCooldownSeconds));
        }

        if (state.Phase < WH40KBattlePhase.Assault)
            return Loc.GetString("w40k-cmd-reinforcement-readiness-phase-lock");

        if (state.Phase >= WH40KBattlePhase.Apocalypse)
            return Loc.GetString("w40k-cmd-reinforcement-readiness-apocalypse-lock");

        return Loc.GetString("w40k-cmd-reinforcement-readiness-ready");
    }

    private void RebuildCards(WH40KCommandNodeBoundUserInterfaceState state)
    {
        _cardsRoot.RemoveAllChildren();
        var options = state.ReinforcementOptions ?? Array.Empty<WH40KCommandNodeReinforcementOptionState>();
        if (options.Length == 0)
        {
            var empty = new PanelContainer
            {
                PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                    WH40KCommandUiStyles.CardBackgroundMuted,
                    WH40KCommandUiStyles.MutedBorder)
            };
            empty.AddChild(new Label
            {
                Text = Loc.GetString("w40k-cmd-reinforcement-window-empty"),
                StyleClasses = { "LabelSubText" }
            });
            _cardsRoot.AddChild(empty);
            return;
        }

        var compactMode = options.Length <= 3;
        _cardsScroll.HScrollEnabled = !compactMode;
        _cardsRoot.HorizontalExpand = compactMode;
        _cardsRoot.SeparationOverride = compactMode ? 8 : 10;

        foreach (var option in options)
        {
            _cardsRoot.AddChild(CreateOptionCard(state, option, compactMode));
        }
    }

    private Control CreateOptionCard(
        WH40KCommandNodeBoundUserInterfaceState state,
        WH40KCommandNodeReinforcementOptionState option,
        bool compactMode)
    {
        var selectedCount = ResolveSelectedCount(option.OptionId, option.MaxCount);
        var selectedCost = GetCostByCount(option, selectedCount);
        var canCall = CanCall(state, selectedCount, option.MaxCount, selectedCost);

        var card = new PanelContainer
        {
            MinWidth = compactMode ? 0 : 280,
            HorizontalExpand = compactMode,
            SizeFlagsStretchRatio = 1f,
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                WH40KCommandUiStyles.CardBackground,
                canCall ? _accent : WH40KCommandUiStyles.MutedBorder)
        };
        if (!compactMode)
            card.MaxWidth = 300;

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6
        };
        card.AddChild(box);

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8
        };
        box.AddChild(header);

        header.AddChild(new Label
        {
            Text = WH40KCommandUiStyles.ResolveLocalizedOrRaw(option.Name),
            StyleClasses = { "LabelBig" },
            HorizontalExpand = true,
            ModulateSelfOverride = _accent,
            ClipText = true
        });

        var countBadge = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#22313B".AsSpan()),
                canCall ? _accent : WH40KCommandUiStyles.InfoBadge)
        };
        countBadge.AddChild(new Label
        {
            Text = $"x{selectedCount}",
            ClipText = true
        });
        header.AddChild(countBadge);

        var previewHolder = new PanelContainer
        {
            MinHeight = compactMode ? 108 : 120,
            HorizontalExpand = true,
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                WH40KCommandUiStyles.CardBackgroundAlt,
                WH40KCommandUiStyles.MutedBorder)
        };
        box.AddChild(previewHolder);
        BuildPreviewCluster(previewHolder, option.PreviewPrototypeId, selectedCount);

        var description = new Label
        {
            HorizontalExpand = true,
            ClipText = true,
            StyleClasses = { "LabelSubText" }
        };
        description.Text = CompactText(WH40KCommandUiStyles.ResolveLocalizedOrRaw(option.Description), 96);
        box.AddChild(description);

        var gearCard = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                WH40KCommandUiStyles.CardBackgroundAlt,
                WH40KCommandUiStyles.MutedBorder)
        };
        box.AddChild(gearCard);

        var gearLabel = new Label
        {
            HorizontalExpand = true,
            ClipText = true,
            StyleClasses = { "LabelSubText" }
        };
        var gearParts = option.GearSummary
            .Split(", ", StringSplitOptions.RemoveEmptyEntries)
            .Select(part => WH40KCommandUiStyles.ResolveLocalizedOrRaw(part.Trim()))
            .Where(s => !string.IsNullOrWhiteSpace(s));
        gearLabel.Text = CompactText(
            Loc.GetString("w40k-cmd-reinforcement-window-gear", ("gear", string.Join(", ", gearParts))),
            90);
        gearCard.AddChild(gearLabel);

        var selectorRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4
        };
        box.AddChild(selectorRow);

        for (var count = 1; count <= 3; count++)
        {
            var currentCount = count;
            var cost = GetCostByCount(option, currentCount);
            var selectable = currentCount <= option.MaxCount && cost > 0;

            var button = new Button
            {
                HorizontalExpand = true,
                Text = Loc.GetString(
                    "w40k-cmd-reinforcement-window-count-button",
                    ("count", currentCount),
                    ("cost", cost)),
                Disabled = !selectable
            };
            if (selectedCount == currentCount)
                button.Modulate = _accent;

            button.OnPressed += _ =>
            {
                _selectedCounts[option.OptionId] = currentCount;
                UpdateState(state);
            };
            selectorRow.AddChild(button);
        }

        var callButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString(
                "w40k-cmd-reinforcement-window-call-button",
                ("count", selectedCount),
                ("cost", selectedCost)),
            Disabled = !canCall
        };
        callButton.OnPressed += _ => OnCallRequested?.Invoke(option.OptionId, selectedCount);
        box.AddChild(callButton);

        return card;
    }

    private static void BuildPreviewCluster(PanelContainer holder, string previewPrototypeId, int count)
    {
        var layout = new LayoutContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            InheritChildMeasure = false
        };
        holder.AddChild(layout);

        switch (Math.Clamp(count, 1, 3))
        {
            case 1:
                AddPreview(layout, previewPrototypeId, Vector2.Zero, 2.2f, 1f);
                break;

            case 2:
                AddPreview(layout, previewPrototypeId, new Vector2(-32f, 6f), 2.0f);
                AddPreview(layout, previewPrototypeId, new Vector2(32f, 6f), 2.0f);
                break;

            default:
                AddPreview(layout, previewPrototypeId, new Vector2(-40f, 10f), 1.8f);
                AddPreview(layout, previewPrototypeId, new Vector2(40f, 10f), 1.8f);
                AddPreview(layout, previewPrototypeId, new Vector2(0f, -6f), 2.1f, 1f);
                break;
        }
    }

    private static void AddPreview(
        LayoutContainer layout,
        string previewPrototypeId,
        Vector2 offset,
        float scale,
        float alpha = 1f)
    {
        var preview = new EntityPrototypeView
        {
            MinSize = new Vector2(108, 108),
            Scale = new Vector2(scale, scale),
            ModulateSelfOverride = Color.White.WithAlpha(Math.Clamp(alpha, 0f, 1f))
        };

        if (!string.IsNullOrWhiteSpace(previewPrototypeId))
            preview.SetPrototype(previewPrototypeId);

        layout.AddChild(preview);

        const float halfSize = 54f;
        LayoutContainer.SetAnchorLeft(preview, 0.5f);
        LayoutContainer.SetAnchorRight(preview, 0.5f);
        LayoutContainer.SetAnchorTop(preview, 0.5f);
        LayoutContainer.SetAnchorBottom(preview, 0.5f);
        LayoutContainer.SetMarginLeft(preview, offset.X - halfSize);
        LayoutContainer.SetMarginRight(preview, offset.X + halfSize);
        LayoutContainer.SetMarginTop(preview, offset.Y - halfSize);
        LayoutContainer.SetMarginBottom(preview, offset.Y + halfSize);
    }

    private int ResolveSelectedCount(string optionId, int maxCount)
    {
        if (_selectedCounts.TryGetValue(optionId, out var selected))
            return Math.Clamp(selected, 1, Math.Max(1, maxCount));

        return 1;
    }

    private static bool CanCall(
        WH40KCommandNodeBoundUserInterfaceState state,
        int selectedCount,
        int maxCount,
        int cost)
    {
        if (selectedCount < 1 || selectedCount > maxCount)
            return false;

        if (state.Phase != WH40KBattlePhase.Assault)
            return false;

        if (state.ReinforcementCooldownSeconds > 0)
            return false;

        return cost > 0 && state.CommandPoints >= cost;
    }

    private static int GetCostByCount(WH40KCommandNodeReinforcementOptionState option, int count)
    {
        return count switch
        {
            3 => option.CostX3,
            2 => option.CostX2,
            _ => option.CostX1
        };
    }

    private static string CompactText(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var compact = text.Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);

        while (compact.Contains("  ", StringComparison.Ordinal))
        {
            compact = compact.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (compact.Length <= maxLength)
            return compact;

        return compact[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }
}
