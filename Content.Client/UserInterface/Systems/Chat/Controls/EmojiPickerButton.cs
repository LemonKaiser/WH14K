using System.Numerics;
using Content.Client.UserInterface.Systems.Chat.RichText;
using Content.Shared.Chat;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;

namespace Content.Client.UserInterface.Systems.Chat.Controls;

public sealed class EmojiPickerButton : ChatPopupButton<EmojiPickerPopup>
{
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;

    private const float PopupMargin = 8f;

    public event Action<string>? OnEmojiPicked;

    public EmojiPickerButton()
    {
        IoCManager.InjectDependencies(this);

        MinWidth = 34f;
        ToolTip = Loc.GetString("hud-chatbox-emoji-button-tooltip");
        AddChild(ChatEmojiRichText.CreateCategoryTextureRect(_resourceCache, ChatEmojiCategory.Smileys));
        Popup.OnEmojiPicked += HandleEmojiPicked;
    }

    protected override UIBox2 GetPopupPosition()
    {
        var globalPos = GlobalPosition;
        var popupWidth = EmojiPickerPopup.PopupWidth;
        var popupHeight = EmojiPickerPopup.PopupHeight;
        var rootSize = _uiManager.RootControl.Size;
        var maxX = Math.Max(0f, rootSize.X - popupWidth);
        var x = Math.Clamp(globalPos.X, 0f, maxX);
        var y = Math.Max(0f, globalPos.Y - popupHeight - PopupMargin);

        return UIBox2.FromDimensions(new Vector2(x, y), new Vector2(popupWidth, popupHeight));
    }

    public void SetAvailable(bool available)
    {
        Visible = available;
        Disabled = !available;

        if (!available && Popup.Visible)
            Popup.Close();
    }

    public void RefreshLocalization()
    {
        ToolTip = Loc.GetString("hud-chatbox-emoji-button-tooltip");
        Popup.RefreshLocalization();
    }

    private void HandleEmojiPicked(string emoji)
    {
        Popup.Close();
        OnEmojiPicked?.Invoke(emoji);
    }

    [Obsolete]
    protected override void Dispose(bool disposing)
    {
#pragma warning disable CS0618
        base.Dispose(disposing);
#pragma warning restore CS0618

        if (!disposing)
            return;

        Popup.OnEmojiPicked -= HandleEmojiPicked;
    }
}
