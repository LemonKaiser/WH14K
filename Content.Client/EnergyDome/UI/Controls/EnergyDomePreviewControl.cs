using System.Numerics;
using Content.Shared.EnergyDome;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.EnergyDome.UI.Controls;

/// <summary>
/// Renders compact shield-shape preview with impact compass and sector integrity bars.
/// </summary>
public sealed class EnergyDomePreviewControl : Control
{
    private enum WallSide : byte
    {
        Left,
        Right,
        Top,
        Bottom
    }

    private static readonly Color Background = Color.FromHex("#0f141d");
    private static readonly Color Border = Color.FromHex("#465067");
    private static readonly Color DisabledColor = Color.FromHex("#5a6273");
    private static readonly Color OverloadColor = Color.FromHex("#d5603b");
    private static readonly Color IntegrityGood = Color.FromHex("#4fc27d");
    private static readonly Color IntegrityBad = Color.FromHex("#bf4141");
    private static readonly Color ImpactColor = Color.FromHex("#f59b58");

    private bool _enabled;
    private float _charge;
    private float _overload;
    private EnergyDomeOperationMode _mode = EnergyDomeOperationMode.Bubble;
    private EnergyDomeSizePreset _size = EnergyDomeSizePreset.Small;
    private EnergyDomeColorPreset _color = EnergyDomeColorPreset.Blue;
    private float[] _incomingCompass = Array.Empty<float>();
    private float[] _sectorIntegrity = Array.Empty<float>();

    public EnergyDomePreviewControl()
    {
        MinSize = new Vector2(260f, 240f);
    }

    public void ApplyState(EnergyDomeBuiState state)
    {
        _enabled = state.Enabled;
        _charge = Math.Clamp(state.ChargeFraction, 0f, 1f);
        _overload = Math.Clamp(state.OverloadFraction, 0f, 1f);
        _mode = state.Mode;
        _size = state.Size;
        _color = state.Color;
        _incomingCompass = state.IncomingCompass ?? Array.Empty<float>();
        _sectorIntegrity = state.SectorIntegrity ?? Array.Empty<float>();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, Background);
        handle.DrawRect(PixelSizeBox, Border, false);

        var center = PixelSize / 2f;
        center.Y -= 8f;
        var baseRadius = MathF.Min(PixelSize.X, PixelSize.Y) * 0.24f;
        var sizeScale = _size switch
        {
            EnergyDomeSizePreset.Small => 0.95f,
            EnergyDomeSizePreset.Medium => 1.15f,
            EnergyDomeSizePreset.Huge => 2.13f,
            _ => 0.95f
        };
        var radius = MathF.Max(24f, baseRadius * sizeScale);
        var color = GetShieldColor();
        var alpha = _enabled ? 0.90f : 0.30f;
        var fillColor = color.WithAlpha(alpha * (0.45f + _charge * 0.35f));
        var edgeColor = color.WithAlpha(alpha);

        DrawShieldShape(handle, center, radius, fillColor, edgeColor);
        DrawOverloadHalo(handle, center, radius, _overload);
        DrawImpactCompass(handle, center, radius);
        DrawSectorIntegrity(handle, center, radius + 30f);
    }

    private void DrawShieldShape(
        DrawingHandleScreen handle,
        Vector2 center,
        float radius,
        Color fillColor,
        Color edgeColor)
    {
        switch (_mode)
        {
            case EnergyDomeOperationMode.Wall:
            {
                var half = GetWallHalfExtents(radius);
                var box = new UIBox2(center - half, center + half);
                handle.DrawRect(box, fillColor);
                handle.DrawRect(box, edgeColor, false);
                break;
            }
            default:
                handle.DrawCircle(center, radius, fillColor, true);
                handle.DrawCircle(center, radius, edgeColor, false);
                break;
        }
    }

    private void DrawOverloadHalo(DrawingHandleScreen handle, Vector2 center, float radius, float overloadFraction)
    {
        if (overloadFraction <= 0.001f)
            return;

        var alpha = 0.2f + overloadFraction * 0.35f;
        var color = OverloadColor.WithAlpha(alpha);

        if (_mode == EnergyDomeOperationMode.Wall)
        {
            var half = GetWallHalfExtents(radius);
            var padding = new Vector2(4f + overloadFraction * 7f, 4f + overloadFraction * 5f);
            var box = new UIBox2(center - half - padding, center + half + padding);
            handle.DrawRect(box, color, false);
            return;
        }

        var haloRadius = radius + 5f + overloadFraction * 10f;
        handle.DrawCircle(center, haloRadius, color, false);
    }

    private void DrawImpactCompass(DrawingHandleScreen handle, Vector2 center, float radius)
    {
        if (_mode == EnergyDomeOperationMode.Wall)
        {
            DrawWallImpactCompass(handle, center, radius);
            return;
        }

        DrawBubbleImpactCompass(handle, center, radius);
    }

    private void DrawBubbleImpactCompass(DrawingHandleScreen handle, Vector2 center, float radius)
    {
        var bins = _incomingCompass.Length;
        if (bins == 0)
            return;

        var tau = MathF.PI * 2f;
        for (var i = 0; i < bins; i++)
        {
            var value = Math.Clamp(_incomingCompass[i], 0f, 1f);
            if (value <= 0.01f)
                continue;

            var angle = tau * (i / (float) bins) - MathF.PI / 2f;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var markerCenter = center + dir * (radius + 9f);
            var markerRadius = 4f + value * 4f;
            var color = ImpactColor.WithAlpha(0.45f + value * 0.45f);

            DrawArc(
                handle,
                markerCenter,
                markerRadius,
                angle - MathF.PI / 2f,
                angle + MathF.PI / 2f,
                color);
        }
    }

    private void DrawWallImpactCompass(DrawingHandleScreen handle, Vector2 center, float radius)
    {
        var bins = _incomingCompass.Length;
        if (bins == 0)
            return;

        var left = 0f;
        var right = 0f;
        var top = 0f;
        var bottom = 0f;
        var tau = MathF.PI * 2f;
        for (var i = 0; i < bins; i++)
        {
            var value = Math.Clamp(_incomingCompass[i], 0f, 1f);
            if (value <= 0.01f)
                continue;

            var angle = tau * (i / (float) bins) - MathF.PI / 2f;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            if (MathF.Abs(dir.X) >= MathF.Abs(dir.Y))
            {
                if (dir.X >= 0f)
                    right = MathF.Max(right, value);
                else
                    left = MathF.Max(left, value);
            }
            else if (dir.Y >= 0f)
            {
                bottom = MathF.Max(bottom, value);
            }
            else
            {
                top = MathF.Max(top, value);
            }
        }

        var half = GetWallHalfExtents(radius);
        DrawWallSideIndicator(handle, center, half, WallSide.Left, left);
        DrawWallSideIndicator(handle, center, half, WallSide.Right, right);
        DrawWallSideIndicator(handle, center, half, WallSide.Top, top);
        DrawWallSideIndicator(handle, center, half, WallSide.Bottom, bottom);
    }

    private void DrawWallSideIndicator(
        DrawingHandleScreen handle,
        Vector2 center,
        Vector2 halfExtents,
        WallSide side,
        float intensity)
    {
        if (intensity <= 0.01f)
            return;

        var offset = 8f + intensity * 7f;
        var color = ImpactColor.WithAlpha(0.45f + intensity * 0.45f);
        var verticalLength = halfExtents.Y * 2f;
        var horizontalLength = halfExtents.X * 2f;

        switch (side)
        {
            case WallSide.Left:
            {
                var x = center.X - halfExtents.X - offset;
                handle.DrawLine(
                    new Vector2(x, center.Y - verticalLength * 0.5f),
                    new Vector2(x, center.Y + verticalLength * 0.5f),
                    color);
                break;
            }
            case WallSide.Right:
            {
                var x = center.X + halfExtents.X + offset;
                handle.DrawLine(
                    new Vector2(x, center.Y - verticalLength * 0.5f),
                    new Vector2(x, center.Y + verticalLength * 0.5f),
                    color);
                break;
            }
            case WallSide.Top:
            {
                var y = center.Y - halfExtents.Y - offset;
                handle.DrawLine(
                    new Vector2(center.X - horizontalLength * 0.5f, y),
                    new Vector2(center.X + horizontalLength * 0.5f, y),
                    color);
                break;
            }
            case WallSide.Bottom:
            {
                var y = center.Y + halfExtents.Y + offset;
                handle.DrawLine(
                    new Vector2(center.X - horizontalLength * 0.5f, y),
                    new Vector2(center.X + horizontalLength * 0.5f, y),
                    color);
                break;
            }
        }
    }

    private static Vector2 GetWallHalfExtents(float radius)
    {
        var width = radius * 1.9f;
        var height = radius * 0.62f;
        return new Vector2(width, height) * 0.5f;
    }

    private static void DrawArc(
        DrawingHandleScreen handle,
        Vector2 center,
        float radius,
        float startAngle,
        float endAngle,
        Color color)
    {
        const int segments = 8;
        var previous = center + new Vector2(MathF.Cos(startAngle), MathF.Sin(startAngle)) * radius;

        for (var i = 1; i <= segments; i++)
        {
            var t = i / (float) segments;
            var angle = startAngle + (endAngle - startAngle) * t;
            var next = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            handle.DrawLine(previous, next, color);
            previous = next;
        }
    }

    private void DrawSectorIntegrity(DrawingHandleScreen handle, Vector2 center, float radius)
    {
        if (_sectorIntegrity.Length == 0)
            return;

        var barWidth = 34f;
        var barHeight = 5f;
        for (var i = 0; i < _sectorIntegrity.Length; i++)
        {
            var value = Math.Clamp(_sectorIntegrity[i], 0f, 1f);
            var start = center + new Vector2((i - (_sectorIntegrity.Length - 1) * 0.5f) * (barWidth + 5f), radius);
            var half = new Vector2(barWidth, barHeight) * 0.5f;
            var box = new UIBox2(start - half, start + half);
            var fill = new UIBox2(box.Left, box.Top, box.Left + box.Width * value, box.Bottom);
            var color = Color.InterpolateBetween(IntegrityBad, IntegrityGood, value);

            handle.DrawRect(box, Border.WithAlpha(0.45f));
            if (fill.Width > 0.01f)
                handle.DrawRect(fill, color.WithAlpha(0.9f));
            handle.DrawRect(box, Border.WithAlpha(0.65f), false);
        }
    }

    private Color GetShieldColor()
    {
        if (!_enabled)
            return DisabledColor;

        return _color == EnergyDomeColorPreset.Red
            ? Color.FromHex("#cf5d5d")
            : Color.FromHex("#5ab4de");
    }
}
