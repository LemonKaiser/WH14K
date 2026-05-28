using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.RichText;

public sealed partial class WH40KTitleEffectTag : IMarkupTagHandler
{
    private static readonly Color DefaultTitleColor = new(135, 206, 250);

    public string Name => "wh40ktitlefx";

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        if (!node.Value.TryGetString(out var text) || string.IsNullOrWhiteSpace(text))
        {
            control = null;
            return false;
        }

        var effectRaw = ReadString(node.Attributes, "effect", string.Empty);
        if (string.IsNullOrWhiteSpace(effectRaw))
            effectRaw = ReadString(node.Attributes, "titleeffect", string.Empty);

        var effect = ParseEffect(effectRaw);

        var palette = ParsePalette(node.Attributes);
        if (palette.Count == 0)
            palette.Add(DefaultTitleColor);

        var animated = ReadBool(node.Attributes, "animated", false);
        var durationMs = Math.Clamp(ReadInt(node.Attributes, "duration", 3500), 400, 60000);
        var phaseMs = ReadInt(node.Attributes, "phase", 0);

        var revealMs = ReadInt(node.Attributes, "reveal", ReadInt(node.Attributes, "titlereveal", 900));
        var holdMs = ReadInt(node.Attributes, "hold", ReadInt(node.Attributes, "titlehold", 10000));
        var dissolveMs = ReadInt(node.Attributes, "dissolve", ReadInt(node.Attributes, "titledissolve", 900));
        var cursor = ReadBool(node.Attributes, "cursor", true);
        var outlineEnabled = ReadBool(node.Attributes, "outline", false);
        var outlineWidth = Math.Clamp(ReadInt(node.Attributes, "outlinewidth", 1), 1, 3);
        var outlineAlphaPercent = Math.Clamp(ReadInt(node.Attributes, "outlinealpha", 70), 1, 100);
        var outlineColor = Color.White;
        if (node.Attributes.TryGetValue("outlinecolor", out var outlineColorParam) &&
            outlineColorParam.TryGetString(out var outlineRaw) &&
            TryResolveColor(outlineRaw, out var parsedOutline))
        {
            outlineColor = parsedOutline;
            outlineEnabled = true;
        }

        control = new WH40KTitleEffectControl(
            text,
            palette,
            animated,
            durationMs,
            phaseMs,
            effect,
            Math.Clamp(revealMs, 100, 120000),
            Math.Clamp(holdMs, 100, 120000),
            Math.Clamp(dissolveMs, 100, 120000),
            cursor,
            outlineEnabled,
            outlineColor,
            outlineWidth,
            outlineAlphaPercent);
        return true;
    }

    private static WH40KTitleFxMode ParseEffect(string source)
    {
        return source.Trim().ToLowerInvariant() switch
        {
            "binary" => WH40KTitleFxMode.Binary,
            "scan" => WH40KTitleFxMode.Scan,
            "fish" or "fish-swim" => WH40KTitleFxMode.Fish,
            "scramble-decode" or "scramble" => WH40KTitleFxMode.ScrambleDecode,
            "typewriter-cursor" or "typewriter" => WH40KTitleFxMode.TypewriterCursor,
            "wave" => WH40KTitleFxMode.Wave,
            "glitch-slice" or "glitch" => WH40KTitleFxMode.GlitchSlice,
            "noise-dissolve" or "dissolve-noise" or "noise" => WH40KTitleFxMode.NoiseDissolve,
            "scanline" => WH40KTitleFxMode.Scanline,
            "flip" or "discord-flip" => WH40KTitleFxMode.Flip,
            _ => WH40KTitleFxMode.None,
        };
    }

    private static List<Color> ParsePalette(IReadOnlyDictionary<string, MarkupParameter> attributes)
    {
        if (attributes.TryGetValue("palette", out var paletteParameter) &&
            paletteParameter.TryGetString(out var paletteRaw))
        {
            return ParsePaletteString(paletteRaw);
        }

        if (attributes.TryGetValue("color", out var colorParameter) &&
            colorParameter.TryGetString(out var colorRaw) &&
            TryResolveColor(colorRaw, out var color))
        {
            return new List<Color> { color };
        }

        return new List<Color>();
    }

    private static List<Color> ParsePaletteString(string paletteRaw)
    {
        var result = new List<Color>();
        var parts = paletteRaw.Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var token = part.Trim();
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (TryResolveColor(token, out var color))
                result.Add(color);
        }

        return result;
    }

    private static bool TryResolveColor(string source, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(source))
            return false;

        var trimmed = source.Trim();
        if (Color.TryFromHex(trimmed) is { } hex)
        {
            color = hex;
            return true;
        }

        if (Color.TryFromName(trimmed, out var named))
        {
            color = named;
            return true;
        }

        return false;
    }

    private static int ReadInt(IReadOnlyDictionary<string, MarkupParameter> attrs, string key, int fallback)
    {
        if (!attrs.TryGetValue(key, out var parameter))
            return fallback;

        if (parameter.TryGetLong(out var longValue) && longValue != null)
            return (int) longValue.Value;

        if (parameter.TryGetString(out var stringValue) && int.TryParse(stringValue, out var parsed))
            return parsed;

        return fallback;
    }

    private static string ReadString(IReadOnlyDictionary<string, MarkupParameter> attrs, string key, string fallback)
    {
        if (!attrs.TryGetValue(key, out var parameter))
            return fallback;

        return parameter.TryGetString(out var value)
            ? value
            : fallback;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, MarkupParameter> attrs, string key, bool fallback)
    {
        if (!attrs.TryGetValue(key, out var parameter))
            return fallback;

        if (parameter.TryGetLong(out var longValue) && longValue != null)
            return longValue.Value != 0;

        if (!parameter.TryGetString(out var stringValue))
            return fallback;

        return stringValue.Equals("1", StringComparison.Ordinal) ||
               stringValue.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               stringValue.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

internal enum WH40KTitleFxMode : byte
{
    None = 0,
    Binary = 1,
    Scan = 2,
    Fish = 3,
    ScrambleDecode = 4,
    TypewriterCursor = 5,
    Wave = 6,
    GlitchSlice = 7,
    NoiseDissolve = 8,
    Scanline = 9,
    Flip = 10,
}

internal sealed partial class WH40KTitleEffectControl : Control
{
    [Dependency] private  IGameTiming _timing = default!;

    private static readonly Rune[] NoiseRunes = "01ABCDEFGHIJKLMNOPQRSTUVWXYZ#$%&*+-?".EnumerateRunes().ToArray();
    private static readonly Rune FishRune = new(0x1F41F);

    private readonly List<Rune> _runes;
    private readonly List<Color> _palette;
    private readonly bool _animated;
    private readonly int _durationMs;
    private readonly int _phaseMs;
    private readonly WH40KTitleFxMode _mode;
    private readonly int _revealMs;
    private readonly int _holdMs;
    private readonly int _dissolveMs;
    private readonly bool _cursorEnabled;
    private readonly bool _outlineEnabled;
    private readonly Color _outlineColor;
    private readonly int _outlineWidth;
    private readonly int _outlineAlphaPercent;
    private readonly int _seed;

    public WH40KTitleEffectControl(
        string text,
        List<Color> palette,
        bool animated,
        int durationMs,
        int phaseMs,
        WH40KTitleFxMode mode,
        int revealMs,
        int holdMs,
        int dissolveMs,
        bool cursorEnabled,
        bool outlineEnabled,
        Color outlineColor,
        int outlineWidth,
        int outlineAlphaPercent)
    {
        IoCManager.InjectDependencies(this);

        _palette = palette;
        _animated = animated;
        _durationMs = durationMs;
        _phaseMs = phaseMs;
        _mode = mode;
        _revealMs = revealMs;
        _holdMs = holdMs;
        _dissolveMs = dissolveMs;
        _cursorEnabled = cursorEnabled;
        _outlineEnabled = outlineEnabled;
        _outlineColor = outlineColor;
        _outlineWidth = outlineWidth;
        _outlineAlphaPercent = outlineAlphaPercent;
        _seed = ComputeStableSeed(text);

        _runes = new List<Rune>();
        foreach (var rune in text.EnumerateRunes())
        {
            _runes.Add(rune);
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var font = ResolveFont();
        var width = 0f;
        foreach (var rune in _runes)
        {
            width += GetRuneAdvance(font, rune);
        }

        var height = font.GetLineHeight(UIScale);
        return new Vector2(width / UIScale, height / UIScale);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_runes.Count == 0)
            return;

        var font = ResolveFont();
        var baseline = SizeBox.TopLeft + new Vector2(0f, font.GetAscent(UIScale));
        var elapsedMs = _timing.RealTime.TotalMilliseconds + _phaseMs;
        var shift = ResolveGradientShift(elapsedMs);

        if (_mode == WH40KTitleFxMode.Fish)
        {
            DrawFish(handle, font, baseline, elapsedMs, shift);
            return;
        }

        var flipAngle = _mode == WH40KTitleFxMode.Flip
            ? ResolveFlipAngle(elapsedMs)
            : 0f;
        var divisor = Math.Max(1, _runes.Count - 1);

        for (var index = 0; index < _runes.Count; index++)
        {
            var sourceRune = _runes[index];
            var drawRune = sourceRune;
            var drawColor = SampleGradient(_palette, ((_runes.Count == 1 ? 0f : index / (float) divisor) + shift) % 1f);
            var alpha = 1f;
            var offset = Vector2.Zero;
            var glitchSplit = false;

            switch (_mode)
            {
                case WH40KTitleFxMode.Binary:
                    drawRune = ResolveBinaryRune(index, sourceRune, elapsedMs);
                    break;
                case WH40KTitleFxMode.Scan:
                    drawColor = ApplyScanColor(index, drawColor, elapsedMs);
                    break;
                case WH40KTitleFxMode.Fish:
                    break;
                case WH40KTitleFxMode.ScrambleDecode:
                    drawRune = ResolveScrambleDecodeRune(index, sourceRune, elapsedMs);
                    break;
                case WH40KTitleFxMode.TypewriterCursor:
                    drawRune = ResolveTypewriterRune(index, sourceRune, elapsedMs);
                    break;
                case WH40KTitleFxMode.Wave:
                    offset = ResolveWaveOffset(index, elapsedMs);
                    break;
                case WH40KTitleFxMode.GlitchSlice:
                    ResolveGlitchState(index, sourceRune, elapsedMs, ref drawRune, ref offset, ref alpha, ref glitchSplit);
                    break;
                case WH40KTitleFxMode.NoiseDissolve:
                    ResolveNoiseDissolveState(index, sourceRune, elapsedMs, ref drawRune, ref alpha);
                    break;
                case WH40KTitleFxMode.Scanline:
                    drawColor = ApplyScanlineColor(index, drawColor, elapsedMs);
                    break;
                case WH40KTitleFxMode.Flip:
                    break;
            }

            drawColor = drawColor.WithAlpha(Math.Clamp(drawColor.A * alpha, 0.02f, 1f));
            var drawPos = baseline + offset;
            var baseTransform = handle.GetTransform();
            var applyFlipTransform = MathF.Abs(flipAngle) > 0.001f && drawRune.Value != ' ';

            if (applyFlipTransform)
                handle.SetTransform(CreateGlyphFlipTransform(baseTransform, drawPos, font, sourceRune, flipAngle));

            if (_outlineEnabled && drawRune.Value != ' ')
            {
                var outlineAlpha = Math.Clamp(_outlineAlphaPercent / 100f, 0.02f, 1f) * Math.Clamp(alpha, 0.05f, 1f);
                DrawOutline(handle, font, drawRune, drawPos, _outlineColor.WithAlpha(outlineAlpha));
            }

            if (glitchSplit)
            {
                font.DrawChar(handle, drawRune, drawPos + new Vector2(-0.8f, 0f), UIScale, new Color(255, 90, 90, 120));
                font.DrawChar(handle, drawRune, drawPos + new Vector2(0.8f, 0f), UIScale, new Color(120, 210, 255, 120));
            }

            font.DrawChar(handle, drawRune, drawPos, UIScale, drawColor);

            if (applyFlipTransform)
                handle.SetTransform(baseTransform);

            baseline += new Vector2(GetRuneAdvance(font, sourceRune), 0f);
        }
    }

    private void DrawFish(
        DrawingHandleScreen handle,
        Font font,
        Vector2 baseline,
        double elapsedMs,
        float shift)
    {
        var displayRunes = ResolveFishDisplayRunes(elapsedMs);
        if (displayRunes.Count == 0)
            return;

        var divisor = Math.Max(1, displayRunes.Count - 1);
        for (var index = 0; index < displayRunes.Count; index++)
        {
            var drawRune = displayRunes[index];
            var drawColor = SampleGradient(_palette, ((displayRunes.Count == 1 ? 0f : index / (float) divisor) + shift) % 1f);

            if (_outlineEnabled && drawRune.Value != ' ')
            {
                var outlineAlpha = Math.Clamp(_outlineAlphaPercent / 100f, 0.02f, 1f);
                DrawOutline(handle, font, drawRune, baseline, _outlineColor.WithAlpha(outlineAlpha));
            }

            font.DrawChar(handle, drawRune, baseline, UIScale, drawColor);
            baseline += new Vector2(GetRuneAdvance(font, drawRune), 0f);
        }
    }

    private List<Rune> ResolveFishDisplayRunes(double elapsedMs)
    {
        var normalHoldMs = Math.Max(100, _holdMs);
        var swimStepMs = Math.Max(100, _revealMs);

        if (_runes.Count == 0)
            return _runes;

        var hasWrappingParens = _runes.Count >= 2 &&
                                _runes[0].Value == '(' &&
                                _runes[^1].Value == ')';

        var prefixCount = hasWrappingParens ? 1 : 0;
        var suffixCount = hasWrappingParens ? 1 : 0;
        var coreCount = Math.Max(0, _runes.Count - prefixCount - suffixCount);
        if (coreCount <= 0)
            return _runes;

        var swimSteps = coreCount;
        var cycleMs = normalHoldMs + (swimSteps * swimStepMs);
        var cyclePosition = elapsedMs % cycleMs;
        if (cyclePosition < 0)
            cyclePosition += cycleMs;

        if (cyclePosition < normalHoldMs)
            return _runes;

        cyclePosition -= normalHoldMs;
        var step = Math.Clamp((int) (cyclePosition / swimStepMs), 0, swimSteps - 1);
        var result = new List<Rune>(_runes);
        var fishIndex = prefixCount + (coreCount - 1 - step);
        result[fishIndex] = FishRune;

        return result;
    }

    private float ResolveFlipAngle(double elapsedMs)
    {
        const int normalHoldMs = 5000;
        var toUpsideDownMs = Math.Max(1, _dissolveMs);
        var upsideDownHoldMs = Math.Max(1, _holdMs);
        var toNormalMs = Math.Max(1, _revealMs);
        var cycleMs = normalHoldMs + toUpsideDownMs + upsideDownHoldMs + toNormalMs;

        var cyclePosition = elapsedMs % cycleMs;
        if (cyclePosition < 0)
            cyclePosition += cycleMs;

        if (cyclePosition < normalHoldMs)
            return 0f;

        cyclePosition -= normalHoldMs;
        if (cyclePosition < toUpsideDownMs)
        {
            var progress = (float) (cyclePosition / toUpsideDownMs);
            return EaseInOutSine(progress) * MathF.PI;
        }

        cyclePosition -= toUpsideDownMs;
        if (cyclePosition < upsideDownHoldMs)
            return MathF.PI;

        cyclePosition -= upsideDownHoldMs;
        var restoreProgress = (float) (cyclePosition / toNormalMs);
        return MathF.PI * (1f - EaseInOutSine(restoreProgress));
    }

    private Matrix3x2 CreateGlyphFlipTransform(
        Matrix3x2 baseTransform,
        Vector2 drawPos,
        Font font,
        Rune sourceRune,
        float angle)
    {
        var advance = GetRuneAdvance(font, sourceRune);
        var ascent = font.GetAscent(UIScale);
        var lineHeight = font.GetLineHeight(UIScale);
        var pivot = new Vector2(
            drawPos.X + (advance / 2f),
            drawPos.Y - ascent + (lineHeight / 2f));

        return Matrix3x2.CreateTranslation(-pivot) *
               Matrix3x2.CreateRotation(angle) *
               Matrix3x2.CreateTranslation(pivot) *
               baseTransform;
    }

    private float ResolveGradientShift(double elapsedMs)
    {
        if (!_animated || _durationMs <= 0)
            return 0f;

        var wrapped = elapsedMs % _durationMs;
        if (wrapped < 0)
            wrapped += _durationMs;

        return (float) (wrapped / _durationMs);
    }

    private Rune ResolveBinaryRune(int index, Rune sourceRune, double elapsedMs)
    {
        const int binaryHoldMs = 5000;
        var textHoldMs = Math.Max(1, _holdMs);
        var toBinaryMs = Math.Max(1, _dissolveMs);
        var toTextMs = Math.Max(1, _revealMs);
        var cycleMs = textHoldMs + toBinaryMs + binaryHoldMs + toTextMs;

        var cyclePosition = elapsedMs % cycleMs;
        if (cyclePosition < 0)
            cyclePosition += cycleMs;

        if (cyclePosition < textHoldMs)
            return sourceRune;

        cyclePosition -= textHoldMs;
        var cycleIndex = (long) Math.Floor(elapsedMs / cycleMs);
        if (cyclePosition < toBinaryMs)
        {
            var progress = (float) (cyclePosition / toBinaryMs);
            return ResolveBinaryTransitionRune(index, sourceRune, elapsedMs, cycleIndex, progress, true);
        }

        cyclePosition -= toBinaryMs;
        if (cyclePosition < binaryHoldMs)
            return ResolveBinaryDigit(index, elapsedMs);

        cyclePosition -= binaryHoldMs;
        var restoreProgress = (float) (cyclePosition / toTextMs);
        return ResolveBinaryTransitionRune(index, sourceRune, elapsedMs, cycleIndex, restoreProgress, false);
    }

    private Rune ResolveBinaryTransitionRune(
        int index,
        Rune sourceRune,
        double elapsedMs,
        long cycleIndex,
        float progress,
        bool toBinary)
    {
        var threshold = ComputeTransitionThreshold(index, cycleIndex, toBinary ? 1 : 2);
        var switched = progress >= threshold;
        if (MathF.Abs(progress - threshold) <= 0.08f)
            switched = ShouldUseBinary(index, elapsedMs, toBinary ? 11 : 23);

        if (toBinary)
            return switched ? ResolveBinaryDigit(index, elapsedMs) : sourceRune;

        return switched ? sourceRune : ResolveBinaryDigit(index, elapsedMs);
    }

    private Rune ResolveBinaryDigit(int index, double elapsedMs)
    {
        var tick = (long) (elapsedMs / 55.0);
        var value = tick * 6364136223846793005L + (long) _seed + index * 1442695040888963407L;
        return ((value >> 2) & 1) == 0
            ? new Rune('0')
            : new Rune('1');
    }

    private Rune ResolveScrambleDecodeRune(int index, Rune sourceRune, double elapsedMs)
    {
        const int scrambleHoldMs = 2200;
        var revealMs = Math.Max(1, _revealMs);
        var holdMs = Math.Max(1, _holdMs);
        var dissolveMs = Math.Max(1, _dissolveMs);
        var cycleMs = revealMs + holdMs + dissolveMs + scrambleHoldMs;
        var cyclePosition = elapsedMs % cycleMs;
        if (cyclePosition < 0)
            cyclePosition += cycleMs;

        var cycleIndex = (long) Math.Floor(elapsedMs / cycleMs);
        if (cyclePosition < revealMs)
        {
            var progress = (float) (cyclePosition / revealMs);
            var threshold = ComputeTransitionThreshold(index, cycleIndex, 31);
            var decoded = progress >= threshold;
            if (MathF.Abs(progress - threshold) <= 0.09f)
                decoded = !ShouldUseNoiseRune(index, elapsedMs, 32);

            return decoded ? sourceRune : ResolveNoiseRune(index, elapsedMs, 33);
        }

        cyclePosition -= revealMs;
        if (cyclePosition < holdMs)
            return sourceRune;

        cyclePosition -= holdMs;
        if (cyclePosition < dissolveMs)
        {
            var progress = (float) (cyclePosition / dissolveMs);
            var threshold = ComputeTransitionThreshold(index, cycleIndex, 34);
            var dissolved = progress >= threshold;
            if (MathF.Abs(progress - threshold) <= 0.09f)
                dissolved = ShouldUseNoiseRune(index, elapsedMs, 35);

            return dissolved ? ResolveNoiseRune(index, elapsedMs, 36) : sourceRune;
        }

        return ResolveNoiseRune(index, elapsedMs, 37);
    }

    private Rune ResolveTypewriterRune(int index, Rune sourceRune, double elapsedMs)
    {
        const int emptyHoldMs = 1200;
        var revealMs = Math.Max(1, _revealMs);
        var holdMs = Math.Max(1, _holdMs);
        var dissolveMs = Math.Max(1, _dissolveMs);
        var cycleMs = revealMs + holdMs + dissolveMs + emptyHoldMs;

        var cyclePosition = elapsedMs % cycleMs;
        if (cyclePosition < 0)
            cyclePosition += cycleMs;

        var runeCount = _runes.Count;
        if (runeCount <= 0)
            return sourceRune;

        if (cyclePosition < revealMs)
        {
            var progress = (float) (cyclePosition / revealMs);
            var visibleRunes = Math.Clamp((int) MathF.Floor(progress * runeCount), 0, runeCount);
            if (index < visibleRunes)
                return sourceRune;

            if (_cursorEnabled && index == Math.Clamp(visibleRunes, 0, runeCount - 1) && IsCursorVisible(elapsedMs))
                return new Rune('_');

            return new Rune(' ');
        }

        cyclePosition -= revealMs;
        if (cyclePosition < holdMs)
        {
            if (_cursorEnabled && index == runeCount - 1 && IsCursorVisible(elapsedMs))
                return new Rune('_');

            return sourceRune;
        }

        cyclePosition -= holdMs;
        if (cyclePosition < dissolveMs)
        {
            var progress = (float) (cyclePosition / dissolveMs);
            var visibleRunes = Math.Clamp(runeCount - (int) MathF.Floor(progress * runeCount), 0, runeCount);
            if (index < visibleRunes)
                return sourceRune;

            if (_cursorEnabled && index == Math.Clamp(visibleRunes, 0, runeCount - 1) && IsCursorVisible(elapsedMs))
                return new Rune('_');

            return new Rune(' ');
        }

        if (_cursorEnabled && index == 0 && IsCursorVisible(elapsedMs))
            return new Rune('_');

        return new Rune(' ');
    }

    private Vector2 ResolveWaveOffset(int index, double elapsedMs)
    {
        var t = (float) (elapsedMs * 0.0062f);
        var y = MathF.Sin(t + index * 0.75f) * 2.2f;
        return new Vector2(0f, y);
    }

    private void ResolveGlitchState(
        int index,
        Rune sourceRune,
        double elapsedMs,
        ref Rune drawRune,
        ref Vector2 offset,
        ref float alpha,
        ref bool glitchSplit)
    {
        drawRune = sourceRune;
        var glitched = ShouldGlitch(index, elapsedMs);
        if (!glitched)
            return;

        glitchSplit = true;
        alpha = 0.92f;

        if (ShouldUseNoiseRune(index, elapsedMs, 61))
            drawRune = ResolveNoiseRune(index, elapsedMs, 62);

        var jitterX = (Noise01(index, elapsedMs, 63) - 0.5f) * 2.2f;
        var jitterY = (Noise01(index, elapsedMs, 64) - 0.5f) * 1.4f;
        offset = new Vector2(jitterX, jitterY);
    }

    private void ResolveNoiseDissolveState(
        int index,
        Rune sourceRune,
        double elapsedMs,
        ref Rune drawRune,
        ref float alpha)
    {
        const int noiseHoldMs = 2200;
        var holdMs = Math.Max(1, _holdMs);
        var dissolveMs = Math.Max(1, _dissolveMs);
        var revealMs = Math.Max(1, _revealMs);
        var cycleMs = holdMs + dissolveMs + noiseHoldMs + revealMs;

        var cyclePosition = elapsedMs % cycleMs;
        if (cyclePosition < 0)
            cyclePosition += cycleMs;

        var cycleIndex = (long) Math.Floor(elapsedMs / cycleMs);
        drawRune = sourceRune;
        alpha = 1f;

        if (cyclePosition < holdMs)
            return;

        cyclePosition -= holdMs;
        if (cyclePosition < dissolveMs)
        {
            var progress = (float) (cyclePosition / dissolveMs);
            var threshold = ComputeTransitionThreshold(index, cycleIndex, 71);
            var dissolved = progress >= threshold;
            drawRune = dissolved ? ResolveNoiseRune(index, elapsedMs, 72) : sourceRune;
            alpha = dissolved
                ? 0.45f + (Noise01(index, elapsedMs, 73) * 0.35f)
                : 1f;
            return;
        }

        cyclePosition -= dissolveMs;
        if (cyclePosition < noiseHoldMs)
        {
            drawRune = ResolveNoiseRune(index, elapsedMs, 74);
            alpha = 0.3f + (Noise01(index, elapsedMs, 75) * 0.4f);
            return;
        }

        cyclePosition -= noiseHoldMs;
        var restoreProgress = (float) (cyclePosition / revealMs);
        var restoreThreshold = ComputeTransitionThreshold(index, cycleIndex, 76);
        var restored = restoreProgress >= restoreThreshold;
        drawRune = restored ? sourceRune : ResolveNoiseRune(index, elapsedMs, 77);
        alpha = restored
            ? 1f
            : 0.35f + (Noise01(index, elapsedMs, 78) * 0.35f);
    }

    private Color ApplyScanColor(int index, Color baseColor, double elapsedMs)
    {
        var duration = Math.Max(1200, _revealMs + _holdMs + _dissolveMs);
        var scanProgress = (float) ((elapsedMs % duration) / duration);
        if (scanProgress < 0f)
            scanProgress += 1f;

        var charPos = _runes.Count <= 1
            ? 0f
            : index / (float) (_runes.Count - 1);
        var distance = MathF.Abs(charPos - scanProgress);
        distance = MathF.Min(distance, 1f - distance);
        var influence = MathF.Max(0f, 1f - distance / 0.22f);
        return Color.InterpolateBetween(baseColor, Color.White, influence * 0.75f);
    }

    private Color ApplyScanlineColor(int index, Color baseColor, double elapsedMs)
    {
        var duration = Math.Max(1400, _revealMs + _holdMs + _dissolveMs);
        var scanProgress = (float) ((elapsedMs % duration) / duration);
        if (scanProgress < 0f)
            scanProgress += 1f;

        var charPos = _runes.Count <= 1
            ? 0f
            : index / (float) (_runes.Count - 1);
        var distance = MathF.Abs(charPos - scanProgress);
        var sweep = MathF.Max(0f, 1f - distance / 0.16f);
        var lineColor = Color.InterpolateBetween(baseColor, Color.White, sweep * 0.7f);

        var stripeTick = (long) (elapsedMs / 85.0);
        var stripe = ((stripeTick + index) & 3) == 0;
        if (stripe)
            lineColor = Color.InterpolateBetween(lineColor, Color.Black, 0.22f);

        return lineColor;
    }

    private bool IsCursorVisible(double elapsedMs)
    {
        return ((long) (elapsedMs / 260.0) & 1) == 0;
    }

    private bool ShouldGlitch(int index, double elapsedMs)
    {
        var tick = (long) (elapsedMs / 95.0);
        return ((Hash(tick, index, 60) % 1000UL) / 1000f) < 0.18f;
    }

    private bool ShouldUseNoiseRune(int index, double elapsedMs, int salt)
    {
        var tick = (long) (elapsedMs / 70.0);
        return (Hash(tick, index, salt) & 1UL) == 0;
    }

    private Rune ResolveNoiseRune(int index, double elapsedMs, int salt)
    {
        var tick = (long) (elapsedMs / 55.0);
        var value = Hash(tick, index, salt);
        var pick = (int) (value % (ulong) NoiseRunes.Length);
        return NoiseRunes[pick];
    }

    private float Noise01(int index, double elapsedMs, int salt)
    {
        var tick = (long) (elapsedMs / 65.0);
        var value = Hash(tick, index, salt);
        return (value % 1_000_000UL) / 1_000_000f;
    }

    private float ComputeTransitionThreshold(int index, long cycleIndex, int salt)
    {
        var value = Hash(cycleIndex, index, salt);
        return (value % 1_000_000UL) / 1_000_000f;
    }

    private static float EaseInOutSine(float progress)
    {
        var clamped = Math.Clamp(progress, 0f, 1f);
        return 0.5f - (MathF.Cos(clamped * MathF.PI) * 0.5f);
    }

    private bool ShouldUseBinary(int index, double elapsedMs, int salt)
    {
        var tick = (long) (elapsedMs / 70.0);
        return (Hash(tick, index, salt) & 1UL) == 0;
    }

    private ulong Hash(long tick, int index, int salt)
    {
        unchecked
        {
            var value = (ulong) tick;
            value ^= (ulong) _seed * 0x9E3779B97F4A7C15UL;
            value ^= (ulong) index * 0xBF58476D1CE4E5B9UL;
            value ^= (ulong) salt * 0x94D049BB133111EBUL;
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return value;
        }
    }

    private static int ComputeStableSeed(string text)
    {
        unchecked
        {
            var hash = 17;
            foreach (var rune in text.EnumerateRunes())
            {
                hash = (hash * 31) + rune.Value;
            }

            return hash;
        }
    }

    private Font ResolveFont()
    {
        if (TryGetStyleProperty<Font>("font", out var font))
            return font;

        return UserInterfaceManager.ThemeDefaults.LabelFont;
    }

    private float GetRuneAdvance(Font font, Rune rune)
    {
        var metrics = font.GetCharMetrics(rune, UIScale, fallback: true);
        if (metrics is { } measured)
            return measured.Advance;

        return font.GetCharMetrics(new Rune('?'), UIScale, fallback: false)?.Advance ?? 0f;
    }

    private void DrawOutline(
        DrawingHandleScreen handle,
        Font font,
        Rune rune,
        Vector2 drawPos,
        Color outlineColor)
    {
        var width = Math.Clamp(_outlineWidth, 1, 3);
        for (var x = -width; x <= width; x++)
        {
            for (var y = -width; y <= width; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                if (Math.Abs(x) + Math.Abs(y) > width * 2)
                    continue;

                font.DrawChar(handle, rune, drawPos + new Vector2(x, y), UIScale, outlineColor);
            }
        }
    }

    private static Color SampleGradient(IReadOnlyList<Color> palette, float t)
    {
        if (palette.Count == 1)
            return palette[0];

        var clamped = Math.Clamp(t, 0f, 1f);
        var segments = palette.Count - 1;
        var scaled = clamped * segments;
        var segment = Math.Min(segments - 1, (int) scaled);
        var localT = scaled - segment;
        return Color.InterpolateBetween(palette[segment], palette[segment + 1], localT);
    }
}
