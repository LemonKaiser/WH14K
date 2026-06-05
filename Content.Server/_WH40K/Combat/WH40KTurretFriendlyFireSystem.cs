using Content.Shared._WH40K.Combat;
using Content.Shared.Damage.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Turrets;

namespace Content.Server._WH40K.Combat;

/// <summary>
/// Prevents WH40K faction-locked turrets from damaging allied faction members,
/// even if a friendly steps into the line of fire.
/// </summary>
public sealed partial class WH40KTurretFriendlyFireSystem : EntitySystem
{
    [Dependency] private WH40KAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcFactionMemberComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
    }

    private void OnBeforeDamageChanged(Entity<NpcFactionMemberComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || args.Origin is not { } origin)
            return;

        if (!_attackerResolver.TryResolveAttacker(origin, out var attacker))
            attacker = origin;

        if (attacker == ent.Owner ||
            !HasComp<DeployableTurretComponent>(attacker) ||
            !HasComp<WH40KTurretFactionLockComponent>(attacker) ||
            !TryComp<NpcFactionMemberComponent>(attacker, out var attackerFaction))
        {
            return;
        }

        if (_npcFaction.IsEntityFriendly((attacker, attackerFaction), (ent.Owner, ent.Comp)))
            args.Cancelled = true;
    }
}
