using Content.Client.UserInterface.Systems.Chat;
using Content.Shared._WH40K.Chat;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Chat;

public sealed class WH40KLocalizedChatSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KLocalizedChatEvent>(OnLocalizedChat);
    }

    private void OnLocalizedChat(WH40KLocalizedChatEvent ev)
    {
        var message = ResolveLocMessage(ev);

        var escaped = FormattedMessage.EscapeText(message);
        var wrapped = Loc.GetString("chat-manager-server-wrap-message", ("message", escaped));

        var chatMsg = new ChatMessage(
            ChatChannel.Server,
            message,
            wrapped,
            NetEntity.Invalid,
            null,
            hideChat: false,
            colorOverride: ev.ColorOverride);

        var chatController = _uiManager.GetUIController<ChatUIController>();
        chatController.ProcessChatMessage(chatMsg, speechBubble: false);
    }

    private string ResolveLocMessage(WH40KLocalizedChatEvent ev)
    {
        if (ev.LocArgs == null || ev.LocArgs.Count == 0)
            return Loc.GetString(ev.LocKey);

        var args = new (string, object)[ev.LocArgs.Count];
        var i = 0;
        foreach (var kv in ev.LocArgs)
        {
            object value;
            if (ev.ResolveArgValues && Loc.HasString(kv.Value))
                value = Loc.GetString(kv.Value);
            else
                value = kv.Value;

            args[i++] = (kv.Key, value);
        }

        return Loc.GetString(ev.LocKey, args);
    }
}
