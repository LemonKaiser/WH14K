using System.Collections.Generic;
using Content.Shared.Explosion.Components;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Explosion;

/// <summary>
/// Spawns WH40K-only secondary explosion visuals (shockwave and debris puffs).
/// </summary>
public sealed partial class WH40KExplosionVisualEffectsSystem : EntitySystem
{
    private const string ShockWaveEffect = "WH40KExplosionEffectShockWave";
    private const string DebrisEffect = "WH40KExplosionEffectDebris";
    private const int ShockWaveMinIterations = 5;
    private const float ShockWaveMinPeakIntensity = 4.5f;

    [Dependency] private  SharedMapSystem _map = default!;
    [Dependency] private  IRobustRandom _random = default!;

    private readonly HashSet<EntityUid> _spawnedEffects = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ExplosionVisualsComponent, ComponentShutdown>(OnExplosionVisualShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _spawnedEffects.Clear());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ExplosionVisualsComponent>();
        while (query.MoveNext(out var uid, out var visuals))
        {
            if (_spawnedEffects.Contains(uid))
                continue;

            if (visuals.Intensity.Count == 0 || !_map.MapExists(visuals.Epicenter.MapId))
                continue;

            _spawnedEffects.Add(uid);

            if (ShouldSpawnShockWave(visuals.Intensity))
                Spawn(ShockWaveEffect, visuals.Epicenter);

            SpawnDebris(visuals.Epicenter, visuals.Intensity.Count);
        }
    }

    private static bool ShouldSpawnShockWave(IReadOnlyList<float> intensity)
    {
        if (intensity.Count < ShockWaveMinIterations)
            return false;

        var peakIntensity = 0f;
        foreach (var value in intensity)
        {
            if (value > peakIntensity)
                peakIntensity = value;
        }

        return peakIntensity >= ShockWaveMinPeakIntensity;
    }

    private void SpawnDebris(MapCoordinates epicenter, int intensityIterations)
    {
        // Scale visual clutter with blast size while keeping count bounded.
        var count = Math.Clamp((int) MathF.Round(MathF.Sqrt(intensityIterations) * 3f), 3, 14);
        var radius = Math.Clamp(intensityIterations * 0.35f, 0.8f, 4f);

        for (var i = 0; i < count; i++)
        {
            if (!_random.Prob(0.85f))
                continue;

            var offset = _random.NextVector2(0.15f, radius);
            var pos = new MapCoordinates(epicenter.Position + offset, epicenter.MapId);
            Spawn(DebrisEffect, pos);
        }
    }

    private void OnExplosionVisualShutdown(EntityUid uid, ExplosionVisualsComponent component, ComponentShutdown args)
    {
        _spawnedEffects.Remove(uid);
    }
}
