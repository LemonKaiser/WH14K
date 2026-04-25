using System.Collections.Generic;
using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._WH40K.Notifications;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._WH40K.Notifications;

[UsedImplicitly]
public sealed class WH40KNotificationUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    private const int HudTopMargin = 18;
    private static readonly SoundPathSpecifier AnnounceSound = new("/Audio/Announcements/announce.ogg");
    private static readonly AudioParams AnnounceSoundParams = AudioParams.Default.WithVolume(-4f);

    private readonly Queue<WH40KNotificationEvent> _pending = new();
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
        EnsureHud();
        if (_hud == null)
        {
            _pending.Enqueue(ev);
            return;
        }

        if (_hud.IsBusy)
        {
            _pending.Enqueue(ev);
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

        ShowNow(_pending.Dequeue());
    }

    private void TryShowPending()
    {
        if (_hud == null || _hud.IsBusy || _pending.Count == 0)
            return;

        ShowNow(_pending.Dequeue());
    }

    private void ShowNow(WH40KNotificationEvent ev)
    {
        if (_hud == null)
            return;

        _audio ??= EntityManager.System<SharedAudioSystem>();
        _audio?.PlayGlobal(AnnounceSound, Filter.Local(), false, AnnounceSoundParams);
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
}
