using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared.Chat;
using Content.Shared.CCVar;
using Content.Shared._WH40K.Notifications;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Notifications;

[UsedImplicitly]
public sealed partial class WH40KNotificationUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    private const int HudTopMargin = 18;
    private const int MaxPendingNotifications = 12;
    private static readonly SoundSpecifier DefaultNotificationSound = new SoundPathSpecifier("/Audio/Announcements/announce.ogg");

    [Dependency] private  IConfigurationManager _cfg = default!;
    [Dependency] private  ILocalizationManager _loc = default!;

    private readonly List<WH40KNotificationEvent> _pending = new();
    private SharedAudioSystem? _audio;
    private WH40KNotificationHudControl? _hud;

    public override void Initialize()
    {
        base.Initialize();

        var gameplayStateLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad += OnScreenLoad;
        gameplayStateLoad.OnScreenUnload += OnScreenUnload;
    }

    public void OnStateEntered(GameplayState state)
    {
        EnsureHud();
    }

    public void OnStateExited(GameplayState state)
    {
        ShutdownHud();
    }

    public void Push(WH40KNotificationEvent ev)
    {
        ev = NormalizeNotification(ev);

        if (ShouldMirrorToChat(ev))
            PushToChat(ev);

        if (!ShouldAcceptHud(ev))
            return;

        EnsureHud();
        if (_hud == null)
        {
            QueueNotification(ev);
            return;
        }

        if (_hud.IsBusy)
        {
            QueueNotification(ev);
            return;
        }

        ShowNow(ev);
    }

    private void OnScreenLoad()
    {
        EnsureHud();
        TryShowPending();
    }

    private void OnScreenUnload()
    {
        ShutdownHud();
    }

    private void EnsureHud()
    {
        if (_hud is { Disposed: true })
            _hud = null;

        if (_hud != null || UIManager.ActiveScreen == null)
            return;

        if (UIManager.ActiveScreen.GetWidget<MainViewport>()?.Parent is not LayoutContainer viewportLayout)
            return;

        _hud = new WH40KNotificationHudControl();
        _hud.NotificationClosed += OnNotificationClosed;

        viewportLayout.AddChild(_hud);
        LayoutContainer.SetAnchorAndMarginPreset(_hud, LayoutContainer.LayoutPreset.CenterTop, margin: HudTopMargin);
        LayoutContainer.SetPosition(_hud, new Vector2(-WH40KNotificationHudControl.CanvasWidth * 0.5f, 0f));
        _hud.SetPositionLast();
    }

    private void OnNotificationClosed()
    {
        if (_hud == null || _pending.Count == 0)
            return;

        var next = _pending[0];
        _pending.RemoveAt(0);
        ShowNow(next);
    }

    private void TryShowPending()
    {
        if (_hud == null || _hud.IsBusy || _pending.Count == 0)
            return;

        var next = _pending[0];
        _pending.RemoveAt(0);
        ShowNow(next);
    }

    private void ShowNow(WH40KNotificationEvent ev)
    {
        if (_hud == null)
            return;

        _audio ??= EntityManager.System<SharedAudioSystem>();
        PlaySound(ev);
        _hud.Show(ev);
    }

    private void ShutdownHud()
    {
        _pending.Clear();

        if (_hud == null)
            return;

        _hud.NotificationClosed -= OnNotificationClosed;
        if (!_hud.Disposed)
            _hud.Orphan();

        _hud = null;
    }

    private WH40KNotificationEvent NormalizeNotification(WH40KNotificationEvent ev)
    {
        var category = ev.Category == WH40KNotificationCategory.Auto
            ? InferDirectCategory(ev)
            : ev.Category;
        var priority = ev.Priority == WH40KNotificationPriority.Auto
            ? WH40KNotificationMetadata.DefaultPriority(category)
            : ev.Priority;
        var icon = ev.Icon == WH40KNotificationIcon.Auto
            ? WH40KNotificationMetadata.DefaultIcon(category, ev.AccentColor)
            : ev.Icon;
        var title = ResolveTitle(ev.Title, category, ev.AccentColor);

        var size = ev.Size;
        if (!ev.IgnoreUserPreferences)
        {
            var mode = _cfg.GetCVar(CCVars.WH40KNotificationDisplayMode);
            if (string.Equals(mode, WH40KNotificationMetadata.DisplayModeCompact, StringComparison.OrdinalIgnoreCase))
            {
                size = WH40KNotificationSize.Compact;
            }
        }

        var stackKey = string.IsNullOrWhiteSpace(ev.StackKey)
            ? BuildDefaultStackKey(category)
            : ev.StackKey.Trim();

        return new WH40KNotificationEvent(
            title,
            ev.Text,
            ev.AccentColor,
            ev.DurationSeconds,
            ev.Marquee,
            size,
            category,
            priority,
            icon,
            stackKey,
            ev.IgnoreUserPreferences,
            ev.Sound);
    }

    private string ResolveTitle(string title, WH40KNotificationCategory category, Color accentColor)
    {
        if (IsDefaultVoxTitle(title))
            title = WH40KNotificationMetadata.DefaultTitle(category, accentColor);

        return _loc.TryGetString(title, out var localizedTitle)
            ? localizedTitle
            : title;
    }

    private bool IsDefaultVoxTitle(string title)
    {
        if (string.Equals(title, "wh40k-notification-title-vox", StringComparison.Ordinal) ||
            string.Equals(title, "Vox report", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _loc.TryGetString("wh40k-notification-title-vox", out var localizedVox) &&
               string.Equals(title, localizedVox, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldAcceptHud(WH40KNotificationEvent ev)
    {
        if (ev.IgnoreUserPreferences || ev.Category == WH40KNotificationCategory.Admin)
            return true;

        var mode = _cfg.GetCVar(CCVars.WH40KNotificationDisplayMode);
        if (string.Equals(mode, WH40KNotificationMetadata.DisplayModeOff, StringComparison.OrdinalIgnoreCase))
            return false;

        return GetEnabledCategories().Contains(ev.Category);
    }

    private bool ShouldMirrorToChat(WH40KNotificationEvent ev)
    {
        if (ev.IgnoreUserPreferences || ev.Category == WH40KNotificationCategory.Admin)
            return true;

        return _cfg.GetCVar(CCVars.WH40KNotificationChatEnabled);
    }

    private void PushToChat(WH40KNotificationEvent ev)
    {
        var chatController = UIManager.GetUIController<ChatUIController>();
        var title = string.IsNullOrWhiteSpace(ev.Title) ? null : ev.Title.Trim();
        var text = ev.Text.Trim();
        var message = string.IsNullOrWhiteSpace(title)
            ? text
            : $"{title}: {text}";

        var escaped = FormattedMessage.EscapeText(message);
        var wrapped = Loc.GetString("chat-manager-server-wrap-message", ("message", escaped));

        var chatMessage = new ChatMessage(
            ChatChannel.Notifications,
            message,
            wrapped,
            NetEntity.Invalid,
            null,
            hideChat: false,
            colorOverride: ev.AccentColor);

        chatController.ProcessChatMessage(chatMessage, speechBubble: false);
    }

    private HashSet<WH40KNotificationCategory> GetEnabledCategories()
    {
        var result = new HashSet<WH40KNotificationCategory>();
        foreach (var part in _cfg.GetCVar(CCVars.WH40KNotificationEnabledCategories)
                     .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (WH40KNotificationMetadata.TryParseCategory(part, out var category))
                result.Add(category);
        }

        return result;
    }

    private void QueueNotification(WH40KNotificationEvent ev)
    {
        if (!string.IsNullOrWhiteSpace(ev.StackKey))
        {
            var existing = _pending.FindIndex(pending =>
                string.Equals(pending.StackKey, ev.StackKey, StringComparison.OrdinalIgnoreCase));

            if (existing >= 0)
            {
                _pending.RemoveAt(existing);
            }
        }

        var insertAt = _pending.FindIndex(pending => pending.Priority < ev.Priority);
        if (insertAt < 0)
            _pending.Add(ev);
        else
            _pending.Insert(insertAt, ev);

        if (_pending.Count <= MaxPendingNotifications)
            return;

        var lowestIndex = _pending.Count - 1;
        if (_pending[lowestIndex].Priority >= ev.Priority && !ReferenceEquals(_pending[lowestIndex], ev))
            return;

        _pending.RemoveAt(lowestIndex);
    }

    private void PlaySound(WH40KNotificationEvent ev)
    {
        var volume = ev.IgnoreUserPreferences || ev.Category == WH40KNotificationCategory.Admin
            ? 1f
            : Math.Clamp(_cfg.GetCVar(CCVars.WH40KNotificationSoundVolume), 0f, 1f);

        if (volume <= 0.001f)
            return;

        var gain = Lerp(-24f, -4f, volume);
        _audio?.PlayGlobal(ev.Sound ?? DefaultNotificationSound, Filter.Local(), false, AudioParams.Default.WithVolume(gain));
    }

    private static WH40KNotificationCategory InferDirectCategory(WH40KNotificationEvent ev)
    {
        if (ev.AccentColor.Equals(WH40KNotificationColors.Weather))
            return WH40KNotificationCategory.Weather;

        if (ev.AccentColor.Equals(WH40KNotificationColors.Objective))
            return WH40KNotificationCategory.Objective;

        if (ev.AccentColor.Equals(WH40KNotificationColors.Admin))
            return WH40KNotificationCategory.Admin;

        if (ev.AccentColor.Equals(WH40KNotificationColors.Event))
            return WH40KNotificationCategory.Event;

        return WH40KNotificationCategory.Info;
    }

    private static string BuildDefaultStackKey(WH40KNotificationCategory category)
    {
        return category switch
        {
            WH40KNotificationCategory.Admin => string.Empty,
            WH40KNotificationCategory.Critical => string.Empty,
            WH40KNotificationCategory.Point => "category:point",
            WH40KNotificationCategory.Weather => "category:weather",
            WH40KNotificationCategory.Event => "category:event",
            WH40KNotificationCategory.Objective => "category:objective",
            WH40KNotificationCategory.Mission => "category:mission",
            WH40KNotificationCategory.Economy => "category:economy",
            WH40KNotificationCategory.Reinforcement => "category:reinforcement",
            _ => "category:info"
        };
    }

    private static float Lerp(float from, float to, float value)
    {
        return from + (to - from) * Math.Clamp(value, 0f, 1f);
    }
}
