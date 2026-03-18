using Content.Client.Stylesheets;
using Content.Shared.Chat;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Systems.Chat.Controls;

public sealed class ChannelSelectorItemButton : Button
{
    public const string StyleClassChatSelectorOptionButton = "ChatSelectorOptionButton";


    public readonly ChatSelectChannel Channel;

    public bool IsHidden => Parent == null;

    public ChannelSelectorItemButton(ChatSelectChannel selector)
    {
        Channel = selector;
        AddStyleClass(StyleClassChatSelectorOptionButton);

        RefreshLocalization();
    }

    public void RefreshLocalization()
    {
        Text = ChannelSelectorButton.ChannelSelectorName(Channel);

        var prefix = ChatUIController.ChannelPrefixes[Channel];
        if (prefix != default)
            Text = Loc.GetString("hud-chatbox-select-name-prefixed", ("name", Text), ("prefix", prefix));
    }
}
