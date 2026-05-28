#nullable disable warnings

using Content.Server._WH40K.Command.Components;
using Content.Shared.Mind;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Server._WH40K.MetaProgress;

public enum WH40KCombatVictimKind : byte
{
    Invalid = 0,
    PlayerOwned = 1,
    ClaimedReinforcement = 2
}

public readonly record struct WH40KCombatVictimResolution(
    WH40KCombatVictimKind Kind,
    NetUserId? UserId,
    string Reason)
{
    public bool CountsForValidatedRewards => Kind != WH40KCombatVictimKind.Invalid;
    public bool CountsForRawStats => Kind == WH40KCombatVictimKind.PlayerOwned;
}

public sealed partial class WH40KCombatVictimResolverSystem : EntitySystem
{
    [Dependency] private  SharedMindSystem _mind = default!;
    [Dependency] private  IPlayerManager _players = default!;

    public WH40KCombatVictimResolution ResolveForValidatedRewards(EntityUid victim)
    {
        if (TryComp<WH40KReinforcementRewardStateComponent>(victim, out var reinforcement))
        {
            if (!reinforcement.WasClaimedByPlayer || reinforcement.ClaimedUserId is not { } claimedUserId)
                return new WH40KCombatVictimResolution(WH40KCombatVictimKind.Invalid, null, "reinforcement-unclaimed");

            return new WH40KCombatVictimResolution(WH40KCombatVictimKind.ClaimedReinforcement, claimedUserId, "reinforcement-claimed");
        }

        return TryResolvePlayerUserId(victim, out var userId)
            ? new WH40KCombatVictimResolution(WH40KCombatVictimKind.PlayerOwned, userId, "player-owned")
            : new WH40KCombatVictimResolution(WH40KCombatVictimKind.Invalid, null, "non-player-victim");
    }

    public WH40KCombatVictimResolution ResolveForRawCombatStats(EntityUid victim)
    {
        if (HasComp<WH40KReinforcementRewardStateComponent>(victim))
            return new WH40KCombatVictimResolution(WH40KCombatVictimKind.Invalid, null, "reinforcement-body");

        return TryResolvePlayerUserId(victim, out var userId)
            ? new WH40KCombatVictimResolution(WH40KCombatVictimKind.PlayerOwned, userId, "player-owned")
            : new WH40KCombatVictimResolution(WH40KCombatVictimKind.Invalid, null, "non-player-victim");
    }

    private bool TryResolvePlayerUserId(EntityUid entity, out NetUserId userId)
    {
        userId = default;

        if (_players.TryGetSessionByEntity(entity, out var session))
        {
            userId = session.UserId;
            return true;
        }

        if (_mind.TryGetMind(entity, out _, out var mind) && mind.UserId is { } resolvedUserId)
        {
            userId = resolvedUserId;
            return true;
        }

        return false;
    }
}
