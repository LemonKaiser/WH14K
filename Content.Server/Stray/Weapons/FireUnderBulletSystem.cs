using Content.Shared.Stray.Weapons.FireUnderBullet;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Projectiles;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server.Stray.Weapons.FireUnderBullet;

public sealed partial class FireUnderBulletSystem : SharedFireUnderBulletSystem
{
    [Dependency] private  AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private  TransformSystem _transform = default!;
    [Dependency] private  IGameTiming _timing = default!;

    private EntityQuery<TransformComponent> _xformQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        _xformQuery = GetEntityQuery<TransformComponent>();
        SubscribeLocalEvent<FireUnderBulletComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<GunComponent, AmmoShotEvent>(OnShoot);
        SubscribeLocalEvent<FireUnderBulletComponent, ProjectileHitEvent>(OnHit);
    }

    private void OnShoot(Entity<GunComponent> ent, ref AmmoShotEvent args)
    {
        foreach (var projectile in args.FiredProjectiles)
        {
            if (!TryComp<FireUnderBulletComponent>(projectile, out var comp))
                continue;

            comp.pickedUp = false;
            comp.removeTime = _timing.CurTime + TimeSpan.FromSeconds(0.3f);
            comp.minusTime = _timing.CurTime;
            comp.startTime = _timing.CurTime + TimeSpan.FromSeconds(0.07f);
        }
    }

    private void OnHit(EntityUid uid, FireUnderBulletComponent component, ref ProjectileHitEvent args)
    {
        if (component.HotspotExpose)
        {
            TryExposeHotspot(args.Target, component, uid);
            return;
        }

        if (component.HitRelease)
        {
            ReleaseGas((uid, component));
        }
    }

    private void OnInit(EntityUid uid, FireUnderBulletComponent component, ref ComponentInit args)
    {
        component.removeTime = _timing.CurTime + TimeSpan.FromSeconds(0.3f);
        component.minusTime = _timing.CurTime;
        component.startTime = _timing.CurTime + TimeSpan.FromSeconds(0.07f);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FireUnderBulletComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.startTime < _timing.CurTime)
            {
                if (comp.HotspotExpose || comp.HitRelease)
                {
                    if (!comp.pickedUp && comp.removeTime < _timing.CurTime)
                        QueueDel(uid);

                    continue;
                }

                if (!comp.pickedUp)
                {
                    ReleaseGas((uid, comp));
                    if (comp.removeTime < _timing.CurTime)
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

    private void TryExposeHotspot(EntityUid target, FireUnderBulletComponent component, EntityUid? source)
    {
        if (!_xformQuery.TryGetComponent(target, out var xform))
            return;

        var grid = xform.GridUid;
        var map = xform.MapUid;
        if (grid == null && map == null)
            return;

        if (grid == null)
            return;

        var indices = _transform.GetGridTilePositionOrDefault((target, xform));

        var exposeRadius = Math.Max(0, component.HotspotExposeRadius);
        for (var dx = -exposeRadius; dx <= exposeRadius; dx++)
        {
            for (var dy = -exposeRadius; dy <= exposeRadius; dy++)
            {
                var tile = indices + new Vector2i(dx, dy);
                if (component.HotspotSeedMoles > 0f)
                {
                    var mix = _atmosphereSystem.GetTileMixture(grid, map, tile, true);
                    if (mix != null)
                        mix.AdjustMoles(component.HotspotSeedGas, component.HotspotSeedMoles);
                }

                _atmosphereSystem.HotspotExpose(grid.Value, tile, component.HotspotTemperature, component.HotspotVolume, source, true);
            }
        }

        if (component.HotspotCleanupDelay > 0f)
        {
            var cleanupAfter = TimeSpan.FromSeconds(component.HotspotCleanupDelay);
            var cleanupRadius = Math.Max(0, component.HotspotCleanupRadius);
            var cleanupTemp = component.HotspotCleanupTemperature;
            var cleanupRemoveGases = component.HotspotCleanupRemoveGases;
            var gridUid = grid.Value;
            var mapUid = map;
            var center = indices;

            Timer.Spawn(cleanupAfter, () =>
            {
                for (var cx = -cleanupRadius; cx <= cleanupRadius; cx++)
                {
                    for (var cy = -cleanupRadius; cy <= cleanupRadius; cy++)
                    {
                        var tile = center + new Vector2i(cx, cy);
                        _atmosphereSystem.HotspotExtinguish(gridUid, tile);

                        var mix = _atmosphereSystem.GetTileMixture(gridUid, mapUid, tile, true);
                        if (mix == null || mix.Immutable)
                            continue;

                        if (cleanupTemp > 0f)
                            mix.Temperature = MathF.Min(mix.Temperature, cleanupTemp);

                        if (cleanupRemoveGases)
                        {
                            mix.SetMoles(Gas.Plasma, 0f);
                            mix.SetMoles(Gas.Tritium, 0f);
                            mix.SetMoles(Gas.WaterVapor, 0f);
                        }
                    }
                }
            });
        }
    }
}
