using System;
using Content.Server._WH40K.Clothing.Components;
using Content.Shared.Clothing;
using Content.Shared.Trigger;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Systems;
using Content.Shared._WH40K.Clothing;

namespace Content.Server._WH40K.Clothing;

public sealed class WH40KExplosiveBeltSystem : EntitySystem
{
    [Dependency] private readonly TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KExplosiveBeltComponent, WH40KActivateExplosiveBeltActionEvent>(OnActivateBelt);
        SubscribeLocalEvent<WH40KExplosiveBeltComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<WH40KExplosiveBeltComponent, ClothingGotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<WH40KExplosiveBeltComponent, AttemptTriggerEvent>(OnAttemptTrigger);
    }

    private void OnActivateBelt(Entity<WH40KExplosiveBeltComponent> ent, ref WH40KActivateExplosiveBeltActionEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Wearer == null || !TryComp<TimerTriggerComponent>(ent, out var timer))
            return;

        _trigger.StopTimerTrigger((ent.Owner, timer));
        _trigger.SetDelay((ent.Owner, timer), TimeSpan.FromSeconds(Math.Max(0.1f, args.DelaySeconds)));

        if (_trigger.ActivateTimerTrigger((ent.Owner, timer), args.Performer))
            args.Handled = true;
    }

    private void OnEquipped(Entity<WH40KExplosiveBeltComponent> ent, ref ClothingGotEquippedEvent args)
    {
        ent.Comp.Wearer = args.Wearer;
    }

    private void OnUnequipped(Entity<WH40KExplosiveBeltComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        if (ent.Comp.Wearer != args.Wearer)
            return;

        ent.Comp.Wearer = null;

        if (!TryComp<TimerTriggerComponent>(ent, out var timer))
            return;

        _trigger.StopTimerTrigger((ent.Owner, timer));
        _trigger.SetDelay((ent.Owner, timer), TimeSpan.FromSeconds(Math.Max(0.1f, ent.Comp.DefaultDelaySeconds)));
    }

    private void OnAttemptTrigger(Entity<WH40KExplosiveBeltComponent> ent, ref AttemptTriggerEvent args)
    {
        if (ent.Comp.Wearer != null)
            return;

        if (args.Key != null && !string.Equals(args.Key, TriggerSystem.DefaultTriggerKey, StringComparison.Ordinal))
            return;

        args.Cancelled = true;
    }
}
