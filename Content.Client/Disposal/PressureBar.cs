using System;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client.Disposal;

public sealed class PressureBar : ProgressBar
{
    private static readonly Color LowPressureColor = Color.FromHex("#8B3030".AsSpan());
    private static readonly Color MidPressureColor = Color.FromHex("#A57C35".AsSpan());
    private static readonly Color HighPressureColor = Color.FromHex("#C9A94C".AsSpan());
    private static readonly Color BackgroundColor = Color.FromHex("#0A0A0E".AsSpan());
    private static readonly Color BorderColor = Color.FromHex("#2A2418".AsSpan());
    private static readonly Color BorderStrongColor = Color.FromHex("#3D3422".AsSpan());

    private const float LeftSideSize = 0.5f;
    private const float RightSideSize = 0.5f;

    public float CurrentPressure { get; private set; }

    public bool UpdatePressure(TimeSpan fullPressureTime, float pressurePerSecond)
    {
        var currentTime = IoCManager.Resolve<IGameTiming>().CurTime;
        var pressure = (float)Math.Min(1.0f, 1.0f - (fullPressureTime.TotalSeconds - currentTime.TotalSeconds) * pressurePerSecond);
        UpdatePressureBar(pressure);
        return pressure >= 1.0f;
    }

    private void UpdatePressureBar(float pressure)
    {
        CurrentPressure = Math.Clamp(pressure, MinValue, MaxValue);
        Value = CurrentPressure;

        var normalized = MaxValue <= 0f
            ? 0f
            : Math.Clamp(CurrentPressure / MaxValue, 0f, 1f);

        Color fillColor;
        if (normalized <= LeftSideSize)
        {
            fillColor = Blend(LowPressureColor, MidPressureColor, normalized / LeftSideSize);
        }
        else
        {
            fillColor = Blend(MidPressureColor, HighPressureColor, (normalized - LeftSideSize) / RightSideSize);
        }

        BackgroundStyleBoxOverride ??= new StyleBoxFlat
        {
            BackgroundColor = BackgroundColor,
            BorderColor = BorderStrongColor,
            BorderThickness = new Thickness(1),
        };

        ForegroundStyleBoxOverride ??= new StyleBoxFlat();

        var foregroundStyle = (StyleBoxFlat)ForegroundStyleBoxOverride;
        foregroundStyle.BackgroundColor = fillColor;
        foregroundStyle.BorderColor = BorderColor;
        foregroundStyle.BorderThickness = new Thickness(1);
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return new Color(
            from.R + (to.R - from.R) * amount,
            from.G + (to.G - from.G) * amount,
            from.B + (to.B - from.B) * amount,
            from.A + (to.A - from.A) * amount);
    }
}
