using System;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared.NPC.Systems;

namespace Content.Server._WH40K.GameTicking.Rules;

public sealed class WH40KTeamNpcFactionSystem : EntitySystem
{
    private const string TeamImperium = "Imperium";
    private const string TeamHeretics = "Heretics";

    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KTeamMemberComponent, ComponentStartup>(OnTeamMemberStartup);
    }

    private void OnTeamMemberStartup(Entity<WH40KTeamMemberComponent> ent, ref ComponentStartup args)
    {
        ApplyTeamFaction(ent.Owner, ent.Comp.TeamId);
    }

    public void RefreshAllTeamFactions()
    {
        var query = EntityQueryEnumerator<WH40KTeamMemberComponent>();
        while (query.MoveNext(out var uid, out var member))
        {
            ApplyTeamFaction(uid, member.TeamId);
        }
    }

    public bool ApplyTeamFaction(EntityUid entity, string teamId)
    {
        if (!TryResolveFaction(teamId, out var factionId))
            return false;

        _npcFaction.ClearFactions(entity);
        _npcFaction.AddFaction(entity, factionId);
        return true;
    }

    private static bool TryResolveFaction(string teamId, out string factionId)
    {
        if (string.Equals(teamId, TeamImperium, StringComparison.OrdinalIgnoreCase))
        {
            factionId = TeamImperium;
            return true;
        }

        if (string.Equals(teamId, TeamHeretics, StringComparison.OrdinalIgnoreCase))
        {
            factionId = TeamHeretics;
            return true;
        }

        factionId = string.Empty;
        return false;
    }
}
