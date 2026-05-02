using Content.Shared.ActionBlocker;
using Content.Shared.Chat;
using Content.Shared.Emoting;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Speech;
using Content.Shared.Storage.Components;
using Content.Shared.Throwing;
using Content.Shared.UserInterface;
using Content.Shared.Wieldable;

namespace Content.Shared._WH40K.Cinematic;

public sealed class SharedWH40KCinematicLockSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KCinematicLockedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, UpdateCanMoveEvent>(OnUpdateCanMove);

        SubscribeLocalEvent<WH40KCinematicLockedComponent, ConsciousAttemptEvent>(OnConsciousAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, UseAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, PickupAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, DropAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, ThrowAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, AttackAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, ChangeDirectionAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, EmoteAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, SpeakAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, PullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, UserOpenActivatableUIAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, IntrinsicUIOpenAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, InGameOocMessageAttemptEvent>(OnInGameOocAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, WieldAttemptEvent>(OnWieldAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, UnwieldAttemptEvent>(OnUnwieldAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, ItemToggleActivateAttemptEvent>(OnItemToggleActivateAttempt);
        SubscribeLocalEvent<WH40KCinematicLockedComponent, ItemToggleDeactivateAttemptEvent>(OnItemToggleDeactivateAttempt);

        SubscribeLocalEvent<StorageOpenAttemptEvent>(OnStorageOpenAttempt);
        SubscribeLocalEvent<OpenableOpenAttemptEvent>(OnOpenableOpenAttempt);
    }

    private void OnStartup(Entity<WH40KCinematicLockedComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<PullableComponent>(ent, out var pullable))
            _pulling.TryStopPull(ent, pullable);

        _blocker.UpdateCanMove(ent);
    }

    private void OnShutdown(Entity<WH40KCinematicLockedComponent> ent, ref ComponentShutdown args)
    {
        _blocker.UpdateCanMove(ent);
    }

    private void OnUpdateCanMove(Entity<WH40KCinematicLockedComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        args.Cancel();
    }

    private void OnConsciousAttempt(Entity<WH40KCinematicLockedComponent> ent, ref ConsciousAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnInteractionAttempt(Entity<WH40KCinematicLockedComponent> ent, ref InteractionAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnAttempt(EntityUid uid, WH40KCinematicLockedComponent component, CancellableEntityEventArgs args)
    {
        args.Cancel();
    }

    private void OnPullAttempt(Entity<WH40KCinematicLockedComponent> ent, ref PullAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnInGameOocAttempt(Entity<WH40KCinematicLockedComponent> ent, ref InGameOocMessageAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnWieldAttempt(Entity<WH40KCinematicLockedComponent> ent, ref WieldAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnUnwieldAttempt(Entity<WH40KCinematicLockedComponent> ent, ref UnwieldAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnItemToggleActivateAttempt(Entity<WH40KCinematicLockedComponent> ent, ref ItemToggleActivateAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnItemToggleDeactivateAttempt(Entity<WH40KCinematicLockedComponent> ent, ref ItemToggleDeactivateAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnStorageOpenAttempt(ref StorageOpenAttemptEvent args)
    {
        if (HasComp<WH40KCinematicLockedComponent>(args.User))
            args.Cancelled = true;
    }

    private void OnOpenableOpenAttempt(ref OpenableOpenAttemptEvent args)
    {
        if (args.User is not { } user || !HasComp<WH40KCinematicLockedComponent>(user))
            return;

        args.Cancelled = true;
    }
}
