using System;
using Content.Shared.Mech;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared.Mech.Components;
using Content.Shared._WH40K.GameTicking.Rules;

namespace Content.Server._WH40K.Overlays;

/// <summary>
/// Mirrors WH40K team icon data from the mech pilot to the mech entity itself
/// so ally-only faction status icons remain visible while piloting.
/// </summary>
public sealed class WH40KMechFactionIconSyncSystem : EntitySystem
{
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MechPilotComponent, ComponentStartup>(OnPilotStartup);
        SubscribeLocalEvent<MechPilotComponent, ComponentShutdown>(OnPilotShutdown);
        SubscribeLocalEvent<MechPilotComponent, MechPilotAssignedEvent>(OnPilotAssigned);
    }

    private void OnPilotStartup(Entity<MechPilotComponent> ent, ref ComponentStartup args)
    {
        if (!ent.Comp.Mech.IsValid() || Deleted(ent.Comp.Mech))
            return;

        SyncMechFactionIcon(ent.Comp.Mech, ent.Owner);
    }

    private void OnPilotShutdown(Entity<MechPilotComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<MechComponent>(ent.Comp.Mech, out var mech))
            return;

        if (mech.PilotSlot.ContainedEntity is { } activePilot &&
            activePilot != ent.Owner &&
            HasComp<MechPilotComponent>(activePilot))
        {
            SyncMechFactionIcon(ent.Comp.Mech, activePilot);
            return;
        }

        RemComp<WH40KTeamBattleFactionIconComponent>(ent.Comp.Mech);
    }

    private void OnPilotAssigned(Entity<MechPilotComponent> ent, ref MechPilotAssignedEvent args)
    {
        if (!args.Mech.IsValid() || Deleted(args.Mech))
            return;

        SyncMechFactionIcon(args.Mech, ent.Owner);
    }

    private void SyncMechFactionIcon(EntityUid mech, EntityUid pilot)
    {
        if (!mech.IsValid() || Deleted(mech))
            return;

        if (!TryResolvePilotTeamId(pilot, out var teamId))
        {
            RemComp<WH40KTeamBattleFactionIconComponent>(mech);
            return;
        }

        var mechIcon = EnsureComp<WH40KTeamBattleFactionIconComponent>(mech);
        if (string.Equals(mechIcon.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
            return;

        mechIcon.TeamId = teamId;
        Dirty(mech, mechIcon);
    }

    private bool TryResolvePilotTeamId(EntityUid pilot, out string teamId)
    {
        if (TryComp<WH40KTeamBattleFactionIconComponent>(pilot, out var pilotIcon) &&
            !string.IsNullOrWhiteSpace(pilotIcon.TeamId))
        {
            teamId = pilotIcon.TeamId;
            return true;
        }

        if (_teamRule.TryGetTeamIdFromEntity(pilot, out teamId))
            return true;

        teamId = string.Empty;
        return false;
    }
}
