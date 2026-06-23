using Content.Server.Construction;
using Content.Shared.Interaction;
using Content.Shared._WH40K.GunGame;

namespace Content.Server._WH40K.GunGame;

/// <summary>
/// Prevents construction-style tool interactions, such as cutting clothes apart,
/// on Gun Game equipment that is meant to stay fixed on the player.
/// </summary>
public sealed class WH40KGunGameLockedInteractionSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KGunGameLockedComponent, InteractUsingEvent>(
            OnInteractUsing,
            before: new[] { typeof(ConstructionSystem) });
    }

    private static void OnInteractUsing(Entity<WH40KGunGameLockedComponent> ent, ref InteractUsingEvent args)
    {
        if (!ent.Comp.BlockInteractUsing)
            return;

        args.Handled = true;
    }
}
