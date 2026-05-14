using System.Diagnostics.CodeAnalysis;
using Content.Shared.Chat;
using JetBrains.Annotations;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.RichText;

[UsedImplicitly]
public sealed class ChatEmojiTag : IMarkupTagHandler
{
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public string Name => ChatEmojiRichText.EmojiMarkupTag;

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;

        if (!node.Attributes.TryGetValue("alias", out var aliasParam) ||
            !aliasParam.TryGetString(out var alias) ||
            !ChatEmoji.TryGet(alias, _prototypeManager, out var emoji))
        {
            return false;
        }

        control = ChatEmojiRichText.CreateInlineTextureRect(_resourceCache, emoji);
        return true;
    }
}
