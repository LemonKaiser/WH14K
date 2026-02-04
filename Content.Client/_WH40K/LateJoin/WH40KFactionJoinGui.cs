using System.Numerics;
using Content.Client.LateJoin;
using Content.Shared._WH40K.LateJoin;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Content.Client.Resources;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.LateJoin;

public sealed class WH40KFactionJoinGui : DefaultWindow
{
    [Dependency] private readonly IEntitySystemManager _entitySystem = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;

    private readonly WH40KFactionSystem _factionSystem;
    private readonly SpriteSystem _sprites;
    private readonly ISawmill _sawmill;

    private readonly BoxContainer _root;
    private readonly BoxContainer _row;
    private bool _selectionHandled;
    private bool _subscribed;

    public WH40KFactionJoinGui(IReadOnlyList<WH40KFactionInfo>? initialFactions = null)
    {
        MinSize = SetSize = new Vector2(470, 450);
        IoCManager.InjectDependencies(this);

        _factionSystem = _entitySystem.GetEntitySystem<WH40KFactionSystem>();
        _sprites = _entitySystem.GetEntitySystem<SpriteSystem>();
        _sawmill = _logManager.GetSawmill("wh40k.factionjoin");

        Title = Loc.GetString("wh40k-faction-join-title");

        _row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        _root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Children = { _row }
        };

        ContentsContainer.AddChild(_root);

        _factionSystem.FactionsUpdated += OnFactionsUpdated;
        _subscribed = true;
        OnClose += HandleClosed;
        if (initialFactions != null)
            ApplyFactions(initialFactions);
        _factionSystem.RequestFactions();
    }

    private void OnFactionsUpdated(IReadOnlyList<WH40KFactionInfo> factions)
    {
        ApplyFactions(factions);
    }

    private void ApplyFactions(IReadOnlyList<WH40KFactionInfo> factions)
    {
        _row.RemoveAllChildren();

        if (factions.Count == 0)
        {
            new LateJoinGui().OpenCentered();
            Close();
            return;
        }

        for (var i = 0; i < factions.Count; i++)
        {
            if (i > 0)
                _row.AddChild(BuildSeparator());

            var button = BuildFactionButton(factions[i]);
            _row.AddChild(button);
        }
    }

    private Control BuildFactionButton(WH40KFactionInfo faction)
    {
        var button = new ContainerButton
        {
            MinSize = new Vector2(140, 220),
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var inner = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var topSpacer = new Control
        {
            VerticalExpand = true,
            SizeFlagsStretchRatio = 1.3f,
        };

        var icon = new TextureRect
        {
            TextureScale = new Vector2(4f, 4f),
            Stretch = TextureRect.StretchMode.KeepCentered,
            MinSize = new Vector2(96, 96),
            HorizontalAlignment = HAlignment.Center,
        };

        if (faction.Logo != null)
            icon.Texture = _sprites.Frame0(faction.Logo);
        else
            icon.Visible = false;

        var labelSpacer = new Control
        {
            MinSize = new Vector2(0, 30),
        };

        var label = new Label
        {
            HorizontalAlignment = HAlignment.Center,
            Align = Label.AlignMode.Center,
            Text = Loc.GetString(faction.Name)
        };
        label.FontOverride = _resourceCache.GetFont("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf", 28);

        var bottomSpacer = new Control
        {
            VerticalExpand = true,
            SizeFlagsStretchRatio = 0.4f,
        };

        inner.AddChild(topSpacer);
        inner.AddChild(icon);
        inner.AddChild(labelSpacer);
        inner.AddChild(label);
        inner.AddChild(bottomSpacer);
        button.AddChild(inner);

        button.OnPressed += _ =>
        {
            if (_selectionHandled)
                return;

            _selectionHandled = true;
            if (faction.Departments.Count == 0)
            {
                _sawmill.Info($"Faction '{faction.Id}' has no departments; late join list will be empty.");
            }

            new LateJoinGui(faction.Departments).OpenCentered();
            Close();
        };

        return button;
    }

    private Control BuildSeparator()
    {
        var separator = new PanelContainer
        {
            MinSize = new Vector2(2, 0),
            VerticalExpand = true,
        };

        separator.PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#3D4059"),
        };

        return separator;
    }

    private void HandleClosed()
    {
        if (!_subscribed)
            return;

        _subscribed = false;
        _factionSystem.FactionsUpdated -= OnFactionsUpdated;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && _subscribed)
            _factionSystem.FactionsUpdated -= OnFactionsUpdated;
    }
}
