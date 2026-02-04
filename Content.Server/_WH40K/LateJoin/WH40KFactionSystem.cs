using System.Collections.Generic;
using Content.Server.GameTicking;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared._WH40K.LateJoin;
using Content.Shared.GameTicking.Components;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Server.Player;

namespace Content.Server._WH40K.LateJoin;

public sealed class WH40KFactionSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _gameTicker = default!;

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
                    new List<ProtoId<DepartmentPrototype>>(team.Departments)));
            }

            break;
        }

        return result;
    }
}
