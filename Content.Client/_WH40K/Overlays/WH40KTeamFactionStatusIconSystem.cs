using System;
using Content.Shared.Ghost;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Content.Shared._WH40K.GameTicking.Rules;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Overlays;

/// <summary>
/// Shows WH40K team faction icons without HUD equipment.
/// Icon is only visible for allies (same team as local viewer).
/// </summary>
public sealed class WH40KTeamFactionStatusIconSystem : EntitySystem
{
    private static readonly ProtoId<FactionIconPrototype> ImperiumIcon = "WH40KFactionIconImperium";
    private static readonly ProtoId<FactionIconPrototype> HereticsIcon = "WH40KFactionIconHeretics";

    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KTeamBattleFactionIconComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<WH40KTeamBattleFactionIconComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_player.LocalSession?.AttachedEntity is not { Valid: true } viewer)
            return;

        // Ghost spectators should not get team-faction reveal from this overlay.
        if (HasComp<GhostComponent>(viewer))
            return;

        if (!TryComp<WH40KTeamBattleFactionIconComponent>(viewer, out var viewerFaction))
            return;

        if (!TryCanonicalizeTeamId(viewerFaction.TeamId, out var viewerTeam))
            return;

        if (!TryCanonicalizeTeamId(ent.Comp.TeamId, out var targetTeam))
            return;

        if (!string.Equals(viewerTeam, targetTeam, StringComparison.OrdinalIgnoreCase))
            return;

        var iconId = string.Equals(targetTeam, "Imperium", StringComparison.OrdinalIgnoreCase)
            ? ImperiumIcon
            : HereticsIcon;

        if (_prototype.Resolve(iconId, out var icon))
            args.StatusIcons.Add(icon);
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
