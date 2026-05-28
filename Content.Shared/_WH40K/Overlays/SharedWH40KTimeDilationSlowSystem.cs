using System;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._WH40K.Overlays;

public sealed partial class SharedWH40KTimeDilationSlowSystem : EntitySystem
{
    // Prevent near-zero melee rates from feeling like a permanent weapon lock.
    private const float MinEffectiveMeleeMultiplier = 0.25f;

    [Dependency] private  MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KTimeDilationSlowedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WH40KTimeDilationSlowedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KTimeDilationSlowedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<MeleeWeaponComponent, GetMeleeAttackRateEvent>(OnGetMeleeAttackRate);
    }

    private void OnStartup(EntityUid uid, WH40KTimeDilationSlowedComponent component, ComponentStartup args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnShutdown(EntityUid uid, WH40KTimeDilationSlowedComponent component, ComponentShutdown args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefreshSpeed(EntityUid uid, WH40KTimeDilationSlowedComponent component, ref RefreshMovementSpeedModifiersEvent args)
    {
        var speed = Math.Clamp(component.SpeedMultiplier, 0.01f, 1.0f);
        args.ModifySpeed(speed, speed, MovementSpeedModifierLayer.Status);
    }

    private void OnGetMeleeAttackRate(EntityUid uid, MeleeWeaponComponent component, ref GetMeleeAttackRateEvent args)
    {
        if (!TryComp<WH40KTimeDilationSlowedComponent>(args.User, out var slowed))
            return;

        var multiplier = Math.Clamp(slowed.MeleeAttackRateMultiplier, MinEffectiveMeleeMultiplier, 1.0f);
        args.Multipliers *= multiplier;
    }
}
