using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Chat.Prototypes;

[Prototype]
public sealed partial class ChatCustomEmojiPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ResPath RsiPath;

    [DataField]
    public string? State;

    public ChatEmojiDefinition ToDefinition()
    {
        return new ChatEmojiDefinition(
            ID,
            string.Empty,
            ChatEmojiCategory.Custom,
            RsiPath,
            string.IsNullOrWhiteSpace(State) ? ID : State);
    }
}
