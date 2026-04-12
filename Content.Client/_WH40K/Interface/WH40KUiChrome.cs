using System;
using System.Collections.Generic;
using Robust.Client.Animations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Animations;

namespace Content.Client._WH40K.Interface;

public static class WH40KUiChrome
{
    public const string Cog = "⚙";
    public const string Diamond = "◆";
    public const string DiamondOutline = "◇";
    public const string DiamondNested = "◈";
    public const string Eye = "◉";
    public const string Blade = "⚔";
    public const string Arrow = "▸";
    public const string Skull = "☠";

    private sealed class LoopState
    {
        public bool HandlerAttached;
        public Dictionary<string, Animation> Animations { get; } = new();
    }

    private static readonly Dictionary<Control, LoopState> LoopStates = new();
    private static readonly object LoopStatesLock = new();

    public static string DecorateTitle(string glyph, string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? glyph
            : $"{glyph} {text}";
    }

    public static string DecorateIfMissing(string? text, string glyph)
    {
        if (string.IsNullOrWhiteSpace(text))
            return glyph;

        var trimmed = text.TrimStart();
        return trimmed.StartsWith(glyph, StringComparison.Ordinal)
            ? text
            : $"{glyph} {text}";
    }

    public static void StartLoopingPulse(Control control, string key, Color dim, Color bright, float seconds = 3f)
    {
        var animation = CreatePulseAnimation(dim, bright, seconds);
        var shouldPlay = false;

        lock (LoopStatesLock)
        {
            if (!LoopStates.TryGetValue(control, out var state))
            {
                state = new LoopState();
                LoopStates[control] = state;
            }

            if (!state.HandlerAttached)
            {
                control.AnimationCompleted += completedKey =>
                {
                    Animation? completedAnimation = null;
                    lock (LoopStatesLock)
                    {
                        if (LoopStates.TryGetValue(control, out var loopState) &&
                            loopState.Animations.TryGetValue(completedKey, out var storedAnimation))
                        {
                            completedAnimation = storedAnimation;
                        }
                    }

                    if (completedAnimation == null || control.HasRunningAnimation(completedKey))
                        return;

                    control.PlayAnimation(completedAnimation, completedKey);
                };
                state.HandlerAttached = true;
            }

            state.Animations[key] = animation;
            shouldPlay = true;
        }

        if (!shouldPlay)
            return;

        if (control.HasRunningAnimation(key))
            control.StopAnimation(key);

        control.PlayAnimation(animation, key);
    }

    public static void PlayHoverFlash(Control control, string key, Color? from = null, float seconds = 0.15f)
    {
        if (control.HasRunningAnimation(key))
            control.StopAnimation(key);

        control.Modulate = Color.White;
        control.PlayAnimation(CreateFlashAnimation(from ?? new Color(0.75f, 0.75f, 0.8f, 1f), Color.White, seconds), key);
    }

    public static void PlayFadeIn(Control control, string key, float duration = 0.25f, float delay = 0f)
    {
        if (control.HasRunningAnimation(key))
            control.StopAnimation(key);

        control.Modulate = new Color(1f, 1f, 1f, 0f);
        control.PlayAnimation(CreateFadeAnimation(duration, delay), key);
    }

    public static void PlayFadeOut(Control control, string key, Color accent, float duration = 0.18f, float delay = 0f)
    {
        if (control.HasRunningAnimation(key))
            control.StopAnimation(key);

        control.Modulate = Color.White;
        control.PlayAnimation(CreateFadeOutAnimation(accent, duration, delay), key);
    }

    public static Animation CreatePulseAnimation(Color dim, Color bright, float seconds = 3f)
    {
        var halfDuration = MathF.Max(0.05f, seconds / 2f);
        var fullDuration = MathF.Max(seconds, 0.1f);
        var softenedBright = Blend(dim, bright, 0.32f);
        return new Animation
        {
            Length = TimeSpan.FromSeconds(fullDuration),
            AnimationTracks =
            {
                new AnimationTrackControlProperty
                {
                    Property = nameof(Control.Modulate),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(dim, 0f),
                        new AnimationTrackProperty.KeyFrame(softenedBright, halfDuration, Easings.InOutSine),
                        new AnimationTrackProperty.KeyFrame(dim, fullDuration, Easings.InOutSine),
                    }
                }
            }
        };
    }

    public static Animation CreateFlashAnimation(Color from, Color to, float seconds = 0.15f)
    {
        return new Animation
        {
            Length = TimeSpan.FromSeconds(MathF.Max(seconds, 0.05f)),
            AnimationTracks =
            {
                new AnimationTrackControlProperty
                {
                    Property = nameof(Control.Modulate),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(from, 0f),
                        new AnimationTrackProperty.KeyFrame(to, seconds, Easings.OutQuad),
                    }
                }
            }
        };
    }

    public static Animation CreateFadeAnimation(float duration = 0.25f, float delay = 0f)
    {
        duration = MathF.Max(duration, 0.05f);
        delay = MathF.Max(delay, 0f);

        return new Animation
        {
            Length = TimeSpan.FromSeconds(delay + duration),
            AnimationTracks =
            {
                new AnimationTrackControlProperty
                {
                    Property = nameof(Control.Modulate),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Color(1f, 1f, 1f, 0f), delay),
                        new AnimationTrackProperty.KeyFrame(Color.White, delay + duration, Easings.OutCubic),
                    }
                }
            }
        };
    }

    public static Animation CreateFadeOutAnimation(Color accent, float duration = 0.18f, float delay = 0f)
    {
        duration = MathF.Max(duration, 0.05f);
        delay = MathF.Max(delay, 0f);
        var flashTime = delay + MathF.Min(0.06f, duration * 0.35f);
        var accentFlash = new Color(
            MathF.Min(1f, accent.R + 0.18f),
            MathF.Min(1f, accent.G + 0.18f),
            MathF.Min(1f, accent.B + 0.18f),
            MathF.Max(accent.A, 0.92f));

        return new Animation
        {
            Length = TimeSpan.FromSeconds(delay + duration),
            AnimationTracks =
            {
                new AnimationTrackControlProperty
                {
                    Property = nameof(Control.Modulate),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Color.White, delay),
                        new AnimationTrackProperty.KeyFrame(accentFlash, flashTime, Easings.OutQuad),
                        new AnimationTrackProperty.KeyFrame(new Color(accentFlash.R, accentFlash.G, accentFlash.B, 0f), delay + duration, Easings.InCubic),
                    }
                }
            }
        };
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
