using Content.Shared.Movement.Systems;

namespace Content.Shared._WH40K.Morale;

public sealed partial class SharedWH40KMoraleBoostSystem : EntitySystem
{
    [Dependency] private  MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KMoraleBoostedComponent, ComponentStartup>(OnBuffStartup);
        SubscribeLocalEvent<WH40KMoraleBoostedComponent, ComponentShutdown>(OnBuffShutdown);
        SubscribeLocalEvent<WH40KMoraleBoostedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnBuffStartup(EntityUid uid, WH40KMoraleBoostedComponent component, ComponentStartup args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnBuffShutdown(EntityUid uid, WH40KMoraleBoostedComponent component, ComponentShutdown args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefreshSpeed(EntityUid uid, WH40KMoraleBoostedComponent component, ref RefreshMovementSpeedModifiersEvent args)
    {
        var speed = Math.Max(0.01f, component.SpeedMultiplier);
        args.ModifySpeed(speed, speed, MovementSpeedModifierLayer.Status);
    }
}
