using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

public sealed class WH40KGradientTag : IMarkupTagHandler
{
    public string Name => "wh40kgradient";

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        if (!node.Value.TryGetString(out var text) || string.IsNullOrWhiteSpace(text))
        {
            control = null;
            return false;
        }

        var palette = ParsePalette(node.Attributes);
        if (palette.Count == 0)
        {
            control = null;
            return false;
        }

        var animated = ReadBool(node.Attributes, "animated", false);
        var durationMs = Math.Clamp(ReadInt(node.Attributes, "duration", 3500), 400, 60000);
        var phaseMs = ReadInt(node.Attributes, "phase", 0);

        var auraEnabled = ReadBool(node.Attributes, "aura", false);
        var auraRadius = Math.Clamp(ReadInt(node.Attributes, "auraradius", 1), 1, 4);
        var auraAlphaPercent = Math.Clamp(ReadInt(node.Attributes, "auraalpha", 65), 1, 100);
        var auraColor = Color.White;
        if (node.Attributes.TryGetValue("auracolor", out var auraColorParam) &&
            auraColorParam.TryGetString(out var auraColorRaw) &&
            TryResolveColor(auraColorRaw, out var parsedAura))
        {
            auraColor = parsedAura;
            auraEnabled = true;
        }

        var titleEffect = ParseTitleEffectMode(ReadString(node.Attributes, "titleeffect", string.Empty));
        var titleChars = Math.Max(0, ReadInt(node.Attributes, "titlechars", 0));
        var titleRevealMs = Math.Clamp(ReadInt(node.Attributes, "titlereveal", 900), 100, 120000);
        var titleHoldMs = Math.Clamp(ReadInt(node.Attributes, "titlehold", 10000), 100, 120000);
        var titleDissolveMs = Math.Clamp(ReadInt(node.Attributes, "titledissolve", 900), 100, 120000);
        if (titleChars <= 0)
            titleEffect = WH40KTitleEffectMode.None;

        control = new WH40KGradientNameControl(
            text,
            palette,
            animated,
            durationMs,
            phaseMs,
            auraEnabled,
            auraColor,
            auraRadius,
            auraAlphaPercent,
            titleEffect,
            titleChars,
            titleRevealMs,
            titleHoldMs,
            titleDissolveMs);
        return true;
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

    private static WH40KTitleEffectMode ParseTitleEffectMode(string source)
    {
        return source.Trim().ToLowerInvariant() switch
        {
            "binary" => WH40KTitleEffectMode.Binary,
            "scan" => WH40KTitleEffectMode.Scan,
            _ => WH40KTitleEffectMode.None,
        };
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

internal enum WH40KTitleEffectMode : byte
{
    None = 0,
    Binary = 1,
    Scan = 2,
}

internal sealed class WH40KGradientNameControl : Control
{
    [Dependency] private readonly IGameTiming _timing = default!;
    private const int BinaryScrambleHoldMs = 5000;

    private readonly List<Rune> _runes;
    private readonly List<Color> _palette;
    private readonly bool _animated;
    private readonly int _durationMs;
    private readonly int _phaseMs;

    private readonly bool _auraEnabled;
    private readonly Color _auraColor;
    private readonly int _auraRadius;
    private readonly int _auraAlphaPercent;

    private readonly WH40KTitleEffectMode _titleEffect;
    private readonly int _titleChars;
    private readonly int _titleRevealMs;
    private readonly int _titleHoldMs;
    private readonly int _titleDissolveMs;
    private readonly int _binarySeed;
    private readonly int _detectedTitlePrefixRuneCount;

    public WH40KGradientNameControl(
        string text,
        List<Color> palette,
        bool animated,
        int durationMs,
        int phaseMs,
        bool auraEnabled,
        Color auraColor,
        int auraRadius,
        int auraAlphaPercent,
        WH40KTitleEffectMode titleEffect,
        int titleChars,
        int titleRevealMs,
        int titleHoldMs,
        int titleDissolveMs)
    {
        IoCManager.InjectDependencies(this);

        _palette = palette;
        _animated = animated;
        _durationMs = durationMs;
        _phaseMs = phaseMs;

        _auraEnabled = auraEnabled;
        _auraColor = auraColor;
        _auraRadius = auraRadius;
        _auraAlphaPercent = auraAlphaPercent;

        _titleEffect = titleEffect;
        _titleChars = Math.Max(0, titleChars);
        _titleRevealMs = titleRevealMs;
        _titleHoldMs = titleHoldMs;
        _titleDissolveMs = titleDissolveMs;
        _binarySeed = ComputeStableSeed(text);

        _runes = new List<Rune>();
        foreach (var rune in text.EnumerateRunes())
        {
            _runes.Add(rune);
        }

        _detectedTitlePrefixRuneCount = DetectTitlePrefixRuneCount(_runes);
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

        var shift = 0f;
        if (_animated)
        {
            var wrapped = elapsedMs % _durationMs;
            if (wrapped < 0)
                wrapped += _durationMs;

            shift = (float) (wrapped / _durationMs);
        }

        var titleLimit = Math.Min(_titleChars, _runes.Count);
        if (_detectedTitlePrefixRuneCount > 0)
            titleLimit = Math.Min(titleLimit, _detectedTitlePrefixRuneCount);
        var auraColor = _auraColor.WithAlpha(Math.Clamp(_auraAlphaPercent / 100f, 0.05f, 1f));
        var divisor = Math.Max(1, _runes.Count - 1);

        for (var index = 0; index < _runes.Count; index++)
        {
            var sourceRune = _runes[index];
            var rune = ResolveTitleRune(index, sourceRune, titleLimit, elapsedMs);

            var t = _runes.Count == 1
                ? shift
                : (index / (float) divisor) + shift;
            t -= MathF.Floor(t);

            var color = SampleGradient(_palette, t);
            color = ApplyTitleColorEffect(index, color, titleLimit, elapsedMs);

            DrawRune(handle, font, rune, baseline, color, auraColor);
            baseline += new Vector2(GetRuneAdvance(font, sourceRune), 0f);
        }
    }

    private void DrawRune(DrawingHandleScreen handle, Font font, Rune rune, Vector2 baseline, Color color, Color auraColor)
    {
        if (_auraEnabled)
        {
            var offsets = _auraRadius switch
            {
                1 => AuraOffsetsR1,
                2 => AuraOffsetsR2,
                3 => AuraOffsetsR3,
                _ => AuraOffsetsR4,
            };

            foreach (var offset in offsets)
            {
                font.DrawChar(handle, rune, baseline + offset, UIScale, auraColor);
            }
        }

        font.DrawChar(handle, rune, baseline, UIScale, color);
    }

    private Rune ResolveTitleRune(int index, Rune sourceRune, int titleLimit, double elapsedMs)
    {
        if (_titleEffect != WH40KTitleEffectMode.Binary || index >= titleLimit || titleLimit <= 0)
            return sourceRune;

        // Binary cycle:
        // 1) Hold readable title text.
        // 2) Randomly replace title runes with 0/1.
        // 3) Hold fully binary scramble.
        // 4) Randomly restore original title text.
        var textHoldMs = Math.Max(1, _titleHoldMs);
        var toBinaryMs = Math.Max(1, _titleDissolveMs);
        var binaryHoldMs = BinaryScrambleHoldMs;
        var toTextMs = Math.Max(1, _titleRevealMs);
        var cycleMs = textHoldMs + toBinaryMs + binaryHoldMs + toTextMs;
        if (cycleMs <= 0)
            return sourceRune;

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
            return ResolveBinaryTransitionRune(index, sourceRune, elapsedMs, cycleIndex, progress, toBinary: true);
        }

        cyclePosition -= toBinaryMs;
        if (cyclePosition < binaryHoldMs)
            return ResolveBinaryRune(index, elapsedMs);

        cyclePosition -= binaryHoldMs;
        var restoreProgress = (float) (cyclePosition / toTextMs);
        return ResolveBinaryTransitionRune(index, sourceRune, elapsedMs, cycleIndex, restoreProgress, toBinary: false);
    }

    private Color ApplyTitleColorEffect(int index, Color color, int titleLimit, double elapsedMs)
    {
        if (_titleEffect != WH40KTitleEffectMode.Scan || index >= titleLimit || titleLimit <= 0)
            return color;

        var duration = Math.Max(1200, _titleRevealMs + _titleHoldMs + _titleDissolveMs);
        var scanProgress = (float) ((elapsedMs % duration) / duration);
        if (scanProgress < 0f)
            scanProgress += 1f;

        var charPos = titleLimit <= 1
            ? 0f
            : index / (float) (titleLimit - 1);
        var distance = MathF.Abs(charPos - scanProgress);
        distance = MathF.Min(distance, 1f - distance);

        var influence = MathF.Max(0f, 1f - distance / 0.22f);
        return Color.InterpolateBetween(color, Color.White, influence * 0.75f);
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

        // Add a short noisy band around per-rune switch threshold to avoid hard edges.
        if (MathF.Abs(progress - threshold) <= 0.08f)
            switched = ShouldUseBinary(index, elapsedMs, toBinary ? 11 : 23);

        if (toBinary)
            return switched ? ResolveBinaryRune(index, elapsedMs) : sourceRune;

        return switched ? sourceRune : ResolveBinaryRune(index, elapsedMs);
    }

    private float ComputeTransitionThreshold(int index, long cycleIndex, int salt)
    {
        unchecked
        {
            long value = _binarySeed;
            value = (value * 1103515245L) + 12345L;
            value ^= index * 2654435761L;
            value ^= cycleIndex * 40503L;
            value ^= salt * 81173L;
            var positive = (ulong) value;
            return (positive % 1_000_000UL) / 1_000_000f;
        }
    }

    private bool ShouldUseBinary(int index, double elapsedMs, int salt = 0)
    {
        var tick = (long) (elapsedMs / 70.0);
        var value = tick * 1103515245L + (long) _binarySeed + index * 2654435761L + salt * 97531L;
        return (value & 1) == 0;
    }

    private Rune ResolveBinaryRune(int index, double elapsedMs)
    {
        var tick = (long) (elapsedMs / 55.0);
        var value = tick * 6364136223846793005L + (long) _binarySeed + index * 1442695040888963407L;
        return ((value >> 2) & 1) == 0
            ? new Rune('0')
            : new Rune('1');
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

    private static int DetectTitlePrefixRuneCount(IReadOnlyList<Rune> runes)
    {
        if (runes.Count < 3 || runes[0].Value != '(')
            return 0;

        for (var i = 1; i < runes.Count - 1; i++)
        {
            if (runes[i].Value == ')' && Rune.IsWhiteSpace(runes[i + 1]))
                return i + 2;
        }

        return 0;
    }

    private Font ResolveFont()
    {
        if (TryGetStyleProperty<Font>("font", out var font))
            return font;

        return UserInterfaceManager.ThemeDefaults.LabelFont;
    }

    private float GetRuneAdvance(Font font, Rune rune)
    {
        if (font.TryGetCharMetrics(rune, UIScale, out var metrics))
            return metrics.Advance;

        return font.GetCharMetrics(new Rune('?'), UIScale, fallback: false)?.Advance ?? 0f;
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

    private static readonly Vector2[] AuraOffsetsR1 =
    {
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
    };

    private static readonly Vector2[] AuraOffsetsR2 =
    {
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
        new(-2, 0), new(2, 0), new(0, -2), new(0, 2),
        new(-1, -1), new(1, -1), new(-1, 1), new(1, 1),
    };

    private static readonly Vector2[] AuraOffsetsR3 =
    {
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
        new(-2, 0), new(2, 0), new(0, -2), new(0, 2),
        new(-3, 0), new(3, 0), new(0, -3), new(0, 3),
        new(-2, -1), new(2, -1), new(-2, 1), new(2, 1),
        new(-1, -2), new(1, -2), new(-1, 2), new(1, 2),
    };

    private static readonly Vector2[] AuraOffsetsR4 =
    {
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
        new(-2, 0), new(2, 0), new(0, -2), new(0, 2),
        new(-3, 0), new(3, 0), new(0, -3), new(0, 3),
        new(-4, 0), new(4, 0), new(0, -4), new(0, 4),
        new(-2, -1), new(2, -1), new(-2, 1), new(2, 1),
        new(-1, -2), new(1, -2), new(-1, 2), new(1, 2),
        new(-3, -1), new(3, -1), new(-3, 1), new(3, 1),
        new(-1, -3), new(1, -3), new(-1, 3), new(1, 3),
    };
}
