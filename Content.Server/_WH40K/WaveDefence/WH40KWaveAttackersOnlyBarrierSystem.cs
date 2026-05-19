using Content.Server._WH40K.WaveDefence.Components;
using Content.Shared._WH40K.WaveDefence;
using Robust.Shared.Physics.Events;

namespace Content.Server._WH40K.WaveDefence;

/// <summary>
/// Lets WaveDefence attackers pass through a spawn barrier while everyone else remains blocked.
/// </summary>
public sealed class WH40KWaveAttackersOnlyBarrierSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KWaveAttackersOnlyBarrierComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(Entity<WH40KWaveAttackersOnlyBarrierComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled || !args.OurFixture.Hard || !args.OtherFixture.Hard)
            return;

        if (HasComp<WH40KWaveDefenceAttackerComponent>(args.OtherEntity))
            args.Cancelled = true;
    }
}
