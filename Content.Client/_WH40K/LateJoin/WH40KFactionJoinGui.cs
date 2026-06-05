using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Localization;
using Content.Client.Resources;
using Content.Client.UserInterface.Controls;
using Content.Shared.CCVar;
using Content.Shared._WH40K.LateJoin;
using Content.Shared.Roles;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.LateJoin;

public sealed partial class WH40KFactionJoinGui : FancyWindow, ILocalizedControl
{
    private static readonly ProtoId<DepartmentPrototype> MechanicusDepartment = "Mechanicus";
    private static readonly ProtoId<DepartmentPrototype> DarkMechanicumDepartment = "DarkMechanicum";

    private static readonly ProtoId<JobPrototype>[] ImperiumHiddenLateJoinJobs =
    [
        "SpecialistHWS",
        "SpecialistSWS"
    ];

    private static readonly ProtoId<JobPrototype>[] HereticsHiddenLateJoinJobs =
    [
        "HSpecialistHWS",
        "HSpecialistSWS"
    ];

    private static readonly Dictionary<string, Color> FactionAccentColors = new()
    {
        { "Imperium", Color.FromHex("#F3C548") },
        { "Heretics", Color.FromHex("#C7483F") },
        { "Tau", Color.FromHex("#F4F4F4") },
    };

    private static readonly Color CardBackground = Color.FromHex("#12151B");
    private static readonly Color CardHoverBackground = Color.FromHex("#1B1F27");
    private static readonly Color SeparatorColor = Color.FromHex("#5D4D2A");
    private static readonly Color SoftTextColor = Color.FromHex("#A79668");
    private static readonly Color VsTextColor = Color.FromHex("#7E6A3A");
    private static readonly Vector2 SingleRowBaseWindowSize = new(500f, 404f);
    private static readonly Vector2 TwoRowBaseWindowSize = new(520f, 500f);
    private static readonly Vector2 SpaciousCardSize = new(200f, 280f);
    private static readonly Vector2 CompactCardSize = new(220f, 178f);
    private const float WindowChromeWidth = 24f;
    private const float CardRowMargin = 8f;
    private const int SingleRowGap = 8;
    private const int TwoRowGap = 10;
    private const float SeparatorWidth = 44f;

    [Dependency] private  IEntitySystemManager _entitySystems = default!;
    [Dependency] private  IResourceCache _resourceCache = default!;
    [Dependency] private  IConfigurationManager _cfg = default!;

    private readonly SpriteSystem _sprites;
    private readonly BoxContainer _root;
    private readonly ScrollContainer _factionScroll;
    private readonly BoxContainer _factionLayoutHost;
    private readonly PanelContainer _statusPanel;
    private readonly RichTextLabel _statusLabel;
    private IReadOnlyList<WH40KFactionInfo> _latestFactions;
    private float _uiWindowOpacity = 1f;

    public event Action<string>? FactionSelected;

    public WH40KFactionSelectionPurpose Purpose { get; }

    public WH40KFactionJoinGui(WH40KFactionSelectionPurpose purpose, IReadOnlyList<WH40KFactionInfo> initialFactions)
    {
        MinSize = SetSize = SingleRowBaseWindowSize;
        IoCManager.InjectDependencies(this);

        Purpose = purpose;
        _latestFactions = initialFactions;
        _sprites = _entitySystems.GetEntitySystem<SpriteSystem>();

        _factionLayoutHost = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        _factionScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            Children = { _factionLayoutHost },
        };

        _statusLabel = new RichTextLabel
        {
            HorizontalExpand = true,
        };
        _statusLabel.SetMessage(string.Empty, defaultColor: SoftTextColor);

        _statusPanel = new PanelContainer
        {
            HorizontalExpand = true,
            Visible = false,
            Margin = new Thickness(8, 0, 8, 8),
            PanelOverride = CreateCardStyle(CardBackground.WithAlpha(0.95f), SeparatorColor, new Thickness(1), 10, 10, 8, 8)
        };
        _statusPanel.AddChild(_statusLabel);

        _root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 4,
            Children = { _factionScroll, _statusPanel }
        };

        ContentsContainer.AddChild(_root);
        ApplyFactions(initialFactions);
        _cfg.OnValueChanged(CCVars.UiWindowOpacity, OnUiWindowOpacityChanged, true);
        Relocalize();
    }

    private void OnUiWindowOpacityChanged(float opacity)
    {
        _uiWindowOpacity = opacity;
        _statusPanel.PanelOverride = CreateCardStyle(CardBackground.WithAlpha(0.95f), SeparatorColor, new Thickness(1), 10, 10, 8, 8);
        ApplyFactions(_latestFactions);
    }

    public void Relocalize()
    {
        Title = Loc.GetString("wh40k-faction-join-title");
        ApplyFactions(_latestFactions);
    }

    public void UpdateFactions(IReadOnlyList<WH40KFactionInfo> factions)
    {
        ApplyFactions(factions);
    }

    public static IReadOnlyList<ProtoId<DepartmentPrototype>> BuildDepartmentFilterForFaction(WH40KFactionInfo faction)
    {
        return faction.Id switch
        {
            "Imperium" => faction.Departments.Where(d => d != MechanicusDepartment).ToList(),
            "Heretics" => faction.Departments.Where(d => d != DarkMechanicumDepartment).ToList(),
            _ => faction.Departments
        };
    }

    public static IReadOnlyCollection<ProtoId<JobPrototype>>? BuildHiddenJobsForFaction(string factionId)
    {
        return factionId switch
        {
            "Imperium" => ImperiumHiddenLateJoinJobs,
            "Heretics" => HereticsHiddenLateJoinJobs,
            _ => null
        };
    }

    private void ApplyFactions(IReadOnlyList<WH40KFactionInfo> factions)
    {
        _latestFactions = factions;
        _factionLayoutHost.RemoveAllChildren();
        UpdateStatusPanel(factions);
        _factionScroll.HScrollEnabled = factions.Count > 4;
        UpdateWindowSize(factions.Count);

        if (factions.Count == 0)
        {
            _factionLayoutHost.AddChild(new Label
            {
                HorizontalExpand = true,
                VerticalExpand = true,
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                Text = Loc.GetString("wh40k-faction-join-empty"),
            });
            return;
        }

        _factionLayoutHost.AddChild(factions.Count <= 3
            ? BuildSingleRowLayout(factions)
            : BuildTwoRowLayout(factions));
    }

    private Control BuildSingleRowLayout(IReadOnlyList<WH40KFactionInfo> factions)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = false,
            VerticalExpand = true,
            HorizontalAlignment = HAlignment.Center,
            Margin = new Thickness(8, 8, 8, 8),
            SeparationOverride = SingleRowGap,
            MinSize = new Vector2(GetSingleRowContentWidth(factions.Count), 0f),
        };

        for (var index = 0; index < factions.Count; index++)
        {
            if (index > 0)
                row.AddChild(BuildSeparator());

            row.AddChild(BuildFactionButton(factions[index], compact: false));
        }

        return row;
    }

    private Control BuildTwoRowLayout(IReadOnlyList<WH40KFactionInfo> factions)
    {
        var columnCount = (factions.Count + 1) / 2;
        var columns = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = false,
            VerticalExpand = true,
            HorizontalAlignment = HAlignment.Center,
            Margin = new Thickness(8, 8, 8, 8),
            SeparationOverride = TwoRowGap,
            MinSize = new Vector2(GetTwoRowContentWidth(columnCount), 0f),
        };

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var column = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = false,
                VerticalExpand = true,
                SeparationOverride = 8,
                MinSize = new Vector2(CompactCardSize.X, 0f),
            };

            column.AddChild(BuildFactionButton(factions[columnIndex], compact: true));

            var secondRowIndex = columnIndex + columnCount;
            if (secondRowIndex < factions.Count)
            {
                column.AddChild(BuildFactionButton(factions[secondRowIndex], compact: true));
            }
            else
            {
                column.AddChild(new Control
                {
                    MinSize = CompactCardSize,
                    VerticalExpand = true,
                });
            }

            columns.AddChild(column);
        }

        return columns;
    }

    private Control BuildFactionButton(WH40KFactionInfo faction, bool compact)
    {
        var accent = FactionAccentColors.GetValueOrDefault(faction.Id, Color.White);
        var accentDim = accent.WithAlpha(0.5f);
        var disabled = !faction.CanSelect;
        var cardSize = compact ? CompactCardSize : SpaciousCardSize;
        var iconSize = compact ? new Vector2(88f, 88f) : new Vector2(110f, 110f);
        var iconScale = compact ? new Vector2(3.85f, 3.85f) : new Vector2(4.5f, 4.5f);
        var countFontSize = compact ? 20 : 24;
        var nameFontSize = compact ? 22 : 26;
        var topSpacerRatio = compact ? 0.35f : 0.6f;
        var bottomSpacerRatio = compact ? 0.2f : 0.4f;

        var button = new ContainerButton
        {
            MinSize = cardSize,
            HorizontalExpand = false,
            VerticalExpand = true,
            Disabled = disabled,
        };

        if (disabled && !string.IsNullOrWhiteSpace(faction.DisabledReason))
            button.ToolTip = GetDisabledReasonText(faction);

        var normalStyle = CreateCardStyle(
            disabled ? CardBackground.WithAlpha(0.85f) : CardBackground,
            disabled ? accentDim.WithAlpha(0.5f) : accentDim,
            new Thickness(2),
            16, 16, 12, 12);

        var hoverStyle = CreateCardStyle(
            CardHoverBackground,
            accent,
            new Thickness(2),
            16, 16, 12, 12);

        var cardPanel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = normalStyle,
        };

        var accentBar = new PanelContainer
        {
            MinSize = new Vector2(0, 3),
            HorizontalExpand = true,
            PanelOverride = CreateFlatStyle(accent),
        };

        var inner = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var countLabel = new Label
        {
            HorizontalAlignment = HAlignment.Center,
            Align = Label.AlignMode.Center,
            Text = faction.PlayerCount.ToString(),
            FontColorOverride = disabled ? accentDim : accent,
        };
        countLabel.FontOverride = _resourceCache.GetFont(new ResPath("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf"), countFontSize);

        var topSpacer = new Control
        {
            VerticalExpand = true,
            SizeFlagsStretchRatio = topSpacerRatio,
        };

        var icon = new TextureRect
        {
            TextureScale = iconScale,
            Stretch = TextureRect.StretchMode.KeepCentered,
            MinSize = iconSize,
            HorizontalAlignment = HAlignment.Center,
            ModulateSelfOverride = disabled ? Color.White.WithAlpha(0.55f) : Color.White,
        };

        if (faction.Logo != null)
            icon.Texture = _sprites.Frame0(faction.Logo);
        else
            icon.Visible = false;

        var bottomSpacer = new Control
        {
            VerticalExpand = true,
            SizeFlagsStretchRatio = bottomSpacerRatio,
        };

        var nameLabel = new Label
        {
            HorizontalAlignment = HAlignment.Center,
            Align = Label.AlignMode.Center,
            Text = Loc.GetString(faction.Name),
            FontColorOverride = disabled ? accentDim : accent,
        };
        nameLabel.FontOverride = _resourceCache.GetFont(new ResPath("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf"), nameFontSize);

        inner.AddChild(accentBar);
        inner.AddChild(countLabel);
        inner.AddChild(topSpacer);
        inner.AddChild(icon);
        inner.AddChild(bottomSpacer);
        inner.AddChild(nameLabel);

        inner.AddChild(new Control
        {
            VerticalExpand = true,
            SizeFlagsStretchRatio = 0.2f,
        });

        cardPanel.AddChild(inner);
        button.AddChild(cardPanel);

        button.OnMouseEntered += _ =>
        {
            if (!disabled)
                cardPanel.PanelOverride = hoverStyle;
        };
        button.OnMouseExited += _ => cardPanel.PanelOverride = normalStyle;
        button.OnPressed += _ =>
        {
            if (!faction.CanSelect)
                return;

            FactionSelected?.Invoke(faction.Id);
        };

        return button;
    }

    private Control BuildSeparator()
    {
        var container = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            MinSize = new Vector2(44, 0),
            HorizontalAlignment = HAlignment.Center,
        };

        var topLine = new PanelContainer
        {
            VerticalExpand = true,
            HorizontalAlignment = HAlignment.Center,
            MinSize = new Vector2(2, 0),
            PanelOverride = CreateFlatStyle(SeparatorColor),
        };

        var vsLabel = new Label
        {
            HorizontalAlignment = HAlignment.Center,
            Align = Label.AlignMode.Center,
            Text = "VS",
            FontColorOverride = VsTextColor,
            Margin = new Thickness(0, 6, 0, 6),
        };
        vsLabel.FontOverride = _resourceCache.GetFont(new ResPath("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf"), 16);

        var bottomLine = new PanelContainer
        {
            VerticalExpand = true,
            HorizontalAlignment = HAlignment.Center,
            MinSize = new Vector2(2, 0),
            PanelOverride = CreateFlatStyle(SeparatorColor),
        };

        container.AddChild(topLine);
        container.AddChild(vsLabel);
        container.AddChild(bottomLine);
        return container;
    }

    private void UpdateWindowSize(int factionCount)
    {
        var size = factionCount switch
        {
            <= 0 => SingleRowBaseWindowSize,
            <= 3 => new Vector2(
                MathF.Max(SingleRowBaseWindowSize.X, GetSingleRowContentWidth(factionCount) + WindowChromeWidth),
                SingleRowBaseWindowSize.Y),
            _ => new Vector2(
                TwoRowBaseWindowSize.X,
                TwoRowBaseWindowSize.Y),
        };

        MinSize = size;
        SetSize = size;
    }

    private static float GetSingleRowContentWidth(int factionCount)
    {
        if (factionCount <= 0)
            return 0f;

        var separators = Math.Max(0, factionCount - 1);
        var gapCount = Math.Max(0, factionCount * 2 - 2);
        return CardRowMargin * 2f +
               factionCount * SpaciousCardSize.X +
               separators * SeparatorWidth +
               gapCount * SingleRowGap;
    }

    private static float GetTwoRowContentWidth(int columnCount)
    {
        if (columnCount <= 0)
            return 0f;

        return CardRowMargin * 2f +
               columnCount * CompactCardSize.X +
               Math.Max(0, columnCount - 1) * TwoRowGap;
    }

    private void UpdateStatusPanel(IReadOnlyList<WH40KFactionInfo> factions)
    {
        var disabledFactions = factions
            .Where(faction => !faction.CanSelect && !string.IsNullOrWhiteSpace(faction.DisabledReason))
            .ToArray();

        if (disabledFactions.Length == 0)
        {
            _statusLabel.SetMessage(string.Empty, defaultColor: SoftTextColor);
            _statusPanel.Visible = false;
            return;
        }

        var showFactionPrefix = disabledFactions.Length > 1;
        var lines = disabledFactions.Select(faction => GetDisabledReasonText(faction, showFactionPrefix));

        _statusLabel.SetMessage(string.Join("\n", lines), defaultColor: SoftTextColor);
        _statusPanel.Visible = true;
    }

    private StyleBox CreateFlatStyle(Color background)
    {
        return WindowOpacityHelper.CreateOpacityStyle(new StyleBoxFlat { BackgroundColor = background }, _uiWindowOpacity);
    }

    private StyleBox CreateCardStyle(
        Color background,
        Color border,
        Thickness borderThickness,
        float marginLeft,
        float marginRight,
        float marginTop,
        float marginBottom)
    {
        var style = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = borderThickness,
            ContentMarginLeftOverride = marginLeft,
            ContentMarginRightOverride = marginRight,
            ContentMarginTopOverride = marginTop,
            ContentMarginBottomOverride = marginBottom,
        };

        return WindowOpacityHelper.CreateOpacityStyle(style, _uiWindowOpacity);
    }

    private static string GetDisabledReasonText(WH40KFactionInfo faction, bool showFactionPrefix = false)
    {
        if (string.IsNullOrWhiteSpace(faction.DisabledReason))
            return string.Empty;

        if (faction.DisabledReason == "wh40k-faction-balance-blocked")
        {
            return faction.Id switch
            {
                "Imperium" => Loc.GetString("wh40k-faction-balance-blocked-imperium"),
                "Heretics" => Loc.GetString("wh40k-faction-balance-blocked-heretics"),
                _ => Loc.GetString(faction.DisabledReason),
            };
        }

        var reason = Loc.GetString(faction.DisabledReason);
        if (!showFactionPrefix)
            return reason;

        return $"{Loc.GetString(faction.Name)}: {reason}";
    }

    [Obsolete]
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cfg.UnsubValueChanged(CCVars.UiWindowOpacity, OnUiWindowOpacityChanged);

#pragma warning disable CS0618
        base.Dispose(disposing);
#pragma warning restore CS0618
    }
}
