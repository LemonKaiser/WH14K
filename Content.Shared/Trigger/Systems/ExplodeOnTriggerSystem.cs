using Content.Shared.Explosion.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Components.Effects;

namespace Content.Shared.Trigger.Systems;

public sealed class ExplodeOnTriggerSystem : XOnTriggerSystem<ExplodeOnTriggerComponent>
{
    [Dependency] private readonly SharedExplosionSystem _explosion = default!;

    protected override void OnTrigger(Entity<ExplodeOnTriggerComponent> ent, EntityUid target, ref TriggerEvent args)
    {
        // WH40K
        var user = ResolveExplosionUser(ent.Owner, args.User);
        _explosion.TriggerExplosive(target, user: user);
        args.Handled = true;
        // WH40K
    }

    // WH40K
    private EntityUid? ResolveExplosionUser(EntityUid owner, EntityUid? fallback)
    {
        if (TryComp(owner, out ProjectileComponent? projectile) && projectile.Shooter is { } shooter)
            return shooter;

        if (TryComp(owner, out TimerTriggerComponent? timer) && timer.User is { } timerUser)
            return timerUser;

        if (TryComp(owner, out ThrownItemComponent? thrown) && thrown.Thrower is { } thrower)
            return thrower;

        return fallback;
    }
    // WH40K
}

public sealed class ExplosionOnTriggerSystem : XOnTriggerSystem<ExplosionOnTriggerComponent>
{
    [Dependency] private readonly SharedExplosionSystem _explosion = default!;

    protected override void OnTrigger(Entity<ExplosionOnTriggerComponent> ent, EntityUid target, ref TriggerEvent args)
    {
        // WH40K
        var user = ResolveExplosionUser(ent.Owner, args.User);
        _explosion.QueueExplosion(target,
            ent.Comp.ExplosionType,
            ent.Comp.TotalIntensity,
            ent.Comp.IntensitySlope,
            ent.Comp.MaxTileIntensity,
            ent.Comp.TileBreakScale,
            ent.Comp.MaxTileBreak,
            ent.Comp.CanCreateVacuum,
            user);
        args.Handled = true;
        // WH40K
    }

    // WH40K
    private EntityUid? ResolveExplosionUser(EntityUid owner, EntityUid? fallback)
    {
        if (TryComp(owner, out ProjectileComponent? projectile) && projectile.Shooter is { } shooter)
            return shooter;

        if (TryComp(owner, out TimerTriggerComponent? timer) && timer.User is { } timerUser)
            return timerUser;

        if (TryComp(owner, out ThrownItemComponent? thrown) && thrown.Thrower is { } thrower)
            return thrower;

        return fallback;
    }
    // WH40K
}
