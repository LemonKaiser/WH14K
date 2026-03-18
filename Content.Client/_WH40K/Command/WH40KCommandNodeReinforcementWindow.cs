using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.GameMode;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeReinforcementWindow : FancyWindow
{
    private static readonly Color ImperiumColor = Color.FromHex("#F3C548");

    public event Action<string, int>? OnCallRequested;

    private readonly Label _teamLine;
    private readonly Label _statusLine;
    private readonly ScrollContainer _cardsScroll;
    private readonly BoxContainer _cardsRoot;
    private readonly StyleBoxFlat _headerStyle;
    private readonly Dictionary<string, int> _selectedCounts = new();

    public WH40KCommandNodeReinforcementWindow()
    {
        Title = Loc.GetString("wh40k-command-node-reinforcement-window-title");
        MinSize = SetSize = new Vector2(1120, 620);

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
        _statusLine = new Label();
        headerBox.AddChild(_teamLine);
        headerBox.AddChild(_statusLine);

        var body = new PanelContainer
        {
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#1F2433"),
                BorderColor = Color.FromHex("#56607A"),
                BorderThickness = new Thickness(1)
            }
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
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state)
    {
        var accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, ImperiumColor);
        _headerStyle.BorderColor = accent;

        Title = Loc.GetString("wh40k-command-node-reinforcement-window-title-team", ("team", state.TeamName));
        _teamLine.Text = Loc.GetString("wh40k-command-node-team", ("team", state.TeamName));
        _teamLine.ModulateSelfOverride = accent;
        _statusLine.Text = BuildStatusLine(state);
        RebuildCards(state, accent);
    }

    private string BuildStatusLine(WH40KCommandNodeBoundUserInterfaceState state)
    {
        if (state.ReinforcementCooldownSeconds > 0)
        {
            return Loc.GetString(
                "wh40k-command-node-reinforcement-readiness-cooldown",
                ("seconds", state.ReinforcementCooldownSeconds));
        }

        if (state.Phase < WH40KBattlePhase.Assault)
            return Loc.GetString("wh40k-command-node-reinforcement-readiness-phase-lock");

        if (state.Phase >= WH40KBattlePhase.Apocalypse)
            return Loc.GetString("wh40k-command-node-reinforcement-readiness-apocalypse-lock");

        return Loc.GetString("wh40k-command-node-reinforcement-readiness-ready");
    }

    private void RebuildCards(WH40KCommandNodeBoundUserInterfaceState state, Color accent)
    {
        _cardsRoot.RemoveAllChildren();
        var options = state.ReinforcementOptions ?? Array.Empty<WH40KCommandNodeReinforcementOptionState>();
        if (options.Length == 0)
        {
            _cardsRoot.AddChild(new Label
            {
                Text = Loc.GetString("wh40k-command-node-reinforcement-window-empty")
            });
            return;
        }

        var compactMode = options.Length <= 3;
        _cardsScroll.HScrollEnabled = !compactMode;
        _cardsRoot.HorizontalExpand = compactMode;
        _cardsRoot.SeparationOverride = compactMode ? 8 : 10;

        foreach (var option in options)
        {
            _cardsRoot.AddChild(CreateOptionCard(state, option, accent, compactMode));
        }
    }

    private Control CreateOptionCard(
        WH40KCommandNodeBoundUserInterfaceState state,
        WH40KCommandNodeReinforcementOptionState option,
        Color accent,
        bool compactMode)
    {
        var selectedCount = ResolveSelectedCount(option.OptionId, option.MaxCount);
        var card = new PanelContainer
        {
            MinWidth = compactMode ? 0 : 320,
            VerticalExpand = true,
            HorizontalExpand = compactMode,
            SizeFlagsStretchRatio = 1f,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#252A3A"),
                BorderColor = accent,
                BorderThickness = new Thickness(1)
            }
        };
        if (!compactMode)
            card.MaxWidth = 360;

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            VerticalExpand = true
        };
        card.AddChild(box);

        box.AddChild(new Label
        {
            Text = option.Name,
            ModulateSelfOverride = accent
        });

        var previewHolder = new PanelContainer
        {
            MinHeight = compactMode ? 156 : 170,
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#1C2030"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
        };
        box.AddChild(previewHolder);
        BuildPreviewCluster(previewHolder, option.PreviewPrototypeId, selectedCount);

        box.AddChild(CreateWrappedLabel(option.Description, compactMode ? 336f : 300f));

        box.AddChild(CreateWrappedLabel(
            Loc.GetString(
                "wh40k-command-node-reinforcement-window-gear",
                ("gear", option.GearSummary)),
            compactMode ? 336f : 300f));

        box.AddChild(new Control
        {
            VerticalExpand = true
        });

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
                    "wh40k-command-node-reinforcement-window-count-button",
                    ("count", currentCount),
                    ("cost", cost)),
                Disabled = !selectable,
                Modulate = selectedCount == currentCount ? accent : Color.White
            };
            button.OnPressed += _ =>
            {
                _selectedCounts[option.OptionId] = currentCount;
                UpdateState(state);
            };
            selectorRow.AddChild(button);
        }

        var selectedCost = GetCostByCount(option, selectedCount);
        var canCall = CanCall(state, selectedCount, option.MaxCount, selectedCost);
        var callButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString(
                "wh40k-command-node-reinforcement-window-call-button",
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
                AddPreview(layout, previewPrototypeId, Vector2.Zero, 2.45f, 1f);
                break;

            case 2:
                AddPreview(layout, previewPrototypeId, new Vector2(-34f, 4f), 2.2f);
                AddPreview(layout, previewPrototypeId, new Vector2(34f, 4f), 2.2f);
                break;

            default:
                AddPreview(layout, previewPrototypeId, new Vector2(-44f, 8f), 1.95f);
                AddPreview(layout, previewPrototypeId, new Vector2(44f, 8f), 1.95f);
                AddPreview(layout, previewPrototypeId, new Vector2(0f, -6f), 2.45f, 1f);
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
            MinSize = new Vector2(140, 140),
            Scale = new Vector2(scale, scale),
            ModulateSelfOverride = Color.White.WithAlpha(Math.Clamp(alpha, 0f, 1f))
        };

        if (!string.IsNullOrWhiteSpace(previewPrototypeId))
            preview.SetPrototype(previewPrototypeId);

        layout.AddChild(preview);

        const float halfSize = 70f;
        LayoutContainer.SetAnchorLeft(preview, 0.5f);
        LayoutContainer.SetAnchorRight(preview, 0.5f);
        LayoutContainer.SetAnchorTop(preview, 0.5f);
        LayoutContainer.SetAnchorBottom(preview, 0.5f);
        LayoutContainer.SetMarginLeft(preview, offset.X - halfSize);
        LayoutContainer.SetMarginRight(preview, offset.X + halfSize);
        LayoutContainer.SetMarginTop(preview, offset.Y - halfSize);
        LayoutContainer.SetMarginBottom(preview, offset.Y + halfSize);
    }

    private static RichTextLabel CreateWrappedLabel(string text, float maxWidth, Color? color = null)
    {
        var label = new RichTextLabel
        {
            HorizontalExpand = true,
            MaxWidth = maxWidth
        };

        var normalized = string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Replace("\\n", "\n", StringComparison.Ordinal);
        label.SetMessage(
            FormattedMessage.FromMarkupPermissive(FormattedMessage.EscapeText(normalized)),
            tagsAllowed: null,
            defaultColor: color ?? Color.White);

        return label;
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
}
