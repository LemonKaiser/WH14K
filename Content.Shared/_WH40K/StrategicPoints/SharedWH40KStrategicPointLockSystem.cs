using Content.Shared.Construction.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Pulling.Events;

namespace Content.Shared._WH40K.StrategicPoints;

/// <summary>
/// Keeps strategic anchors and completed points fixed in place.
/// </summary>
public sealed class SharedWH40KStrategicPointLockSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KStrategicPointComponent, BeingPulledAttemptEvent>(OnPointBeingPulledAttempt);
        SubscribeLocalEvent<WH40KStrategicPointComponent, PullAttemptEvent>(OnPointPullAttempt);
        SubscribeLocalEvent<WH40KStrategicPointComponent, UnanchorAttemptEvent>(OnPointUnanchorAttempt);

        SubscribeLocalEvent<WH40KStrategicPointAnchorComponent, BeingPulledAttemptEvent>(OnAnchorBeingPulledAttempt);
        SubscribeLocalEvent<WH40KStrategicPointAnchorComponent, PullAttemptEvent>(OnAnchorPullAttempt);
        SubscribeLocalEvent<WH40KStrategicPointAnchorComponent, UnanchorAttemptEvent>(OnAnchorUnanchorAttempt);
    }

    private static void OnPointBeingPulledAttempt(Entity<WH40KStrategicPointComponent> ent, ref BeingPulledAttemptEvent args)
    {
        args.Cancel();
    }

    private static void OnPointPullAttempt(Entity<WH40KStrategicPointComponent> ent, ref PullAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private static void OnPointUnanchorAttempt(Entity<WH40KStrategicPointComponent> ent, ref UnanchorAttemptEvent args)
    {
        args.Cancel();
    }

    private static void OnAnchorBeingPulledAttempt(Entity<WH40KStrategicPointAnchorComponent> ent, ref BeingPulledAttemptEvent args)
    {
        args.Cancel();
    }

    private static void OnAnchorPullAttempt(Entity<WH40KStrategicPointAnchorComponent> ent, ref PullAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private static void OnAnchorUnanchorAttempt(Entity<WH40KStrategicPointAnchorComponent> ent, ref UnanchorAttemptEvent args)
    {
        args.Cancel();
    }
}
