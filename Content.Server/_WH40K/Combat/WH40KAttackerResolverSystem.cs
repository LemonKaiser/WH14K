using Content.Server.Combat;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Combat;

/// <summary>
/// Legacy WH40K wrapper over the shared combat attacker resolver.
/// </summary>
public sealed class WH40KAttackerResolverSystem : EntitySystem
{
    [Dependency] private readonly CombatAttackerResolverSystem _resolver = default!;

    public bool TryResolveAttacker(EntityUid origin, out EntityUid attacker)
    {
        return _resolver.TryResolveAttacker(origin, out attacker);
    }

    public bool TryResolveAttacker(EntityUid origin, out EntityUid attacker, out ActorComponent? attackerActor)
    {
        return _resolver.TryResolveAttacker(origin, out attacker, out attackerActor);
    }
}
