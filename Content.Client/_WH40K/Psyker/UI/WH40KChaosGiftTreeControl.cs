using System;
using System.Numerics;
using Content.Shared._WH40K.Psyker;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using UIControl = Robust.Client.UserInterface.Control;

namespace Content.Client._WH40K.Psyker.UI;

/// <summary>
/// Styled node graph for chaos gift upgrades.
/// It keeps button-driven interactions but renders a much richer viewport shell around them.
/// </summary>
public sealed class WH40KChaosGiftTreeControl : LayoutContainer
{
    private static readonly Color CanvasBackgroundColor = Color.FromHex("#101822");
    private static readonly Color CanvasBorderColor = Color.FromHex("#3E5365");
    private static readonly Color GridColor = Color.FromHex("#5E735B").WithAlpha(0.08f);
    private static readonly Color DesignFrameColor = Color.FromHex("#6A7A88").WithAlpha(0.38f);
    private static readonly Color HeaderTextColor = Color.FromHex("#E7F2F7");
    private static readonly Color RootConnectorColor = Color.FromHex("#D2A454");
    private static readonly Color CardBackgroundColor = Color.FromHex("#16212D");
    private static readonly Color CardMutedBackgroundColor = Color.FromHex("#141A22");
    private static readonly Color CardLockedBorderColor = Color.FromHex("#4E5D69");
    private static readonly Color RootBackgroundColor = Color.FromHex("#131A24");
    private static readonly Color FinalBackgroundColor = Color.FromHex("#18222E");
    private static readonly Color DefaultAccentColor = Color.FromHex("#7EC8FF");
    private static readonly Color DefaultRootBorderColor = Color.FromHex("#7A8B97");
    private static readonly Color ExReadyBorderColor = Color.FromHex("#D2A454");

    private static readonly Vector2 RootSize = new(108f, 86f);
    private static readonly Vector2 FinalSize = new(108f, 84f);
    private static readonly Vector2 NodeSize = new(128f, 76f);
    private static readonly Vector2 PathHeaderSize = new(168f, 40f);

    private const int TierNodeCost = 1;
    private const int ExNodeCost = 3;
    private const float GridStep = 44f;

    private readonly string[] _pathTooltipKeys =
    {
        "w40k-ch-upgrade-path-power",
        "w40k-ch-upgrade-path-cooldown",
        "w40k-ch-upgrade-path-cast-time",
    };

    private readonly string[] _pathHeaderKeys =
    {
        "w40k-ch-upgrade-path-power-short",
        "w40k-ch-upgrade-path-cooldown-short",
        "w40k-ch-upgrade-path-cast-time-short",
    };

    private readonly string[] _tierLabels = { "I", "II", "III" };
    private readonly int[] _tiers = new int[3];

    private readonly PanelContainer _rootPlate;
    private readonly TextureRect _rootIcon;
    private readonly StyleBoxFlat _rootStyle;

    private readonly PanelContainer _finalPlate;
    private readonly TextureRect _finalIcon;
    private readonly Label _finalLabel;
    private readonly Button _finalButton;
    private readonly StyleBoxFlat _finalStyle;

    private readonly PanelContainer[] _pathHeaderPlates = new PanelContainer[3];
    private readonly Label[] _pathHeaders = new Label[3];
    private readonly StyleBoxFlat[] _pathHeaderStyles = new StyleBoxFlat[3];
    private readonly Button[,] _nodes = new Button[3, 3];
    private readonly StyleBoxFlat[,] _nodeStyles = new StyleBoxFlat[3, 3];

    private Color _accentColor = DefaultAccentColor;
    private bool _exUnlocked;
    private bool _interactionEnabled = true;
    private float _time;
    private string? _focusTitle;

    public event Action<int, int>? UpgradeNodePressed;
    public event Action? UpgradeExPressed;

    public WH40KChaosGiftTreeControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
        MinHeight = 470f;
        MinWidth = 680f;

        _rootStyle = new StyleBoxFlat
        {
            BackgroundColor = RootBackgroundColor,
            BorderColor = DefaultRootBorderColor,
            BorderThickness = new Thickness(2f)
        };

        _rootPlate = new PanelContainer
        {
            PanelOverride = _rootStyle,
            MinSize = RootSize,
            SetSize = RootSize
        };

        _rootIcon = new TextureRect
        {
            MinSize = new Vector2(74f, 74f),
            SetSize = new Vector2(74f, 74f),
            HorizontalAlignment = UIControl.HAlignment.Center,
            VerticalAlignment = UIControl.VAlignment.Center,
            Stretch = TextureRect.StretchMode.KeepAspectCentered
        };
        _rootPlate.AddChild(_rootIcon);
        AddChild(_rootPlate);

        _finalStyle = new StyleBoxFlat
        {
            BackgroundColor = FinalBackgroundColor,
            BorderColor = DefaultRootBorderColor,
            BorderThickness = new Thickness(2f)
        };

        _finalPlate = new PanelContainer
        {
            PanelOverride = _finalStyle,
            MinSize = FinalSize,
            SetSize = FinalSize
        };

        var finalBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 0,
            HorizontalAlignment = UIControl.HAlignment.Center,
            VerticalAlignment = UIControl.VAlignment.Center
        };

        _finalIcon = new TextureRect
        {
            MinSize = new Vector2(54f, 54f),
            SetSize = new Vector2(54f, 54f),
            HorizontalAlignment = UIControl.HAlignment.Center,
            Stretch = TextureRect.StretchMode.KeepAspectCentered
        };

        _finalLabel = new Label
        {
            Text = string.Empty,
            Align = Label.AlignMode.Center,
            HorizontalAlignment = UIControl.HAlignment.Center,
            FontColorOverride = HeaderTextColor
        };

        _finalButton = new Button
        {
            Text = string.Empty,
            HorizontalExpand = true,
            VerticalExpand = true,
            Modulate = Color.Transparent,
            StyleBoxOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.Transparent,
                BorderColor = Color.Transparent,
                BorderThickness = new Thickness(0f)
            }
        };
        _finalButton.OnPressed += _ => UpgradeExPressed?.Invoke();

        finalBox.AddChild(_finalIcon);
        finalBox.AddChild(_finalLabel);
        _finalPlate.AddChild(finalBox);
        _finalPlate.AddChild(_finalButton);
        AddChild(_finalPlate);

        for (var col = 0; col < 3; col++)
        {
            var headerStyle = new StyleBoxFlat
            {
                BackgroundColor = MixColor(Color.FromHex("#131A24"), _accentColor, 0.14f),
                BorderColor = _accentColor.WithAlpha(0.62f),
                BorderThickness = new Thickness(1f)
            };

            var headerPlate = new PanelContainer
            {
                PanelOverride = headerStyle,
                MinSize = PathHeaderSize,
                SetSize = PathHeaderSize,
                RectClipContent = true
            };

            var header = new Label
            {
                MinSize = PathHeaderSize,
                SetSize = PathHeaderSize,
                HorizontalExpand = true,
                VerticalExpand = true,
                Align = Label.AlignMode.Center,
                HorizontalAlignment = UIControl.HAlignment.Center,
                VerticalAlignment = UIControl.VAlignment.Center,
                FontColorOverride = HeaderTextColor,
                ClipText = true,
                StyleClasses = { "LabelBig" }
            };

            _pathHeaderStyles[col] = headerStyle;
            _pathHeaders[col] = header;
            _pathHeaderPlates[col] = headerPlate;
            headerPlate.AddChild(header);
            AddChild(headerPlate);

            for (var row = 0; row < 3; row++)
            {
                var style = new StyleBoxFlat
                {
                    BackgroundColor = CardMutedBackgroundColor,
                    BorderColor = CardLockedBorderColor,
                    BorderThickness = new Thickness(2f)
                };

                var node = new Button
                {
                    Disabled = false,
                    ClipText = true,
                    TextAlign = Label.AlignMode.Center,
                    Text = string.Empty,
                    MinSize = NodeSize,
                    SetSize = NodeSize,
                    StyleBoxOverride = style
                };

                var capturedCol = col;
                var capturedTier = row + 1;
                node.OnPressed += _ => UpgradeNodePressed?.Invoke(capturedCol, capturedTier);

                _nodeStyles[col, row] = style;
                _nodes[col, row] = node;
                AddChild(node);
            }
        }

        ApplyNodeCaptions();
        ApplyPathHeaderTexts();
        RefreshTooltips();
        RefreshNodeStyles();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
        RefreshNodeStyles();
        InvalidateArrange();
    }

    public void SetFocus(Texture? abilityIcon, string abilityTitle, Color accent)
    {
        _accentColor = accent;
        _focusTitle = abilityTitle;
        _rootIcon.Texture = abilityIcon;
        _finalIcon.Texture = abilityIcon;
        RefreshTooltips();
        RefreshNodeStyles();
    }

    public void ClearFocus()
    {
        _accentColor = DefaultAccentColor;
        _focusTitle = null;
        _rootIcon.Texture = null;
        _finalIcon.Texture = null;
        RefreshTooltips();
        RefreshNodeStyles();
    }

    public void SetPathLabels(
        string powerKey,
        string cooldownKey,
        string utilityKey,
        string? powerTooltipKey = null,
        string? cooldownTooltipKey = null,
        string? utilityTooltipKey = null)
    {
        _pathHeaderKeys[0] = powerKey;
        _pathHeaderKeys[1] = cooldownKey;
        _pathHeaderKeys[2] = utilityKey;

        _pathTooltipKeys[0] = powerTooltipKey ?? powerKey;
        _pathTooltipKeys[1] = cooldownTooltipKey ?? cooldownKey;
        _pathTooltipKeys[2] = utilityTooltipKey ?? utilityKey;

        ApplyPathHeaderTexts();
        RefreshTooltips();
        RefreshNodeStyles();
    }

    public void RelocalizeControl()
    {
        ApplyNodeCaptions();
        ApplyPathHeaderTexts();
        RefreshTooltips();
    }

    public void SetUpgradeProgress(byte powerTier, byte cooldownTier, byte utilityTier, bool exUnlocked)
    {
        _tiers[0] = Math.Clamp(powerTier, (byte) 0, (byte) 3);
        _tiers[1] = Math.Clamp(cooldownTier, (byte) 0, (byte) 3);
        _tiers[2] = Math.Clamp(utilityTier, (byte) 0, (byte) 3);
        _exUnlocked = exUnlocked;
        RefreshNodeStyles();
    }

    public void SetInteractionEnabled(bool enabled)
    {
        _interactionEnabled = enabled;
        _finalButton.Disabled = !enabled;

        for (var col = 0; col < 3; col++)
        {
            for (var row = 0; row < 3; row++)
            {
                _nodes[col, row].Disabled = !enabled;
            }
        }

        RefreshNodeStyles();
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var safeWidth = MathF.Max(1f, finalSize.X);
        var safeHeight = MathF.Max(1f, finalSize.Y);

        var rootCenter = new Vector2(safeWidth * 0.5f, 60f);
        SetPosition(_rootPlate, Snap(rootCenter - RootSize * 0.5f));

        var rowOneY = MathF.Max(212f, safeHeight * 0.355f);
        var rowStep = MathF.Max(84f, safeHeight * 0.15f);
        var rowTwoY = rowOneY + rowStep;
        var rowThreeY = rowTwoY + rowStep;
        var finalY = MathF.Min(safeHeight - FinalSize.Y * 0.5f - 18f, rowThreeY + 96f);

        var colX = new[]
        {
            safeWidth * 0.22f,
            safeWidth * 0.50f,
            safeWidth * 0.78f,
        };

        for (var col = 0; col < 3; col++)
        {
            var headerY = rowOneY - NodeSize.Y * 0.5f - PathHeaderSize.Y - 14f;
            SetPosition(_pathHeaderPlates[col], Snap(new Vector2(colX[col] - PathHeaderSize.X * 0.5f, headerY)));
        }

        SetNodePosition(0, 0, colX[0], rowOneY);
        SetNodePosition(0, 1, colX[0], rowTwoY);
        SetNodePosition(0, 2, colX[0], rowThreeY);
        SetNodePosition(1, 0, colX[1], rowOneY);
        SetNodePosition(1, 1, colX[1], rowTwoY);
        SetNodePosition(1, 2, colX[1], rowThreeY);
        SetNodePosition(2, 0, colX[2], rowOneY);
        SetNodePosition(2, 1, colX[2], rowTwoY);
        SetNodePosition(2, 2, colX[2], rowThreeY);

        var finalCenter = new Vector2(safeWidth * 0.5f, finalY);
        SetPosition(_finalPlate, Snap(finalCenter - FinalSize * 0.5f));

        return base.ArrangeOverride(finalSize);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        DrawBackground(handle);
        DrawDesignFrame(handle);
        DrawBranchConnectors(handle);
    }

    private void DrawBackground(DrawingHandleScreen handle)
    {
        var canvasBackground = MixColor(CanvasBackgroundColor, _accentColor, 0.06f);
        var dynamicGridColor = MixColor(GridColor.WithAlpha(1f), _accentColor, 0.22f).WithAlpha(0.09f);
        handle.DrawRect(PixelSizeBox, canvasBackground);

        for (var x = 0f; x <= PixelWidth; x += GridStep)
        {
            handle.DrawLine(new Vector2(x, 0f), new Vector2(x, PixelHeight), dynamicGridColor);
        }

        for (var y = 0f; y <= PixelHeight; y += GridStep)
        {
            handle.DrawLine(new Vector2(0f, y), new Vector2(PixelWidth, y), dynamicGridColor);
        }

        var center = new Vector2(PixelWidth * 0.5f, PixelHeight * 0.5f);
        var pulse = (MathF.Sin(_time * 1.35f) + 1f) * 0.5f;
        var baseRadius = MathF.Min(PixelWidth, PixelHeight);
        handle.DrawCircle(center, baseRadius * 0.14f, MixColor(_accentColor, Color.FromHex("#4A8D79"), 0.5f).WithAlpha(0.04f + pulse * 0.03f));
        handle.DrawCircle(center, baseRadius * 0.22f, RootConnectorColor.WithAlpha(0.035f));
        handle.DrawCircle(center, baseRadius * 0.30f, _accentColor.WithAlpha(0.03f));
    }

    private void DrawDesignFrame(DrawingHandleScreen handle)
    {
        var designFrameColor = MixColor(DesignFrameColor.WithAlpha(1f), _accentColor, 0.18f).WithAlpha(0.38f);
        var canvasBorderColor = MixColor(CanvasBorderColor, _accentColor, 0.16f);
        var frame = new UIBox2(10f, 10f, PixelWidth - 10f, PixelHeight - 10f);
        handle.DrawRect(frame, designFrameColor, filled: false);
        handle.DrawRect(PixelSizeBox, canvasBorderColor, filled: false);
    }

    private void DrawBranchConnectors(DrawingHandleScreen handle)
    {
        var rootAnchor = GetAnchorBottom(_rootPlate);
        var finalAnchor = GetAnchorTop(_finalPlate);
        var topHub = Snap(new Vector2(rootAnchor.X, rootAnchor.Y + 46f));
        var bottomHub = Snap(new Vector2(finalAnchor.X, finalAnchor.Y - 46f));

        DrawSegment(handle, rootAnchor, topHub, RootConnectorColor.WithAlpha(0.88f));
        DrawSegment(handle, bottomHub, finalAnchor, RootConnectorColor.WithAlpha(0.88f));
        DrawCircleHub(handle, topHub);
        DrawCircleHub(handle, bottomHub);

        for (var col = 0; col < 3; col++)
        {
            var first = GetAnchorTop(_nodes[col, 0]);
            var second = GetAnchorTop(_nodes[col, 1]);
            var secondFrom = GetAnchorBottom(_nodes[col, 0]);
            var third = GetAnchorTop(_nodes[col, 2]);
            var thirdFrom = GetAnchorBottom(_nodes[col, 1]);
            var finalFrom = GetAnchorBottom(_nodes[col, 2]);
            var pathColor = _accentColor.WithAlpha(0.72f + (col == 1 ? 0.08f : 0f));

            DrawConnector(handle, topHub, first, RootConnectorColor.WithAlpha(0.72f));
            DrawSegment(handle, secondFrom, second, pathColor);
            DrawSegment(handle, thirdFrom, third, pathColor);
            DrawConnector(handle, finalFrom, bottomHub, pathColor.WithAlpha(0.78f));
        }
    }

    private void RefreshNodeStyles()
    {
        _rootStyle.BackgroundColor = MixColor(RootBackgroundColor, _accentColor, 0.10f + (MathF.Sin(_time * 1.15f) + 1f) * 0.02f);
        _rootStyle.BorderColor = _accentColor.WithAlpha(0.86f);
        _finalStyle.BackgroundColor = !_interactionEnabled
            ? MixColor(FinalBackgroundColor, Color.FromHex("#1A1F27"), 0.35f)
            : MixColor(FinalBackgroundColor, _accentColor, 0.14f);
        _finalStyle.BorderColor = _exUnlocked
            ? ExReadyBorderColor.WithAlpha(!_interactionEnabled ? 0.45f : _finalButton.IsHovered ? 1f : 0.96f)
            : _accentColor.WithAlpha(!_interactionEnabled ? 0.32f : _finalButton.IsHovered ? 0.96f : 0.72f);

        for (var col = 0; col < 3; col++)
        {
            _pathHeaderStyles[col].BackgroundColor = MixColor(Color.FromHex("#131A24"), _accentColor, 0.14f + (col == 1 ? 0.04f : 0f));
            _pathHeaderStyles[col].BorderColor = _accentColor.WithAlpha(0.54f + (col == 1 ? 0.08f : 0f));
            _pathHeaders[col].FontColorOverride = MixColor(HeaderTextColor, _accentColor, 0.18f + (col == 1 ? 0.04f : 0f));

            for (var row = 0; row < 3; row++)
            {
                var unlocked = row < _tiers[col];
                var hovered = _nodes[col, row].IsHovered;
                var fill = !_interactionEnabled
                    ? MixColor(CardMutedBackgroundColor, Color.FromHex("#1A1F27"), unlocked ? 0.18f : 0.32f)
                    : unlocked
                        ? MixColor(CardBackgroundColor, _accentColor, hovered ? 0.42f : 0.28f)
                        : hovered
                            ? MixColor(CardMutedBackgroundColor, Color.White, 0.05f)
                            : CardMutedBackgroundColor;
                var border = !_interactionEnabled
                    ? CardLockedBorderColor.WithAlpha(0.55f)
                    : unlocked
                        ? _accentColor.WithAlpha(hovered ? 0.98f : 0.82f)
                        : CardLockedBorderColor;

                _nodeStyles[col, row].BackgroundColor = fill;
                _nodeStyles[col, row].BorderColor = border;
            }
        }
    }

    private void SetNodePosition(int col, int row, float centerX, float centerY)
    {
        SetPosition(_nodes[col, row], Snap(new Vector2(centerX - NodeSize.X * 0.5f, centerY - NodeSize.Y * 0.5f)));
    }

    private void ApplyNodeCaptions()
    {
        _finalLabel.Text = Loc.GetString("w40k-ch-upgrade-node-ex-label", ("cost", ExNodeCost));

        for (var col = 0; col < 3; col++)
        {
            for (var row = 0; row < 3; row++)
            {
                _nodes[col, row].Text = Loc.GetString(
                    "w40k-ch-upgrade-node-tier-label",
                    ("tier", _tierLabels[row]),
                    ("cost", TierNodeCost));
            }
        }
    }

    private void ApplyPathHeaderTexts()
    {
        for (var col = 0; col < 3; col++)
        {
            _pathHeaders[col].Text = Loc.GetString(_pathHeaderKeys[col]);
            _pathHeaderPlates[col].ToolTip = Loc.GetString(_pathTooltipKeys[col]);
        }
    }

    private void RefreshTooltips()
    {
        _rootPlate.ToolTip = _focusTitle;
        _finalPlate.ToolTip = _focusTitle == null
            ? null
            : Loc.GetString(
                "w40k-ch-upgrade-final-tooltip",
                ("gift", _focusTitle));

        for (var col = 0; col < 3; col++)
        {
            var pathTitle = Loc.GetString(_pathTooltipKeys[col]);
            for (var row = 0; row < 3; row++)
            {
                _nodes[col, row].ToolTip = _focusTitle == null
                    ? null
                    : Loc.GetString(
                        "w40k-ch-upgrade-node-tier-tooltip",
                        ("path", pathTitle),
                        ("tier", _tierLabels[row]));
            }
        }
    }

    private void DrawCircleHub(DrawingHandleScreen handle, Vector2 position)
    {
        handle.DrawCircle(position, 6f, _accentColor.WithAlpha(0.15f));
        handle.DrawCircle(position, 3f, _accentColor.WithAlpha(0.88f));
    }

    private static void DrawConnector(DrawingHandleScreen handle, Vector2 from, Vector2 to, Color color)
    {
        var midY = from.Y + (to.Y - from.Y) * 0.5f;
        var p0 = Snap(from);
        var p1 = Snap(new Vector2(from.X, midY));
        var p2 = Snap(new Vector2(to.X, midY));
        var p3 = Snap(to);
        DrawSegment(handle, p0, p1, color);
        DrawSegment(handle, p1, p2, color);
        DrawSegment(handle, p2, p3, color);
    }

    private static void DrawSegment(DrawingHandleScreen handle, Vector2 from, Vector2 to, Color color)
    {
        if (from == to)
            return;

        handle.DrawLine(from, to, color);
    }

    private static Color MixColor(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return new Color(
            a.R + (b.R - a.R) * amount,
            a.G + (b.G - a.G) * amount,
            a.B + (b.B - a.B) * amount,
            1f);
    }

    private static Vector2 GetAnchorTop(UIControl control)
    {
        var position = control.PixelPosition;
        var size = control.PixelSize;
        return new Vector2(position.X + size.X * 0.5f, position.Y);
    }

    private static Vector2 GetAnchorBottom(UIControl control)
    {
        var position = control.PixelPosition;
        var size = control.PixelSize;
        return new Vector2(position.X + size.X * 0.5f, position.Y + size.Y);
    }

    private static Vector2 Snap(Vector2 value)
    {
        return new Vector2(MathF.Round(value.X), MathF.Round(value.Y));
    }
}
