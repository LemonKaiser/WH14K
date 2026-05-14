using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Content.Server._WH40K.MetaProgress;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Administration.Systems;
using Content.Server.Discord.DiscordLink;
using Content.Server.GameTicking;
using Content.Server.Players.RateLimiting;
using Content.Server.Preferences.Managers;
using Content.Server.Roles.Jobs;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Roles.Jobs;
using Content.Shared.Players.RateLimiting;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Replays;
using Robust.Shared.GameObjects.Components.Localization;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Managers;

/// <summary>
///     Dispatches chat messages to clients.
/// </summary>
internal sealed partial class ChatManager : IChatManager
{
    private static readonly Dictionary<string, string> PatronOocColors = new()
    {
        // I had plans for multiple colors and those went nowhere so...
        { "nuclear_operative", "#aa00ff" },
        { "syndicate_agent", "#aa00ff" },
        { "revolutionary", "#aa00ff" }
    };

    private const string DefaultMetaOocNameColorHex = "#87CEFA";
    private const string JobIconNoId = "JobIconNoId";
    private const string JobIconUnknown = "JobIconUnknown";

    private enum WH40KOocDecorationLineMode : byte
    {
        Off = 0,
        Admins = 1,
        On = 2,
    }

    [Dependency] private readonly IReplayRecordingManager _replay = default!;
    [Dependency] private readonly IServerNetManager _netManager = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IServerPreferencesManager _preferencesManager = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly INetConfigurationManager _netConfigManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly PlayerRateLimitManager _rateLimitManager = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly DiscordChatLink _discordLink = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private ISawmill _sawmill = default!;

    /// <summary>
    /// The maximum length a player-sent message can be sent
    /// </summary>
    public int MaxMessageLength => _configurationManager.GetCVar(CCVars.ChatMaxMessageLength);

    private bool _oocEnabled = true;
    private bool _adminOocEnabled = true;
    private ChatSelectChannel _emojiAllowedChannels = ChatEmoji.DefaultAllowedChannels;

    private readonly Dictionary<NetUserId, ChatUser> _players = new();

    private JobSystem Jobs => _entityManager.System<JobSystem>();

    public void Initialize()
    {
        _netManager.RegisterNetMessage<MsgChatMessage>();
        _netManager.RegisterNetMessage<MsgUpdateChatMessage>();
        _netManager.RegisterNetMessage<MsgDeleteChatMessagesBy>();

        _configurationManager.OnValueChanged(CCVars.OocEnabled, OnOocEnabledChanged, true);
        _configurationManager.OnValueChanged(CCVars.AdminOocEnabled, OnAdminOocEnabledChanged, true);
        _configurationManager.OnValueChanged(CCVars.ChatEmojiAllowedChannels, OnEmojiAllowedChannelsChanged, true);

        _sawmill = _logManager.GetSawmill("SERVER");

        RegisterRateLimits();
    }

    private void OnOocEnabledChanged(bool val)
    {
        if (_oocEnabled == val) return;

        _oocEnabled = val;
        DispatchServerAnnouncement(Loc.GetString(val ? "chat-manager-ooc-chat-enabled-message" : "chat-manager-ooc-chat-disabled-message"));
    }

    private void OnAdminOocEnabledChanged(bool val)
    {
        if (_adminOocEnabled == val) return;

        _adminOocEnabled = val;
        DispatchServerAnnouncement(Loc.GetString(val ? "chat-manager-admin-ooc-chat-enabled-message" : "chat-manager-admin-ooc-chat-disabled-message"));
    }

    private void OnEmojiAllowedChannelsChanged(string raw)
    {
        _emojiAllowedChannels = ChatEmoji.ParseAllowedChannels(raw);
    }

        public void DeleteMessagesBy(NetUserId uid)
        {
            if (!_players.TryGetValue(uid, out var user))
                return;

        var msg = new MsgDeleteChatMessagesBy { Key = user.Key, Entities = user.Entities };
        _netManager.ServerSendToAll(msg);
    }

    [return: NotNullIfNotNull(nameof(author))]
    public ChatUser? EnsurePlayer(NetUserId? author)
    {
        if (author == null)
            return null;

        ref var user = ref CollectionsMarshal.GetValueRefOrAddDefault(_players, author.Value, out var exists);
        if (!exists || user == null)
            user = new ChatUser(_players.Count);

        return user;
    }

    #region Server Announcements

    public void DispatchServerAnnouncement(string message, Color? colorOverride = null)
    {
        var wrappedMessage = Loc.GetString("chat-manager-server-wrap-message", ("message", FormattedMessage.EscapeText(message)));
        ChatMessageToAll(ChatChannel.Server, message, wrappedMessage, EntityUid.Invalid, hideChat: false, recordReplay: true, colorOverride: colorOverride);
        _sawmill.Info(message);

        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Server announcement: {message}");
    }

    public void DispatchServerMessage(ICommonSession player, string message, bool suppressLog = false)
    {
        var wrappedMessage = Loc.GetString("chat-manager-server-wrap-message", ("message", FormattedMessage.EscapeText(message)));
        ChatMessageToOne(ChatChannel.Server, message, wrappedMessage, default, false, player.Channel);

        if (!suppressLog)
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Server message to {player:Player}: {message}");
    }

    public void SendAdminAnnouncement(string message, AdminFlags? flagBlacklist, AdminFlags? flagWhitelist)
    {
        var clients = _adminManager.ActiveAdmins.Where(p =>
        {
            var adminData = _adminManager.GetAdminData(p);

            DebugTools.AssertNotNull(adminData);

            if (adminData == null)
                return false;

            if (flagBlacklist != null && adminData.HasFlag(flagBlacklist.Value))
                return false;

            return flagWhitelist == null || adminData.HasFlag(flagWhitelist.Value);

        }).Select(p => p.Channel);

        var wrappedMessage = Loc.GetString("chat-manager-send-admin-announcement-wrap-message",
            ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")), ("message", FormattedMessage.EscapeText(message)));

        ChatMessageToMany(ChatChannel.Admin, message, wrappedMessage, default, false, true, clients);
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Admin announcement: {message}");
    }

    public void SendAdminAnnouncementMessage(ICommonSession player, string message, bool suppressLog = true)
    {
        var wrappedMessage = Loc.GetString("chat-manager-send-admin-announcement-wrap-message",
            ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
            ("message", FormattedMessage.EscapeText(message)));
        ChatMessageToOne(ChatChannel.Admin, message, wrappedMessage, default, false, player.Channel);
    }

    public void SendAdminAlert(string message)
    {
        var wrappedMessage = Loc.GetString("chat-manager-send-admin-announcement-wrap-message",
            ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")), ("message", FormattedMessage.EscapeText(message)));

        SendAdminAlertNoFormatOrEscape(wrappedMessage);
    }

    public void SendAdminAlertNoFormatOrEscape(string message)
    {
        var clients = _adminManager.ActiveAdmins.Select(p => p.Channel);

        ChatMessageToMany(ChatChannel.AdminAlert, message, message, default, false, true, clients);
    }


    public void SendAdminAlert(EntityUid player, string message)
    {
        var mindSystem = _entityManager.System<SharedMindSystem>();
        if (!mindSystem.TryGetMind(player, out var mindId, out var mind))
        {
            SendAdminAlert(message);
            return;
        }

        var adminSystem = _entityManager.System<AdminSystem>();
        var antag = mind.UserId != null && (adminSystem.GetCachedPlayerInfo(mind.UserId.Value)?.Antag ?? false);

        // We shouldn't be repeating this but I don't want to touch any more chat code than necessary
        var playerName = mind.UserId is { } userId && _player.TryGetSessionById(userId, out var session)
            ? session.Name
            : "Unknown";

        SendAdminAlert($"{playerName}{(antag ? " (ANTAG)" : "")} {message}");
    }

    public void SendHookOOC(string sender, string message)
    {
        if (!_oocEnabled && _configurationManager.GetCVar(CCVars.DisablingOOCDisablesRelay))
        {
            return;
        }
        var wrappedMessage = Loc.GetString("chat-manager-send-hook-ooc-wrap-message", ("senderName", sender), ("message", FormattedMessage.EscapeText(message)));
        if (TryDispatchTranslatedHookOoc(sender, message, wrappedMessage))
            return;

        ChatMessageToAll(ChatChannel.OOC, message, wrappedMessage, source: EntityUid.Invalid, hideChat: false, recordReplay: true);
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Hook OOC from {sender}: {message}");
    }

    public void SendHookAdmin(string sender, string message)
    {
        var clients = _adminManager.ActiveAdmins.Select(p => p.Channel);

        var wrappedMessage = Loc.GetString("chat-manager-send-hook-admin-wrap-message", ("senderName", sender), ("message", FormattedMessage.EscapeText(message)));
        foreach (var client in clients)
        {
            ChatMessageToOne(
                ChatChannel.AdminChat,
                message,
                wrappedMessage,
                source: EntityUid.Invalid,
                hideChat: false,
                client: client,
                recordReplay: false,
                audioPath: _netConfigManager.GetClientCVar(client, CCVars.AdminChatSoundPath),
                audioVolume: _netConfigManager.GetClientCVar(client, CCVars.AdminChatSoundVolume));
        }

        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Hook admin from {sender}: {message}");
    }

    #endregion

    #region Public OOC Chat API

    /// <summary>
    ///     Called for a player to attempt sending an OOC, out-of-game. message.
    /// </summary>
    /// <param name="player">The player sending the message.</param>
    /// <param name="message">The message.</param>
    /// <param name="type">The type of message.</param>
    public void TrySendOOCMessage(ICommonSession player, string message, OOCChatType type)
    {
        if (HandleRateLimit(player) != RateLimitStatus.Allowed)
            return;

        // Check if message exceeds the character limit
        if (message.Length > MaxMessageLength)
        {
            DispatchServerMessage(player, Loc.GetString("chat-manager-max-message-length-exceeded-message", ("limit", MaxMessageLength)));
            return;
        }

        switch (type)
        {
            case OOCChatType.OOC:
                SendOOC(player, message);
                break;
            case OOCChatType.Admin:
                SendAdminChat(player, message);
                break;
        }
    }

    #endregion

    #region Private API

    private void SendOOC(ICommonSession player, string message)
    {
        message = ChatEmoji.ApplyPolicy(message, ChatSelectChannel.OOC, _emojiAllowedChannels);
        if (string.IsNullOrWhiteSpace(message))
            return;

        var adminDecorationPriority = _configurationManager.GetCVar(CCVars.WH40KMetaAdminPriorityOverDecorations);
        var fullLineMode = NormalizeDecorationLineMode(_configurationManager.GetCVar(CCVars.WH40KMetaOocDecorationLineMode));
        var isAdmin = _adminManager.IsAdmin(player);
        var lobbyIsolationMode = IsLobbyOocIsolationMode(player);

        if (isAdmin)
        {
            if (!_adminOocEnabled)
            {
                return;
            }
        }
        else if (!_oocEnabled && !lobbyIsolationMode)
        {
            return;
        }

        var escapedMessage = FormattedMessage.EscapeText(message);
        Color? colorOverride = null;
        string? metaNameColorHex = null;
        string? metaNameMarkup = null;
        var playerName = player.Name;
        string titlePrefix;
        WH40KMetaDecorationSnapshotEntry? titleEntry;
        WH40KMetaDecorationSnapshotEntry? colorEntry;

        TryResolveMetaOocDecorations(
            player,
            adminDecorationPriority,
            out playerName,
            out metaNameColorHex,
            out metaNameMarkup,
            out titlePrefix,
            out titleEntry,
            out colorEntry);

        if (adminDecorationPriority && _adminManager.HasAdminFlag(player, AdminFlags.NameColor))
        {
            var prefs = _preferencesManager.GetPreferences(player.UserId);
            colorOverride = prefs.AdminOOCColor;
            metaNameColorHex = null;
            metaNameMarkup = null;
        }

        string? patronColor = null;
        if (_netConfigManager.GetClientCVar(player.Channel, CCVars.ShowOocPatronColor) &&
            player.Channel.UserData.PatronTier is { } patron &&
            PatronOocColors.TryGetValue(patron, out var resolvedPatronColor))
        {
            patronColor = resolvedPatronColor;
        }

        var formatContext = new WH40KOocFormatContext(
            player.Name,
            playerName,
            colorOverride,
            patronColor,
            metaNameColorHex,
            metaNameMarkup,
            titlePrefix,
            titleEntry,
            colorEntry,
            adminDecorationPriority,
            isAdmin);

        var wrappedMessage = BuildOocWrappedMessage(formatContext, message, null);

        if (TryDispatchTranslatedOoc(player, message, wrappedMessage, lobbyIsolationMode, formatContext))
        {
            _discordLink.SendMessage(message, player.Name, ChatChannel.OOC);
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"OOC from {player:Player}: {message}");
            return;
        }

        //TODO: player.Name color, this will need to change the structure of the MsgChatMessage
        if (lobbyIsolationMode && !isAdmin)
        {
            var lobbyClients = _player.Sessions
                .Where(session => !IsSessionInRoundGameplay(session))
                .Select(session => session.Channel)
                .ToList();

            ChatMessageToMany(ChatChannel.OOC, message, wrappedMessage, EntityUid.Invalid, hideChat: false, recordReplay: true, clients: lobbyClients, colorOverride: colorOverride, author: player.UserId);
        }
        else
        {
            ChatMessageToAll(ChatChannel.OOC, message, wrappedMessage, EntityUid.Invalid, hideChat: false, recordReplay: true, colorOverride: colorOverride, author: player.UserId);
        }

        _discordLink.SendMessage(message, player.Name, ChatChannel.OOC);
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"OOC from {player:Player}: {message}");
    }

    private bool IsLobbyOocIsolationMode(ICommonSession sender)
    {
        var ticker = _entityManager.System<GameTicker>();

        if (!_configurationManager.GetCVar(CCVars.OocLobbyIsolatedDuringRound))
            return false;

        if (ticker.RunLevel != GameRunLevel.InRound)
            return false;

        // If OOC has been manually enabled during the round (e.g. by setooc),
        // lobby messages should again be visible to in-round players.
        if (_oocEnabled)
            return false;

        return !IsSessionInRoundGameplay(sender);
    }

    private bool IsSessionInRoundGameplay(ICommonSession session)
    {
        return _entityManager.System<GameTicker>().UserHasJoinedGame(session);
    }

    private void TryResolveMetaOocDecorations(
        ICommonSession player,
        bool adminDecorationPriority,
        out string displayName,
        out string? nameColorHex,
        out string? nameMarkup,
        out string titlePrefix,
        out WH40KMetaDecorationSnapshotEntry? titleEntry,
        out WH40KMetaDecorationSnapshotEntry? colorEntry)
    {
        displayName = player.Name;
        nameColorHex = null;
        nameMarkup = null;
        titlePrefix = string.Empty;
        titleEntry = null;
        colorEntry = null;

        WH40KMetaProgressSnapshot snapshot;
        try
        {
            snapshot = _entityManager.System<WH40KMetaProgressSystem>().GetSnapshot(player.UserId);
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Failed to resolve WH40K OOC decorations for {player.Name}: {e.Message}");
            return;
        }

        var selectedTitleId = snapshot.DecorationSelection.SelectedOocTitleId;
        var selectedColorId = snapshot.DecorationSelection.SelectedOocNameColorId;

        foreach (var entry in snapshot.Decorations)
        {
            if (!entry.Unlocked)
                continue;

            if (entry.Category == WH40KMetaDecorationCategory.OocTitles &&
                string.Equals(entry.Id, selectedTitleId, StringComparison.Ordinal))
            {
                titleEntry = entry;
                continue;
            }

            if (entry.Category == WH40KMetaDecorationCategory.OocNameColors &&
                string.Equals(entry.Id, selectedColorId, StringComparison.Ordinal))
            {
                colorEntry = entry;
            }
        }

        var adminTitleForced = adminDecorationPriority && !string.IsNullOrWhiteSpace(_adminManager.GetAdminData(player)?.Title);
        if (!adminTitleForced && titleEntry != null && !titleEntry.SuppressTitlePrefix)
        {
            var titleLocKey = string.IsNullOrWhiteSpace(titleEntry.PreviewKey)
                ? titleEntry.TitleKey
                : titleEntry.PreviewKey;

            if (!string.IsNullOrWhiteSpace(titleLocKey))
            {
                var titleText = Loc.GetString(titleLocKey);
                if (!string.IsNullOrWhiteSpace(titleText))
                    titlePrefix = $"({titleText})";
            }
        }

        displayName = string.IsNullOrWhiteSpace(titlePrefix)
            ? player.Name
            : $"{titlePrefix} {player.Name}";
        var titlePrefixRuneCount = CountRunes(titlePrefix);

        if (TryBuildDecoratedNameMarkup(colorEntry, titleEntry, player.Name, titlePrefix, titlePrefixRuneCount, out var decoratedMarkup))
        {
            nameMarkup = decoratedMarkup;
            return;
        }

        if (colorEntry == null)
            return;

        if (string.IsNullOrWhiteSpace(colorEntry.OocColorHex))
            return;

        if (Color.TryFromHex(colorEntry.OocColorHex) is not { } solidColor)
            return;

        nameColorHex = solidColor.ToHex();
    }

    private static bool TryBuildDecoratedNameMarkup(
        WH40KMetaDecorationSnapshotEntry? colorEntry,
        WH40KMetaDecorationSnapshotEntry? titleEntry,
        string playerName,
        string titlePrefix,
        int titlePrefixRuneCount,
        [NotNullWhen(true)] out string? markup)
    {
        markup = null;

        var hasNameMarkup = TryBuildNameDecorationMarkup(colorEntry, playerName, out var nameMarkup);

        var hasTitleMarkup = TryBuildTitleDecorationMarkup(
            titleEntry,
            colorEntry,
            titlePrefix,
            titlePrefixRuneCount,
            out var titleMarkup);

        if (!hasNameMarkup && !hasTitleMarkup)
            return false;

        var builder = new StringBuilder();
        builder.Append(hasTitleMarkup
            ? titleMarkup
            : FormattedMessage.EscapeText(titlePrefix));
        if (titlePrefixRuneCount > 0)
            builder.Append(' ');
        builder.Append(hasNameMarkup
            ? nameMarkup
            : FormattedMessage.EscapeText(playerName));

        markup = builder.ToString();
        return true;
    }

    private static bool TryBuildDecoratedOocLineMarkup(
        WH40KMetaDecorationSnapshotEntry? colorEntry,
        WH40KMetaDecorationSnapshotEntry? titleEntry,
        string playerName,
        string titlePrefix,
        string message,
        [NotNullWhen(true)] out string? markup)
    {
        markup = null;

        var fullLine = BuildRawOocLine(playerName, titlePrefix, message);
        if (string.IsNullOrWhiteSpace(fullLine))
            return false;

        if (TryBuildTitleDecorationMarkup(
                titleEntry,
                colorEntry,
                fullLine,
                CountRunes(fullLine),
                out var titleLineMarkup))
        {
            markup = titleLineMarkup;
            return true;
        }

        if (TryBuildNameDecorationMarkup(colorEntry, fullLine, out var lineNameMarkup))
        {
            markup = lineNameMarkup;
            return true;
        }

        return false;
    }

    private static string BuildRawOocLine(string playerName, string titlePrefix, string message)
    {
        var builder = new StringBuilder();
        builder.Append("OOC: ");
        if (!string.IsNullOrWhiteSpace(titlePrefix))
        {
            builder.Append(titlePrefix);
            builder.Append(' ');
        }
        builder.Append(playerName);
        builder.Append(": ");
        builder.Append(message);
        return builder.ToString();
    }

    private static bool TryBuildNameDecorationMarkup(
        WH40KMetaDecorationSnapshotEntry? colorEntry,
        string playerName,
        out string markup)
    {
        markup = string.Empty;
        if (colorEntry == null)
            return false;

        var resolvedColorEntry = colorEntry;
        var palette = new List<string>();
        foreach (var paletteEntry in resolvedColorEntry.OocGradientColors)
        {
            if (!TryResolveGradientColor(paletteEntry, out var color))
                continue;

            palette.Add(color.ToHex());
        }

        var hasGradient = palette.Count >= 2;
        if (!hasGradient && TryResolveGradientColor(resolvedColorEntry.OocColorHex, out var solidColor))
        {
            palette.Add(solidColor.ToHex());
        }

        var hasAura = false;
        var auraColorHex = string.Empty;
        var auraRadius = 0;
        var auraAlphaPercent = 0;
        if (resolvedColorEntry.OocAuraRadius > 0 &&
            resolvedColorEntry.OocAuraAlphaPercent > 0 &&
            TryResolveGradientColor(resolvedColorEntry.OocAuraHex, out var auraColor))
        {
            hasAura = true;
            auraColorHex = auraColor.ToHex();
            auraRadius = Math.Clamp(resolvedColorEntry.OocAuraRadius, 1, 4);
            auraAlphaPercent = Math.Clamp(resolvedColorEntry.OocAuraAlphaPercent, 1, 100);
        }

        if (palette.Count == 0 && hasAura && TryResolveGradientColor(DefaultMetaOocNameColorHex, out var defaultColor))
        {
            palette.Add(defaultColor.ToHex());
        }

        if (palette.Count == 0 && !hasAura)
            return false;

        var safeName = SanitizeGradientParameter(playerName);
        var safePalette = SanitizeGradientParameter(string.Join("|", palette));
        var animated = hasGradient && resolvedColorEntry.OocGradientAnimated ? 1 : 0;
        var durationMs = Math.Clamp(resolvedColorEntry.OocGradientDurationMs, 400, 60000);

        var builder = new StringBuilder();
        builder.Append("[wh40kgradient=\"")
            .Append(safeName)
            .Append("\" palette=\"")
            .Append(safePalette)
            .Append("\" animated=")
            .Append(animated)
            .Append(" duration=")
            .Append(durationMs);

        if (hasAura)
        {
            builder.Append(" aura=1")
                .Append(" auracolor=\"")
                .Append(SanitizeGradientParameter(auraColorHex))
                .Append("\" auraradius=")
                .Append(auraRadius)
                .Append(" auraalpha=")
                .Append(auraAlphaPercent);
        }

        builder.Append("/]");
        markup = builder.ToString();
        return true;
    }

    private static bool TryBuildTitleDecorationMarkup(
        WH40KMetaDecorationSnapshotEntry? titleEntry,
        WH40KMetaDecorationSnapshotEntry? colorEntry,
        string titleText,
        int titleTextRuneCount,
        out string markup)
    {
        markup = string.Empty;

        if (titleEntry == null || titleTextRuneCount <= 0)
            return false;

        if (!TryBuildPalette(titleEntry, colorEntry, out var palette, out var animated, out var durationMs))
            return false;

        var safeTitle = SanitizeGradientParameter(titleText);
        var safePalette = SanitizeGradientParameter(string.Join("|", palette));

        var revealMs = Math.Clamp(titleEntry.OocTitleEffectRevealMs, 100, 120000);
        var holdMs = Math.Clamp(titleEntry.OocTitleEffectHoldMs, 100, 120000);
        var dissolveMs = Math.Clamp(titleEntry.OocTitleEffectDissolveMs, 100, 120000);
        var hasEffect = TryNormalizeTitleEffect(titleEntry.OocTitleEffect, out var effect);
        var hasOutline = TryResolveTitleOutlineFromEntry(titleEntry, out var outlineColorHex, out var outlineWidth, out var outlineAlphaPercent);

        var builder = new StringBuilder();
        builder.Append("[wh40ktitlefx=\"")
            .Append(safeTitle)
            .Append("\" palette=\"")
            .Append(safePalette)
            .Append("\" animated=")
            .Append(animated)
            .Append(" duration=")
            .Append(durationMs)
            .Append(" reveal=")
            .Append(revealMs)
            .Append(" hold=")
            .Append(holdMs)
            .Append(" dissolve=")
            .Append(dissolveMs)
            .Append(" cursor=1");

        if (hasEffect)
        {
            builder.Append(" effect=\"")
                .Append(SanitizeGradientParameter(effect))
                .Append("\"");
        }

        if (hasOutline)
        {
            builder.Append(" outline=1")
                .Append(" outlinecolor=\"")
                .Append(SanitizeGradientParameter(outlineColorHex))
                .Append("\" outlinewidth=")
                .Append(outlineWidth)
                .Append(" outlinealpha=")
                .Append(outlineAlphaPercent);
        }

        builder.Append("/]");
        markup = builder.ToString();
        return true;
    }

    private static bool TryBuildPalette(
        WH40KMetaDecorationSnapshotEntry? primaryEntry,
        WH40KMetaDecorationSnapshotEntry? fallbackEntry,
        [NotNullWhen(true)] out List<string>? palette,
        out int animated,
        out int durationMs)
    {
        palette = new List<string>();
        animated = 0;
        durationMs = 3500;

        WH40KMetaDecorationSnapshotEntry? sourceEntry = null;
        if (TryAppendPaletteFromEntry(primaryEntry, palette))
            sourceEntry = primaryEntry;
        else if (TryAppendPaletteFromEntry(fallbackEntry, palette))
            sourceEntry = fallbackEntry;

        if (palette.Count == 0 && TryResolveGradientColor(DefaultMetaOocNameColorHex, out var defaultColor))
            palette.Add(defaultColor.ToHex());

        if (palette.Count == 0)
            return false;

        var hasGradient = palette.Count >= 2;
        animated = hasGradient && sourceEntry?.OocGradientAnimated == true ? 1 : 0;
        durationMs = Math.Clamp(sourceEntry?.OocGradientDurationMs ?? 3500, 400, 60000);
        return true;
    }

    private static bool TryAppendPaletteFromEntry(
        WH40KMetaDecorationSnapshotEntry? entry,
        List<string> palette)
    {
        if (entry == null)
            return false;

        var startCount = palette.Count;
        foreach (var paletteEntry in entry.OocGradientColors)
        {
            if (!TryResolveGradientColor(paletteEntry, out var color))
                continue;

            palette.Add(color.ToHex());
        }

        if (palette.Count == startCount && TryResolveGradientColor(entry.OocColorHex, out var solidColor))
            palette.Add(solidColor.ToHex());

        return palette.Count > startCount;
    }

    private static bool TryResolveTitleOutlineFromEntry(
        WH40KMetaDecorationSnapshotEntry? entry,
        out string outlineColorHex,
        out int outlineWidth,
        out int outlineAlphaPercent)
    {
        outlineColorHex = string.Empty;
        outlineWidth = 0;
        outlineAlphaPercent = 0;

        if (entry == null ||
            entry.OocTitleOutlineWidth <= 0 ||
            entry.OocTitleOutlineAlphaPercent <= 0 ||
            !TryResolveGradientColor(entry.OocTitleOutlineHex, out var outlineColor))
        {
            return false;
        }

        outlineColorHex = outlineColor.ToHex();
        outlineWidth = Math.Clamp(entry.OocTitleOutlineWidth, 1, 3);
        outlineAlphaPercent = Math.Clamp(entry.OocTitleOutlineAlphaPercent, 1, 100);
        return true;
    }

    private static bool TryNormalizeTitleEffect(string? source, out string effect)
    {
        effect = string.Empty;

        if (string.IsNullOrWhiteSpace(source))
            return false;

        effect = source.Trim().ToLowerInvariant() switch
        {
            "binary" => "binary",
            "scan" => "scan",
            "fish" or "fish-swim" => "fish",
            "scramble-decode" or "scramble" => "scramble-decode",
            "typewriter-cursor" or "typewriter" => "typewriter-cursor",
            "wave" => "wave",
            "glitch-slice" or "glitch" => "glitch-slice",
            "noise-dissolve" or "dissolve-noise" or "noise" => "noise-dissolve",
            "scanline" => "scanline",
            "flip" or "discord-flip" => "flip",
            _ => string.Empty,
        };

        return effect.Length > 0;
    }

    private static int CountRunes(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    private static bool TryResolveGradientColor(string value, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();

        if (Color.TryFromHex(trimmed) is { } hex)
        {
            color = hex;
            return true;
        }

        if (Color.TryFromName(trimmed, out var named))
        {
            color = named;
            return true;
        }

        return false;
    }

    private static string SanitizeGradientParameter(string value)
    {
        return value
            .Replace("\"", "'")
            .Replace("[", "(")
            .Replace("]", ")")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }

    private static WH40KOocDecorationLineMode NormalizeDecorationLineMode(int value)
    {
        return value switch
        {
            1 => WH40KOocDecorationLineMode.Admins,
            2 => WH40KOocDecorationLineMode.On,
            _ => WH40KOocDecorationLineMode.Off,
        };
    }

    private static bool ShouldDecorateFullLine(
        WH40KOocDecorationLineMode mode,
        bool isAdmin,
        bool adminDecorationPriority)
    {
        return mode switch
        {
            WH40KOocDecorationLineMode.Off => false,
            WH40KOocDecorationLineMode.Admins => isAdmin && !adminDecorationPriority,
            WH40KOocDecorationLineMode.On => true,
            _ => false,
        };
    }

    private void SendAdminChat(ICommonSession player, string message)
    {
        if (!_adminManager.IsAdmin(player))
        {
            _adminLogger.Add(LogType.Chat, LogImpact.Extreme, $"{player:Player} attempted to send admin message but was not admin");
            return;
        }

        message = ChatEmoji.ApplyPolicy(message, ChatSelectChannel.Admin, _emojiAllowedChannels);
        if (string.IsNullOrWhiteSpace(message))
            return;

        var clients = _adminManager.ActiveAdmins.Select(p => p.Channel);
        var wrappedMessage = Loc.GetString("chat-manager-send-admin-chat-wrap-message",
                                        ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
                                        ("playerName", player.Name), ("message", FormattedMessage.EscapeText(message)));

        foreach (var client in clients)
        {
            var isSource = client != player.Channel;
            ChatMessageToOne(ChatChannel.AdminChat,
                message,
                wrappedMessage,
                default,
                false,
                client,
                audioPath: isSource ? _netConfigManager.GetClientCVar(client, CCVars.AdminChatSoundPath) : default,
                audioVolume: isSource ? _netConfigManager.GetClientCVar(client, CCVars.AdminChatSoundVolume) : default,
                author: player.UserId);
        }

        _discordLink.SendMessage(message, player.Name, ChatChannel.AdminChat);
        _adminLogger.Add(LogType.Chat, $"Admin chat from {player:Player}: {message}");
    }

    #endregion

    #region Utility

    private (string? SenderJobIconId, bool? SenderNameIsProperNoun) ResolveSenderChatVisuals(EntityUid source, NetUserId? authorUserId)
    {
        if (source == EntityUid.Invalid || !_entityManager.EntityExists(source))
            return (TryResolveAuthorJobIcon(authorUserId), null);

        bool? properNoun = null;
        if (_entityManager.TryGetComponent(source, out GrammarComponent? grammar))
            properNoun = grammar.ProperNoun;

        string? iconId = null;
        if (_entityManager.TryGetComponent(source, out JobStatusComponent? status) &&
            status.JobStatusIcon is { } statusIcon &&
            _prototypeManager.HasIndex<JobIconPrototype>(statusIcon.Id) &&
            !IsNonSpecificJobIcon(statusIcon.Id))
        {
            iconId = statusIcon.Id;
        }

        iconId ??= TryResolveAuthorJobIcon(authorUserId);

        return (iconId, properNoun);
    }

    private string? TryResolveAuthorJobIcon(NetUserId? authorUserId)
    {
        if (authorUserId is not { } userId)
            return null;

        if (!_player.TryGetSessionById(userId, out var session))
            return null;

        var mindId = session.ContentData()?.Mind;
        if (mindId == null || !Jobs.MindTryGetJob(mindId.Value, out var job))
            return null;

        var iconId = job.Icon.Id;
        if (IsNonSpecificJobIcon(iconId) || !_prototypeManager.HasIndex<JobIconPrototype>(iconId))
            return null;

        return iconId;
    }

    private static bool IsNonSpecificJobIcon(string iconId)
    {
        return string.Equals(iconId, JobIconNoId, StringComparison.Ordinal) ||
               string.Equals(iconId, JobIconUnknown, StringComparison.Ordinal);
    }

    private ChatMessage CreateChatMessage(
        ChatChannel channel,
        string message,
        string wrappedMessage,
        EntityUid source,
        bool hideChat,
        Color? colorOverride = null,
        string? audioPath = null,
        float audioVolume = 0,
        NetUserId? author = null,
        ChatSpeechTransport speechTransport = ChatSpeechTransport.Direct,
        uint? serverMessageId = null)
    {
        var user = author == null ? null : EnsurePlayer(author);
        var netSource = _entityManager.GetNetEntity(source);
        user?.AddEntity(netSource);
        var (senderJobIconId, senderNameIsProperNoun) = ResolveSenderChatVisuals(source, author);

        return new ChatMessage(
            channel,
            message,
            wrappedMessage,
            netSource,
            user?.Key,
            hideChat,
            colorOverride,
            audioPath,
            audioVolume,
            senderJobIconId,
            senderNameIsProperNoun,
            speechTransport,
            serverMessageId);
    }

    public void ChatMessageToOne(ChatChannel channel, string message, string wrappedMessage, EntityUid source, bool hideChat, INetChannel client, Color? colorOverride = null, bool recordReplay = false, string? audioPath = null, float audioVolume = 0, NetUserId? author = null, ChatSpeechTransport speechTransport = ChatSpeechTransport.Direct, uint? serverMessageId = null)
    {
        var msg = CreateChatMessage(channel, message, wrappedMessage, source, hideChat, colorOverride, audioPath, audioVolume, author, speechTransport, serverMessageId);
        _netManager.ServerSendMessage(new MsgChatMessage() { Message = msg }, client);

        if (!recordReplay)
            return;

        if ((channel & ChatChannel.AdminRelated) == 0 ||
            _configurationManager.GetCVar(CCVars.ReplayRecordAdminChat))
        {
            _replay.RecordServerMessage(msg);
        }
    }

    public void UpdateChatMessageToOne(ChatChannel channel, string message, string wrappedMessage, EntityUid source, bool hideChat, INetChannel client, uint serverMessageId, Color? colorOverride = null, string? audioPath = null, float audioVolume = 0, NetUserId? author = null, ChatSpeechTransport speechTransport = ChatSpeechTransport.Direct)
    {
        var msg = CreateChatMessage(channel, message, wrappedMessage, source, hideChat, colorOverride, audioPath, audioVolume, author, speechTransport, serverMessageId);
        _netManager.ServerSendMessage(new MsgUpdateChatMessage { Message = msg }, client);
    }

    public void ChatMessageToMany(ChatChannel channel, string message, string wrappedMessage, EntityUid source, bool hideChat, bool recordReplay, IEnumerable<INetChannel> clients, Color? colorOverride = null, string? audioPath = null, float audioVolume = 0, NetUserId? author = null, ChatSpeechTransport speechTransport = ChatSpeechTransport.Direct, uint? serverMessageId = null)
        => ChatMessageToMany(channel, message, wrappedMessage, source, hideChat, recordReplay, clients.ToList(), colorOverride, audioPath, audioVolume, author, speechTransport, serverMessageId);

    public void ChatMessageToMany(ChatChannel channel, string message, string wrappedMessage, EntityUid source, bool hideChat, bool recordReplay, List<INetChannel> clients, Color? colorOverride = null, string? audioPath = null, float audioVolume = 0, NetUserId? author = null, ChatSpeechTransport speechTransport = ChatSpeechTransport.Direct, uint? serverMessageId = null)
    {
        var msg = CreateChatMessage(channel, message, wrappedMessage, source, hideChat, colorOverride, audioPath, audioVolume, author, speechTransport, serverMessageId);
        _netManager.ServerSendToMany(new MsgChatMessage() { Message = msg }, clients);

        if (!recordReplay)
            return;

        if ((channel & ChatChannel.AdminRelated) == 0 ||
            _configurationManager.GetCVar(CCVars.ReplayRecordAdminChat))
        {
            _replay.RecordServerMessage(msg);
        }
    }

    public void ChatMessageToManyFiltered(Filter filter, ChatChannel channel, string message, string wrappedMessage, EntityUid source,
        bool hideChat, bool recordReplay, Color? colorOverride = null, string? audioPath = null, float audioVolume = 0, ChatSpeechTransport speechTransport = ChatSpeechTransport.Direct, uint? serverMessageId = null)
    {
        if (!recordReplay && !filter.Recipients.Any())
            return;

        var clients = new List<INetChannel>();
        foreach (var recipient in filter.Recipients)
        {
            clients.Add(recipient.Channel);
        }

        ChatMessageToMany(channel, message, wrappedMessage, source, hideChat, recordReplay, clients, colorOverride, audioPath, audioVolume, speechTransport: speechTransport, serverMessageId: serverMessageId);
    }

    public void ChatMessageToAll(ChatChannel channel, string message, string wrappedMessage, EntityUid source, bool hideChat, bool recordReplay, Color? colorOverride = null, string? audioPath = null, float audioVolume = 0, NetUserId? author = null, ChatSpeechTransport speechTransport = ChatSpeechTransport.Direct, uint? serverMessageId = null)
    {
        var msg = CreateChatMessage(channel, message, wrappedMessage, source, hideChat, colorOverride, audioPath, audioVolume, author, speechTransport, serverMessageId);
        _netManager.ServerSendToAll(new MsgChatMessage() { Message = msg });

        if (!recordReplay)
            return;

        if ((channel & ChatChannel.AdminRelated) == 0 ||
            _configurationManager.GetCVar(CCVars.ReplayRecordAdminChat))
        {
            _replay.RecordServerMessage(msg);
        }
    }

    public bool MessageCharacterLimit(ICommonSession? player, string message)
    {
        var isOverLength = false;

        // Non-players don't need to be checked.
        if (player == null)
            return false;

        // Check if message exceeds the character limit if the sender is a player
        if (message.Length > MaxMessageLength)
        {
            var feedback = Loc.GetString("chat-manager-max-message-length-exceeded-message", ("limit", MaxMessageLength));

            DispatchServerMessage(player, feedback);

            isOverLength = true;
        }

        return isOverLength;
    }

    #endregion
}

public enum OOCChatType : byte
{
    OOC,
    Admin
}
