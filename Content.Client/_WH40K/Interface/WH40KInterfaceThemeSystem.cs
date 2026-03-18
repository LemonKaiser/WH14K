using System;
using System.Collections.Generic;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Client._WH40K.LateJoin;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.Interface;
using Robust.Client.Console;
using Robust.Client.State;
using Robust.Shared;
using Robust.Shared.Console;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Interface;

/// <summary>
/// Auto-assigns WH40K UI themes once per round based on team selection/spawn.
/// Players can still manually switch themes mid-round.
/// </summary>
public sealed class WH40KInterfaceThemeSystem : EntitySystem
{
    private const string TeamIdentityMapId = "WH40KTeamIdentityMap";
    private const string TeamIdentityDefaultProfileId = "WH40KTeamIdentityProfileImperium";
    private const string DefaultThemeId = "SS14DefaultTheme";
    private const string ImperiumThemeId = "WH40KImperiumTheme";
    private const string HereticsThemeId = "WH40KChaosTheme";

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IClientConsoleHost _console = default!;
    [Dependency] private readonly IStateManager _state = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private ClientGameTicker _ticker = default!;
    private WH40KFactionSystem _factions = default!;
    private bool _lastRoundStarted;
    private bool _autoAssignedThisRound;
    private bool _manualOverrideThisRound;
    private bool _wh40kRoundActive;
    private bool _settingThemeInternally;
    private bool _trackManualThemeChanges;

    public override void Initialize()
    {
        base.Initialize();

        _ticker = EntityManager.System<ClientGameTicker>();
        _factions = EntityManager.System<WH40KFactionSystem>();
        _lastRoundStarted = _ticker.IsGameStarted;

        _ticker.LobbyStatusUpdated += OnLobbyStatusUpdated;
        _factions.FactionsUpdated += OnFactionsUpdated;
        _console.AnyCommandExecuted += OnAnyCommandExecuted;

        SubscribeNetworkEvent<WH40KTeamThemeAssignedEvent>(OnThemeAssignment);

        Subs.CVar(_cfg, CVars.InterfaceTheme, OnInterfaceThemeChanged, true);
        _trackManualThemeChanges = true;

        if (_ticker.IsGameStarted)
            _factions.RequestFactions(force: true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _ticker.LobbyStatusUpdated -= OnLobbyStatusUpdated;
        _factions.FactionsUpdated -= OnFactionsUpdated;
        _console.AnyCommandExecuted -= OnAnyCommandExecuted;
    }

    public void NotifyFactionSelected(string factionId)
    {
        if (!_ticker.IsGameStarted || !_wh40kRoundActive)
            return;

        TryApplyTeamTheme(factionId);
    }

    private void OnLobbyStatusUpdated()
    {
        var started = _ticker.IsGameStarted;

        if (started && !_lastRoundStarted)
        {
            _autoAssignedThisRound = false;
            _manualOverrideThisRound = false;
        }
        else if (!started && _lastRoundStarted)
        {
            _autoAssignedThisRound = false;
            _manualOverrideThisRound = false;
            _wh40kRoundActive = false;
        }

        _lastRoundStarted = started;

        if (started)
            _factions.RequestFactions(force: true);
    }

    private void OnFactionsUpdated(IReadOnlyList<Content.Shared._WH40K.LateJoin.WH40KFactionInfo> factions)
    {
        _wh40kRoundActive = _ticker.IsGameStarted && factions.Count > 0;
    }

    private void OnThemeAssignment(WH40KTeamThemeAssignedEvent ev, EntitySessionEventArgs args)
    {
        _wh40kRoundActive = true;

        if (string.IsNullOrWhiteSpace(ev.TeamId))
        {
            ApplyTheme(DefaultThemeId);
            return;
        }

        TryApplyTeamTheme(ev.TeamId);
    }

    private void OnAnyCommandExecuted(IConsoleShell _shell, string commandName, string _argStr, string[] args)
    {
        if (!_ticker.IsGameStarted || !_wh40kRoundActive)
            return;

        if (!commandName.Equals("observe", StringComparison.OrdinalIgnoreCase))
            return;

        if (_state.CurrentState is not LobbyState)
            return;

        if (args.Length > 0 && args[0].Equals("admin", StringComparison.OrdinalIgnoreCase))
            return;

        ApplyTheme(DefaultThemeId);
    }

    private void OnInterfaceThemeChanged(string _)
    {
        if (!_trackManualThemeChanges || _settingThemeInternally)
            return;

        if (_ticker.IsGameStarted && _wh40kRoundActive)
            _manualOverrideThisRound = true;
    }

    private void TryApplyTeamTheme(string teamId)
    {
        if (_manualOverrideThisRound || _autoAssignedThisRound)
            return;

        var theme = ResolveThemeByTeam(teamId);
        if (theme == null)
        {
            ApplyTheme(DefaultThemeId);
            return;
        }

        ApplyTheme(theme);
        _autoAssignedThisRound = true;
    }

    private string? ResolveThemeByTeam(string teamId)
    {
        if (TryResolveTeamIdentityProfile(teamId, out var profile) &&
            !string.IsNullOrWhiteSpace(profile.InterfaceThemeId))
        {
            return profile.InterfaceThemeId;
        }

        if (teamId.Equals("Imperium", StringComparison.OrdinalIgnoreCase))
            return ImperiumThemeId;

        if (teamId.Equals("Heretics", StringComparison.OrdinalIgnoreCase))
            return HereticsThemeId;

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

    private void ApplyTheme(string themeId)
    {
        if (_cfg.GetCVar(CVars.InterfaceTheme) == themeId)
            return;

        _settingThemeInternally = true;
        _cfg.SetCVar(CVars.InterfaceTheme, themeId);
        _settingThemeInternally = false;
    }
}
