using Content.Shared.Stray.Weapons.FireUnderBullet;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Map;

namespace Content.Server.Stray.Weapons.FireUnderBullet;

public sealed class FireUnderBulletSystem : SharedFireUnderBulletSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSys = default!;
    [Dependency] protected readonly IGameTiming Timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FireUnderBulletComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnShoot);
        SubscribeLocalEvent<FireUnderBulletComponent, ProjectileHitEvent>(OnHit);
    }

    private void OnShoot(EntityUid uid, GunComponent component, ref GunShotEvent args)
    {
        foreach (var dat in args.Ammo)
        {
            if (dat.Uid == null || !TryComp<FireUnderBulletComponent>(dat.Uid, out var comp))
                continue;

            comp.pickedUp = false;
            comp.removeTime = Timing.CurTime + TimeSpan.FromSeconds(0.3f);
            comp.minusTime = Timing.CurTime;
            comp.startTime = Timing.CurTime + TimeSpan.FromSeconds(0.07f);
        }
    }

    private void OnHit(EntityUid uid, FireUnderBulletComponent component, ref ProjectileHitEvent args)
    {
        if (component.HitRelease)
        {
            ReleaseGas((uid, component));
        }
    }

    private void OnInit(EntityUid uid, FireUnderBulletComponent component, ref ComponentInit args)
    {
        component.removeTime = Timing.CurTime + TimeSpan.FromSeconds(0.3f);
        component.minusTime = Timing.CurTime;
        component.startTime = Timing.CurTime + TimeSpan.FromSeconds(0.07f);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FireUnderBulletComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.startTime < Timing.CurTime)
            {
                if (!comp.pickedUp)
                {
                    ReleaseGas((uid, comp));
                    if (comp.removeTime < Timing.CurTime)
                    {
                        QueueDel(uid);
                    }
                }
            }
        }
    }

    private void ReleaseGas(Entity<FireUnderBulletComponent> ent)
    {
        var component = ent.Comp;
        var environment = _atmosphereSystem.GetContainingMixture(ent.Owner, false, true);
        if (environment == null)
            return;

        var removed = component.releaseGas.Clone();
        removed.Temperature = component.releaseTemp;
        
        // Scale moles by releaseSpeed
        if (component.releaseSpeed != 1.0f)
        {
            for (int i = 0; i < Atmospherics.TotalNumberOfGases; i++)
            {
                removed.SetMoles(i, removed.GetMoles(i) * component.releaseSpeed);
            }
        }

        _atmosphereSystem.Merge(environment, removed);
    }
}
