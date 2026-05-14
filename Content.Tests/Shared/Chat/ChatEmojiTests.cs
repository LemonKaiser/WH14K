using Content.Shared.Chat;
using NUnit.Framework;

namespace Content.Tests.Shared.Chat;

[TestFixture]
public sealed class ChatEmojiTests
{
    [Test]
    public void ReplaceAliasesConvertsKnownAliases()
    {
        Assert.That(ChatEmoji.TryGet("grinning", out var grinning), Is.True);
        Assert.That(ChatEmoji.TryGet("sob", out var sob), Is.True);

        var replaced = ChatEmoji.ReplaceAliases("Status: :grinning: :sob:");

        Assert.That(replaced, Is.EqualTo($"Status: {grinning.Value} {sob.Value}"));
    }

    [Test]
    public void ReplaceAliasesUpdatesCursorPosition()
    {
        Assert.That(ChatEmoji.TryGet("grinning", out var grinning), Is.True);

        var rewritten = ChatEmoji.ReplaceAliases("Look :grinning:", "Look :grinning:".Length, out var cursorPosition);

        Assert.That(rewritten, Is.EqualTo($"Look {grinning.Value}"));
        Assert.That(cursorPosition, Is.EqualTo(rewritten.Length));
    }

    [Test]
    public void ApplyPolicyLeavesAliasLiteralWhenChannelIsForbidden()
    {
        var processed = ChatEmoji.ApplyPolicy(":grinning:", ChatSelectChannel.OOC, ChatSelectChannel.LOOC);

        Assert.That(processed, Is.EqualTo(":grinning:"));
    }

    [Test]
    public void ApplyPolicyStripsDirectEmojiWhenChannelIsForbidden()
    {
        Assert.That(ChatEmoji.TryGet("sob", out var sob), Is.True);
        Assert.That(ChatEmoji.TryGet("heart", out var heart), Is.True);

        var processed = ChatEmoji.ApplyPolicy($"hello {sob.Value} {heart.Value} world", ChatSelectChannel.OOC, ChatSelectChannel.LOOC);

        Assert.That(processed, Is.EqualTo("hello   world"));
    }

    [Test]
    public void ApplyPolicyKeepsSupplementalEmojiWhenChannelIsAllowed()
    {
        Assert.That(ChatEmoji.TryGet("saluting_face", out var salutingFace), Is.True);

        var processed = ChatEmoji.ApplyPolicy(salutingFace.Value, ChatSelectChannel.OOC, ChatSelectChannel.OOC);

        Assert.That(processed, Is.EqualTo(salutingFace.Value));
    }

    [Test]
    public void ParseAllowedChannelsHandlesSpecialValuesAndLists()
    {
        Assert.That(ChatEmoji.ParseAllowedChannels("none"), Is.EqualTo(ChatSelectChannel.None));
        Assert.That(ChatEmoji.ParseAllowedChannels("LOOC,OOC"), Is.EqualTo(ChatSelectChannel.LOOC | ChatSelectChannel.OOC));
        Assert.That(ChatEmoji.ParseAllowedChannels("*"), Is.EqualTo(ChatEmoji.DefaultAllowedChannels));
    }

    [Test]
    public void StartsWithPotentialAliasDetectsEmojiStyleInput()
    {
        Assert.That(ChatEmoji.StartsWithPotentialAlias(":"), Is.True);
        Assert.That(ChatEmoji.StartsWithPotentialAlias("::"), Is.True);
        Assert.That(ChatEmoji.StartsWithPotentialAlias(":grinning"), Is.True);
        Assert.That(ChatEmoji.StartsWithPotentialAlias(":grinning:"), Is.True);
    }

    [Test]
    public void StartsWithPotentialAliasDistinguishesRadioStyleInput()
    {
        Assert.That(ChatEmoji.StartsWithPotentialAlias(":h hello"), Is.False);
        Assert.That(ChatEmoji.StartsWithPotentialAlias(":h"), Is.True);
        Assert.That(ChatEmoji.StartsWithPotentialAlias(";hello"), Is.False);
        Assert.That(ChatEmoji.StartsWithPotentialAlias(".h hello"), Is.False);
    }
}
