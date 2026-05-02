using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;

namespace Content.Shared._WH40K.Cinematic;

public sealed class SharedWH40KCinematicProtectionSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KCinematicProtectedComponent, GettingInteractedWithAttemptEvent>(OnGettingInteractedWithAttempt);
        SubscribeLocalEvent<WH40KCinematicProtectedComponent, IsEquippingTargetAttemptEvent>(OnIsEquippingTargetAttempt);
        SubscribeLocalEvent<WH40KCinematicProtectedComponent, IsUnequippingTargetAttemptEvent>(OnIsUnequippingTargetAttempt);
    }

    private void OnGettingInteractedWithAttempt(Entity<WH40KCinematicProtectedComponent> ent, ref GettingInteractedWithAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnIsEquippingTargetAttempt(Entity<WH40KCinematicProtectedComponent> ent, ref IsEquippingTargetAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnIsUnequippingTargetAttempt(Entity<WH40KCinematicProtectedComponent> ent, ref IsUnequippingTargetAttemptEvent args)
    {
        args.Cancel();
    }
}
