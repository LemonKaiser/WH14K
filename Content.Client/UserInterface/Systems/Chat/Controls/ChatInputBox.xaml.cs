using Content.Shared.Chat;
using Content.Shared.Input;
using Robust.Client.UserInterface.Controls;
using System;

namespace Content.Client.UserInterface.Systems.Chat.Controls;

[Virtual]
public class ChatInputBox : PanelContainer
{
    public const string StyleClassChatPanel = "ChatPanel";
    public const string StyleClassChatLineEdit = "ChatLineEdit";
    public const string StyleClassChatFilterOptionButton = "ChatFilterOptionButton";

    public readonly EmojiPickerButton EmojiButton;
    public readonly ChannelSelectorButton ChannelSelector;
    public readonly HistoryLineEdit Input;
    public readonly ChannelFilterButton FilterButton;
    protected readonly BoxContainer Container;
    protected ChatChannel ActiveChannel { get; private set; } = ChatChannel.Local;
    private bool _inputLocked;
    private bool _emojiAllowed = true;
    private string? _lockedPlaceholder;
    private string? _lockedToolTip;

    public ChatInputBox()
    {
        Container = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4
        };
        AddChild(Container);

        EmojiButton = new EmojiPickerButton
        {
            Name = "EmojiButton",
            StyleClasses = { StyleClassChatFilterOptionButton }
        };
        EmojiButton.OnEmojiPicked += InsertEmoji;
        Container.AddChild(EmojiButton);

        ChannelSelector = new ChannelSelectorButton
        {
            Name = "ChannelSelector",
            ToggleMode = true,
            StyleClasses = { ChannelSelectorItemButton.StyleClassChatSelectorOptionButton },
            MinWidth = 75
        };
        Container.AddChild(ChannelSelector);
        Input = new HistoryLineEdit
        {
            Name = "Input",
            PlaceHolder = GetChatboxInfoPlaceholder(),
            HorizontalExpand = true,
            StyleClasses = { StyleClassChatLineEdit }
        };
        Container.AddChild(Input);
        FilterButton = new ChannelFilterButton
        {
            Name = "FilterButton",
            StyleClasses = { StyleClassChatFilterOptionButton }
        };
        Container.AddChild(FilterButton);
        AddStyleClass(StyleClassChatPanel);
        ChannelSelector.OnChannelSelect += UpdateActiveChannel;
    }

    private void UpdateActiveChannel(ChatSelectChannel selectedChannel)
    {
        ActiveChannel = (ChatChannel) selectedChannel;
    }

    private void InsertEmoji(string emoji)
    {
        if (_inputLocked)
            return;

        Input.InsertAtCursor(emoji);
        Input.GrabKeyboardFocus();
    }

    private static string GetChatboxInfoPlaceholder()
    {
        return (BoundKeyHelper.IsBound(ContentKeyFunctions.FocusChat),
                BoundKeyHelper.IsBound(ContentKeyFunctions.CycleChatChannelForward)) switch
            {
                (true, true) => Loc.GetString("hud-chatbox-info",
                    ("talk-key", BoundKeyHelper.ShortKeyName(ContentKeyFunctions.FocusChat)),
                    ("cycle-key", BoundKeyHelper.ShortKeyName(ContentKeyFunctions.CycleChatChannelForward))),
                (true, false) => Loc.GetString("hud-chatbox-info-talk",
                    ("talk-key", BoundKeyHelper.ShortKeyName(ContentKeyFunctions.FocusChat))),
                (false, true) => Loc.GetString("hud-chatbox-info-cycle",
                    ("cycle-key", BoundKeyHelper.ShortKeyName(ContentKeyFunctions.CycleChatChannelForward))),
                (false, false) => Loc.GetString("hud-chatbox-info-unbound")
            };
    }

    public void RefreshLocalization()
    {
        Input.PlaceHolder = _lockedPlaceholder ?? GetChatboxInfoPlaceholder();
        EmojiButton.RefreshLocalization();
        ChannelSelector.RefreshLocalization();
        FilterButton.RefreshLocalization();
    }

    public void SetEmojiAllowed(bool allowed)
    {
        _emojiAllowed = allowed;
        ApplyEmojiButtonState();
    }

    private void ApplyEmojiButtonState()
    {
        EmojiButton.SetAvailable(_emojiAllowed);

        if (!_inputLocked)
            return;

        EmojiButton.Disabled = true;
        EmojiButton.Popup.Close();
    }

    public void SetInputLockState(bool locked, string? placeholder = null, string? toolTip = null)
    {
        var nextPlaceholder = locked ? placeholder ?? string.Empty : null;
        var nextToolTip = locked ? toolTip : null;
        var changed =
            _inputLocked != locked ||
            !string.Equals(_lockedPlaceholder, nextPlaceholder, StringComparison.Ordinal) ||
            !string.Equals(_lockedToolTip, nextToolTip, StringComparison.Ordinal);

        if (!changed)
        {
            ApplyEmojiButtonState();
            return;
        }

        var lockingNow = locked && !_inputLocked;
        _inputLocked = locked;
        _lockedPlaceholder = nextPlaceholder;
        _lockedToolTip = nextToolTip;

        Input.PlaceHolder = _lockedPlaceholder ?? GetChatboxInfoPlaceholder();
        Input.ToolTip = _lockedToolTip;
        Input.Editable = !locked;
        ApplyEmojiButtonState();
        Input.InvalidateMeasure();
        Input.InvalidateArrange();

        if (lockingNow)
        {
            Input.Clear();
            Input.ReleaseKeyboardFocus();
        }
    }

    [Obsolete]
    protected override void Dispose(bool disposing)
    {
#pragma warning disable CS0618
        base.Dispose(disposing);
#pragma warning restore CS0618

        if (!disposing)
            return;

        EmojiButton.OnEmojiPicked -= InsertEmoji;
    }
}
