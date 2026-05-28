using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.Administration.Managers;
using Content.Client.Chat;
using Content.Client.Chat.Managers;
using Content.Client.Chat.TypingIndicator;
using Content.Client.Chat.UI;
using Content.Client.Examine;
using Content.Client.Gameplay;
using Content.Client.Ghost;
using Content.Client.Mind;
using Content.Client.Roles;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Damage.ForceSay;
using Content.Shared.Decals;
using Content.Shared.Input;
using Content.Shared.Radio;
using Content.Shared.Roles.RoleCodeword;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects.Components.Localization;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Replays;
using Robust.Shared.Timing;
using Robust.Shared.Utility;


namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController : UIController
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private IChatManager _manager = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IReplayRecordingManager _replayRecording = default!;

    [UISystemDependency] private readonly ExamineSystem? _examine = default;
    [UISystemDependency] private readonly GhostSystem? _ghost = default;
    [UISystemDependency] private readonly TypingIndicatorSystem? _typingIndicator = default;
    [UISystemDependency] private readonly ChatSystem? _chatSys = default;
    [UISystemDependency] private readonly TransformSystem? _transform = default;
    [UISystemDependency] private readonly JobSystem? _jobSystem = default!;
    [UISystemDependency] private readonly MindSystem? _mindSystem = default!;
    [UISystemDependency] private readonly RoleCodewordSystem? _roleCodewordSystem = default!;

    private static readonly ProtoId<ColorPalettePrototype> ChatNamePalette = "ChatNames";
    private const string RoleIconMarkupTag = "wh40kjobicon";
    private const string NameTagOpen = "[Name]";
    private const string BubbleHeaderTagOpen = "[BubbleHeader]";
    private const string JobIconNoId = "JobIconNoId";
    private const string JobIconUnknown = "JobIconUnknown";
    private const int SenderRoleIconCacheLimit = 512;
    private string[] _chatNameColors = default!;
    private bool _chatNameColorsEnabled;

    private ISawmill _sawmill = default!;

    public static readonly Dictionary<char, ChatSelectChannel> PrefixToChannel = new()
    {
        {SharedChatSystem.LocalPrefix, ChatSelectChannel.Local},
        {SharedChatSystem.WhisperPrefix, ChatSelectChannel.Whisper},
        {SharedChatSystem.ConsolePrefix, ChatSelectChannel.Console},
        {SharedChatSystem.LOOCPrefix, ChatSelectChannel.LOOC},
        {SharedChatSystem.OOCPrefix, ChatSelectChannel.OOC},
        {SharedChatSystem.EmotesPrefix, ChatSelectChannel.Emotes},
        {SharedChatSystem.EmotesAltPrefix, ChatSelectChannel.Emotes},
        {SharedChatSystem.AdminPrefix, ChatSelectChannel.Admin},
        {SharedChatSystem.RadioCommonPrefix, ChatSelectChannel.Radio},
        {SharedChatSystem.DeadPrefix, ChatSelectChannel.Dead}
    };

    public static readonly Dictionary<ChatSelectChannel, string> ChannelPrefixes = new()
    {
        {ChatSelectChannel.Local, SharedChatSystem.LocalPrefix.ToString()},
        {ChatSelectChannel.Whisper, SharedChatSystem.WhisperPrefix.ToString()},
        {ChatSelectChannel.Console, SharedChatSystem.ConsolePrefix.ToString()},
        {ChatSelectChannel.LOOC, SharedChatSystem.LOOCPrefix.ToString()},
        {ChatSelectChannel.OOC, SharedChatSystem.OOCPrefix.ToString()},
        {ChatSelectChannel.Emotes, SharedChatSystem.EmotesPrefix.ToString()},
        {ChatSelectChannel.Admin, SharedChatSystem.AdminPrefix.ToString()},
        {ChatSelectChannel.Radio, SharedChatSystem.DefaultChannelPrefix},
        {ChatSelectChannel.Dead, SharedChatSystem.DeadPrefix.ToString()}
    };

    /// <summary>
    ///     The max amount of chars allowed to fit in a single speech bubble.
    /// </summary>
    private const int SingleBubbleCharLimit = SpeechBubbleDisplayPolicy.MaxVisibleTextElements;

    /// <summary>
    ///     Base queue delay each speech bubble has.
    /// </summary>
    private const float BubbleDelayBase = 0.2f;

    /// <summary>
    ///     Factor multiplied by speech bubble char length to add to delay.
    /// </summary>
    private const float BubbleDelayFactor = 0.8f / SingleBubbleCharLimit;

    /// <summary>
    ///     The max amount of speech bubbles over a single entity at once.
    /// </summary>
    private const int SpeechBubbleCap = 4;

    /// <summary>
    ///     The max queued speech bubbles kept per entity before oldest queued entries are dropped.
    /// </summary>
    private const int SpeechBubbleQueueCap = SpeechBubbleDisplayPolicy.MaxQueuedBubblesPerEntity;

    private LayoutContainer _speechBubbleRoot = default!;

    /// <summary>
    ///     Speech bubbles that are currently visible on screen.
    ///     We track them to push them up when new ones get added.
    /// </summary>
    private readonly Dictionary<EntityUid, List<SpeechBubble>> _activeSpeechBubbles =
        new();

    /// <summary>
    ///     Speech bubbles that are to-be-sent because of the "rate limit" they have.
    /// </summary>
    private readonly Dictionary<EntityUid, SpeechBubbleQueueData> _queuedSpeechBubbles
        = new();

    private readonly HashSet<ChatBox> _chats = new();
    public IReadOnlySet<ChatBox> Chats => _chats;

    /// <summary>
    ///     The max amount of characters an entity can send in one message
    /// </summary>
    public int MaxMessageLength => _config.GetCVar(CCVars.ChatMaxMessageLength);

    /// <summary>
    /// For currently disabled chat filters,
    /// unread messages (messages received since the channel has been filtered out).
    /// </summary>
    private readonly Dictionary<ChatChannel, int> _unreadMessages = new();

    private const int MaxHistoryLength = 2048;
    public readonly List<(GameTick Tick, ChatMessage Msg)> History = new();
    private readonly Dictionary<int, string> _senderRoleIconCache = new();

    // Maintains which channels a client should be able to filter (for showing in the chatbox)
    // and select (for attempting to send on).
    // This may not always actually match with what the server will actually allow them to
    // send / receive on, it is only what the user can select in the UI. For example,
    // if a user is silenced from speaking for some reason this may still contain ChatChannel.Local, it is left up
    // to the server to handle invalid attempts to use particular channels and not send messages for
    // channels the user shouldn't be able to hear.
    //
    // Note that Command is an available selection in the chatbox channel selector,
    // which is not actually a chat channel but is always available.
    public ChatSelectChannel CanSendChannels { get; private set; }
    public ChatChannel FilterableChannels { get; private set; }
    public ChatSelectChannel SelectableChannels { get; private set; }
    private ChatSelectChannel PreferredChannel { get; set; } = ChatSelectChannel.OOC;
    private ChatSelectChannel _emojiAllowedChannels = ChatEmoji.DefaultAllowedChannels;

    public event Action<ChatSelectChannel>? CanSendChannelsChanged;
    public event Action<ChatChannel>? FilterableChannelsChanged;
    public event Action<ChatSelectChannel>? SelectableChannelsChanged;
    public event Action<ChatChannel, int?>? UnreadMessageCountsUpdated;
    public event Action<ChatMessage>? MessageAdded;

    public override void Initialize()
    {
        _sawmill = Logger.GetSawmill("chat");
        _sawmill.Level = LogLevel.Info;
        _admin.AdminStatusUpdated += UpdateChannelPermissions;
        _player.LocalPlayerAttached += OnAttachedChanged;
        _player.LocalPlayerDetached += OnAttachedChanged;
        _state.OnStateChanged += StateChanged;
        _net.RegisterNetMessage<MsgChatMessage>(OnChatMessage);
        _net.RegisterNetMessage<MsgUpdateChatMessage>(OnUpdateChatMessage);
        _net.RegisterNetMessage<MsgDeleteChatMessagesBy>(OnDeleteChatMessagesBy);
        SubscribeNetworkEvent<DamageForceSayEvent>(OnDamageForceSay);
        _config.OnValueChanged(CCVars.ChatEnableColorName, (value) => { _chatNameColorsEnabled = value; });
        _chatNameColorsEnabled = _config.GetCVar(CCVars.ChatEnableColorName);
        _config.OnValueChanged(CCVars.ChatEmojiAllowedChannels, OnEmojiAllowedChannelsChanged, true);

        _speechBubbleRoot = new LayoutContainer();

        UpdateChannelPermissions();

        _input.SetInputCommand(ContentKeyFunctions.FocusChat,
            InputCmdHandler.FromDelegate(_ => FocusChat()));

        _input.SetInputCommand(ContentKeyFunctions.FocusLocalChat,
            InputCmdHandler.FromDelegate(_ => FocusChannel(ChatSelectChannel.Local)));

        _input.SetInputCommand(ContentKeyFunctions.FocusEmote,
            InputCmdHandler.FromDelegate(_ => FocusChannel(ChatSelectChannel.Emotes)));

        _input.SetInputCommand(ContentKeyFunctions.FocusWhisperChat,
            InputCmdHandler.FromDelegate(_ => FocusChannel(ChatSelectChannel.Whisper)));

        _input.SetInputCommand(ContentKeyFunctions.FocusLOOC,
            InputCmdHandler.FromDelegate(_ => FocusChannel(ChatSelectChannel.LOOC)));

        _input.SetInputCommand(ContentKeyFunctions.FocusOOC,
            InputCmdHandler.FromDelegate(_ => FocusChannel(ChatSelectChannel.OOC)));

        _input.SetInputCommand(ContentKeyFunctions.FocusAdminChat,
            InputCmdHandler.FromDelegate(_ => FocusChannel(ChatSelectChannel.Admin)));

        _input.SetInputCommand(ContentKeyFunctions.FocusRadio,
            InputCmdHandler.FromDelegate(_ => FocusChannel(ChatSelectChannel.Radio)));

        _input.SetInputCommand(ContentKeyFunctions.FocusDeadChat,
            InputCmdHandler.FromDelegate(_ => FocusChannel(ChatSelectChannel.Dead)));

        _input.SetInputCommand(ContentKeyFunctions.FocusConsoleChat,
            InputCmdHandler.FromDelegate(_ => FocusChannel(ChatSelectChannel.Console)));

        _input.SetInputCommand(ContentKeyFunctions.CycleChatChannelForward,
            InputCmdHandler.FromDelegate(_ => CycleChatChannel(true)));

        _input.SetInputCommand(ContentKeyFunctions.CycleChatChannelBackward,
            InputCmdHandler.FromDelegate(_ => CycleChatChannel(false)));

        var gameplayStateLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad += OnScreenLoad;
        gameplayStateLoad.OnScreenUnload += OnScreenUnload;

        var nameColors = _prototypeManager.Index(ChatNamePalette).Colors.Values.ToArray();
        _chatNameColors = new string[nameColors.Length];
        for (var i = 0; i < nameColors.Length; i++)
        {
            _chatNameColors[i] = nameColors[i].ToHex();
        }

        _config.OnValueChanged(CCVars.ChatWindowOpacity, OnChatWindowOpacityChanged);

        InitializeHighlights();
    }

    public void OnScreenLoad()
    {
        SetMainChat(true);

        var viewportContainer = UIManager.ActiveScreen!.FindControl<LayoutContainer>("ViewportContainer");
        SetSpeechBubbleRoot(viewportContainer);

        SetChatWindowOpacity(_config.GetCVar(CCVars.ChatWindowOpacity));

        var chatBox = UIManager.ActiveScreen.GetWidget<ChatBox>() ?? UIManager.ActiveScreen.GetWidget<ResizableChatBox>();
        chatBox?.Relocalize();
    }

    public void OnScreenUnload()
    {
        SetMainChat(false);
    }

    private void OnChatWindowOpacityChanged(float opacity)
    {
        SetChatWindowOpacity(opacity);
    }

    private void SetChatWindowOpacity(float opacity)
    {
        var chatBox = UIManager.ActiveScreen?.GetWidget<ChatBox>() ?? UIManager.ActiveScreen?.GetWidget<ResizableChatBox>();

        var panel = chatBox?.ChatWindowPanel;
        if (panel is null)
            return;

        Color color;
        if (panel.PanelOverride is StyleBoxFlat styleBoxFlat)
            color = styleBoxFlat.BackgroundColor;
        else if (panel.TryGetStyleProperty<StyleBox>(PanelContainer.StylePropertyPanel, out var style)
                 && style is StyleBoxFlat propStyleBoxFlat)
            color = propStyleBoxFlat.BackgroundColor;
        else
            color = Color.FromHex("#25252ADD");

        panel.PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = color.WithAlpha(opacity)
        };
    }

    public void SetMainChat(bool setting)
    {
        if (UIManager.ActiveScreen == null)
        {
            return;
        }

        ChatBox chatBox;
        string? chatSizeRaw;

        switch (UIManager.ActiveScreen)
        {
            case DefaultGameScreen defaultScreen:
                chatBox = defaultScreen.ChatBox;
                chatSizeRaw = _config.GetCVar(CCVars.DefaultScreenChatSize);
                SetChatSizing(chatSizeRaw, defaultScreen, setting);
                break;
            case SeparatedChatGameScreen separatedScreen:
                chatBox = separatedScreen.ChatBox;
                chatSizeRaw = _config.GetCVar(CCVars.SeparatedScreenChatSize);
                SetChatSizing(chatSizeRaw, separatedScreen, setting);
                break;
            default:
                // this could be better?
                var maybeChat = UIManager.ActiveScreen.GetWidget<ChatBox>();

                chatBox = maybeChat ?? throw new Exception("Cannot get chat box in screen!");

                break;
        }

        chatBox.Main = setting;
    }

    private void SetChatSizing(string sizing, InGameScreen screen, bool setting)
    {
        if (!setting)
        {
            screen.OnChatResized -= StoreChatSize;
            return;
        }

        screen.OnChatResized += StoreChatSize;

        if (string.IsNullOrEmpty(sizing))
        {
            return;
        }

        var split = sizing.Split(",");

        var chatSize = new Vector2(
            float.Parse(split[0], CultureInfo.InvariantCulture),
            float.Parse(split[1], CultureInfo.InvariantCulture));


        screen.SetChatSize(chatSize);
    }

    private void StoreChatSize(Vector2 size)
    {
        if (UIManager.ActiveScreen == null)
        {
            throw new Exception("Cannot get active screen!");
        }

        var stringSize =
            $"{size.X.ToString(CultureInfo.InvariantCulture)},{size.Y.ToString(CultureInfo.InvariantCulture)}";
        switch (UIManager.ActiveScreen)
        {
            case DefaultGameScreen _:
                _config.SetCVar(CCVars.DefaultScreenChatSize, stringSize);
                break;
            case SeparatedChatGameScreen _:
                _config.SetCVar(CCVars.SeparatedScreenChatSize, stringSize);
                break;
            default:
                // do nothing
                return;
        }

        _config.SaveToFile();
    }

    private void FocusChat()
    {
        foreach (var chat in _chats)
        {
            if (!chat.Main)
                continue;

            chat.Focus();
            break;
        }
    }

    private void FocusChannel(ChatSelectChannel channel)
    {
        foreach (var chat in _chats)
        {
            if (!chat.Main)
                continue;

            chat.Focus(channel);
            break;
        }
    }

    private void CycleChatChannel(bool forward)
    {
        foreach (var chat in _chats)
        {
            if (!chat.Main)
                continue;

            chat.CycleChatChannel(forward);
            break;
        }
    }

    private void StateChanged(StateChangedEventArgs args)
    {
        _senderRoleIconCache.Clear();

        if (args.NewState is GameplayState)
        {
            PreferredChannel = ChatSelectChannel.Local;
        }

        UpdateChannelPermissions();
    }

    public void SetSpeechBubbleRoot(LayoutContainer root)
    {
        _speechBubbleRoot.Orphan();
        root.AddChild(_speechBubbleRoot);
        LayoutContainer.SetAnchorPreset(_speechBubbleRoot, LayoutContainer.LayoutPreset.Wide);
        _speechBubbleRoot.SetPositionLast();
    }

    private void OnAttachedChanged(EntityUid uid)
    {
        UpdateChannelPermissions();

        UpdateAutoFillHighlights();
    }

    private void AddSpeechBubble(ChatMessage msg, SpeechBubble.SpeechType speechType)
    {
        var ent = EntityManager.GetEntity(msg.SenderEntity);

        if (!EntityManager.EntityExists(ent))
        {
            _sawmill.Debug("Got local chat message with invalid sender entity: {0}", msg.SenderEntity);
            return;
        }

        EnqueueSpeechBubble(ent, msg, speechType);
    }

    private void CreateSpeechBubble(EntityUid entity, SpeechBubbleData speechData)
    {
        var bubble =
            SpeechBubble.CreateSpeechBubble(speechData.Type, speechData.Message, entity);

        bubble.OnDied += SpeechBubbleDied;

        if (_activeSpeechBubbles.TryGetValue(entity, out var existing))
        {
            // Push up existing bubbles above the mob's head.
            foreach (var existingBubble in existing)
            {
                existingBubble.VerticalOffset += bubble.ContentSize.Y;
            }
        }
        else
        {
            existing = new List<SpeechBubble>();
            _activeSpeechBubbles.Add(entity, existing);
        }

        existing.Add(bubble);
        _speechBubbleRoot.AddChild(bubble);

        if (existing.Count > SpeechBubbleCap)
        {
            // Get the next speech bubble to fade
            // Any speech bubbles before it are already fading
            var last = existing[^(SpeechBubbleCap + 1)];
            last.FadeNow();
        }
    }

    private void SpeechBubbleDied(EntityUid entity, SpeechBubble bubble)
    {
        RemoveSpeechBubble(entity, bubble);
    }

    private void EnqueueSpeechBubble(EntityUid entity, ChatMessage message, SpeechBubble.SpeechType speechType)
    {
        // Don't enqueue speech bubbles for other maps. TODO: Support multiple viewports/maps?
        if (EntityManager.GetComponent<TransformComponent>(entity).MapID != _eye.CurrentEye.Position.MapId)
            return;

        if (!_queuedSpeechBubbles.TryGetValue(entity, out var queueData))
        {
            queueData = new SpeechBubbleQueueData();
            _queuedSpeechBubbles.Add(entity, queueData);
        }

        while (queueData.MessageQueue.Count >= SpeechBubbleQueueCap)
        {
            // Keep the most recent speech backlog bounded while chat history still preserves full message flow.
            queueData.MessageQueue.Dequeue();
        }

        queueData.MessageQueue.Enqueue(new SpeechBubbleData(message, speechType));
    }

    public void RemoveSpeechBubble(EntityUid entityUid, SpeechBubble bubble)
    {
        bubble.OnDied -= SpeechBubbleDied;
        bubble.Orphan();

        if (!_activeSpeechBubbles.TryGetValue(entityUid, out var list))
            return;

        list.Remove(bubble);

        if (list.Count == 0)
        {
            _activeSpeechBubbles.Remove(entityUid);
        }
    }

    private void UpdateChannelPermissions()
    {
        CanSendChannels = default;
        FilterableChannels = default;

        // Can always send console stuff.
        CanSendChannels |= ChatSelectChannel.Console;

        // can always send/recieve OOC
        CanSendChannels |= ChatSelectChannel.OOC;
        CanSendChannels |= ChatSelectChannel.LOOC;
        FilterableChannels |= ChatChannel.OOC;
        FilterableChannels |= ChatChannel.LOOC;

        // can always hear server (nobody can actually send server messages).
        FilterableChannels |= ChatChannel.Server;

        if (_state.CurrentState is GameplayStateBase)
        {
            // can always hear local / radio / emote / notifications when in the game
            FilterableChannels |= ChatChannel.Local;
            FilterableChannels |= ChatChannel.Whisper;
            FilterableChannels |= ChatChannel.Radio;
            FilterableChannels |= ChatChannel.Emotes;
            FilterableChannels |= ChatChannel.Notifications;

            // Can only send local / radio / emote when attached to a non-ghost entity.
            // TODO: this logic is iffy (checking if controlling something that's NOT a ghost), is there a better way to check this?
            if (_ghost is not {IsGhost: true})
            {
                CanSendChannels |= ChatSelectChannel.Local;
                CanSendChannels |= ChatSelectChannel.Whisper;
                CanSendChannels |= ChatSelectChannel.Radio;
                CanSendChannels |= ChatSelectChannel.Emotes;
            }
        }

        // Only ghosts and admins can send / see deadchat.
        if (_admin.HasFlag(AdminFlags.Admin) || _ghost is {IsGhost: true})
        {
            FilterableChannels |= ChatChannel.Dead;
            CanSendChannels |= ChatSelectChannel.Dead;
        }

        // only admins can see / filter asay
        if (_admin.HasFlag(AdminFlags.Adminchat))
        {
            FilterableChannels |= ChatChannel.Admin;
            FilterableChannels |= ChatChannel.AdminAlert;
            FilterableChannels |= ChatChannel.AdminChat;
            CanSendChannels |= ChatSelectChannel.Admin;
        }

        SelectableChannels = CanSendChannels;

        // Necessary so that we always have a channel to fall back to.
        DebugTools.Assert((CanSendChannels & ChatSelectChannel.OOC) != 0, "OOC must always be available");
        DebugTools.Assert((FilterableChannels & ChatChannel.OOC) != 0, "OOC must always be available");
        DebugTools.Assert((SelectableChannels & ChatSelectChannel.OOC) != 0, "OOC must always be available");

        // let our chatbox know all the new settings
        CanSendChannelsChanged?.Invoke(CanSendChannels);
        FilterableChannelsChanged?.Invoke(FilterableChannels);
        SelectableChannelsChanged?.Invoke(SelectableChannels);
    }

    public void ClearUnfilteredUnreads(ChatChannel channels)
    {
        foreach (var channel in _unreadMessages.Keys.ToArray())
        {
            if ((channels & channel) == 0)
                continue;

            _unreadMessages[channel] = 0;
            UnreadMessageCountsUpdated?.Invoke(channel, 0);
        }
    }

    public override void FrameUpdate(FrameEventArgs delta)
    {
        UpdateQueuedSpeechBubbles(delta);
    }

    private void UpdateQueuedSpeechBubbles(FrameEventArgs delta)
    {
        // Update queued speech bubbles.
        if (_queuedSpeechBubbles.Count == 0 || _examine == null)
        {
            return;
        }

        foreach (var (entity, queueData) in _queuedSpeechBubbles.ShallowClone())
        {
            if (!EntityManager.EntityExists(entity))
            {
                _queuedSpeechBubbles.Remove(entity);
                continue;
            }

            queueData.TimeLeft -= delta.DeltaSeconds;
            if (queueData.TimeLeft > 0)
            {
                continue;
            }

            if (queueData.MessageQueue.Count == 0)
            {
                _queuedSpeechBubbles.Remove(entity);
                continue;
            }

            var msg = queueData.MessageQueue.Dequeue();

            queueData.TimeLeft += BubbleDelayBase + msg.Message.Message.Length * BubbleDelayFactor;

            // We keep the queue around while it has 0 items. This allows us to keep the timer.
            // When the timer hits 0 and there's no messages left, THEN we can clear it up.
            CreateSpeechBubble(entity, msg);
        }

        var player = _player.LocalEntity;
        var predicate = static (EntityUid uid, (EntityUid compOwner, EntityUid? attachedEntity) data)
            => uid == data.compOwner || uid == data.attachedEntity;
        var playerPos = player != null
            ? _eye.CurrentEye.Position
            : MapCoordinates.Nullspace;

        var occluded = player != null && _examine.IsOccluded(player.Value);

        foreach (var (ent, bubs) in _activeSpeechBubbles)
        {
            if (EntityManager.Deleted(ent))
            {
                SetBubbles(bubs, false);
                continue;
            }

            if (ent == player)
            {
                SetBubbles(bubs, true);
                continue;
            }

            var otherPos = _transform?.GetMapCoordinates(ent) ?? MapCoordinates.Nullspace;

            if (occluded && !_examine.InRangeUnOccluded(
                    playerPos,
                    otherPos, 0f,
                    (ent, player), predicate))
            {
                SetBubbles(bubs, false);
                continue;
            }

            SetBubbles(bubs, true);
        }
    }

    private void SetBubbles(List<SpeechBubble> bubbles, bool visible)
    {
        foreach (var bubble in bubbles)
        {
            bubble.Visible = visible;
        }
    }

    public ChatSelectChannel MapLocalIfGhost(ChatSelectChannel channel)
    {
        if (channel == ChatSelectChannel.Local && _ghost is {IsGhost: true})
            return ChatSelectChannel.Dead;

        return channel;
    }

    private bool TryGetRadioChannel(string text, out RadioChannelPrototype? radioChannel)
    {
        radioChannel = null;
        return _player.LocalEntity is EntityUid { Valid: true } uid
           && _chatSys != null
           && _chatSys.TryProcessRadioMessage(uid, text, out _, out radioChannel, quiet: true);
    }

    public void UpdateSelectedChannel(ChatBox box)
    {
        var (prefixChannel, _, radioChannel) = SplitInputContents(box.ChatInput.Input.Text);
        var selectedChannel = box.SelectedChannel == ChatSelectChannel.None
            ? GetPreferredChannel()
            : box.SelectedChannel;

        if (prefixChannel == ChatSelectChannel.None)
            box.ChatInput.ChannelSelector.UpdateChannelSelectButton(selectedChannel, null);
        else
            box.ChatInput.ChannelSelector.UpdateChannelSelectButton(prefixChannel, radioChannel);

        var effectiveChannel = ResolveEffectiveInputChannel(box);

        box.ChatInput.SetEmojiAllowed(IsEmojiAllowed(effectiveChannel));
    }

    public ChatSelectChannel ResolveEffectiveInputChannel(ChatBox box)
    {
        var (prefixChannel, _, _) = SplitInputContents(box.ChatInput.Input.Text);
        var selectedChannel = box.SelectedChannel == ChatSelectChannel.None
            ? GetPreferredChannel()
            : box.SelectedChannel;

        return prefixChannel == ChatSelectChannel.None
            ? MapLocalIfGhost(selectedChannel)
            : prefixChannel;
    }

    public (ChatSelectChannel chatChannel, string text, RadioChannelPrototype? radioChannel) SplitInputContents(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return (ChatSelectChannel.None, text, null);

        // We only cut off prefix only if it is not a radio or local channel, which both map to the same /say command
        // because ????????

        ChatSelectChannel chatChannel;
        if (TryGetRadioChannel(text, out var radioChannel))
            chatChannel = ChatSelectChannel.Radio;
        else
            chatChannel = PrefixToChannel.GetValueOrDefault(text[0]);

        if ((CanSendChannels & chatChannel) == 0)
            return (ChatSelectChannel.None, text, null);

        if (chatChannel == ChatSelectChannel.Radio)
            return (chatChannel, text, radioChannel);

        if (chatChannel == ChatSelectChannel.Local)
        {
            if (_ghost?.IsGhost != true)
                return (chatChannel, text, null);
            else
                chatChannel = ChatSelectChannel.Dead;
        }

        return (chatChannel, text[1..].TrimStart(), null);
    }

    public void SendMessage(ChatBox box, ChatSelectChannel channel)
    {
        _typingIndicator?.ClientSubmittedChatText();

        var text = box.ChatInput.Input.Text;
        box.ChatInput.Input.Clear();
        box.ChatInput.Input.ReleaseKeyboardFocus();
        UpdateSelectedChannel(box);

        if (string.IsNullOrWhiteSpace(text))
            return;

        (var prefixChannel, text, var _) = SplitInputContents(text);

        // Check if message is longer than the character limit
        if (text.Length > MaxMessageLength)
        {
            var locWarning = Loc.GetString("chat-manager-max-message-length",
                ("maxMessageLength", MaxMessageLength));
            box.AddLine(locWarning, Color.Orange, allowAliasMarkup: false);
            return;
        }

        if (prefixChannel != ChatSelectChannel.None)
            channel = prefixChannel;
        else if (channel == ChatSelectChannel.Radio)
        {
            // Selected radio should use the headset's default channel, not the common channel.
            text = BuildSelectedRadioMessage(text);
        }

        _manager.SendMessage(text, prefixChannel == 0 ? channel : prefixChannel);
    }

    internal static string BuildSelectedRadioMessage(string text)
    {
        return $"{SharedChatSystem.DefaultChannelPrefix} {text}";
    }

    private void OnDamageForceSay(DamageForceSayEvent ev, EntitySessionEventArgs _)
    {
        var chatBox = UIManager.ActiveScreen?.GetWidget<ChatBox>() ?? UIManager.ActiveScreen?.GetWidget<ResizableChatBox>();
        if (chatBox == null)
            return;

        var msg = chatBox.ChatInput.Input.Text.TrimEnd();
        // Don't send on OOC/LOOC obviously!

        // we need to handle selected channel
        // and prefix-channel separately..
        var allowedChannels = ChatSelectChannel.Local | ChatSelectChannel.Whisper;
        if ((chatBox.SelectedChannel & allowedChannels) == ChatSelectChannel.None)
            return;

        // none can be returned from this if theres no prefix,
        // so we allow it in that case (assuming the previous check will have exited already if its an invalid channel)
        var prefixChannel = SplitInputContents(msg).chatChannel;
        if (prefixChannel != ChatSelectChannel.None && (prefixChannel & allowedChannels) == ChatSelectChannel.None)
            return;

        if (_player.LocalSession?.AttachedEntity is not { } ent
            || !EntityManager.TryGetComponent<DamageForceSayComponent>(ent, out var forceSay))
            return;

        if (string.IsNullOrWhiteSpace(msg))
            return;

        var modifiedText = ev.Suffix != null
            ? Loc.GetString(forceSay.ForceSayMessageWrap,
                ("message", msg), ("suffix", ev.Suffix))
            : Loc.GetString(forceSay.ForceSayMessageWrapNoSuffix,
                ("message", msg));

        chatBox.ChatInput.Input.SetText(modifiedText);
        chatBox.ChatInput.Input.ForceSubmitText();
    }

    private void OnChatMessage(MsgChatMessage message)
    {
        var msg = message.Message;
        ProcessChatMessage(msg);

        if ((msg.Channel & ChatChannel.AdminRelated) == 0 ||
            _config.GetCVar(CCVars.ReplayRecordAdminChat))
        {
            _replayRecording.RecordClientMessage(msg);
        }
    }

    private void OnUpdateChatMessage(MsgUpdateChatMessage message)
    {
        ApplyChatMessageUpdate(message.Message);
    }

    private void ApplyChatMessageUpdate(ChatMessage msg)
    {
        if (msg.ServerMessageId is not { } messageId)
            return;

        var decoratedMessage = CloneChatMessage(msg, msg.Read);
        DecorateChatMessage(decoratedMessage);

        var updatedHistory = false;
        for (var i = 0; i < History.Count; i++)
        {
            if (History[i].Msg.ServerMessageId != messageId)
                continue;

            updatedHistory = true;
            History[i] = (History[i].Tick, CloneChatMessage(decoratedMessage, History[i].Msg.Read));
        }

        if (updatedHistory)
        {
            foreach (var chat in _chats)
            {
                chat.TryUpdateMessage(decoratedMessage);
            }
        }

        var updatedQueue = UpdateQueuedSpeechBubbleMessages(decoratedMessage, messageId);
        var updatedActive = UpdateActiveSpeechBubbleMessages(decoratedMessage, messageId);
        if (!updatedHistory && !updatedQueue && !updatedActive)
            return;
    }

    public void ProcessChatMessage(ChatMessage msg, bool speechBubble = true)
    {
        DecorateChatMessage(msg);

        // Log all incoming chat to repopulate when filter is un-toggled
        if (!msg.HideChat)
        {
            History.Add((_timing.CurTick, msg));

            if (History.Count > MaxHistoryLength)
                History.RemoveAt(0);

            MessageAdded?.Invoke(msg);

            if (!msg.Read)
            {
                _sawmill.Debug($"Message filtered: {msg.Channel}: {msg.Message}");
                if (!_unreadMessages.TryGetValue(msg.Channel, out var count))
                    count = 0;

                count += 1;
                _unreadMessages[msg.Channel] = count;
                UnreadMessageCountsUpdated?.Invoke(msg.Channel, count);
            }
        }

        // Local messages that have an entity attached get a speech bubble.
        if (!speechBubble || msg.SenderEntity == default)
            return;

        switch (msg.Channel)
        {
            case ChatChannel.Local:
                AddSpeechBubble(msg, SpeechBubble.SpeechType.Say);
                break;

            case ChatChannel.Whisper:
                AddSpeechBubble(msg, SpeechBubble.SpeechType.Whisper);
                break;

            case ChatChannel.Dead:
                if (_ghost is not {IsGhost: true})
                    break;

                AddSpeechBubble(msg, SpeechBubble.SpeechType.Say);
                break;

            case ChatChannel.Emotes:
                AddSpeechBubble(msg, SpeechBubble.SpeechType.Emote);
                break;

            case ChatChannel.LOOC:
                if (_config.GetCVar(CCVars.LoocAboveHeadShow))
                    AddSpeechBubble(msg, SpeechBubble.SpeechType.Looc);
                break;
        }
    }

    private void DecorateChatMessage(ChatMessage msg)
    {
        // color the name unless it's something like "the old man"
        if ((msg.Channel == ChatChannel.Local || msg.Channel == ChatChannel.Whisper) && _chatNameColorsEnabled)
        {
            var properNoun = msg.SenderNameIsProperNoun;
            if (properNoun == null)
            {
                var sender = _ent.GetEntity(msg.SenderEntity);
                if (_ent.EntityExists(sender))
                    properNoun = _ent.GetComponentOrNull<GrammarComponent>(sender)?.ProperNoun;
            }

            if (properNoun == true)
                msg.WrappedMessage = SharedChatSystem.InjectTagInsideTag(msg, "Name", "color", GetNameColor(SharedChatSystem.GetStringInsideTag(msg, "Name")));
        }

        // Color any words chosen by the client.
        foreach (var highlight in _highlights)
        {
            msg.WrappedMessage = SharedChatSystem.InjectTagAroundString(msg, highlight, "color", _highlightsColor);
        }

        // Color any codewords for minds that have roles that use them
        if (_player.LocalUser != null && _mindSystem != null && _roleCodewordSystem != null)
        {
            if (_mindSystem.TryGetMind(_player.LocalUser.Value, out var mindId) && _ent.TryGetComponent(mindId, out RoleCodewordComponent? codewordComp))
            {
                foreach (var (_, codewordData) in codewordComp.RoleCodewords)
                {
                    foreach (string codeword in codewordData.Codewords)
                        msg.WrappedMessage = SharedChatSystem.InjectTagAroundString(msg, codeword, "color", codewordData.Color.ToHex());
                }
            }
        }
    }

    public string GetChatDisplayMessage(ChatMessage msg)
    {
        if (msg.SenderEntity == default || msg.WrappedMessage.Contains($"[{RoleIconMarkupTag}", StringComparison.Ordinal))
            return msg.WrappedMessage;

        var iconId = TryGetMessageJobIconId(msg);
        if (!string.IsNullOrWhiteSpace(iconId))
            CacheSenderRoleIcon(msg.SenderKey, iconId);
        else
            iconId = TryGetCachedSenderRoleIcon(msg.SenderKey);

        if (string.IsNullOrWhiteSpace(iconId))
            return msg.WrappedMessage;

        return InjectRoleIconMarkup(msg.WrappedMessage, iconId);
    }

    private string? TryGetMessageJobIconId(ChatMessage msg)
    {
        if (!string.IsNullOrWhiteSpace(msg.SenderJobIconId) &&
            _prototypeManager.HasIndex<JobIconPrototype>(msg.SenderJobIconId) &&
            !IsNonSpecificJobIcon(msg.SenderJobIconId))
        {
            return msg.SenderJobIconId;
        }

        return TryGetSenderJobIconId(msg.SenderEntity);
    }

    private string? TryGetSenderJobIconId(NetEntity senderNetEntity)
    {
        var sender = _ent.GetEntity(senderNetEntity);
        if (!_ent.EntityExists(sender))
            return null;

        if (_ent.TryGetComponent(sender, out JobStatusComponent? jobStatus) &&
            jobStatus.JobStatusIcon is { } statusIcon &&
            _prototypeManager.HasIndex<JobIconPrototype>(statusIcon))
        {
            var statusIconId = statusIcon.Id;
            if (!IsNonSpecificJobIcon(statusIconId))
                return statusIconId;
        }

        if (_mindSystem == null || _jobSystem == null)
            return null;

        if (!_mindSystem.TryGetMind(sender, out var mindId, out _))
            return null;

        if (!_jobSystem.MindTryGetJob(mindId, out var job))
            return null;

        if (!_prototypeManager.HasIndex<JobIconPrototype>(job.Icon))
            return null;

        var jobIconId = job.Icon.Id;
        return IsNonSpecificJobIcon(jobIconId)
            ? null
            : jobIconId;
    }

    private static bool IsNonSpecificJobIcon(string iconId)
    {
        return string.Equals(iconId, JobIconNoId, StringComparison.Ordinal) ||
               string.Equals(iconId, JobIconUnknown, StringComparison.Ordinal);
    }

    private void CacheSenderRoleIcon(int? senderKey, string iconId)
    {
        if (senderKey is not { } key)
            return;

        _senderRoleIconCache[key] = iconId;

        if (_senderRoleIconCache.Count > SenderRoleIconCacheLimit)
            _senderRoleIconCache.Clear();
    }

    private string? TryGetCachedSenderRoleIcon(int? senderKey)
    {
        if (senderKey is not { } key ||
            !_senderRoleIconCache.TryGetValue(key, out var iconId))
        {
            return null;
        }

        if (_prototypeManager.HasIndex<JobIconPrototype>(iconId))
            return iconId;

        _senderRoleIconCache.Remove(key);
        return null;
    }

    private static string InjectRoleIconMarkup(string wrappedMessage, string iconId)
    {
        var iconMarkup = $"[{RoleIconMarkupTag} icon=\"{iconId}\"/] ";

        var nameIndex = wrappedMessage.IndexOf(NameTagOpen, StringComparison.Ordinal);
        if (nameIndex >= 0)
            return wrappedMessage.Insert(nameIndex, iconMarkup);

        var headerIndex = wrappedMessage.IndexOf(BubbleHeaderTagOpen, StringComparison.Ordinal);
        if (headerIndex >= 0)
            return wrappedMessage.Insert(headerIndex + BubbleHeaderTagOpen.Length, iconMarkup);

        return iconMarkup + wrappedMessage;
    }

    public void OnDeleteChatMessagesBy(MsgDeleteChatMessagesBy msg)
    {
        // This will delete messages from an entity even if different players were the author.
        // Usages of the erase admin verb should be rare enough that this does not matter.
        // Otherwise the client would need to know that one entity has multiple author players,
        // or the server would need to track when and which entities a player sent messages as.
        History.RemoveAll(h => h.Msg.SenderKey == msg.Key || msg.Entities.Contains(h.Msg.SenderEntity));
        _senderRoleIconCache.Remove(msg.Key);
        Repopulate();
    }

    private bool UpdateQueuedSpeechBubbleMessages(ChatMessage msg, uint messageId)
    {
        var updated = false;

        foreach (var (_, queueData) in _queuedSpeechBubbles)
        {
            if (queueData.MessageQueue.Count == 0)
                continue;

            var queued = queueData.MessageQueue.ToArray();
            var changed = false;
            for (var i = 0; i < queued.Length; i++)
            {
                if (queued[i].Message.ServerMessageId != messageId)
                    continue;

                changed = true;
                updated = true;
                queued[i] = new SpeechBubbleData(CloneChatMessage(msg, queued[i].Message.Read), queued[i].Type);
            }

            if (!changed)
                continue;

            queueData.MessageQueue.Clear();
            foreach (var speechBubbleData in queued)
            {
                queueData.MessageQueue.Enqueue(speechBubbleData);
            }
        }

        return updated;
    }

    private bool UpdateActiveSpeechBubbleMessages(ChatMessage msg, uint messageId)
    {
        var entity = EntityManager.GetEntity(msg.SenderEntity);
        if (!EntityManager.EntityExists(entity) ||
            !_activeSpeechBubbles.TryGetValue(entity, out var activeBubbles) ||
            activeBubbles.Count == 0)
        {
            return false;
        }

        var updated = false;
        for (var i = 0; i < activeBubbles.Count; i++)
        {
            var activeBubble = activeBubbles[i];
            if (activeBubble.ServerMessageId != messageId)
                continue;

            var replacement = SpeechBubble.CreateSpeechBubble(activeBubble.BubbleType, CloneChatMessage(msg, read: true), entity);
            replacement.VerticalOffset = activeBubble.VerticalOffset;
            replacement.Visible = activeBubble.Visible;
            replacement.SyncLifetimeFrom(activeBubble);
            replacement.OnDied += SpeechBubbleDied;

            _speechBubbleRoot.AddChild(replacement);
            activeBubble.OnDied -= SpeechBubbleDied;
            activeBubble.Orphan();
            activeBubbles[i] = replacement;
            updated = true;
        }

        return updated;
    }

    private static ChatMessage CloneChatMessage(ChatMessage source, bool read)
    {
        var clone = new ChatMessage(
            source.Channel,
            source.Message,
            source.WrappedMessage,
            source.SenderEntity,
            source.SenderKey,
            source.HideChat,
            source.MessageColorOverride,
            source.AudioPath,
            source.AudioVolume,
            source.SenderJobIconId,
            source.SenderNameIsProperNoun,
            source.SpeechTransport,
            source.ServerMessageId)
        {
            Read = read
        };

        return clone;
    }

    public void RegisterChat(ChatBox chat)
    {
        _chats.Add(chat);
        UpdateSelectedChannel(chat);
    }

    public void UnregisterChat(ChatBox chat)
    {
        _chats.Remove(chat);
    }

    public ChatSelectChannel GetPreferredChannel()
    {
        return MapLocalIfGhost(PreferredChannel);
    }

    public void NotifyChatTextChange()
    {
        _typingIndicator?.ClientChangedChatText();
    }

    public void NotifyChatFocus(bool isFocused)
    {
        _typingIndicator?.ClientChangedChatFocus(isFocused);
    }

    public void Repopulate()
    {
        foreach (var chat in _chats)
        {
            chat.Repopulate();
        }
    }

    public bool IsEmojiAllowed(ChatSelectChannel channel)
    {
        return ChatEmoji.IsAllowed(_emojiAllowedChannels, MapLocalIfGhost(channel));
    }

    public bool IsEmojiAllowed(ChatChannel channel)
    {
        return ChatEmoji.IsAllowed(_emojiAllowedChannels, channel);
    }

    private void OnEmojiAllowedChannelsChanged(string raw)
    {
        _emojiAllowedChannels = ChatEmoji.ParseAllowedChannels(raw);

        foreach (var chat in _chats)
        {
            UpdateSelectedChannel(chat);
        }
    }

    /// <summary>
    /// Returns the chat name color for a mob
    /// </summary>
    /// <param name="name">Name of the mob</param>
    /// <returns>Hex value of the color</returns>
    public string GetNameColor(string name)
    {
        var colorIdx = Math.Abs(name.GetHashCode() % _chatNameColors.Length);
        return _chatNameColors[colorIdx];
    }

    private readonly record struct SpeechBubbleData(ChatMessage Message, SpeechBubble.SpeechType Type);

    private sealed class SpeechBubbleQueueData
    {
        /// <summary>
        ///     Time left until the next speech bubble can appear.
        /// </summary>
        public float TimeLeft { get; set; }

        public Queue<SpeechBubbleData> MessageQueue { get; } = new();
    }
}
