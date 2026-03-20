using System;
using Content.Shared.Ghost;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Content.Shared._WH40K.Squads;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Overlays;

public sealed class WH40KSquadStatusIconSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KSquadLeaderComponent, GetStatusIconsEvent>(OnGetLeaderIcons);
        SubscribeLocalEvent<WH40KSquadAssignableComponent, GetStatusIconsEvent>(OnGetMemberIcons);
    }

    private void OnGetLeaderIcons(Entity<WH40KSquadLeaderComponent> ent, ref GetStatusIconsEvent args)
    {
        if (!ent.Comp.SquadActive || !CanViewSquadIcon(ent.Owner))
            return;

        if (!TryResolveLeaderIcon(ent.Comp.TeamId, out var iconId))
            return;

        if (_prototype.TryIndex<FactionIconPrototype>(iconId, out var icon))
            args.StatusIcons.Add(icon);
    }

    private void OnGetMemberIcons(Entity<WH40KSquadAssignableComponent> ent, ref GetStatusIconsEvent args)
    {
        if (ent.Comp.AssignedLeader == null || ent.Comp.AssignedSlot is < 1 or > 5)
            return;

        if (!CanViewSquadIcon(ent.Owner))
            return;

        if (!TryResolveMemberIcon(ent.Comp.TeamId, ent.Comp.AssignedSlot, out var iconId))
            return;

        if (_prototype.TryIndex<FactionIconPrototype>(iconId, out var icon))
            args.StatusIcons.Add(icon);
    }

    private bool CanViewSquadIcon(EntityUid target)
    {
        if (_player.LocalSession?.AttachedEntity is not { Valid: true } viewer)
            return false;

        if (HasComp<GhostComponent>(viewer))
            return false;

        if (!TryResolveViewerSquadLeader(viewer, out var viewerLeader))
            return false;

        if (TryComp<WH40KSquadLeaderComponent>(target, out var leader) &&
            leader.SquadActive)
        {
            return viewerLeader == target;
        }

        return TryComp<WH40KSquadAssignableComponent>(target, out var assignable) &&
               assignable.AssignedLeader == viewerLeader &&
               assignable.AssignedSlot is >= 1 and <= 5;
    }

    private static bool TryResolveLeaderIcon(string teamId, out string iconId)
    {
        iconId = string.Empty;
        if (!TryCanonicalizeTeamId(teamId, out var canonical))
            return false;

        iconId = canonical == "Heretics"
            ? "WH40KSquadIconHereticsLeader"
            : "WH40KSquadIconImperiumLeader";
        return true;
    }

    private static bool TryResolveMemberIcon(string teamId, byte slot, out string iconId)
    {
        iconId = string.Empty;
        if (!TryCanonicalizeTeamId(teamId, out var canonical) || slot is < 1 or > 5)
            return false;

        var prefix = canonical == "Heretics" ? "WH40KSquadIconHeretics" : "WH40KSquadIconImperium";
        iconId = $"{prefix}{slot}";
        return true;
    }

    private bool TryResolveViewerSquadLeader(EntityUid viewer, out EntityUid leaderUid)
    {
        if (TryComp<WH40KSquadLeaderComponent>(viewer, out var leader) &&
            leader.SquadActive)
        {
            leaderUid = viewer;
            return true;
        }

        if (TryComp<WH40KSquadAssignableComponent>(viewer, out var assignable) &&
            assignable.AssignedLeader is { } assignedLeader &&
            assignable.AssignedSlot is >= 1 and <= 5)
        {
            leaderUid = assignedLeader;
            return true;
        }

        leaderUid = default;
        return false;
    }

    private static bool TryCanonicalizeTeamId(string? teamId, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        if (string.Equals(teamId, "Imperium", StringComparison.OrdinalIgnoreCase))
        {
            canonical = "Imperium";
            return true;
        }

        if (string.Equals(teamId, "Heretics", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(teamId, "Chaos", StringComparison.OrdinalIgnoreCase))
        {
            canonical = "Heretics";
            return true;
        }

        return false;
    }
}
