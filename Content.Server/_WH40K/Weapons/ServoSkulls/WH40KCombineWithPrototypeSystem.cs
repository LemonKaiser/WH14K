using Content.Server._WH40K.Weapons.ServoSkulls.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Trigger.Components;

namespace Content.Server._WH40K.Weapons.ServoSkulls;

public sealed partial class WH40KCombineWithPrototypeSystem : EntitySystem
{
    [Dependency] private  SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KCombineWithPrototypeComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInteractUsing(Entity<WH40KCombineWithPrototypeComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<ActiveTimerTriggerComponent>(ent) || HasComp<ActiveTimerTriggerComponent>(args.Used))
            return;

        if (Prototype(args.Used)?.ID != ent.Comp.RequiredPrototype)
            return;

        args.Handled = true;

        // Free the user's hands before spawning the merged result so it can be picked back up immediately.
        _hands.TryDrop(args.User, ent.Owner, checkActionBlocker: false, doDropInteraction: false);
        _hands.TryDrop(args.User, args.Used, checkActionBlocker: false, doDropInteraction: false);

        var result = Spawn(ent.Comp.ResultPrototype, Transform(args.User).Coordinates);

        QueueDel(args.Used);
        QueueDel(ent.Owner);

        if (ent.Comp.PickupResult)
            _hands.TryPickupAnyHand(args.User, result, checkActionBlocker: false);
    }
}
