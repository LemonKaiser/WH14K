using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Localization;
using Content.Client.Resources;
using Content.Shared._WH40K.LateJoin;
using Content.Shared.Roles;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.LateJoin;

public sealed class WH40KFactionJoinGui : DefaultWindow, ILocalizedControl
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

    private static readonly Color CardBackground = Color.FromHex("#16161E");
    private static readonly Color CardHoverBackground = Color.FromHex("#22222E");
    private static readonly Color SeparatorColor = Color.FromHex("#3D4059");

    [Dependency] private readonly IEntitySystemManager _entitySystems = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;

    private readonly SpriteSystem _sprites;
    private readonly BoxContainer _root;
    private readonly BoxContainer _row;
    private IReadOnlyList<WH40KFactionInfo> _latestFactions;

    public event Action<string>? FactionSelected;

    public WH40KFactionSelectionPurpose Purpose { get; }

    public WH40KFactionJoinGui(WH40KFactionSelectionPurpose purpose, IReadOnlyList<WH40KFactionInfo> initialFactions)
    {
        MinSize = SetSize = new Vector2(500, 380);
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

        _root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Children = { _row }
        };

        ContentsContainer.AddChild(_root);
        ApplyFactions(initialFactions);
        Relocalize();
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
            button.ToolTip = Loc.GetString(faction.DisabledReason);

        var normalStyle = new StyleBoxFlat
        {
            BackgroundColor = disabled ? CardBackground.WithAlpha(0.85f) : CardBackground,
            BorderColor = disabled ? accentDim.WithAlpha(0.5f) : accentDim,
            BorderThickness = new Thickness(2),
            ContentMarginLeftOverride = 16,
            ContentMarginRightOverride = 16,
            ContentMarginTopOverride = 12,
            ContentMarginBottomOverride = 12,
        };

        var hoverStyle = new StyleBoxFlat
        {
            BackgroundColor = CardHoverBackground,
            BorderColor = accent,
            BorderThickness = new Thickness(2),
            ContentMarginLeftOverride = 16,
            ContentMarginRightOverride = 16,
            ContentMarginTopOverride = 12,
            ContentMarginBottomOverride = 12,
        };

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
            PanelOverride = new StyleBoxFlat { BackgroundColor = accent },
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

        if (disabled && !string.IsNullOrWhiteSpace(faction.DisabledReason))
        {
            var reasonLabel = new Label
            {
                HorizontalAlignment = HAlignment.Center,
                Align = Label.AlignMode.Center,
                Text = Loc.GetString(faction.DisabledReason),
                FontColorOverride = Color.FromHex("#8D90AA"),
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalExpand = true,
            };
            reasonLabel.FontOverride = _resourceCache.GetFont(new ResPath("/Fonts/NotoSans/NotoSans-Regular.ttf"), 12);
            inner.AddChild(reasonLabel);
        }

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
            PanelOverride = new StyleBoxFlat { BackgroundColor = SeparatorColor },
        };

        var vsLabel = new Label
        {
            HorizontalAlignment = HAlignment.Center,
            Align = Label.AlignMode.Center,
            Text = "VS",
            FontColorOverride = Color.FromHex("#6D7099"),
            Margin = new Thickness(0, 6, 0, 6),
        };
        vsLabel.FontOverride = _resourceCache.GetFont(new ResPath("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf"), 16);

        var bottomLine = new PanelContainer
        {
            VerticalExpand = true,
            HorizontalAlignment = HAlignment.Center,
            MinSize = new Vector2(2, 0),
            PanelOverride = new StyleBoxFlat { BackgroundColor = SeparatorColor },
        };

        container.AddChild(topLine);
        container.AddChild(vsLabel);
        container.AddChild(bottomLine);
        return container;
    }
}
