using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Systems.Chat.RichText;
using Content.Shared.Chat;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.Controls;

public sealed class EmojiPickerPopup : Popup
{
    public const float PopupWidth = 540f;
    public const float PopupHeight = 440f;
    public const int EmojiColumns = 6;
    private const float EmojiButtonSize = 48f;

    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly Color PanelBackgroundColor = Color.FromHex("#181B22");
    private static readonly Color RailBackgroundColor = Color.FromHex("#121419");
    private static readonly Color ContentBackgroundColor = Color.FromHex("#141721");
    private static readonly Color BorderColor = Color.FromHex("#6A5530");
    private static readonly Color HeaderTextColor = Color.FromHex("#D7B65A");
    private static readonly Color PreviewTextColor = Color.FromHex("#E6DEC7");

    private readonly Dictionary<ChatEmojiCategory, Button> _categoryButtons = new();
    private readonly BoxContainer _categoryBox;
    private readonly GridContainer _emojiGrid;
    private readonly Label _headerLabel;
    private readonly RichTextLabel _previewLabel;
    private readonly PanelContainer _panel;
    private readonly PanelContainer _categoryRail;
    private readonly PanelContainer _emojiPanel;
    private readonly ButtonGroup _categoryGroup = new(false);
    private ChatEmojiCategory _selectedCategory = ChatEmojiCategory.Smileys;
    private bool _openedOnce;

    public event Action<string>? OnEmojiPicked;

    public EmojiPickerPopup()
    {
        IoCManager.InjectDependencies(this);

        MinSize = new Vector2(PopupWidth, PopupHeight);

        _panel = new PanelContainer
        {
            MinSize = MinSize,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = PanelBackgroundColor,
                BorderColor = BorderColor,
                BorderThickness = new Thickness(1),
            }
        };
        AddChild(_panel);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(8),
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        _panel.AddChild(root);

        _categoryRail = new PanelContainer
        {
            MinWidth = 42f,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = RailBackgroundColor,
                BorderColor = BorderColor.WithAlpha(0.8f),
                BorderThickness = new Thickness(1),
            }
        };
        root.AddChild(_categoryRail);

        _categoryBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(4),
        };
        _categoryRail.AddChild(_categoryBox);

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        root.AddChild(content);

        _headerLabel = new Label
        {
            Margin = new Thickness(4, 0, 4, 0),
            FontColorOverride = HeaderTextColor,
        };
        content.AddChild(_headerLabel);

        var emojiScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            ReserveScrollbarSpace = true,
        };
        content.AddChild(emojiScroll);

        _emojiPanel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = ContentBackgroundColor,
                BorderColor = BorderColor.WithAlpha(0.8f),
                BorderThickness = new Thickness(1),
            }
        };
        emojiScroll.AddChild(_emojiPanel);

        _emojiGrid = new GridContainer
        {
            Columns = EmojiColumns,
            HSeparationOverride = 10,
            VSeparationOverride = 10,
            Margin = new Thickness(10, 10, 14, 10),
        };
        _emojiPanel.AddChild(_emojiGrid);

        _previewLabel = new RichTextLabel
        {
            Margin = new Thickness(4, 0, 4, 0),
            ModulateSelfOverride = PreviewTextColor,
        };
        content.AddChild(_previewLabel);

        OnPopupOpen += HandlePopupOpen;

        RebuildCategoryButtons();
        _selectedCategory = GetDefaultCategory();
        SelectCategory(_selectedCategory);
    }

    public void RefreshLocalization()
    {
        RebuildCategoryButtons();
        SelectCategory(_selectedCategory);
    }

    private void SelectCategory(ChatEmojiCategory category)
    {
        if (!_categoryButtons.ContainsKey(category))
            category = GetDefaultCategory();

        _selectedCategory = category;
        _headerLabel.Text = GetCategoryName(category);
        _previewLabel.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("hud-chatbox-emoji-preview-empty")), tagsAllowed: null);

        if (_categoryButtons.TryGetValue(category, out var button))
            button.Pressed = true;

        _emojiGrid.RemoveAllChildren();

        foreach (var emoji in ChatEmoji.EnumerateCategory(category, _prototypeManager))
        {
            var emojiButton = new Button
            {
                MinSize = new Vector2(EmojiButtonSize, EmojiButtonSize),
                ToolTip = $":{emoji.Alias}:",
            };

            emojiButton.AddChild(ChatEmojiRichText.CreatePickerTextureRect(_resourceCache, emoji));
            emojiButton.OnPressed += _ => OnEmojiPicked?.Invoke(emoji.InsertText);
            emojiButton.OnMouseEntered += _ => _previewLabel.SetMessage(ChatEmojiRichText.BuildPreviewMessage(emoji), tagsAllowed: null);
            _emojiGrid.AddChild(emojiButton);
        }
    }

    private void HandlePopupOpen()
    {
        RebuildCategoryButtons();

        if (!_openedOnce || !_categoryButtons.ContainsKey(_selectedCategory))
            _selectedCategory = GetDefaultCategory();

        _openedOnce = true;
        SelectCategory(_selectedCategory);
    }

    private void RebuildCategoryButtons()
    {
        _categoryButtons.Clear();
        _categoryBox.RemoveAllChildren();

        foreach (var category in ChatEmoji.GetCategoryOrder(_prototypeManager))
        {
            var button = new Button
            {
                ToggleMode = true,
                Group = _categoryGroup,
                MinSize = new Vector2(32f, 32f),
                ToolTip = GetCategoryName(category),
            };

            button.AddChild(ChatEmojiRichText.CreateCategoryTextureRect(_resourceCache, GetCategoryIcon(category)));
            button.OnPressed += _ => SelectCategory(category);
            _categoryButtons[category] = button;
            _categoryBox.AddChild(button);
        }
    }

    private ChatEmojiDefinition GetCategoryIcon(ChatEmojiCategory category)
    {
        if (category != ChatEmojiCategory.Custom)
            return ChatEmoji.GetCategoryIcon(category);

        var custom = ChatEmoji.EnumerateCategory(ChatEmojiCategory.Custom, _prototypeManager).ToList();
        if (custom.Count == 0)
            return ChatEmoji.GetCategoryIcon(ChatEmojiCategory.Smileys);

        return _random.Pick(custom);
    }

    private ChatEmojiCategory GetDefaultCategory()
    {
        return ChatEmoji.HasCustomEmojis(_prototypeManager)
            ? ChatEmojiCategory.Custom
            : ChatEmojiCategory.Smileys;
    }

    private static string GetCategoryName(ChatEmojiCategory category)
    {
        return Loc.GetString($"hud-chatbox-emoji-category-{category}");
    }
}
