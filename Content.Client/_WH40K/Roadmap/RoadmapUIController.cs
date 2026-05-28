using Content.Client.Credits;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;

namespace Content.Client._WH40K.Roadmap;

public sealed partial class RoadmapUIController : UIController
{
    [Dependency] private  IConfigurationManager _config = default!;
    [Dependency] private  IUriOpener _uriOpener = default!;

    private RoadmapWindow? _window;
    private CreditsWindow? _creditsWindow;

    public void ToggleRoadmap()
    {
        if (_window != null)
        {
            _window.Close();
            _window = null;
            return;
        }

        _window = UIManager.CreateWindow<RoadmapWindow>();
        _window.OnClose += () => _window = null;

        if (_config.GetCVar(CCVars.InfoLinksDiscord) is { Length: > 0 } discordLink)
        {
            _window.DiscordButton.Visible = true;
            _window.DiscordButton.OnPressed += _ => _uriOpener.OpenUri(discordLink);
        }

        _window.CreditsButton.OnPressed += _ =>
        {
            if (_creditsWindow?.IsOpen == true)
            {
                _creditsWindow.MoveToFront();
                return;
            }

            _creditsWindow = new CreditsWindow();
            _creditsWindow.OpenCentered();
        };

        _window.OpenCentered();
    }

    public void RefreshLocalization()
    {
        if (_window is not { Disposed: false })
            return;

        var wasOpen = _window.IsOpen;
        _window.Close();
        _window = null;

        if (wasOpen)
            ToggleRoadmap();
    }
}
