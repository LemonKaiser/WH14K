using System;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Client._WH40K.LateJoin;
using Content.Shared.CCVar;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.Interface;
using Content.Shared._WH40K.LateJoin;
using Robust.Client.Console;
using Robust.Client.State;
using Robust.Shared;
using Robust.Shared.Console;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Interface;

/// <summary>
/// Resolves WH40K UI themes from the player's saved preference.
/// Auto follows faction assignment; explicit themes stay fixed until changed by the player.
/// </summary>
public sealed partial class WH40KInterfaceThemeSystem : EntitySystem
{
    private const string TeamIdentityMapId = "WH40KTeamIdentityMap";
    private const string TeamIdentityDefaultProfileId = "WH40KTeamIdentityProfileImperium";

    [Dependency] private  IConfigurationManager _cfg = default!;
    [Dependency] private  IClientConsoleHost _console = default!;
    [Dependency] private  IStateManager _state = default!;
    [Dependency] private  IPrototypeManager _proto = default!;

    private ClientGameTicker _ticker = default!;
    private WH40KFactionSystem _factions = default!;
    private bool _lastRoundStarted;
    private bool _wh40kRoundActive;
    private bool _settingPreferenceInternally;
    private string? _currentTeamId;

    public string? CurrentTeamId => _currentTeamId;

    public override void Initialize()
    {
        base.Initialize();

        _ticker = EntityManager.System<ClientGameTicker>();
        _factions = EntityManager.System<WH40KFactionSystem>();
        _lastRoundStarted = _ticker.IsGameStarted;

        _ticker.LobbyStatusUpdated += OnLobbyStatusUpdated;
        _factions.FactionsUpdated += OnFactionsUpdated;
        _factions.FactionSelectionResultReceived += OnFactionSelectionResultReceived;
        _console.AnyCommandExecuted += OnAnyCommandExecuted;

        SubscribeNetworkEvent<WH40KTeamThemeAssignedEvent>(OnThemeAssignment);

        MigrateLegacyThemePreference();
        Subs.CVar(_cfg, CCVars.WH40KInterfaceThemePreference, OnThemePreferenceChanged, true);

        if (_ticker.IsGameStarted)
            _factions.RequestFactions(WH40KFactionSelectionPurpose.Preview, force: true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _ticker.LobbyStatusUpdated -= OnLobbyStatusUpdated;
        _factions.FactionsUpdated -= OnFactionsUpdated;
        _factions.FactionSelectionResultReceived -= OnFactionSelectionResultReceived;
        _console.AnyCommandExecuted -= OnAnyCommandExecuted;
    }

    public void NotifyFactionSelected(string factionId)
    {
        _currentTeamId = NormalizeTeamId(factionId);
        RefreshThemeForCurrentContext();
    }

    private void OnLobbyStatusUpdated()
    {
        var started = _ticker.IsGameStarted;

        if (started && !_lastRoundStarted)
        {
            _currentTeamId = null;
        }
        else if (!started && _lastRoundStarted)
        {
            _wh40kRoundActive = false;
            _currentTeamId = null;
        }

        _lastRoundStarted = started;

        if (started)
            _factions.RequestFactions(WH40KFactionSelectionPurpose.Preview, force: true);

        RefreshThemeForCurrentContext();
    }

    private void OnFactionsUpdated(WH40KFactionsEvent ev)
    {
        _wh40kRoundActive = _ticker.IsGameStarted && ev.Factions.Count > 0;

        if (!_wh40kRoundActive)
            _currentTeamId = null;

        RefreshThemeForCurrentContext();
    }

    private void OnFactionSelectionResultReceived(WH40KFactionSelectionResultEvent ev)
    {
        if (!ev.Accepted)
            return;

        _currentTeamId = NormalizeTeamId(ev.FactionId);
        RefreshThemeForCurrentContext();
    }

    private void OnThemeAssignment(WH40KTeamThemeAssignedEvent ev, EntitySessionEventArgs args)
    {
        _wh40kRoundActive = true;
        _currentTeamId = NormalizeTeamId(ev.TeamId);
        RefreshThemeForCurrentContext();
    }

    private void OnAnyCommandExecuted(IConsoleShell _shell, string commandName, string _argStr, string[] args)
    {
        if (!commandName.Equals("observe", StringComparison.OrdinalIgnoreCase))
            return;

        if (_state.CurrentState is not LobbyState)
            return;

        if (args.Length > 0 && args[0].Equals("admin", StringComparison.OrdinalIgnoreCase))
            return;

        _currentTeamId = null;
        RefreshThemeForCurrentContext();
    }

    private void OnThemePreferenceChanged(string preference)
    {
        if (_settingPreferenceInternally)
            return;

        ApplyThemePreference(preference);
    }

    private void RefreshThemeForCurrentContext()
    {
        ApplyThemePreference(_cfg.GetCVar(CCVars.WH40KInterfaceThemePreference));
    }

    private string? ResolveThemeByTeam(string teamId)
    {
        if (TryResolveTeamIdentityProfile(teamId, out var profile) &&
            !string.IsNullOrWhiteSpace(profile.InterfaceThemeId))
        {
            return profile.InterfaceThemeId;
        }

        if (teamId.Equals("Imperium", StringComparison.OrdinalIgnoreCase))
            return WH40KInterfaceThemes.Imperium;

        if (teamId.Equals("Heretics", StringComparison.OrdinalIgnoreCase))
            return WH40KInterfaceThemes.Heretics;

        return null;
    }

    private bool TryResolveTeamIdentityProfile(string teamId, out WH40KTeamIdentityProfilePrototype profile)
    {
        profile = default!;
        var profileId = ResolveTeamIdentityProfileId(teamId);
        if (_proto.TryIndex(profileId, out WH40KTeamIdentityProfilePrototype? indexedProfile))
        {
            profile = indexedProfile;
            return true;
        }

        if (_proto.TryIndex(TeamIdentityDefaultProfileId, out WH40KTeamIdentityProfilePrototype? fallbackProfile))
        {
            profile = fallbackProfile;
            return true;
        }

        return false;
    }

    private ProtoId<WH40KTeamIdentityProfilePrototype> ResolveTeamIdentityProfileId(string teamId)
    {
        if (!_proto.TryIndex(TeamIdentityMapId, out WH40KTeamIdentityMapPrototype? teamMap))
            return TeamIdentityDefaultProfileId;

        if (!string.IsNullOrWhiteSpace(teamId))
        {
            if (teamMap.TeamProfiles.TryGetValue(teamId, out var directProfile))
                return directProfile;

            foreach (var (mappedTeamId, mappedProfile) in teamMap.TeamProfiles)
            {
                if (string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    return mappedProfile;
            }
        }

        return teamMap.DefaultProfile;
    }

    private void ApplyThemePreference(string preference)
    {
        if (WH40KInterfaceThemes.IsAuto(preference))
        {
            ApplyTheme(ResolveAutoTheme());
            return;
        }

        ApplyTheme(preference);
    }

    private string ResolveAutoTheme()
    {
        if (!_ticker.IsGameStarted || !_wh40kRoundActive || string.IsNullOrWhiteSpace(_currentTeamId))
            return WH40KInterfaceThemes.Default;

        return ResolveThemeByTeam(_currentTeamId) ?? WH40KInterfaceThemes.Default;
    }

    private void MigrateLegacyThemePreference()
    {
        var preference = _cfg.GetCVar(CCVars.WH40KInterfaceThemePreference);
        if (!WH40KInterfaceThemes.IsAuto(preference))
            return;

        var currentTheme = _cfg.GetCVar(CVars.InterfaceTheme);
        if (!IsLegacyManualTheme(currentTheme))
            return;

        SetThemePreference(currentTheme);
    }

    private bool IsLegacyManualTheme(string themeId)
    {
        return !string.IsNullOrWhiteSpace(themeId) &&
               !string.Equals(themeId, WH40KInterfaceThemes.Default, StringComparison.Ordinal) &&
               !WH40KInterfaceThemes.IsFactionTheme(themeId);
    }

    private void SetThemePreference(string preference)
    {
        if (_cfg.GetCVar(CCVars.WH40KInterfaceThemePreference) == preference)
            return;

        _settingPreferenceInternally = true;
        _cfg.SetCVar(CCVars.WH40KInterfaceThemePreference, preference);
        _settingPreferenceInternally = false;
    }

    private static string? NormalizeTeamId(string? teamId)
    {
        return string.IsNullOrWhiteSpace(teamId) ? null : teamId;
    }

    private void ApplyTheme(string themeId)
    {
        if (_cfg.GetCVar(CVars.InterfaceTheme) == themeId)
            return;

        _cfg.SetCVar(CVars.InterfaceTheme, themeId);
    }
}
