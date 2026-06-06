using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._WH40K.Administration;
using Content.Server._WH40K.Administration.Mute;
using Content.Server._WH40K.Chat.Moderation;
using Content.Server.Chat.V2.Repository;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Players.RateLimiting;
using Content.Shared._WH40K.Administration.Mute;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Chat.Managers;

internal sealed partial class ChatManager
{
    private readonly Dictionary<NetUserId, WH40KRepeatedChatSpamTracker> _repeatRateLimitData = new();

    [Dependency] private IGameTiming _gameTiming = default!;

    private TimeSpan _nextRepeatRateLimitSweep;

    public RateLimitStatus HandleRepeatedRateLimit(ICommonSession player, string message)
    {
        if (ShouldBypassWh40KChatRateLimit(player))
            return RateLimitStatus.Allowed;

        var threshold = _configurationManager.GetCVar(CCVars.ChatRepeatRateLimitCount);
        var periodSeconds = _configurationManager.GetCVar(CCVars.ChatRepeatRateLimitPeriod);
        if (threshold <= 0 || periodSeconds <= 0)
            return RateLimitStatus.Allowed;

        var normalized = WH40KRepeatedChatSpamTracker.NormalizeMessage(message);
        if (normalized.Length == 0)
            return RateLimitStatus.Allowed;

        var now = _gameTiming.RealTime;
        SweepRepeatRateLimitState(now);

        if (!_repeatRateLimitData.TryGetValue(player.UserId, out var tracker))
        {
            tracker = new WH40KRepeatedChatSpamTracker();
            _repeatRateLimitData[player.UserId] = tracker;
        }

        var announceDelay = TimeSpan.FromSeconds(_configurationManager.GetCVar(CCVars.ChatRepeatRateLimitAnnounceAdminsDelay));
        var result = tracker.CountMessage(
            now,
            normalized,
            TimeSpan.FromSeconds(periodSeconds),
            threshold,
            announceDelay);

        if (!result.Blocked)
            return RateLimitStatus.Allowed;

        if (result.ShouldAnnounceAdmins)
            RepeatRateLimitAlertAdmins(player, message);

        if (result.FirstViolation)
        {
            DispatchServerMessage(player, Loc.GetString("chat-manager-repeat-rate-limited"), suppressLog: true);
            _adminLogger.Add(
                LogType.ChatRateLimited,
                LogImpact.Medium,
                $"Player {player} breached repeated chat spam limit with message '{TruncateForModerationLog(message)}'");
            HandleWh40KAutomaticSpamPunishment(player, WH40KChatSpamTrigger.RepeatRateLimit);
        }

        return RateLimitStatus.Blocked;
    }

    private void SweepRepeatRateLimitState(TimeSpan now)
    {
        if (_nextRepeatRateLimitSweep > now)
            return;

        _nextRepeatRateLimitSweep = now + TimeSpan.FromSeconds(30);

        List<NetUserId>? expiredUsers = null;
        foreach (var (userId, tracker) in _repeatRateLimitData)
        {
            if (!tracker.CleanupExpired(now))
                continue;

            expiredUsers ??= new List<NetUserId>();
            expiredUsers.Add(userId);
        }

        if (expiredUsers == null)
            return;

        foreach (var userId in expiredUsers)
        {
            _repeatRateLimitData.Remove(userId);
        }
    }

    private void RepeatRateLimitAlertAdmins(ICommonSession player, string message)
    {
        SendAdminAlert(Loc.GetString(
            "chat-manager-repeat-rate-limit-admin-announcement",
            ("player", player.Name),
            ("message", TruncateForModerationLog(message))));
    }

    private bool ShouldBypassWh40KChatRateLimit(ICommonSession player)
    {
        var activeAdminData = _adminManager.GetAdminData(player);
        var adminData = _adminManager.GetAdminData(player, includeDeAdmin: true);
        return WH40KStaffProtection.ShouldBypassChatRateLimits(
            activeAdminData,
            adminData,
            _adminManager.IsPromotedHost(player.UserId));
    }

    private void HandleWh40KAutomaticSpamPunishment(ICommonSession player, WH40KChatSpamTrigger trigger)
    {
        var deleteMessages = _configurationManager.GetCVar(
            trigger == WH40KChatSpamTrigger.RateLimit
                ? CCVars.ChatRateLimitDeleteMessages
                : CCVars.ChatRepeatRateLimitDeleteMessages);

        if (deleteMessages)
            DeletePlayerMessages(player.UserId);

        var punishmentRaw = _configurationManager.GetCVar(
            trigger == WH40KChatSpamTrigger.RateLimit
                ? CCVars.ChatRateLimitPunishment
                : CCVars.ChatRepeatRateLimitPunishment);

        if (ParseSpamPunishment(punishmentRaw) != WH40KChatSpamPunishment.Mute)
            return;

        var muteMinutes = Math.Max(1, _configurationManager.GetCVar(
            trigger == WH40KChatSpamTrigger.RateLimit
                ? CCVars.ChatRateLimitMuteMinutes
                : CCVars.ChatRepeatRateLimitMuteMinutes));

        _ = ApplyAutomaticChatMuteAsync(player, trigger, muteMinutes);
    }

    private void DeletePlayerMessages(NetUserId userId)
    {
        DeleteMessagesBy(userId);
        _entityManager.System<ChatRepositorySystem>().NukeForUserId(userId, out _);
    }

    private async Task ApplyAutomaticChatMuteAsync(
        ICommonSession player,
        WH40KChatSpamTrigger trigger,
        int muteMinutes)
    {
        try
        {
            await Mutes.ApplyMuteAsync(
                player.UserId,
                player.Name,
                WH40KMuteType.Chat,
                Loc.GetString(trigger switch
                {
                    WH40KChatSpamTrigger.RateLimit => "chat-manager-rate-limit-auto-mute-reason",
                    WH40KChatSpamTrigger.RepeatRateLimit => "chat-manager-repeat-rate-limit-auto-mute-reason",
                    _ => "chat-manager-rate-limit-auto-mute-reason"
                }),
                TimeSpan.FromMinutes(muteMinutes),
                adminUserId: null,
                eraseMessages: false);

            if (!_player.TryGetSessionById(player.UserId, out var current) || !Mutes.IsChatMuted(current, out _))
                return;

            DispatchServerMessage(current, Loc.GetString(trigger switch
            {
                WH40KChatSpamTrigger.RateLimit => "chat-manager-rate-limit-auto-muted",
                WH40KChatSpamTrigger.RepeatRateLimit => "chat-manager-repeat-rate-limit-auto-muted",
                _ => "chat-manager-rate-limit-auto-muted"
            }, ("minutes", muteMinutes)), suppressLog: true);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to apply automatic chat mute to {player}: {e}");
        }
    }

    private static WH40KChatSpamPunishment ParseSpamPunishment(string raw)
    {
        return raw.Trim().ToLowerInvariant() switch
        {
            "mute" => WH40KChatSpamPunishment.Mute,
            _ => WH40KChatSpamPunishment.None
        };
    }

    private static string TruncateForModerationLog(string value)
    {
        const int maxLength = 200;
        return value.Length <= maxLength ? value : $"{value[..maxLength]}...";
    }

    private enum WH40KChatSpamPunishment : byte
    {
        None,
        Mute,
    }

    private enum WH40KChatSpamTrigger : byte
    {
        RateLimit,
        RepeatRateLimit,
    }
}
