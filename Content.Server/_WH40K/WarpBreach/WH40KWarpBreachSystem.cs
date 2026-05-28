using Content.Shared._WH40K.WarpBreach;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.WarpBreach;

public sealed partial class WH40KWarpBreachSystem : EntitySystem
{
    [Dependency] private  IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KWarpBreachComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WH40KWarpBreachComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.CreatedAt = _timing.CurTime;
        Dirty(ent);
    }
}
