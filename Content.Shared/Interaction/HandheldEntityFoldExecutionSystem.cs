using Content.Shared.DoAfter;
using Content.Shared.Interaction.Components;
using Robust.Shared.Network;

namespace Content.Shared.Interaction;

/// <summary>
/// Server execution path for generic in-world fold actions for entities that use
/// <see cref="HandheldEntityPlacementComponent"/>.
/// </summary>
public sealed partial class HandheldEntityFoldExecutionSystem : EntitySystem
{
    [Dependency] private  SharedDoAfterSystem _doAfter = default!;
    [Dependency] private  INetManager _net = default!;

    public override void Initialize()
    {
        if (!_net.IsServer)
            return;

        SubscribeLocalEvent<HandheldEntityPlacementComponent, HandheldEntityFoldRequestEvent>(OnFoldRequest);
        SubscribeLocalEvent<HandheldEntityPlacementComponent, HandheldEntityFoldDoAfterEvent>(OnFoldDoAfter);
    }

    private void OnFoldRequest(Entity<HandheldEntityPlacementComponent> ent, ref HandheldEntityFoldRequestEvent args)
    {
        if (!_net.IsServer || args.Handled)
            return;

        var foldAttempt = new HandheldEntityFoldAttemptEvent(args.User);
        RaiseLocalEvent(ent.Owner, foldAttempt);

        if (foldAttempt.Cancelled || foldAttempt.FoldDelay <= TimeSpan.Zero)
            return;

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            foldAttempt.FoldDelay,
            new HandheldEntityFoldDoAfterEvent(),
            ent,
            ent)
        {
            BreakOnMove = foldAttempt.BreakOnMove,
            BreakOnDamage = foldAttempt.BreakOnDamage,
            BreakOnHandChange = foldAttempt.BreakOnHandChange,
            NeedHand = foldAttempt.NeedHand,
        };

        args.Handled = _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnFoldDoAfter(Entity<HandheldEntityPlacementComponent> ent, ref HandheldEntityFoldDoAfterEvent args)
    {
        if (!_net.IsServer || args.Cancelled || args.Handled)
            return;

        var foldComplete = new HandheldEntityFoldCompleteEvent(args.User);
        RaiseLocalEvent(ent.Owner, foldComplete);
        args.Handled = foldComplete.Handled;
    }
}
