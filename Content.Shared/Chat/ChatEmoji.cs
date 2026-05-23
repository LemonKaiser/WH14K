using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Content.Shared.Chat.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Chat;

public enum ChatEmojiCategory : byte
{
    Custom,
    Smileys,
    Nature,
    Food,
    Activities,
    Travel,
    Objects,
    Symbols,
    Flags,
}

public readonly record struct ChatEmojiDefinition(
    string Alias,
    string Value,
    ChatEmojiCategory Category,
    ResPath? RsiPath = null,
    string? RsiState = null)
{
    public bool HasDirectValue => !string.IsNullOrEmpty(Value);

    public string InsertText => HasDirectValue ? Value : $":{Alias}:";

    public ResPath TexturePath => RsiPath ?? ChatEmoji.DefaultEmojiRsiPath;

    public string TextureState => string.IsNullOrWhiteSpace(RsiState) ? Alias : RsiState;
}

public static partial class ChatEmoji
{
    public const string DefaultAllowedChannelsCVar = "Local,Whisper,Radio,LOOC,OOC,Emotes,Dead,Admin";
    public static readonly ResPath DefaultEmojiRsiPath = new("/Textures/Interface/Chat/emoji.rsi");

    public const ChatSelectChannel DefaultAllowedChannels =
        ChatSelectChannel.Local |
        ChatSelectChannel.Whisper |
        ChatSelectChannel.Radio |
        ChatSelectChannel.LOOC |
        ChatSelectChannel.OOC |
        ChatSelectChannel.Emotes |
        ChatSelectChannel.Dead |
        ChatSelectChannel.Admin;

    private static readonly ChatEmojiCategory[] BuiltInCategoryOrder =
    [
        ChatEmojiCategory.Smileys,
        ChatEmojiCategory.Nature,
        ChatEmojiCategory.Food,
        ChatEmojiCategory.Activities,
        ChatEmojiCategory.Travel,
        ChatEmojiCategory.Objects,
        ChatEmojiCategory.Symbols,
        ChatEmojiCategory.Flags,
    ];

    private static readonly Regex AliasRegex =
        new(":([a-z0-9_+-]+):", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly ChatEmojiDefinition[] Definitions =
    [
        new("grinning", "\U0001F600", ChatEmojiCategory.Smileys),
        new("grin", "\U0001F601", ChatEmojiCategory.Smileys),
        new("joy", "\U0001F602", ChatEmojiCategory.Smileys),
        new("smiley", "\U0001F603", ChatEmojiCategory.Smileys),
        new("smile", "\U0001F604", ChatEmojiCategory.Smileys),
        new("sweat_smile", "\U0001F605", ChatEmojiCategory.Smileys),
        new("laughing", "\U0001F606", ChatEmojiCategory.Smileys),
        new("innocent", "\U0001F607", ChatEmojiCategory.Smileys),
        new("wink", "\U0001F609", ChatEmojiCategory.Smileys),
        new("blush", "\U0001F60A", ChatEmojiCategory.Smileys),
        new("heart_eyes", "\U0001F60D", ChatEmojiCategory.Smileys),
        new("sunglasses", "\U0001F60E", ChatEmojiCategory.Smileys),
        new("smirk", "\U0001F60F", ChatEmojiCategory.Smileys),
        new("neutral_face", "\U0001F610", ChatEmojiCategory.Smileys),
        new("expressionless", "\U0001F611", ChatEmojiCategory.Smileys),
        new("unamused", "\U0001F612", ChatEmojiCategory.Smileys),
        new("sweat", "\U0001F613", ChatEmojiCategory.Smileys),
        new("pensive", "\U0001F614", ChatEmojiCategory.Smileys),
        new("worried", "\U0001F61F", ChatEmojiCategory.Smileys),
        new("pouting_face", "\U0001F620", ChatEmojiCategory.Smileys),
        new("rage", "\U0001F621", ChatEmojiCategory.Smileys),
        new("cry", "\U0001F622", ChatEmojiCategory.Smileys),
        new("sob", "\U0001F62D", ChatEmojiCategory.Smileys),
        new("scream", "\U0001F631", ChatEmojiCategory.Smileys),
        new("sleeping", "\U0001F634", ChatEmojiCategory.Smileys),
        new("mask", "\U0001F637", ChatEmojiCategory.Smileys),
        new("slightly_smiling_face", "\U0001F642", ChatEmojiCategory.Smileys),
        new("upside_down_face", "\U0001F643", ChatEmojiCategory.Smileys),
        new("rolling_eyes", "\U0001F644", ChatEmojiCategory.Smileys),
        new("thinking", "\U0001F914", ChatEmojiCategory.Smileys),
        new("rofl", "\U0001F923", ChatEmojiCategory.Smileys),
        new("star_struck", "\U0001F929", ChatEmojiCategory.Smileys),
        new("zany_face", "\U0001F92A", ChatEmojiCategory.Smileys),
        new("pleading_face", "\U0001F97A", ChatEmojiCategory.Smileys),
        new("smiling_face_with_tear", "\U0001F972", ChatEmojiCategory.Smileys),
        new("partying_face", "\U0001F973", ChatEmojiCategory.Smileys),
        new("saluting_face", "\U0001FAE1", ChatEmojiCategory.Smileys),
        new("wave", "\U0001F44B", ChatEmojiCategory.Smileys),
        new("thumbsup", "\U0001F44D", ChatEmojiCategory.Smileys),
        new("thumbsdown", "\U0001F44E", ChatEmojiCategory.Smileys),
        new("clap", "\U0001F44F", ChatEmojiCategory.Smileys),
        new("point_left", "\U0001F448", ChatEmojiCategory.Smileys),
        new("point_right", "\U0001F449", ChatEmojiCategory.Smileys),
        new("ok_hand", "\U0001F44C", ChatEmojiCategory.Smileys),
        new("fist", "\U0001F44A", ChatEmojiCategory.Smileys),
        new("muscle", "\U0001F4AA", ChatEmojiCategory.Smileys),
        new("pray", "\U0001F64F", ChatEmojiCategory.Smileys),
        new("handshake", "\U0001F91D", ChatEmojiCategory.Smileys),
        new("pinched_fingers", "\U0001F90C", ChatEmojiCategory.Smileys),
        new("v", "\u270C\uFE0F", ChatEmojiCategory.Smileys),
        new("writing_hand", "\u270D\uFE0F", ChatEmojiCategory.Smileys),
        new("point_up", "\u261D\uFE0F", ChatEmojiCategory.Smileys),

        new("dog", "\U0001F436", ChatEmojiCategory.Nature),
        new("cat", "\U0001F431", ChatEmojiCategory.Nature),
        new("mouse", "\U0001F42D", ChatEmojiCategory.Nature),
        new("wolf", "\U0001F43A", ChatEmojiCategory.Nature),
        new("fox", "\U0001F98A", ChatEmojiCategory.Nature),
        new("herb", "\U0001F33F", ChatEmojiCategory.Nature),
        new("four_leaf_clover", "\U0001F340", ChatEmojiCategory.Nature),
        new("rose", "\U0001F339", ChatEmojiCategory.Nature),
        new("skull", "\U0001F480", ChatEmojiCategory.Nature),
        new("ghost", "\U0001F47B", ChatEmojiCategory.Nature),
        new("alien", "\U0001F47D", ChatEmojiCategory.Nature),
        new("robot", "\U0001F916", ChatEmojiCategory.Nature),
        new("fire", "\U0001F525", ChatEmojiCategory.Nature),
        new("sparkles", "\u2728", ChatEmojiCategory.Nature),
        new("star", "\u2B50", ChatEmojiCategory.Nature),
        new("sunny", "\u2600\uFE0F", ChatEmojiCategory.Nature),
        new("moon", "\U0001F319", ChatEmojiCategory.Nature),
        new("zap", "\u26A1", ChatEmojiCategory.Nature),
        new("snowflake", "\u2744\uFE0F", ChatEmojiCategory.Nature),
        new("cloud_with_rain", "\U0001F327\uFE0F", ChatEmojiCategory.Nature),

        new("apple", "\U0001F34E", ChatEmojiCategory.Food),
        new("burger", "\U0001F354", ChatEmojiCategory.Food),
        new("pizza", "\U0001F355", ChatEmojiCategory.Food),
        new("fries", "\U0001F35F", ChatEmojiCategory.Food),
        new("taco", "\U0001F32E", ChatEmojiCategory.Food),
        new("coffee", "\u2615", ChatEmojiCategory.Food),
        new("tea", "\U0001F375", ChatEmojiCategory.Food),
        new("beer", "\U0001F37A", ChatEmojiCategory.Food),
        new("tropical_drink", "\U0001F379", ChatEmojiCategory.Food),
        new("birthday", "\U0001F382", ChatEmojiCategory.Food),
        new("icecream", "\U0001F366", ChatEmojiCategory.Food),
        new("lollipop", "\U0001F36D", ChatEmojiCategory.Food),
        new("bowl_with_spoon", "\U0001F963", ChatEmojiCategory.Food),

        new("soccer", "\u26BD", ChatEmojiCategory.Activities),
        new("basketball", "\U0001F3C0", ChatEmojiCategory.Activities),
        new("football", "\U0001F3C8", ChatEmojiCategory.Activities),
        new("baseball", "\u26BE", ChatEmojiCategory.Activities),
        new("video_game", "\U0001F3AE", ChatEmojiCategory.Activities),
        new("game_die", "\U0001F3B2", ChatEmojiCategory.Activities),
        new("dart", "\U0001F3AF", ChatEmojiCategory.Activities),
        new("trophy", "\U0001F3C6", ChatEmojiCategory.Activities),
        new("medal", "\U0001F3C5", ChatEmojiCategory.Activities),
        new("musical_note", "\U0001F3B5", ChatEmojiCategory.Activities),
        new("microphone", "\U0001F3A4", ChatEmojiCategory.Activities),
        new("art", "\U0001F3A8", ChatEmojiCategory.Activities),
        new("performing_arts", "\U0001F3AD", ChatEmojiCategory.Activities),

        new("car", "\U0001F697", ChatEmojiCategory.Travel),
        new("taxi", "\U0001F695", ChatEmojiCategory.Travel),
        new("bus", "\U0001F68C", ChatEmojiCategory.Travel),
        new("train", "\U0001F686", ChatEmojiCategory.Travel),
        new("airplane", "\u2708\uFE0F", ChatEmojiCategory.Travel),
        new("rocket", "\U0001F680", ChatEmojiCategory.Travel),
        new("ship", "\U0001F6A2", ChatEmojiCategory.Travel),
        new("satellite", "\U0001F6F0\uFE0F", ChatEmojiCategory.Travel),
        new("bicycle", "\U0001F6B2", ChatEmojiCategory.Travel),
        new("motorcycle", "\U0001F3CD\uFE0F", ChatEmojiCategory.Travel),

        new("package", "\U0001F4E6", ChatEmojiCategory.Objects),
        new("gift", "\U0001F381", ChatEmojiCategory.Objects),
        new("bulb", "\U0001F4A1", ChatEmojiCategory.Objects),
        new("wrench", "\U0001F527", ChatEmojiCategory.Objects),
        new("gear", "\u2699\uFE0F", ChatEmojiCategory.Objects),
        new("hammer", "\U0001F528", ChatEmojiCategory.Objects),
        new("hammer_and_wrench", "\U0001F6E0\uFE0F", ChatEmojiCategory.Objects),
        new("computer", "\U0001F4BB", ChatEmojiCategory.Objects),
        new("lock", "\U0001F512", ChatEmojiCategory.Objects),
        new("key", "\U0001F511", ChatEmojiCategory.Objects),
        new("moneybag", "\U0001F4B0", ChatEmojiCategory.Objects),
        new("gem", "\U0001F48E", ChatEmojiCategory.Objects),
        new("book", "\U0001F4D6", ChatEmojiCategory.Objects),
        new("pill", "\U0001F48A", ChatEmojiCategory.Objects),

        new("heart", "\u2764\uFE0F", ChatEmojiCategory.Symbols),
        new("orange_heart", "\U0001F9E1", ChatEmojiCategory.Symbols),
        new("yellow_heart", "\U0001F49B", ChatEmojiCategory.Symbols),
        new("green_heart", "\U0001F49A", ChatEmojiCategory.Symbols),
        new("blue_heart", "\U0001F499", ChatEmojiCategory.Symbols),
        new("purple_heart", "\U0001F49C", ChatEmojiCategory.Symbols),
        new("broken_heart", "\U0001F494", ChatEmojiCategory.Symbols),
        new("100", "\U0001F4AF", ChatEmojiCategory.Symbols),
        new("boom", "\U0001F4A5", ChatEmojiCategory.Symbols),
        new("warning", "\u26A0\uFE0F", ChatEmojiCategory.Symbols),
        new("white_check_mark", "\u2705", ChatEmojiCategory.Symbols),
        new("x", "\u274C", ChatEmojiCategory.Symbols),
        new("question", "\u2753", ChatEmojiCategory.Symbols),
        new("grey_question", "\u2754", ChatEmojiCategory.Symbols),
        new("exclamation", "\u2757", ChatEmojiCategory.Symbols),
        new("grey_exclamation", "\u2755", ChatEmojiCategory.Symbols),
        new("recycle", "\u267B\uFE0F", ChatEmojiCategory.Symbols),
        new("peace", "\u262E\uFE0F", ChatEmojiCategory.Symbols),
        new("biohazard", "\u2623\uFE0F", ChatEmojiCategory.Symbols),
        new("radioactive", "\u2622\uFE0F", ChatEmojiCategory.Symbols),
        new("infinity", "\u267E\uFE0F", ChatEmojiCategory.Symbols),
        new("zzz", "\U0001F4A4", ChatEmojiCategory.Symbols),

        new("triangular_flag_on_post", "\U0001F6A9", ChatEmojiCategory.Flags),
        new("checkered_flag", "\U0001F3C1", ChatEmojiCategory.Flags),
        new("black_flag", "\U0001F3F4", ChatEmojiCategory.Flags),
        new("white_flag", "\U0001F3F3\uFE0F", ChatEmojiCategory.Flags),
        new("rainbow_flag", "\U0001F3F3\uFE0F\u200D\U0001F308", ChatEmojiCategory.Flags),
        new("pirate_flag", "\U0001F3F4\u200D\u2620\uFE0F", ChatEmojiCategory.Flags),
        new("flag_ru", "\U0001F1F7\U0001F1FA", ChatEmojiCategory.Flags),
        new("flag_us", "\U0001F1FA\U0001F1F8", ChatEmojiCategory.Flags),
        new("flag_gb", "\U0001F1EC\U0001F1E7", ChatEmojiCategory.Flags),
        new("flag_fr", "\U0001F1EB\U0001F1F7", ChatEmojiCategory.Flags),
        new("flag_de", "\U0001F1E9\U0001F1EA", ChatEmojiCategory.Flags),
        new("flag_ua", "\U0001F1FA\U0001F1E6", ChatEmojiCategory.Flags),
    ];

    private static readonly Dictionary<string, ChatEmojiDefinition> AliasMap = BuildAliasMap();
    private static readonly HashSet<int> KnownEmojiRunes = BuildKnownEmojiRuneSet();

    public static IReadOnlyList<ChatEmojiDefinition> All => Definitions;

    public static IEnumerable<ChatEmojiCategory> GetCategoryOrder(IPrototypeManager? prototypeManager = null)
    {
        if (HasCustomEmojis(prototypeManager))
            yield return ChatEmojiCategory.Custom;

        foreach (var category in BuiltInCategoryOrder)
        {
            yield return category;
        }
    }

    public static bool HasCustomEmojis(IPrototypeManager? prototypeManager)
    {
        return prototypeManager != null && prototypeManager.EnumeratePrototypes<ChatCustomEmojiPrototype>().Any();
    }

    public static IEnumerable<ChatEmojiDefinition> EnumerateCategory(ChatEmojiCategory category, IPrototypeManager? prototypeManager = null)
    {
        if (category == ChatEmojiCategory.Custom)
        {
            if (prototypeManager == null)
                yield break;

            foreach (var definition in EnumerateCustomDefinitions(prototypeManager))
            {
                yield return definition;
            }

            yield break;
        }

        foreach (var definition in Definitions)
        {
            if (definition.Category == category)
                yield return definition;
        }
    }

    public static IEnumerable<ChatEmojiDefinition> EnumerateAll(IPrototypeManager? prototypeManager = null)
    {
        foreach (var definition in Definitions)
        {
            yield return definition;
        }

        if (prototypeManager == null)
            yield break;

        foreach (var definition in EnumerateCustomDefinitions(prototypeManager))
        {
            yield return definition;
        }
    }

    public static bool TryGet(string alias, out ChatEmojiDefinition definition)
    {
        return AliasMap.TryGetValue(alias, out definition);
    }

    public static bool TryGet(string alias, IPrototypeManager? prototypeManager, out ChatEmojiDefinition definition)
    {
        if (TryGet(alias, out definition))
            return true;

        if (prototypeManager != null)
        {
            foreach (var customEmoji in prototypeManager.EnumeratePrototypes<ChatCustomEmojiPrototype>())
            {
                if (!string.Equals(customEmoji.ID, alias, StringComparison.OrdinalIgnoreCase))
                    continue;

                definition = customEmoji.ToDefinition();
                return true;
            }
        }

        definition = default;
        return false;
    }

    public static ChatEmojiDefinition GetCategoryIcon(ChatEmojiCategory category)
    {
        return TryGet(GetCategoryIconAlias(category), out var definition)
            ? definition
            : Definitions[0];
    }

    public static string GetCategoryIconAlias(ChatEmojiCategory category)
    {
        return category switch
        {
            ChatEmojiCategory.Custom => "grinning",
            ChatEmojiCategory.Smileys => "grinning",
            ChatEmojiCategory.Nature => "herb",
            ChatEmojiCategory.Food => "coffee",
            ChatEmojiCategory.Activities => "video_game",
            ChatEmojiCategory.Travel => "bicycle",
            ChatEmojiCategory.Objects => "hammer_and_wrench",
            ChatEmojiCategory.Symbols => "heart",
            ChatEmojiCategory.Flags => "triangular_flag_on_post",
            _ => "grinning",
        };
    }

    public static ChatSelectChannel ParseAllowedChannels(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultAllowedChannels;

        var trimmed = raw.Trim();
        if (string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase) || trimmed == "*")
            return DefaultAllowedChannels;

        if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase))
            return ChatSelectChannel.None;

        var resolved = ChatSelectChannel.None;
        var tokens = trimmed.Split([',', ';', '|', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (string.Equals(token, "all", StringComparison.OrdinalIgnoreCase) || token == "*")
                return DefaultAllowedChannels;

            if (string.Equals(token, "none", StringComparison.OrdinalIgnoreCase))
                return ChatSelectChannel.None;

            if (!Enum.TryParse<ChatSelectChannel>(token, true, out var parsed))
                continue;

            resolved |= parsed & DefaultAllowedChannels;
        }

        return resolved == ChatSelectChannel.None ? DefaultAllowedChannels : resolved;
    }

    public static bool IsAllowed(ChatSelectChannel allowedChannels, ChatSelectChannel channel)
    {
        if (channel == ChatSelectChannel.None || channel == ChatSelectChannel.Console)
            return false;

        return (allowedChannels & channel) != 0;
    }

    public static bool IsAllowed(ChatSelectChannel allowedChannels, ChatChannel channel)
    {
        return TryMapChannel(channel, out var selectChannel) && IsAllowed(allowedChannels, selectChannel);
    }

    public static string ApplyPolicy(string text, ChatSelectChannel channel, ChatSelectChannel allowedChannels)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return IsAllowed(allowedChannels, channel)
            ? ReplaceAliases(text)
            : StripDirectEmoji(text);
    }

    public static string ReplaceAliases(string text)
    {
        return ReplaceAliases(text, prototypeManager: null);
    }

    public static string ReplaceAliases(string text, IPrototypeManager? prototypeManager)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return AliasRegex.Replace(text, match =>
        {
            var alias = match.Groups[1].Value;
            if (!TryGet(alias, prototypeManager, out var emoji) || !emoji.HasDirectValue)
                return match.Value;

            return emoji.Value;
        });
    }

    public static string ReplaceAliases(string text, int cursorPosition, out int newCursorPosition)
    {
        return ReplaceAliases(text, cursorPosition, prototypeManager: null, out newCursorPosition);
    }

    public static string ReplaceAliases(string text, int cursorPosition, IPrototypeManager? prototypeManager, out int newCursorPosition)
    {
        newCursorPosition = Math.Clamp(cursorPosition, 0, text.Length);

        if (string.IsNullOrEmpty(text))
            return text;

        var matches = AliasRegex.Matches(text);
        if (matches.Count == 0)
            return text;

        var builder = new StringBuilder(text.Length);
        var sourceIndex = 0;
        var changed = false;

        foreach (Match match in matches)
        {
            builder.Append(text, sourceIndex, match.Index - sourceIndex);
            sourceIndex = match.Index + match.Length;

            if (!TryGet(match.Groups[1].Value, prototypeManager, out var emoji) || !emoji.HasDirectValue)
            {
                builder.Append(match.Value);
                continue;
            }

            changed = true;
            builder.Append(emoji.Value);

            var matchEnd = match.Index + match.Length;
            if (newCursorPosition >= matchEnd)
            {
                newCursorPosition += emoji.Value.Length - match.Length;
            }
            else if (newCursorPosition > match.Index)
            {
                newCursorPosition = builder.Length;
            }
        }

        if (!changed)
            return text;

        if (sourceIndex < text.Length)
            builder.Append(text, sourceIndex, text.Length - sourceIndex);

        newCursorPosition = Math.Clamp(newCursorPosition, 0, builder.Length);
        return builder.ToString();
    }

    public static bool StartsWithPotentialAlias(string text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != ':')
            return false;

        if (text.Length == 1)
            return true;

        if (text[1] == ':')
            return true;

        var sawAliasCharacter = false;
        for (var index = 1; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch == ':')
                return sawAliasCharacter;

            if (char.IsWhiteSpace(ch) || !IsValidAliasCharacter(ch))
                return false;

            sawAliasCharacter = true;
        }

        return sawAliasCharacter;
    }

    public static bool TryMatchAlias(string text, int index, IPrototypeManager? prototypeManager, out ChatEmojiDefinition definition, out int consumedLength)
    {
        definition = default;
        consumedLength = 0;

        if (index < 0 || index >= text.Length || text[index] != ':')
            return false;

        var end = text.IndexOf(':', index + 1);
        if (end <= index + 1)
            return false;

        var aliasSpan = text.AsSpan(index + 1, end - index - 1);
        if (!IsValidAlias(aliasSpan))
            return false;

        var alias = aliasSpan.ToString();
        if (!TryGet(alias, prototypeManager, out definition))
            return false;

        consumedLength = end - index + 1;
        return true;
    }

    public static string StripDirectEmoji(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsEmojiRune(rune))
                continue;

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    private static bool IsEmojiRune(Rune rune)
    {
        return IsSupplementalEmojiRune(rune) ||
               IsRegionalIndicator(rune) ||
               IsVariationSelector(rune) ||
               IsEmojiModifier(rune) ||
               IsZeroWidthJoiner(rune) ||
               IsKeycap(rune) ||
               KnownEmojiRunes.Contains(rune.Value);
    }

    private static bool IsSupplementalEmojiRune(Rune rune)
    {
        return rune.Value is >= 0x1F000 and <= 0x1FAFF;
    }

    private static bool IsRegionalIndicator(Rune rune)
    {
        return rune.Value is >= 0x1F1E6 and <= 0x1F1FF;
    }

    private static bool IsVariationSelector(Rune rune)
    {
        return rune.Value is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF;
    }

    private static bool IsEmojiModifier(Rune rune)
    {
        return rune.Value is >= 0x1F3FB and <= 0x1F3FF;
    }

    private static bool IsZeroWidthJoiner(Rune rune)
    {
        return rune.Value == 0x200D;
    }

    private static bool IsKeycap(Rune rune)
    {
        return rune.Value == 0x20E3;
    }

    public static bool TryMapChannel(ChatChannel channel, out ChatSelectChannel selectChannel)
    {
        selectChannel = channel switch
        {
            ChatChannel.Local => ChatSelectChannel.Local,
            ChatChannel.Whisper => ChatSelectChannel.Whisper,
            ChatChannel.Radio => ChatSelectChannel.Radio,
            ChatChannel.LOOC => ChatSelectChannel.LOOC,
            ChatChannel.OOC => ChatSelectChannel.OOC,
            ChatChannel.Emotes => ChatSelectChannel.Emotes,
            ChatChannel.Dead => ChatSelectChannel.Dead,
            ChatChannel.Admin or ChatChannel.AdminAlert or ChatChannel.AdminChat => ChatSelectChannel.Admin,
            _ => ChatSelectChannel.None
        };

        return selectChannel != ChatSelectChannel.None;
    }

    private static Dictionary<string, ChatEmojiDefinition> BuildAliasMap()
    {
        var map = new Dictionary<string, ChatEmojiDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in Definitions)
        {
            map[definition.Alias] = definition;
        }

        return map;
    }

    private static HashSet<int> BuildKnownEmojiRuneSet()
    {
        var values = new HashSet<int>();

        foreach (var definition in Definitions)
        {
            foreach (var rune in definition.Value.EnumerateRunes())
            {
                if (IsVariationSelector(rune) || IsZeroWidthJoiner(rune) || IsKeycap(rune))
                    continue;

                values.Add(rune.Value);
            }
        }

        return values;
    }

    private static bool IsValidAlias(ReadOnlySpan<char> alias)
    {
        if (alias.Length == 0)
            return false;

        foreach (var ch in alias)
        {
            if (IsValidAliasCharacter(ch))
                continue;

            return false;
        }

        return true;
    }

    private static bool IsValidAliasCharacter(char ch)
    {
        return ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '+' or '-';
    }

    private static IEnumerable<ChatEmojiDefinition> EnumerateCustomDefinitions(IPrototypeManager prototypeManager)
    {
        foreach (var customEmoji in prototypeManager.EnumeratePrototypes<ChatCustomEmojiPrototype>().OrderBy(proto => proto.ID, StringComparer.OrdinalIgnoreCase))
        {
            yield return customEmoji.ToDefinition();
        }
    }
}
