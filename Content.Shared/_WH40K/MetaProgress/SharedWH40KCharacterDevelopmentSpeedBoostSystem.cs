using Content.Shared.Movement.Systems;

namespace Content.Shared._WH40K.MetaProgress;

public sealed class SharedWH40KCharacterDevelopmentSpeedBoostSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KCharacterDevelopmentSpeedBoostComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WH40KCharacterDevelopmentSpeedBoostComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KCharacterDevelopmentSpeedBoostComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnStartup(EntityUid uid, WH40KCharacterDevelopmentSpeedBoostComponent component, ComponentStartup args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnShutdown(EntityUid uid, WH40KCharacterDevelopmentSpeedBoostComponent component, ComponentShutdown args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefreshSpeed(
        EntityUid uid,
        WH40KCharacterDevelopmentSpeedBoostComponent component,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        var speed = Math.Max(0.01f, component.SpeedMultiplier);
        args.ModifySpeed(speed, speed, MovementSpeedModifierLayer.Status);
    }
}
