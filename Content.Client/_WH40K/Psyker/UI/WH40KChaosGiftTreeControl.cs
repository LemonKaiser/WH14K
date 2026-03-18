using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Content.Shared._WH40K.Psyker;

namespace Content.Client._WH40K.Psyker.UI;

/// <summary>
/// Visual upgrade-tree mock:
/// top source icon -> 3 columns x 3 tiers -> final EX node.
/// </summary>
public sealed class WH40KChaosGiftTreeControl : LayoutContainer
{
    private static readonly Color CanvasBackgroundColor = Color.FromHex("#141729");
    private static readonly Color CanvasBorderColor = Color.FromHex("#4A5371");
    private static readonly Color HeaderPlateBackgroundColor = Color.FromHex("#1B2035");
    private static readonly Color HeaderPlateBorderColor = Color.FromHex("#5E6D94");
    private static readonly Color RootBackgroundColor = Color.FromHex("#23283E");
    private static readonly Color RootBorderDefaultColor = Color.FromHex("#7381A6");
    private static readonly Color FinalBackgroundColor = Color.FromHex("#252B41");
    private static readonly Color FinalBorderDefaultColor = Color.FromHex("#8A94B7");
    private static readonly Color NodeBackgroundColor = Color.FromHex("#2D3347");
    private static readonly Color NodeBorderColor = Color.FromHex("#7C8CB5");
    private static readonly Color HeaderColor = Color.FromHex("#DCE6FF");
    private static readonly Color NodeUnlockedBackgroundColor = Color.FromHex("#2F4768");
    private static readonly Color NodeUnlockedBorderColor = Color.FromHex("#89B6EA");
    private static readonly Color ExReadyBorderColor = Color.FromHex("#D0A4FF");

    private static readonly Vector2 RootSize = new(86f, 86f);
    private static readonly Vector2 FinalSize = new(86f, 86f);
    private static readonly Vector2 NodeSize = new(88f, 64f);
    private static readonly Vector2 PathHeaderSize = new(112f, 30f);
    private const int TierNodeCost = 1;
    private const int ExNodeCost = 3;

    private readonly string[] _pathTooltipKeys =
    {
        "wh40k-chaos-branch-upgrade-path-power",
        "wh40k-chaos-branch-upgrade-path-cooldown",
        "wh40k-chaos-branch-upgrade-path-cast-time",
    };

    private readonly string[] _pathHeaderKeys =
    {
        "wh40k-chaos-branch-upgrade-path-power-short",
        "wh40k-chaos-branch-upgrade-path-cooldown-short",
        "wh40k-chaos-branch-upgrade-path-cast-time-short",
    };

    private readonly string[] _tierLabels = { "I", "II", "III" };

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
    private readonly Button[,] _nodes = new Button[3, 3];
    private readonly StyleBoxFlat[,] _nodeStyles = new StyleBoxFlat[3, 3];

    public event Action<int, int>? UpgradeNodePressed;
    public event Action? UpgradeExPressed;

    public WH40KChaosGiftTreeControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
        MinHeight = 430f;
        MinWidth = 560f;

        _rootStyle = new StyleBoxFlat
        {
            BackgroundColor = RootBackgroundColor,
            BorderColor = RootBorderDefaultColor,
            BorderThickness = new Thickness(2)
        };

        _rootPlate = new PanelContainer
        {
            PanelOverride = _rootStyle,
            MinSize = RootSize,
            SetSize = RootSize
        };

        _rootIcon = new TextureRect
        {
            MinSize = new Vector2(68f, 68f),
            SetSize = new Vector2(68f, 68f),
            HorizontalAlignment = Control.HAlignment.Center,
            VerticalAlignment = Control.VAlignment.Center,
            Stretch = TextureRect.StretchMode.KeepAspectCentered
        };
        _rootPlate.AddChild(_rootIcon);
        AddChild(_rootPlate);

        _finalStyle = new StyleBoxFlat
        {
            BackgroundColor = FinalBackgroundColor,
            BorderColor = FinalBorderDefaultColor,
            BorderThickness = new Thickness(2)
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
            HorizontalAlignment = Control.HAlignment.Center,
            VerticalAlignment = Control.VAlignment.Center
        };
        _finalPlate.AddChild(finalBox);
        _finalIcon = new TextureRect
        {
            MinSize = new Vector2(52f, 52f),
            SetSize = new Vector2(52f, 52f),
            HorizontalAlignment = Control.HAlignment.Center,
            Stretch = TextureRect.StretchMode.KeepAspectCentered
        };
        _finalLabel = new Label
        {
            Text = $"EX ({ExNodeCost})",
            HorizontalAlignment = Control.HAlignment.Center
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
                BorderThickness = new Thickness(0)
            }
        };
        _finalButton.OnPressed += _ => UpgradeExPressed?.Invoke();
        finalBox.AddChild(_finalIcon);
        finalBox.AddChild(_finalLabel);
        _finalPlate.AddChild(_finalButton);
        AddChild(_finalPlate);

        for (var col = 0; col < 3; col++)
        {
            var plateStyle = new StyleBoxFlat
            {
                BackgroundColor = HeaderPlateBackgroundColor,
                BorderColor = HeaderPlateBorderColor,
                BorderThickness = new Thickness(1)
            };

            var headerPlate = new PanelContainer
            {
                PanelOverride = plateStyle,
                MinSize = PathHeaderSize,
                SetSize = PathHeaderSize
            };

            var header = new Label
            {
                HorizontalAlignment = Control.HAlignment.Center,
                VerticalAlignment = Control.VAlignment.Center,
                FontColorOverride = HeaderColor,
                ClipText = true
            };

            _pathHeaders[col] = header;
            _pathHeaderPlates[col] = headerPlate;
            headerPlate.AddChild(header);
            AddChild(headerPlate);

            for (var row = 0; row < 3; row++)
            {
                var style = new StyleBoxFlat
                {
                    BackgroundColor = NodeBackgroundColor,
                    BorderColor = NodeBorderColor,
                    BorderThickness = new Thickness(2)
                };

                var node = new Button
                {
                    Disabled = false,
                    ClipText = true,
                    TextAlign = Label.AlignMode.Center,
                    Text = $"{_tierLabels[row]} ({TierNodeCost})",
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

        ApplyPathHeaderTexts();
    }

    public void SetFocus(Texture? abilityIcon, string abilityTitle, Color accent)
    {
        _rootIcon.Texture = abilityIcon;
        _finalIcon.Texture = abilityIcon;
        _rootStyle.BorderColor = accent;
        _finalStyle.BorderColor = accent;
        _rootPlate.ToolTip = abilityTitle;
        _finalPlate.ToolTip = Loc.GetString(
            "wh40k-chaos-branch-upgrade-final-tooltip",
            ("gift", abilityTitle),
            ("cost", ExNodeCost));

        for (var col = 0; col < 3; col++)
        {
            var pathTitle = Loc.GetString(_pathTooltipKeys[col]);
            for (var row = 0; row < 3; row++)
            {
                _nodes[col, row].ToolTip = Loc.GetString(
                    "wh40k-chaos-branch-upgrade-node-tier-tooltip",
                    ("path", pathTitle),
                    ("tier", _tierLabels[row]),
                    ("cost", TierNodeCost));
            }
        }
    }

    public void ClearFocus()
    {
        _rootIcon.Texture = null;
        _finalIcon.Texture = null;
        _rootStyle.BorderColor = RootBorderDefaultColor;
        _finalStyle.BorderColor = FinalBorderDefaultColor;
        _rootPlate.ToolTip = null;
        _finalPlate.ToolTip = null;

        for (var col = 0; col < 3; col++)
        {
            for (var row = 0; row < 3; row++)
            {
                _nodes[col, row].ToolTip = null;
                _nodeStyles[col, row].BackgroundColor = NodeBackgroundColor;
                _nodeStyles[col, row].BorderColor = NodeBorderColor;
            }
        }
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
    }

    public void SetUpgradeProgress(byte powerTier, byte cooldownTier, byte utilityTier, bool exUnlocked)
    {
        var tiers = new[] { (int) powerTier, (int) cooldownTier, (int) utilityTier };

        for (var col = 0; col < 3; col++)
        {
            for (var row = 0; row < 3; row++)
            {
                var unlocked = row < tiers[col];
                _nodeStyles[col, row].BackgroundColor = unlocked ? NodeUnlockedBackgroundColor : NodeBackgroundColor;
                _nodeStyles[col, row].BorderColor = unlocked ? NodeUnlockedBorderColor : NodeBorderColor;
            }
        }

        _finalStyle.BorderColor = exUnlocked ? ExReadyBorderColor : FinalBorderDefaultColor;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var safeWidth = MathF.Max(1f, finalSize.X);
        var safeHeight = MathF.Max(1f, finalSize.Y);

        var rootCenter = new Vector2(safeWidth * 0.5f, 50f);
        SetPosition(_rootPlate, Snap(rootCenter - RootSize * 0.5f));

        var rowOneY = MathF.Max(176f, safeHeight * 0.36f);
        var rowStep = MathF.Max(66f, safeHeight * 0.16f);
        var rowTwoY = rowOneY + rowStep;
        var rowThreeY = rowTwoY + rowStep;
        var finalY = MathF.Min(safeHeight - FinalSize.Y * 0.5f - 16f, rowThreeY + 86f);

        var colX = new[]
        {
            safeWidth * 0.24f,
            safeWidth * 0.50f,
            safeWidth * 0.76f,
        };

        for (var col = 0; col < 3; col++)
        {
            var headerY = rowOneY - NodeSize.Y * 0.5f - PathHeaderSize.Y - 8f;
            var headerPos = new Vector2(colX[col] - PathHeaderSize.X * 0.5f, headerY);
            SetPosition(_pathHeaderPlates[col], Snap(headerPos));
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
        handle.DrawRect(PixelSizeBox, CanvasBackgroundColor);
        handle.DrawRect(PixelSizeBox, CanvasBorderColor, false);

        var rootAnchor = GetAnchorBottom(_rootPlate);
        for (var col = 0; col < 3; col++)
        {
            DrawConnector(handle, rootAnchor, GetAnchorTop(_nodes[col, 0]));
            DrawConnector(handle, GetAnchorBottom(_nodes[col, 0]), GetAnchorTop(_nodes[col, 1]));
            DrawConnector(handle, GetAnchorBottom(_nodes[col, 1]), GetAnchorTop(_nodes[col, 2]));
            DrawConnector(handle, GetAnchorBottom(_nodes[col, 2]), GetAnchorTop(_finalPlate));
        }
    }

    private void SetNodePosition(int col, int row, float centerX, float centerY)
    {
        var node = _nodes[col, row];
        SetPosition(node, Snap(new Vector2(centerX - NodeSize.X * 0.5f, centerY - NodeSize.Y * 0.5f)));
    }

    private void ApplyPathHeaderTexts()
    {
        for (var col = 0; col < 3; col++)
        {
            _pathHeaders[col].Text = Loc.GetString(_pathHeaderKeys[col]);
            _pathHeaderPlates[col].ToolTip = Loc.GetString(_pathTooltipKeys[col]);
        }
    }

    private static void DrawConnector(DrawingHandleScreen handle, Vector2 from, Vector2 to)
    {
        var color = CanvasBorderColor.WithAlpha(0.82f);
        handle.DrawLine(Snap(from), Snap(to), color);
    }

    private static Vector2 GetAnchorTop(Control control)
    {
        var position = control.PixelPosition;
        var size = control.PixelSize;
        return new Vector2(position.X + size.X * 0.5f, position.Y);
    }

    private static Vector2 GetAnchorBottom(Control control)
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
