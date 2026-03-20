using System;
using System.Collections.Generic;
using Content.Server.GameTicking;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared._WH40K.LateJoin;
using Content.Shared.GameTicking.Components;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.LateJoin;

public sealed class WH40KFactionSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KRequestFactionsEvent>(OnRequestFactions);
    }

    private void OnRequestFactions(WH40KRequestFactionsEvent msg, EntitySessionEventArgs args)
    {
        var factions = BuildFactionList();
        RaiseNetworkEvent(new WH40KFactionsEvent(factions), args.SenderSession);
    }

    public void BroadcastFactionsToAll()
    {
        var factions = BuildFactionList();
        RaiseNetworkEvent(new WH40KFactionsEvent(factions));
    }

    private List<WH40KFactionInfo> BuildFactionList()
    {
        var result = new List<WH40KFactionInfo>();
        var teamPlayerCounts = BuildConnectedTeamCounts();
        var query = EntityQueryEnumerator<WH40KTeamBattleRuleComponent, GameRuleComponent>();

        while (query.MoveNext(out var uid, out var comp, out var rule))
        {
            if (!_gameTicker.IsGameRuleActive(uid, rule))
                continue;

            foreach (var team in comp.Teams)
            {
                result.Add(new WH40KFactionInfo(
                    team.Id,
                    team.Name,
                    team.Logo,
                    new List<ProtoId<DepartmentPrototype>>(team.Departments),
                    teamPlayerCounts.TryGetValue(team.Id, out var teamCount) ? teamCount : 0));
            }

            break;
        }

        return result;
    }

    private Dictionary<string, int> BuildConnectedTeamCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } attached)
                continue;

            if (!TryComp<WH40KTeamMemberComponent>(attached, out var teamMember) || string.IsNullOrWhiteSpace(teamMember.TeamId))
                continue;

            counts.TryGetValue(teamMember.TeamId, out var current);
            counts[teamMember.TeamId] = current + 1;
        }

        return counts;
    }
}
