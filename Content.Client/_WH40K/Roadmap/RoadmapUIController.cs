using Content.Client.Credits;
using Content.Client.Stylesheets;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;

namespace Content.Client._WH40K.Roadmap;

public sealed class RoadmapUIController : UIController
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IUriOpener _uriOpener = default!;

    private RoadmapWindow? _window;

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
            _window.DiscordButton.StyleClasses.Add(StyleNano.ButtonCaution);
            _window.DiscordButton.OnPressed += _ => _uriOpener.OpenUri(discordLink);
        }

        _window.CreditsButton.StyleClasses.Add(StyleNano.ButtonCaution);
        _window.CreditsButton.OnPressed += _ => new CreditsWindow().OpenCentered();

        _window.OpenCentered();
    }

    public void RefreshLocalization()
    {
        if (_window is not { Disposed: false })
            return;

        var wasOpen = _window.IsOpen;
        _window.Dispose();
        _window = null;

        if (wasOpen)
            ToggleRoadmap();
    }
}
