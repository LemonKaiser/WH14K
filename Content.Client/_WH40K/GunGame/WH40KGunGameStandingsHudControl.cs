using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._WH40K.GunGame;
using Robust.Client.Player;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Localization;

namespace Content.Client._WH40K.GunGame;

public sealed class WH40KGunGameStandingsHudControl : Control
{
    private const int TopNameCharacters = 12;
    private const int PersonalNameCharacters = 18;
    private const float PanelHeight = 42f;
    private const float PanelGap = 12f;
    private const float TopMargin = 6f;
    private const float BottomMargin = 4f;
    private const float LevelBoxWidth = 52f;
    private const float DividerWidth = 4f;
    private const float TopNameBoxWidth = 136f;
    private const float TopPanelWidth = LevelBoxWidth + DividerWidth + TopNameBoxWidth;
    private const float PersonalPanelWidth = TopPanelWidth * 1.5f;
    private const float PersonalNameBoxWidth = PersonalPanelWidth - LevelBoxWidth - DividerWidth;
    private const float TopRowWidth = TopPanelWidth * 3f + PanelGap * 2f;
    private const float RowGap = 10f;
    public const float CanvasWidth = TopRowWidth;
    public const float CanvasHeight = TopMargin + PanelHeight + RowGap + PanelHeight + BottomMargin;
    private const float TopRowTop = TopMargin;
    private const float PersonalTop = TopRowTop + PanelHeight + RowGap;
    private const float NameHorizontalPadding = 12f;

    private static readonly Color PanelBackground = Color.Black.WithAlpha(0.74f);
    private static readonly Color PanelInner = Color.Black.WithAlpha(0.58f);
    private static readonly Color NameColor = Color.White.WithAlpha(0.96f);
    private static readonly Color LevelColor = Color.White.WithAlpha(0.98f);
    private static readonly Color EmptyNameColor = Color.White.WithAlpha(0.50f);
    private static readonly Color[] PlaceOutlineColors =
    {
        Color.FromHex("#F0C96A").WithAlpha(0.95f),
        Color.FromHex("#C8CDD4").WithAlpha(0.94f),
        Color.FromHex("#B97A56").WithAlpha(0.94f)
    };
    private static readonly Color PersonalOutlineColor = Color.Black.WithAlpha(0.96f);

    private readonly Font _levelFont;
    private readonly Font _nameFont;
    private readonly IPlayerManager _player;
    private List<WH40KGunGameStandingEntry> _entries = new();

    public WH40KGunGameStandingsHudControl()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _player = IoCManager.Resolve<IPlayerManager>();
        _levelFont = cache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 18);
        _nameFont = cache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 15);

        Visible = false;
        MouseFilter = MouseFilterMode.Ignore;
        MinSize = new Vector2(CanvasWidth, CanvasHeight);
        SetSize = new Vector2(CanvasWidth, CanvasHeight);
    }

    public void Apply(WH40KGunGameStandingsEvent ev)
    {
        _entries = ev.Entries.ToList();
        Visible = _entries.Count > 0;
    }

    public void Clear()
    {
        _entries.Clear();
        Visible = false;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (!Visible)
            return;

        var topEntries = BuildTopEntries();
        var centerX = CanvasWidth * 0.5f;
        var topRowLeft = MathF.Round(centerX - TopRowWidth * 0.5f);
        for (var i = 0; i < topEntries.Count; i++)
        {
            var left = MathF.Round(topRowLeft + i * (TopPanelWidth + PanelGap));
            DrawPanel(handle, left, TopRowTop, TopPanelWidth, TopNameBoxWidth, TopNameCharacters, topEntries[i], PlaceOutlineColors[i]);
        }

        var personalEntry = BuildPersonalEntry();
        var personalLeft = MathF.Round(centerX - PersonalPanelWidth * 0.5f);
        DrawPanel(handle, personalLeft, PersonalTop, PersonalPanelWidth, PersonalNameBoxWidth, PersonalNameCharacters, personalEntry, PersonalOutlineColor);
    }

    private List<StandingPanelEntry> BuildTopEntries()
    {
        var result = new List<StandingPanelEntry>(3);
        for (var i = 0; i < 3; i++)
        {
            if (i < _entries.Count)
            {
                var entry = _entries[i];
                result.Add(new StandingPanelEntry(Math.Max(0, entry.Level), entry.UserName, false));
                continue;
            }

            result.Add(new StandingPanelEntry(0, Loc.GetString("wh40k-gun-game-standings-empty"), true));
        }

        return result;
    }

    private StandingPanelEntry BuildPersonalEntry()
    {
        var localId = _player.LocalSession?.UserId;
        if (localId != null)
        {
            foreach (var entry in _entries)
            {
                if (entry.UserId == localId.Value)
                    return new StandingPanelEntry(Math.Max(0, entry.Level), entry.UserName, false);
            }
        }

        var fallbackName = _player.LocalSession?.Name ?? Loc.GetString("wh40k-gun-game-standings-empty");
        return new StandingPanelEntry(0, fallbackName, string.IsNullOrWhiteSpace(_player.LocalSession?.Name));
    }

    private void DrawPanel(
        DrawingHandleScreen handle,
        float left,
        float top,
        float panelWidth,
        float nameBoxWidth,
        int maxNameCharacters,
        StandingPanelEntry entry,
        Color outlineColor)
    {
        var panel = UIBox2.FromDimensions(new Vector2(left, top), new Vector2(panelWidth, PanelHeight));
        var inner = new UIBox2(panel.Left + 1f, panel.Top + 1f, panel.Right - 1f, panel.Bottom - 1f);
        var dividerLeft = panel.Left + LevelBoxWidth;
        var divider = UIBox2.FromDimensions(new Vector2(dividerLeft, panel.Top), new Vector2(DividerWidth, PanelHeight));
        var name = TruncateName(entry.Name, maxNameCharacters, nameBoxWidth);
        var nameColor = entry.IsPlaceholder ? EmptyNameColor : NameColor;

        handle.DrawRect(panel, PanelBackground);
        handle.DrawRect(inner, PanelInner);
        handle.DrawRect(panel, outlineColor, filled: false);
        handle.DrawRect(divider, outlineColor);

        var levelText = entry.Level.ToString();
        var levelBox = new UIBox2(panel.Left, panel.Top, divider.Left, panel.Bottom);
        DrawCenteredText(handle, _levelFont, levelText, levelBox, LevelColor);

        var nameBox = new UIBox2(divider.Right + NameHorizontalPadding, panel.Top, panel.Right - NameHorizontalPadding, panel.Bottom);
        DrawCenteredText(handle, _nameFont, name, nameBox, nameColor);
    }

    private string TruncateName(string value, int maxCharacters, float nameBoxWidth)
    {
        value = value.Trim();
        if (value.Length == 0)
            return Loc.GetString("wh40k-gun-game-standings-empty");

        var runes = value.EnumerateRunes().ToArray();
        if (runes.Length > maxCharacters)
        {
            var visibleCharacters = Math.Max(1, maxCharacters - 3);
            value = string.Concat(runes.Take(visibleCharacters).Select(r => r.ToString())) + "...";
        }

        var availableWidth = nameBoxWidth - NameHorizontalPadding * 2f;
        while (MeasureTextWidth(_nameFont, value) > availableWidth && value.Length > 3)
        {
            var candidateRunes = value.EnumerateRunes().ToArray();
            if (candidateRunes.Length <= 4)
                break;

            value = string.Concat(candidateRunes.Take(candidateRunes.Length - 4).Select(r => r.ToString())) + "...";
        }

        return value;
    }

    private float MeasureTextWidth(Font font, string text)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            var metrics = font.GetCharMetrics(rune, UIScale, fallback: true);
            width += metrics?.Advance ?? 0f;
        }

        return width;
    }

    private void DrawCenteredText(DrawingHandleScreen handle, Font font, string text, UIBox2 box, Color color)
    {
        if (text.Length == 0)
            return;

        var (boundsLeft, boundsRight) = MeasureTextHorizontalBounds(font, text);
        var baselineX = MathF.Round(box.Center.X - (boundsLeft + boundsRight) * 0.5f);
        var baselineY = MathF.Round(box.Center.Y + (font.GetAscent(UIScale) - font.GetDescent(UIScale)) * 0.5f);
        var baseline = new Vector2(baselineX, baselineY);

        foreach (var rune in text.EnumerateRunes())
        {
            baseline.X += font.DrawChar(handle, rune, baseline, UIScale, color, fallback: true);
        }
    }

    private (float Left, float Right) MeasureTextHorizontalBounds(Font font, string text)
    {
        var penX = 0f;
        var hasGlyph = false;
        var left = 0f;
        var right = 0f;

        foreach (var rune in text.EnumerateRunes())
        {
            var metrics = font.GetCharMetrics(rune, UIScale, fallback: true);
            if (metrics != null && metrics.Value.Width > 0)
            {
                var glyphLeft = penX + metrics.Value.BearingX;
                var glyphRight = glyphLeft + metrics.Value.Width;

                if (!hasGlyph)
                {
                    left = glyphLeft;
                    right = glyphRight;
                    hasGlyph = true;
                }
                else
                {
                    left = MathF.Min(left, glyphLeft);
                    right = MathF.Max(right, glyphRight);
                }
            }

            penX += metrics?.Advance ?? 0f;
        }

        return hasGlyph ? (left, right) : (0f, penX);
    }

    private readonly record struct StandingPanelEntry(int Level, string Name, bool IsPlaceholder);
}
