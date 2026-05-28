using Content.Client.FeedbackPopup;
using Content.Client.Gameplay;
using Content.Client._WH40K.DiscordAuth;
using Content.Client._WH40K.Roadmap;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Client.UserInterface.Systems.Info;
using Content.Shared.CCVar;
using JetBrains.Annotations;
using Robust.Client.Console;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client.UserInterface.Systems.EscapeMenu;

[UsedImplicitly]
public sealed partial class EscapeUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private  IClientConsoleHost _console = default!;
    [Dependency] private  IUriOpener _uri = default!;
    [Dependency] private  IConfigurationManager _cfg = default!;
    [Dependency] private  ChangelogUIController _changelog = default!;
    [Dependency] private  InfoUIController _info = default!;
    [Dependency] private  OptionsUIController _options = default!;
    [Dependency] private  GuidebookUIController _guidebook = default!;
    [Dependency] private  WH40KDiscordAuthUIController _discordAuth = default!;
    [Dependency] private  RoadmapUIController _roadmap = default!;
    [Dependency] private  FeedbackPopupUIController _feedback = null!;

    private Options.UI.EscapeMenu? _escapeWindow;

    private MenuButton? EscapeButton => UIManager.GetActiveUIWidgetOrNull<MenuBar.Widgets.GameTopMenuBar>()?.EscapeButton;

    public void UnloadButton()
    {
        if (EscapeButton == null)
        {
            return;
        }

        EscapeButton.Pressed = false;
        EscapeButton.OnPressed -= EscapeButtonOnOnPressed;
    }

    public void LoadButton()
    {
        if (EscapeButton == null)
        {
            return;
        }

        EscapeButton.OnPressed += EscapeButtonOnOnPressed;
    }

    private void ActivateButton() => EscapeButton!.SetClickPressed(true);
    private void DeactivateButton() => EscapeButton!.SetClickPressed(false);

    public void OnStateEntered(GameplayState state)
    {
        DebugTools.Assert(_escapeWindow == null);

        _escapeWindow = CreateEscapeWindow();

        CommandBinds.Builder
            .Bind(EngineKeyFunctions.EscapeMenu,
                InputCmdHandler.FromDelegate(_ => ToggleWindow()))
            .Register<EscapeUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        if (_escapeWindow != null)
        {
            if (!_escapeWindow.Disposed)
                _escapeWindow.Orphan();

            _escapeWindow = null;
        }

        CommandBinds.Unregister<EscapeUIController>();
    }

    private void EscapeButtonOnOnPressed(ButtonEventArgs obj)
    {
        ToggleWindow();
    }

    private void CloseEscapeWindow()
    {
        _escapeWindow?.Close();
    }

    private Options.UI.EscapeMenu CreateEscapeWindow()
    {
        var window = UIManager.CreateWindow<Options.UI.EscapeMenu>();

        window.OnClose += DeactivateButton;
        window.OnOpen += ActivateButton;

        window.FeedbackButton.OnPressed += _ =>
        {
            CloseEscapeWindow();
            _feedback.ToggleWindow();
        };

        window.ChangelogButton.OnPressed += _ =>
        {
            CloseEscapeWindow();
            _changelog.ToggleWindow();
        };

        window.RoadmapButton.OnPressed += _ =>
        {
            CloseEscapeWindow();
            _roadmap.ToggleRoadmap();
        };

        window.DiscordButton.OnPressed += _ =>
        {
            CloseEscapeWindow();
            _discordAuth.OpenWindowOrStartLink();
        };

        window.RulesButton.OnPressed += _ =>
        {
            CloseEscapeWindow();
            _info.OpenWindow();
        };

        window.DisconnectButton.OnPressed += _ =>
        {
            CloseEscapeWindow();
            _console.ExecuteCommand("disconnect");
        };

        window.OptionsButton.OnPressed += _ =>
        {
            CloseEscapeWindow();
            _options.OpenWindow();
        };

        window.QuitButton.OnPressed += _ =>
        {
            CloseEscapeWindow();
            _console.ExecuteCommand("quit");
        };

        window.WikiButton.OnPressed += _ =>
        {
            _uri.OpenUri(_cfg.GetCVar(CCVars.InfoLinksWiki));
        };

        window.GuidebookButton.OnPressed += _ =>
        {
            _guidebook.ToggleGuidebook();
        };

        window.WikiButton.Visible = _cfg.GetCVar(CCVars.InfoLinksWiki) != "";
        window.DiscordButton.Visible = _discordAuth.ShouldShowEntry();
        return window;
    }

    /// <summary>
    /// Toggles the game menu.
    /// </summary>
    public void ToggleWindow()
    {
        if (_escapeWindow == null)
            return;

        if (_escapeWindow.IsOpen)
        {
            CloseEscapeWindow();
            EscapeButton!.Pressed = false;
        }
        else
        {
            _escapeWindow.OpenCentered();
            EscapeButton!.Pressed = true;
        }
    }

    public void RefreshLocalization()
    {
        if (_escapeWindow == null)
            return;

        var wasOpen = _escapeWindow.IsOpen;
        _escapeWindow.Orphan();
        _escapeWindow = CreateEscapeWindow();

        if (!wasOpen)
            return;

        _escapeWindow.OpenCentered();
        if (EscapeButton != null)
            EscapeButton.Pressed = true;
    }
}
