using System.Numerics;
using Content.Shared.EnergyDome;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.EnergyDome.UI.Controls;

/// <summary>
/// Small linked-cell network view around the current generator.
/// </summary>
public sealed class EnergyDomeLinkedCellMapControl : Control
{
    private static readonly Color Background = Color.FromHex("#101722");
    private static readonly Color Border = Color.FromHex("#485269");
    private static readonly Color LinkColor = Color.FromHex("#5e7fb2");
    private static readonly Color SelfNodeColor = Color.FromHex("#f2ca69");
    private static readonly Color ActiveNodeColor = Color.FromHex("#62c4a7");
    private static readonly Color InactiveNodeColor = Color.FromHex("#7a7f8c");

    private EnergyDomeUiLinkedNode[] _nodes = Array.Empty<EnergyDomeUiLinkedNode>();

    public EnergyDomeLinkedCellMapControl()
    {
        MinSize = new Vector2(220f, 200f);
    }

    public void ApplyNodes(EnergyDomeUiLinkedNode[] nodes)
    {
        _nodes = nodes ?? Array.Empty<EnergyDomeUiLinkedNode>();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, Background);
        handle.DrawRect(PixelSizeBox, Border, false);

        var center = PixelSize / 2f;
        var mapRadius = MathF.Max(18f, MathF.Min(PixelSize.X, PixelSize.Y) * 0.40f);
        handle.DrawCircle(center, mapRadius, Border.WithAlpha(0.35f), false);
        handle.DrawLine(new Vector2(center.X - mapRadius, center.Y), new Vector2(center.X + mapRadius, center.Y), Border.WithAlpha(0.30f));
        handle.DrawLine(new Vector2(center.X, center.Y - mapRadius), new Vector2(center.X, center.Y + mapRadius), Border.WithAlpha(0.30f));

        foreach (var node in _nodes)
        {
            var offset = new Vector2(node.RelativeX, -node.RelativeY) * (mapRadius * 0.86f);
            var clampedOffset = offset;
            var len = clampedOffset.Length();
            if (len > mapRadius * 0.90f && len > 0.001f)
                clampedOffset *= (mapRadius * 0.90f) / len;

            var pos = center + clampedOffset;
            if (!node.IsSelf)
                handle.DrawLine(center, pos, LinkColor.WithAlpha(0.25f + node.ChargeFraction * 0.50f));

            var radius = node.IsSelf ? 6.5f : 4.8f;
            var baseColor = node.IsSelf
                ? SelfNodeColor
                : node.Active
                    ? ActiveNodeColor
                    : InactiveNodeColor;
            var color = baseColor.WithAlpha(0.35f + Math.Clamp(node.ChargeFraction, 0f, 1f) * 0.65f);

            handle.DrawCircle(pos, radius, color, true);
            handle.DrawCircle(pos, radius, Border.WithAlpha(0.8f), false);
        }
    }
}
