using Robust.Shared;
using Robust.Shared.Configuration;
using Content.Client.Gameplay;
using Content.Client.Info;
using Content.Shared.Guidebook;
using Content.Shared.Info;
using Robust.Client.Console;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Client.UserInterface.Systems.Info;

public sealed partial class InfoUIController : UIController, IOnStateExited<GameplayState>
{
    private const string RussianCultureName = "ru-RU";
    private const string EnglishCultureName = "en-US";

    [Dependency] private  IConfigurationManager _cfg = default!;
    [Dependency] private  IClientConsoleHost _consoleHost = default!;
    [Dependency] private  INetManager _netManager = default!;
    [Dependency] private  IPrototypeManager _prototype = default!;

    private RulesPopup? _rulesPopup;
    private RulesAndInfoWindow? _infoWindow;

    private static readonly ProtoId<GuideEntryPrototype> DefaultRuleset = "DefaultRuleset";

    public ProtoId<GuideEntryPrototype> RulesEntryId = DefaultRuleset;

    protected override string SawmillName => "rules";

    public override void Initialize()
    {
        base.Initialize();

        _netManager.RegisterNetMessage<RulesAcceptedMessage>();
        _netManager.RegisterNetMessage<SendRulesInformationMessage>(OnRulesInformationMessage);

        _consoleHost.RegisterCommand("fuckrules",
            "",
            "",
            (_, _, _) =>
        {
            OnAcceptPressed(true);
        });
    }

    private void OnRulesInformationMessage(SendRulesInformationMessage message)
    {
        RulesEntryId = message.CoreRules;

        if (message.ShouldShowRules)
            ShowRules(message.PopupTime);
    }

    public void OnStateExited(GameplayState state)
    {
        if (_infoWindow == null)
            return;

        if (!_infoWindow.Disposed)
            _infoWindow.Orphan();

        _infoWindow = null;
    }

    private void ShowRules(float time)
    {
        if (_rulesPopup != null)
            return;

        _rulesPopup = new RulesPopup
        {
            Timer = time
        };

        _rulesPopup.OnQuitPressed += OnQuitPressed;
        _rulesPopup.OnContinuePressed += OnContinuePressed;
        UIManager.WindowRoot.AddChild(_rulesPopup);
        LayoutContainer.SetAnchorPreset(_rulesPopup, LayoutContainer.LayoutPreset.Wide);
    }

    private void OnQuitPressed()
    {
        _consoleHost.ExecuteCommand("quit");
    }

    private void OnContinuePressed(string cultureName)
    {
        if (cultureName == RussianCultureName || cultureName == EnglishCultureName)
            _cfg.SetCVar(CVars.LocCultureName, cultureName);

        OnAcceptPressed(false);
    }

    private void OnAcceptPressed(bool fuckRules)
    {
        var message = new RulesAcceptedMessage() { FuckRules = fuckRules };
        _netManager.ClientSendMessage(message);

        if (_rulesPopup is { Disposed: false })
            _rulesPopup.Orphan();

        _rulesPopup = null;
    }

    public GuideEntryPrototype GetCoreRuleEntry()
    {
        if (!_prototype.TryIndex(RulesEntryId, out var guideEntryPrototype))
        {
            guideEntryPrototype = _prototype.Index(DefaultRuleset);
            Log.Error($"Couldn't find the following prototype: {RulesEntryId}. Falling back to {DefaultRuleset}, please check that the server has the rules set up correctly");
            return guideEntryPrototype;
        }

        return guideEntryPrototype;
    }

    public void OpenWindow()
    {
        if (_infoWindow == null || _infoWindow.Disposed)
            _infoWindow = UIManager.CreateWindow<RulesAndInfoWindow>();

        _infoWindow?.OpenCentered();
    }

    public void RefreshLocalization()
    {
        if (_infoWindow is not { Disposed: false })
            return;

        var wasOpen = _infoWindow.IsOpen;
        _infoWindow.Orphan();
        _infoWindow = null;

        if (wasOpen)
            OpenWindow();
    }
}
