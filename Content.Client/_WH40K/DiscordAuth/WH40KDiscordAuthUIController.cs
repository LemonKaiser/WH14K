using Content.Shared._WH40K.DiscordAuth;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.GameObjects;

namespace Content.Client._WH40K.DiscordAuth;

[UsedImplicitly]
public sealed class WH40KDiscordAuthUIController : UIController
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private WH40KDiscordAuthSystem? _discordAuth;
    private WH40KDiscordAuthStatusWindow? _window;

    public bool ShouldShowEntry()
    {
        if (!TryActivateDiscordAuth())
            return false;

        _discordAuth!.EnsureSnapshot();
        return true;
    }

    public void OpenWindowOrStartLink()
    {
        if (!TryActivateDiscordAuth())
            return;

        if (_discordAuth!.TryGetCachedSnapshot(out var snapshot))
        {
            if (snapshot.IsLinked)
            {
                OpenWindow(snapshot);
                return;
            }
        }

        _discordAuth.StartLinkFlow();
    }

    public void OpenWindowForSnapshot(WH40KDiscordAuthSnapshot snapshot)
    {
        if (!TryActivateDiscordAuth())
            return;

        if (!snapshot.IsLinked)
        {
            _discordAuth!.StartLinkFlow();
            return;
        }

        OpenWindow(snapshot);
    }

    private void OnDiscordSnapshotUpdated(WH40KDiscordAuthSnapshot snapshot)
    {
        if (_window == null)
            return;

        if (!snapshot.IsLinked)
        {
            CloseWindow();
            return;
        }

        _window.ApplySnapshot(snapshot);
    }

    private void OpenWindow(WH40KDiscordAuthSnapshot snapshot)
    {
        if (_window != null && !_window.Disposed)
        {
            _window.ApplySnapshot(snapshot);
            _window.MoveToFront();
            return;
        }

        _window = new WH40KDiscordAuthStatusWindow();
        _window.ApplySnapshot(snapshot);
        _window.RefreshButton.OnPressed += _ =>
        {
            _window?.MarkRefreshPending();
            _discordAuth?.RefreshProfile();
        };
        _window.UnlinkButton.OnPressed += _ =>
        {
            _discordAuth?.Unlink();
            CloseWindow();
        };
        _window.OnClose += () => _window = null;
        _window.OpenCentered();
    }

    private void CloseWindow()
    {
        if (_window == null)
            return;

        if (!_window.Disposed)
            _window.Close();

        _window = null;
    }

    private bool TryActivateDiscordAuth()
    {
        if (_discordAuth != null)
            return true;

        if (!_entityManager.TrySystem(out WH40KDiscordAuthSystem? discordAuth))
            return false;

        _discordAuth = discordAuth;
        _discordAuth.SnapshotUpdated += OnDiscordSnapshotUpdated;
        _discordAuth.EnsureSnapshot();
        return true;
    }
}
