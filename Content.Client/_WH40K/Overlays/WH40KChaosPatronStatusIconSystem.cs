using System;
using Content.Shared.Ghost;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Content.Shared._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.Psyker;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Overlays;

/// <summary>
/// Shows the selected chaos patron below the heretics faction icon.
/// Leader cultists get a colored outline mod layered over the patron icon.
/// </summary>
public sealed class WH40KChaosPatronStatusIconSystem : EntitySystem
{
    private static readonly ProtoId<FactionIconPrototype> KhorneIcon = "WH40KChaosPatronIconKhorne";
    private static readonly ProtoId<FactionIconPrototype> NurgleIcon = "WH40KChaosPatronIconNurgle";
    private static readonly ProtoId<FactionIconPrototype> SlaaneshIcon = "WH40KChaosPatronIconSlaanesh";
    private static readonly ProtoId<FactionIconPrototype> TzeentchIcon = "WH40KChaosPatronIconTzeentch";

    private static readonly ProtoId<FactionIconPrototype> KhorneLeaderOutline = "WH40KChaosPatronLeaderOutlineKhorne";
    private static readonly ProtoId<FactionIconPrototype> NurgleLeaderOutline = "WH40KChaosPatronLeaderOutlineNurgle";
    private static readonly ProtoId<FactionIconPrototype> SlaaneshLeaderOutline = "WH40KChaosPatronLeaderOutlineSlaanesh";
    private static readonly ProtoId<FactionIconPrototype> TzeentchLeaderOutline = "WH40KChaosPatronLeaderOutlineTzeentch";

    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KChaosPatronStatusIconComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<WH40KChaosPatronStatusIconComponent> ent, ref GetStatusIconsEvent args)
    {
        if (!CanViewPatronIcon(ent.Owner))
            return;

        if (!TryResolvePatronIcon(ent.Comp.Patron, out var iconId))
            return;

        if (_prototype.TryIndex<FactionIconPrototype>(iconId, out var icon))
            args.StatusIcons.Add(icon);

        if (!ent.Comp.IsLeader ||
            !TryResolveLeaderOutline(ent.Comp.Patron, out var outlineId) ||
            !_prototype.TryIndex<FactionIconPrototype>(outlineId, out var outline))
        {
            return;
        }

        args.StatusIcons.Add(outline);
    }

    private bool CanViewPatronIcon(EntityUid target)
    {
        if (_player.LocalSession?.AttachedEntity is not { Valid: true } viewer)
            return false;

        if (HasComp<GhostComponent>(viewer))
            return false;

        if (!TryComp<WH40KTeamBattleFactionIconComponent>(viewer, out var viewerFaction) ||
            !TryComp<WH40KTeamBattleFactionIconComponent>(target, out var targetFaction))
        {
            return false;
        }

        if (!TryCanonicalizeTeamId(viewerFaction.TeamId, out var viewerTeam) ||
            !TryCanonicalizeTeamId(targetFaction.TeamId, out var targetTeam))
        {
            return false;
        }

        return string.Equals(viewerTeam, "Heretics", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(targetTeam, "Heretics", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolvePatronIcon(WH40KChaosPatron patron, out ProtoId<FactionIconPrototype> iconId)
    {
        iconId = patron switch
        {
            WH40KChaosPatron.Khorne => KhorneIcon,
            WH40KChaosPatron.Nurgle => NurgleIcon,
            WH40KChaosPatron.Slaanesh => SlaaneshIcon,
            WH40KChaosPatron.Tzeentch => TzeentchIcon,
            _ => default
        };

        return iconId != default;
    }

    private static bool TryResolveLeaderOutline(WH40KChaosPatron patron, out ProtoId<FactionIconPrototype> iconId)
    {
        iconId = patron switch
        {
            WH40KChaosPatron.Khorne => KhorneLeaderOutline,
            WH40KChaosPatron.Nurgle => NurgleLeaderOutline,
            WH40KChaosPatron.Slaanesh => SlaaneshLeaderOutline,
            WH40KChaosPatron.Tzeentch => TzeentchLeaderOutline,
            _ => default
        };

        return iconId != default;
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
