using System;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._WH40K.Animals;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;

namespace Content.Server._WH40K.Animals;

/// <summary>
/// Makes nearby herd members join retaliation when one member is attacked.
/// </summary>
public sealed partial class WH40KCollectiveRetaliationSystem : EntitySystem
{
    [Dependency] private  EntityLookupSystem _lookup = default!;
    [Dependency] private  NpcFactionSystem _npcFaction = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KCollectiveRetaliationComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WH40KCollectiveRetaliationComponent, DisarmedEvent>(OnDisarmed);
    }

    private void OnDamageChanged(Entity<WH40KCollectiveRetaliationComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } origin)
            return;

        AlertNearbyHerd(ent, origin);
    }

    private void OnDisarmed(Entity<WH40KCollectiveRetaliationComponent> ent, ref DisarmedEvent args)
    {
        AlertNearbyHerd(ent, args.Source);
    }

    private void AlertNearbyHerd(Entity<WH40KCollectiveRetaliationComponent> ent, EntityUid attacker)
    {
        if (string.IsNullOrWhiteSpace(ent.Comp.HerdId) || !HasComp<MobStateComponent>(attacker))
            return;

        AggroAttacker(ent.Owner, attacker);

        foreach (var (uid, herd) in _lookup.GetEntitiesInRange<WH40KCollectiveRetaliationComponent>(Transform(ent.Owner).Coordinates, ent.Comp.Radius))
        {
            if (uid == ent.Owner ||
                !string.Equals(herd.HerdId, ent.Comp.HerdId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AggroAttacker(uid, attacker);
        }
    }

    private void AggroAttacker(EntityUid uid, EntityUid attacker)
    {
        _npcFaction.AggroEntity(uid, attacker);
    }
}
