using System.Numerics;
using System.Linq;
using System.Text;
using Content.Shared.Chat;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.RichText;

public static class ChatEmojiRichText
{
    public const string EmojiMarkupTag = "chatemoji";

    private static readonly Dictionary<char, EmojiMatchPattern[]> DefinitionsByFirstChar = BuildDefinitionsByFirstChar();

    private const float InlineEmojiScale = 20f / 72f;
    private const float PickerEmojiScale = 24f / 72f;
    private const float CategoryEmojiScale = 22f / 72f;

    public static FormattedMessage BuildChatLine(string markup, Color color, bool allowAliasMarkup, IPrototypeManager? prototypeManager = null)
    {
        var parsed = FormattedMessage.FromMarkupOrThrow(markup);
        var emojiDisplay = ReplaceEmojiText(parsed, allowAliasMarkup, prototypeManager);

        var wrapped = new FormattedMessage(emojiDisplay.Count + 2);
        wrapped.PushColor(color);
        wrapped.AddMessage(emojiDisplay);
        wrapped.Pop();
        return wrapped;
    }

    public static FormattedMessage ReplaceEmojiText(FormattedMessage source, bool allowAliasMarkup = true, IPrototypeManager? prototypeManager = null)
    {
        var builder = new StringBuilder(source.ToMarkup().Length + 32);

        foreach (var node in source)
        {
            if (node.Name == null && node.Value.TryGetString(out var text) && !string.IsNullOrEmpty(text))
            {
                AppendTextWithEmojiMarkup(builder, text, allowAliasMarkup, prototypeManager);
                continue;
            }

            builder.Append(node.ToString());
        }

        return builder.Length == 0
            ? FormattedMessage.Empty
            : FormattedMessage.FromMarkupOrThrow(builder.ToString());
    }

    public static TextureRect CreateInlineTextureRect(IResourceCache resourceCache, ChatEmojiDefinition emoji)
    {
        return CreateTextureRect(resourceCache, emoji, InlineEmojiScale, 25f, new Thickness(1f, 2f, 1f, 2f));
    }

    public static TextureRect CreatePickerTextureRect(IResourceCache resourceCache, ChatEmojiDefinition emoji)
    {
        return CreateTextureRect(resourceCache, emoji, PickerEmojiScale, 28f, new Thickness(5f));
    }

    public static TextureRect CreateCategoryTextureRect(IResourceCache resourceCache, ChatEmojiCategory category)
    {
        return CreateTextureRect(resourceCache, ChatEmoji.GetCategoryIcon(category), CategoryEmojiScale, 22f, new Thickness(1f));
    }

    public static TextureRect CreateCategoryTextureRect(IResourceCache resourceCache, ChatEmojiDefinition emoji)
    {
        return CreateTextureRect(resourceCache, emoji, CategoryEmojiScale, 22f, new Thickness(1f));
    }

    public static FormattedMessage BuildPreviewMessage(ChatEmojiDefinition emoji)
    {
        return FormattedMessage.FromMarkupOrThrow(
            $"[{EmojiMarkupTag} alias=\"{emoji.Alias}\"/] {FormattedMessage.EscapeText($":{emoji.Alias}:")}");
    }

    private static TextureRect CreateTextureRect(
        IResourceCache resourceCache,
        ChatEmojiDefinition emoji,
        float textureScale,
        float minSize,
        Thickness margin)
    {
        return new TextureRect
        {
            Texture = ResolveTexture(resourceCache, emoji),
            TextureScale = new Vector2(textureScale, textureScale),
            Stretch = TextureRect.StretchMode.KeepCentered,
            HorizontalAlignment = Control.HAlignment.Center,
            VerticalAlignment = Control.VAlignment.Center,
            CanShrink = true,
            MinSize = new Vector2(minSize, minSize),
            Margin = margin,
        };
    }

    private static Texture ResolveTexture(IResourceCache resourceCache, ChatEmojiDefinition emoji)
    {
        if (resourceCache.TryGetResource<RSIResource>(emoji.TexturePath, out var emojiResource) &&
            emojiResource.RSI.TryGetState(emoji.TextureState, out var state))
        {
            return state.Frame0;
        }

        return resourceCache.GetFallback<TextureResource>().Texture;
    }

    private static void AppendTextWithEmojiMarkup(
        StringBuilder builder,
        string text,
        bool allowAliasMarkup,
        IPrototypeManager? prototypeManager)
    {
        var plainStart = 0;
        var index = 0;

        while (index < text.Length)
        {
            if (allowAliasMarkup &&
                ChatEmoji.TryMatchAlias(text, index, prototypeManager, out var aliasEmoji, out var aliasLength))
            {
                if (index > plainStart)
                    builder.Append(FormattedMessage.EscapeText(text.Substring(plainStart, index - plainStart)));

                builder.Append('[')
                    .Append(EmojiMarkupTag)
                    .Append(" alias=\"")
                    .Append(aliasEmoji.Alias)
                    .Append("\"/]");

                index += aliasLength;
                plainStart = index;
                continue;
            }

            if (TryMatchEmoji(text, index, out var emoji, out var emojiLength))
            {
                if (index > plainStart)
                    builder.Append(FormattedMessage.EscapeText(text.Substring(plainStart, index - plainStart)));

                builder.Append('[')
                    .Append(EmojiMarkupTag)
                    .Append(" alias=\"")
                    .Append(emoji.Alias)
                    .Append("\"/]");

                index += emojiLength;
                plainStart = index;
                continue;
            }

            index += char.IsSurrogatePair(text, index) ? 2 : 1;
        }

        if (plainStart < text.Length)
            builder.Append(FormattedMessage.EscapeText(text.Substring(plainStart)));
    }

    private static bool TryMatchEmoji(string text, int index, out ChatEmojiDefinition emoji, out int consumedLength)
    {
        emoji = default;
        consumedLength = 0;

        if (index >= text.Length)
            return false;

        if (!DefinitionsByFirstChar.TryGetValue(text[index], out var definitions))
            return false;

        foreach (var definition in definitions)
        {
            if (definition.Value.Length > text.Length - index)
                continue;

            if (string.CompareOrdinal(text, index, definition.Value, 0, definition.Value.Length) != 0)
                continue;

            emoji = definition.Emoji;
            consumedLength = definition.Value.Length;
            return true;
        }

        return false;
    }

    private static Dictionary<char, EmojiMatchPattern[]> BuildDefinitionsByFirstChar()
    {
        var grouped = new Dictionary<char, List<EmojiMatchPattern>>();

        foreach (var definition in ChatEmoji.All)
        {
            if (string.IsNullOrEmpty(definition.Value))
                continue;

            AddMatchPattern(grouped, definition.Value, definition);

            var simplifiedValue = StripVariationSelectors(definition.Value);
            if (!string.Equals(simplifiedValue, definition.Value, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(simplifiedValue))
            {
                AddMatchPattern(grouped, simplifiedValue, definition);
            }
        }

        return grouped.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .GroupBy(definition => definition.Value, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderByDescending(definition => definition.Value.Length)
                .ThenBy(definition => definition.Emoji.Alias, StringComparer.Ordinal)
                .ToArray());
    }

    private static void AddMatchPattern(
        Dictionary<char, List<EmojiMatchPattern>> grouped,
        string value,
        ChatEmojiDefinition definition)
    {
        if (string.IsNullOrEmpty(value))
            return;

        var key = value[0];
        if (!grouped.TryGetValue(key, out var list))
        {
            list = new List<EmojiMatchPattern>();
            grouped[key] = list;
        }

        list.Add(new EmojiMatchPattern(value, definition));
    }

    private static string StripVariationSelectors(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (IsVariationSelector(rune))
                continue;

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    private static bool IsVariationSelector(Rune rune)
    {
        return rune.Value is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF;
    }

    private readonly record struct EmojiMatchPattern(string Value, ChatEmojiDefinition Emoji);
}
