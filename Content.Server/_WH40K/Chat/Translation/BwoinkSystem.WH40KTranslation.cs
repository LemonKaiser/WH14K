using System;
using System.Collections.Generic;
using Content.Server._WH40K.Chat.Translation;
using Content.Server._WH40K.Localizations;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared._WH40K.Chat.Translation;
using Robust.Shared.Asynchronous;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using System.Threading.Tasks;

namespace Content.Server.Administration.Systems;

public sealed partial class BwoinkSystem
{
    [Dependency] private readonly ITaskManager _wh40kTaskManager = default!;
    [Dependency] private readonly IWH40KChatTranslationService _wh40kChatTranslation = default!;
    [Dependency] private readonly WH40KPlayerCultureTracker _wh40kPlayerCulture = default!;

    private bool TryDispatchTranslatedAHelp(
        SharedBwoinkSystem.BwoinkTextMessage message,
        ICommonSession senderSession,
        AdminData? senderAdmin,
        IList<INetChannel> admins,
        bool playSound)
    {
        if (!_wh40kChatTranslation.IsConfiguredForAHelp())
            return false;

        var fallbackLanguage = _wh40kPlayerCulture.ResolveLanguageCode(senderSession);
        var sourceLanguage = WH40KChatTranslationMarkup.ResolveLanguageFromText(message.Text, fallbackLanguage);
        if (!WH40KChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
            return false;

        ObserveAHelpTranslationTask(DispatchTranslatedAHelpAsync(
            message,
            senderSession,
            senderAdmin,
            admins,
            playSound,
            fallbackLanguage));
        return true;
    }

    private async Task DispatchTranslatedAHelpAsync(
        SharedBwoinkSystem.BwoinkTextMessage message,
        ICommonSession senderSession,
        AdminData? senderAdmin,
        IList<INetChannel> admins,
        bool playSound,
        string? fallbackLanguage)
    {
        var translation = await _wh40kChatTranslation.TranslateAHelpAsync(message.Text, fallbackLanguage);

        await RunAHelpTranslationOnMainThreadAsync(() =>
        {
            var adminStatusPrefix = ResolveAHelpStatusPrefix(message.AdminOnly, message.PlaySound);
            var adminSenderMarkup = BuildAHelpSenderMarkup(
                senderSession.Name,
                senderAdmin,
                _config.GetCVar(CCVars.AhelpAdminPrefix));

            foreach (var channel in admins)
            {
                var translatedText = BuildAHelpTextForChannel(
                    channel,
                    translation,
                    message.Text,
                    adminSenderMarkup,
                    adminStatusPrefix);

                RaiseNetworkEvent(new SharedBwoinkSystem.BwoinkTextMessage(
                        message.UserId,
                        senderSession.UserId,
                        translatedText,
                        playSound: playSound,
                        adminOnly: message.AdminOnly),
                    channel);
            }

            if (!_playerManager.TryGetSessionById(message.UserId, out var playerSession) ||
                message.AdminOnly ||
                admins.Contains(playerSession.Channel))
            {
                return;
            }

            var playerStatusPrefix = message.PlaySound
                ? null
                : Loc.GetString("bwoink-message-silent");

            var playerSenderMarkup = _overrideClientName != string.Empty
                ? BuildAHelpSenderMarkup(
                    senderAdmin != null ? _overrideClientName : senderSession.Name,
                    senderAdmin,
                    _config.GetCVar(CCVars.AhelpAdminPrefixWebhook))
                : adminSenderMarkup;

            var playerText = BuildAHelpTextForRecipient(
                playerSession,
                translation,
                message.Text,
                playerSenderMarkup,
                _overrideClientName != string.Empty ? playerStatusPrefix : adminStatusPrefix);

            RaiseNetworkEvent(new SharedBwoinkSystem.BwoinkTextMessage(
                    message.UserId,
                    senderSession.UserId,
                    playerText,
                    playSound: playSound,
                    adminOnly: false),
                playerSession.Channel);
        });
    }

    private string BuildAHelpTextForChannel(
        INetChannel channel,
        WH40KChatTranslationPayload? translation,
        string originalText,
        string senderMarkup,
        string? statusPrefix)
    {
        if (!_playerManager.TryGetSessionById(channel.UserId, out var recipient))
            return BuildPlainAHelpWrappedMessage(senderMarkup, originalText, statusPrefix);

        return BuildAHelpTextForRecipient(
            recipient,
            translation,
            originalText,
            senderMarkup,
            statusPrefix);
    }

    private string BuildAHelpTextForRecipient(
        ICommonSession recipient,
        WH40KChatTranslationPayload? translation,
        string originalText,
        string senderMarkup,
        string? statusPrefix)
    {
        if (translation == null)
            return BuildPlainAHelpWrappedMessage(senderMarkup, originalText, statusPrefix);

        if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(recipient, out var recipientLanguage))
            return BuildPlainAHelpWrappedMessage(senderMarkup, originalText, statusPrefix);

        var visibleText = translation.GetVisibleText(recipientLanguage);
        var source = translation.SourceLanguage;
        var original = translation.OriginalText;

        return WH40KChatTranslationFormatting.BuildAHelpWrappedMessage(
            senderMarkup,
            visibleText,
            recipientLanguage,
            source,
            original,
            statusPrefix);
    }

    private static string BuildPlainAHelpWrappedMessage(string senderMarkup, string visibleText, string? statusPrefix)
    {
        var escapedMessage = FormattedMessage.EscapeText(visibleText);
        return string.IsNullOrWhiteSpace(statusPrefix)
            ? $"{senderMarkup}: {escapedMessage}"
            : $"{statusPrefix} {senderMarkup}: {escapedMessage}";
    }

    private static string BuildAHelpSenderMarkup(string senderName, AdminData? senderAdmin, bool includePrefix)
    {
        var adminPrefix = includePrefix && senderAdmin?.Title is { Length: > 0 } title
            ? $"[bold]\\[{title}\\][/bold] "
            : string.Empty;

        if (senderAdmin is not null && senderAdmin.Flags == AdminFlags.Adminhelp)
            return $"[color=purple]{adminPrefix}{senderName}[/color]";

        if (senderAdmin is not null && senderAdmin.HasFlag(AdminFlags.Adminhelp))
            return $"[color=red]{adminPrefix}{senderName}[/color]";

        return senderName;
    }

    private string? ResolveAHelpStatusPrefix(bool adminOnly, bool playSound)
    {
        if (adminOnly)
            return Loc.GetString("bwoink-message-admin-only");

        if (!playSound)
            return Loc.GetString("bwoink-message-silent");

        return null;
    }

    private void ObserveAHelpTranslationTask(Task task)
    {
        _ = ObserveAHelpTranslationTaskAsync(task);
    }

    private async Task ObserveAHelpTranslationTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            Log.Error($"WH40K ahelp translation task failed: {e}");
        }
    }

    private Task RunAHelpTranslationOnMainThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource();
        _wh40kTaskManager.RunOnMainThread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        return tcs.Task;
    }
}
