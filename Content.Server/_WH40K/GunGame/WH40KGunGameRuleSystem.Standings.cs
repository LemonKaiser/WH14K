using System.Collections.Generic;
using System.Linq;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.Preferences;
using Content.Shared._WH40K.GunGame;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.GunGame;

public sealed partial class WH40KGunGameRuleSystem
{
    private void InitializeStandings()
    {
        _player.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    private void RememberPlayerProfile(NetUserId userId, HumanoidCharacterProfile profile, WH40KGunGameRuleComponent rule)
    {
        rule.PlayerProfiles[userId] = profile.Clone();
    }

    private void PushStandings(WH40KGunGameRuleComponent rule)
    {
        var totalLevels = rule.WeaponSequence.Count;
        var entries = new List<WH40KGunGameStandingEntry>();

        foreach (var session in _player.Sessions)
        {
            if (session.Status != SessionStatus.InGame ||
                !rule.PlayerLevel.TryGetValue(session.UserId, out var level) ||
                !rule.PlayerProfiles.TryGetValue(session.UserId, out var profile))
            {
                continue;
            }

            var kills = rule.PlayerKills.GetValueOrDefault(session.UserId);
            entries.Add(new WH40KGunGameStandingEntry(
                session.UserId,
                session.Name,
                GetDisplayedLevel(level, totalLevels),
                kills));
        }

        entries = entries
            .OrderByDescending(entry => entry.Level)
            .ThenByDescending(entry => entry.Kills)
            .ThenBy(entry => entry.UserName, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ev = new WH40KGunGameStandingsEvent(entries);
        foreach (var session in _player.Sessions)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(ev, session);
        }
    }

    private void ClearStandingsHud()
    {
        var ev = new WH40KGunGameStandingsEvent(new List<WH40KGunGameStandingEntry>());
        foreach (var session in _player.Sessions)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(ev, session);
        }
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.InGame &&
            args.NewStatus != SessionStatus.Disconnected &&
            args.NewStatus != SessionStatus.Zombie)
        {
            return;
        }

        if (!TryGetActiveRule(out _, out var rule))
            return;

        PushStandings(rule);
        PushRoundTimer(rule, force: true);
    }
}
