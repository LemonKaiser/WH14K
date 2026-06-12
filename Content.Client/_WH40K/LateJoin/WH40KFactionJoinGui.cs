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
    };

    private static readonly Color CardBackground = Color.FromHex("#12151B");
    private static readonly Color CardHoverBackground = Color.FromHex("#1B1F27");
    private static readonly Color SeparatorColor = Color.FromHex("#5D4D2A");
    private static readonly Color SoftTextColor = Color.FromHex("#A79668");
    private static readonly Color VsTextColor = Color.FromHex("#7E6A3A");

    [Dependency] private  IEntitySystemManager _entitySystems = default!;
    [Dependency] private  IResourceCache _resourceCache = default!;
    [Dependency] private  IConfigurationManager _cfg = default!;

    private readonly SpriteSystem _sprites;
    private readonly BoxContainer _root;
    private readonly BoxContainer _row;
    private readonly PanelContainer _statusPanel;
    private readonly RichTextLabel _statusLabel;
    private IReadOnlyList<WH40KFactionInfo> _latestFactions;
    private float _uiWindowOpacity = 1f;

    public event Action<string>? FactionSelected;

    public WH40KFactionSelectionPurpose Purpose { get; }

    public WH40KFactionJoinGui(WH40KFactionSelectionPurpose purpose, IReadOnlyList<WH40KFactionInfo> initialFactions)
    {
        MinSize = SetSize = new Vector2(500, 404);
        IoCManager.InjectDependencies(this);

        Purpose = purpose;
        _latestFactions = initialFactions;
        _sprites = _entitySystems.GetEntitySystem<SpriteSystem>();

        _row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(8, 8, 8, 8),
            SeparationOverride = 8,
        };

        _statusLabel = new RichTextLabel
        {
            HorizontalExpand = true,
            MaxWidth = 452,
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
            Children = { _row, _statusPanel }
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
        _row.RemoveAllChildren();
        UpdateStatusPanel(factions);

        if (factions.Count == 0)
        {
            _row.AddChild(new Label
            {
                HorizontalExpand = true,
                VerticalExpand = true,
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                Text = Loc.GetString("wh40k-faction-join-empty"),
            });
            return;
        }

        for (var index = 0; index < factions.Count; index++)
        {
            if (index > 0)
                _row.AddChild(BuildSeparator());

            _row.AddChild(BuildFactionButton(factions[index]));
        }
    }

    private Control BuildFactionButton(WH40KFactionInfo faction)
    {
        var accent = FactionAccentColors.GetValueOrDefault(faction.Id, Color.White);
        var accentDim = accent.WithAlpha(0.5f);
        var disabled = !faction.CanSelect;

        var button = new ContainerButton
        {
            MinSize = new Vector2(200, 280),
            HorizontalExpand = true,
            VerticalExpand = true,
            SizeFlagsStretchRatio = 1f,
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
        countLabel.FontOverride = _resourceCache.GetFont(new ResPath("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf"), 24);

        var topSpacer = new Control
        {
            VerticalExpand = true,
            SizeFlagsStretchRatio = 0.6f,
        };

        var icon = new TextureRect
        {
            TextureScale = new Vector2(4.5f, 4.5f),
            Stretch = TextureRect.StretchMode.KeepCentered,
            MinSize = new Vector2(110, 110),
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
            SizeFlagsStretchRatio = 0.4f,
        };

        var nameLabel = new Label
        {
            HorizontalAlignment = HAlignment.Center,
            Align = Label.AlignMode.Center,
            Text = Loc.GetString(faction.Name),
            FontColorOverride = disabled ? accentDim : accent,
        };
        nameLabel.FontOverride = _resourceCache.GetFont(new ResPath("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf"), 26);

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

        var reason = RequiresDisabledReasonCount(faction)
            ? Loc.GetString(faction.DisabledReason, ("count", faction.DisabledReasonCount))
            : Loc.GetString(faction.DisabledReason);

        if (!showFactionPrefix)
            return reason;

        return $"{Loc.GetString(faction.Name)}: {reason}";
    }

    private static bool RequiresDisabledReasonCount(WH40KFactionInfo faction)
    {
        if (faction.DisabledReasonCount <= 0 || string.IsNullOrWhiteSpace(faction.DisabledReason))
            return false;

        return faction.DisabledReason is
            "wh40k-faction-soft-streak-blocked" or
            "wh40k-faction-soft-streak-ignored" or
            "wh40k-faction-hard-streak-blocked";
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
