using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Resources;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.GunGame;

public sealed class WH40KGunGameKillFeedHudControl : Control
{
    private const int MaxEntries = 5;
    private const int MaxNameCharacters = 12;
    private const float ControlWidth = 520f;
    private const float ControlHeight = 236f;
    private const float EntryHeight = 38f;
    private const float EntryGap = 8f;
    private const float RightPadding = 4f;
    private const float BottomPadding = 4f;
    private const float HorizontalPadding = 12f;
    private const float IconSize = 28f;
    private const float IconSlotWidth = 32f;
    private const float IconGap = 12f;
    private const float EntryMinWidth = 138f;
    private const float EntryMaxWidth = 440f;
    private const float OpenSeconds = 0.18f;
    private const float VisibleSeconds = 5f;
    private const float CloseSeconds = 0.24f;
    private const float SlideDistance = 26f;
    private const float LiftDistance = 14f;

    private static readonly Color DefaultBackground = Color.Black.WithAlpha(0.68f);
    private static readonly Color DefaultBorder = Color.White.WithAlpha(0.12f);
    private static readonly Color KillerBackground = Color.FromHex("#8A6719").WithAlpha(0.82f);
    private static readonly Color KillerBorder = Color.FromHex("#F0C96A").WithAlpha(0.58f);
    private static readonly Color VictimBackground = Color.FromHex("#55131B").WithAlpha(0.84f);
    private static readonly Color VictimBorder = Color.FromHex("#A8454B").WithAlpha(0.62f);
    private static readonly Color NameColor = Color.White.WithAlpha(0.95f);

    private readonly Font _font;
    private readonly SpriteSystem _spriteSystem;
    private readonly IPrototypeManager _prototypeManager;
    private readonly List<KillFeedEntry> _entries = new();

    public WH40KGunGameKillFeedHudControl()
    {
        IoCManager.InjectDependencies(this);

        var cache = IoCManager.Resolve<IResourceCache>();
        var entityManager = IoCManager.Resolve<IEntityManager>();
        _prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        _spriteSystem = entityManager.System<SpriteSystem>();
        _font = cache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 13);

        Visible = true;
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = false;
        MinSize = new Vector2(ControlWidth, ControlHeight);
        SetSize = new Vector2(ControlWidth, ControlHeight);
    }

    public void Push(Content.Shared._WH40K.GunGame.WH40KGunGameKillFeedEvent ev)
    {
        if (_entries.Count >= MaxEntries)
            _entries.RemoveAt(0);

        var killerName = TruncateName(ev.KillerName);
        var victimName = TruncateName(ev.VictimName);

        var entry = new KillFeedEntry(
            killerName,
            victimName,
            ResolveWeaponTexture(ev.WeaponPrototypeId),
            ev.UseSkullIcon,
            ResolveBackground(ev.LocalKiller, ev.LocalVictim),
            ResolveBorder(ev.LocalKiller, ev.LocalVictim))
        {
            AnimatedY = ControlHeight - BottomPadding - EntryHeight + LiftDistance
        };

        _entries.Add(entry);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_entries.Count == 0)
            return;

        var delta = args.DeltaSeconds;

        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            entry.Age += delta;

            if (!entry.Closing && entry.Age >= VisibleSeconds)
                entry.Closing = true;

            if (entry.Closing)
                entry.CloseAge += delta;

            var targetY = GetTargetY(i, _entries.Count);
            if (!entry.Initialized)
            {
                entry.AnimatedY = targetY + LiftDistance;
                entry.Initialized = true;
            }
            else
            {
                entry.AnimatedY = Lerp(entry.AnimatedY, targetY, Math.Clamp(delta * 16f, 0f, 1f));
            }
        }

        _entries.RemoveAll(entry => entry.Closing && entry.CloseAge >= CloseSeconds);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_entries.Count == 0)
            return;

        foreach (var entry in _entries)
        {
            var alpha = GetVisualProgress(entry);
            if (alpha <= 0f)
                continue;

            var controlWidth = MathF.Max(ControlWidth, PixelSize.X);
            var width = Math.Clamp(
                HorizontalPadding * 2f +
                MeasureTextWidth(entry.KillerName) +
                IconGap +
                IconSlotWidth +
                IconGap +
                MeasureTextWidth(entry.VictimName),
                EntryMinWidth,
                EntryMaxWidth);

            var slide = (1f - alpha) * SlideDistance;
            var left = controlWidth - RightPadding - width + slide;
            var top = entry.AnimatedY;
            var box = UIBox2.FromDimensions(new Vector2(left, top), new Vector2(width, EntryHeight));

            handle.DrawRect(box, entry.Background.WithAlpha(entry.Background.A * alpha));
            handle.DrawRect(box, entry.Border.WithAlpha(entry.Border.A * alpha), filled: false);

            var shadowBox = new UIBox2(box.Left + 1f, box.Top + 1f, box.Right - 1f, box.Bottom - 1f);
            handle.DrawRect(shadowBox, Color.Black.WithAlpha(0.18f * alpha), filled: false);

            var textY = top + (EntryHeight - _font.GetHeight(UIScale)) * 0.5f;
            var currentX = left + HorizontalPadding;

            handle.DrawString(_font, new Vector2(currentX, textY), entry.KillerName, NameColor.WithAlpha(NameColor.A * alpha));
            currentX += MeasureTextWidth(entry.KillerName) + IconGap;

            var iconLeft = currentX + (IconSlotWidth - IconSize) * 0.5f;
            DrawWeaponIcon(handle, entry.Icon, entry.UseSkullIcon, iconLeft, top + (EntryHeight - IconSize) * 0.5f, alpha);
            currentX += IconSlotWidth + IconGap;

            handle.DrawString(_font, new Vector2(currentX, textY), entry.VictimName, NameColor.WithAlpha(NameColor.A * alpha));
        }
    }

    private float GetTargetY(int index, int count)
    {
        var bottomIndex = count - 1 - index;
        var controlHeight = MathF.Max(ControlHeight, PixelSize.Y);
        return controlHeight - BottomPadding - EntryHeight - bottomIndex * (EntryHeight + EntryGap);
    }

    private Texture? ResolveWeaponTexture(string? prototypeId)
    {
        if (string.IsNullOrWhiteSpace(prototypeId) || !_prototypeManager.HasIndex<EntityPrototype>(prototypeId))
            return null;

        return _spriteSystem.Frame0(new SpriteSpecifier.EntityPrototype(prototypeId));
    }

    private void DrawWeaponIcon(DrawingHandleScreen handle, Texture? texture, bool useSkullIcon, float left, float top, float alpha)
    {
        var iconBox = UIBox2.FromDimensions(new Vector2(left, top), new Vector2(IconSize, IconSize));
        handle.DrawRect(iconBox, Color.Black.WithAlpha(0.22f * alpha));

        if (texture != null)
        {
            handle.DrawTextureRect(texture, iconBox, Color.White.WithAlpha(alpha));
            return;
        }

        if (useSkullIcon)
        {
            DrawSkullIcon(handle, iconBox, alpha);
            return;
        }

        handle.DrawRect(iconBox, Color.White.WithAlpha(0.18f * alpha), filled: false);
        handle.DrawLine(iconBox.TopLeft + new Vector2(4f, 4f), iconBox.BottomRight - new Vector2(4f, 4f), Color.White.WithAlpha(0.5f * alpha));
        handle.DrawLine(new Vector2(iconBox.Right - 4f, iconBox.Top + 4f), new Vector2(iconBox.Left + 4f, iconBox.Bottom - 4f), Color.White.WithAlpha(0.5f * alpha));
    }

    private static void DrawSkullIcon(DrawingHandleScreen handle, UIBox2 iconBox, float alpha)
    {
        var center = iconBox.Center;
        var skullColor = Color.White.WithAlpha(0.9f * alpha);
        var accent = Color.FromHex("#C9CCD1").WithAlpha(0.72f * alpha);

        handle.DrawCircle(center + new Vector2(0f, -3f), 6f, skullColor, filled: false);
        handle.DrawCircle(center + new Vector2(-2.2f, -4f), 1.2f, skullColor);
        handle.DrawCircle(center + new Vector2(2.2f, -4f), 1.2f, skullColor);
        handle.DrawLine(center + new Vector2(-3f, 2.5f), center + new Vector2(3f, 2.5f), accent);
        handle.DrawLine(center + new Vector2(-2f, 2.5f), center + new Vector2(-2f, 6f), accent);
        handle.DrawLine(center + new Vector2(0f, 2.5f), center + new Vector2(0f, 6f), accent);
        handle.DrawLine(center + new Vector2(2f, 2.5f), center + new Vector2(2f, 6f), accent);
    }

    private float GetVisualProgress(KillFeedEntry entry)
    {
        if (entry.Closing)
        {
            var closing = 1f - Math.Clamp(entry.CloseAge / CloseSeconds, 0f, 1f);
            return SmoothStep(closing);
        }

        var opening = Math.Clamp(entry.Age / OpenSeconds, 0f, 1f);
        return SmoothStep(opening);
    }

    private string TruncateName(string value)
    {
        value = value.Trim();
        if (value.Length == 0)
            return "?";

        var runes = value.EnumerateRunes().ToArray();
        if (runes.Length <= MaxNameCharacters)
            return value;

        var visibleCharacters = Math.Max(1, MaxNameCharacters - 3);
        return string.Concat(runes.Take(visibleCharacters).Select(r => r.ToString())) + "...";
    }

    private float MeasureTextWidth(string text)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            var metrics = _font.GetCharMetrics(rune, UIScale, fallback: true);
            width += metrics?.Advance ?? 0f;
        }

        return width;
    }

    private static Color ResolveBackground(bool localKiller, bool localVictim)
    {
        if (localVictim)
            return VictimBackground;

        if (localKiller)
            return KillerBackground;

        return DefaultBackground;
    }

    private static Color ResolveBorder(bool localKiller, bool localVictim)
    {
        if (localVictim)
            return VictimBorder;

        if (localKiller)
            return KillerBorder;

        return DefaultBorder;
    }

    private static float Lerp(float from, float to, float value)
    {
        return from + (to - from) * value;
    }

    private static float SmoothStep(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
    }

    private sealed class KillFeedEntry
    {
        public readonly string KillerName;
        public readonly string VictimName;
        public readonly Texture? Icon;
        public readonly bool UseSkullIcon;
        public readonly Color Background;
        public readonly Color Border;

        public float Age;
        public bool Closing;
        public float CloseAge;
        public float AnimatedY;
        public bool Initialized;

        public KillFeedEntry(string killerName, string victimName, Texture? icon, bool useSkullIcon, Color background, Color border)
        {
            KillerName = killerName;
            VictimName = victimName;
            Icon = icon;
            UseSkullIcon = useSkullIcon;
            Background = background;
            Border = border;
        }
    }
}
