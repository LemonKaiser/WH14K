using Content.Shared.Interaction;
using Robust.Shared.Network;

namespace Content.Shared._WH40K.Mortar;

/// <summary>
/// Shared folded-state interaction guard for mortar.
/// Prevents predicted and server-side load/open interactions while mortar is not deployed.
/// </summary>
public sealed partial class SharedWH40KMortarFoldedInteractionSystem : EntitySystem
{
    [Dependency] private  INetManager _net = default!;

    public override void Initialize()
    {
        // Server-side mortar interaction handling is owned by WH40KMortarSystem.
        // Keep this system client-only to avoid duplicate local-event subscriptions
        // while still blocking folded-state predicted interactions.
        if (!_net.IsClient)
            return;

        SubscribeLocalEvent<WH40KMortarComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WH40KMortarComponent, ActivateInWorldEvent>(OnActivateInWorld);
    }

    private void OnInteractUsing(Entity<WH40KMortarComponent> mortar, ref InteractUsingEvent args)
    {
        if (args.Handled || mortar.Comp.Deployed)
            return;

        if (!HasComp<WH40KMortarShellComponent>(args.Used))
            return;

        args.Handled = true;
    }

    private void OnActivateInWorld(Entity<WH40KMortarComponent> mortar, ref ActivateInWorldEvent args)
    {
        if (mortar.Comp.Deployed)
            return;

        args.Handled = true;
    }
}
