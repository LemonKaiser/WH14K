using System;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Research.Components;
using Content.Server.Research.Systems;
using Content.Shared.Research.Components;

namespace Content.Server._WH40K.Research;

/// <summary>
/// Mirrors WH40K research servers to the team's shared research balance.
/// </summary>
public sealed class WH40KTeamResearchBalanceSyncSystem : EntitySystem
{
    [Dependency] private readonly WH40KTeamRuleFacadeSystem _teamRule = default!;
    [Dependency] private readonly ResearchSystem _research = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KTeamResearchBalanceChangedEvent>(OnTeamResearchBalanceChanged);
        SubscribeLocalEvent<WH40KResearchTeamComponent, ComponentStartup>(OnTeamComponentStartup);
    }

    private void OnTeamComponentStartup(EntityUid uid, WH40KResearchTeamComponent component, ComponentStartup args)
    {
        if (!TryComp<ResearchServerComponent>(uid, out var server))
            return;

        SyncServerToTeamBalance(uid, server, component.TeamId);
    }

    private void OnTeamResearchBalanceChanged(WH40KTeamResearchBalanceChangedEvent args)
    {
        var query = EntityQueryEnumerator<ResearchServerComponent, WH40KResearchTeamComponent>();
        while (query.MoveNext(out var uid, out var server, out var team))
        {
            if (!string.Equals(team.TeamId, args.TeamId, StringComparison.OrdinalIgnoreCase))
                continue;

            _research.SetServerPoints(uid, args.Points, server);
        }
    }

    private void SyncServerToTeamBalance(EntityUid uid, ResearchServerComponent server, string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return;

        if (!_teamRule.TryGetTeamResearchPoints(teamId, out var points))
            return;

        _research.SetServerPoints(uid, points, server);
    }
}
