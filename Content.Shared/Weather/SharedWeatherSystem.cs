using System.Diagnostics.CodeAnalysis;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.Weather;

public abstract class SharedWeatherSystem : EntitySystem
{
    [Dependency] protected readonly IGameTiming Timing = default!;
    [Dependency] protected readonly IPrototypeManager ProtoMan = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefManager = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedRoofSystem _roof = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;

    private EntityQuery<BlockWeatherComponent> _blockQuery;
    private EntityQuery<WeatherStatusEffectComponent> _weatherQuery;

    public static readonly TimeSpan StartupTime = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ShutdownTime = TimeSpan.FromSeconds(15);

    public override void Initialize()
    {
        base.Initialize();

        _blockQuery = GetEntityQuery<BlockWeatherComponent>();
        _weatherQuery = GetEntityQuery<WeatherStatusEffectComponent>();
    }

    public bool IsWeatherPrototype(EntProtoId weatherProto)
    {
        return TryGetWeatherPrototype(weatherProto, out _);
    }

    public bool TryGetWeatherPrototype(
        EntProtoId weatherProto,
        [NotNullWhen(true)] out WeatherStatusEffectComponent? weather)
    {
        weather = null;

        if (!ProtoMan.Resolve(weatherProto, out EntityPrototype? proto))
            return false;

        return proto.TryGetComponent(out weather, Factory);
    }

    public bool TryGetWeatherEffects(
        EntityUid? target,
        [NotNullWhen(true)] out HashSet<Entity<WeatherStatusEffectComponent, StatusEffectComponent>>? effects)
    {
        return _statusEffects.TryEffectsWithComp<WeatherStatusEffectComponent>(target, out effects);
    }

    public bool IsWeatherEnding(Entity<StatusEffectComponent?> weather)
    {
        if (!Resolve(weather, ref weather.Comp, false) || weather.Comp.EndEffectTime is not { } endTime)
            return false;

        return endTime - Timing.CurTime <= ShutdownTime;
    }

    public bool CanWeatherAffect(
        EntityUid uid,
        MapGridComponent grid,
        TileRef tileRef,
        RoofComponent? roofComp = null,
        WeatherStatusEffectComponent? weather = null)
    {
        var exposure = weather?.ExposureMode ?? WeatherExposureMode.UnroofedOnly;
        var isRooved = IsRoovedForWeather(uid, grid, tileRef.GridIndices, roofComp);

        switch (exposure)
        {
            case WeatherExposureMode.UnroofedOnly when isRooved:
            case WeatherExposureMode.RoofedOnly when !isRooved:
                return false;
        }

        if (tileRef.Tile.IsEmpty)
            return true;

        if (weather?.RespectTileWeather ?? true)
        {
            var tileDef = (ContentTileDefinition) _tileDefManager[tileRef.Tile.TypeId];
            if (!tileDef.Weather)
                return false;
        }

        if (weather?.IgnoreBlockers == true)
            return true;

        var anchoredEntities = _mapSystem.GetAnchoredEntitiesEnumerator(uid, grid, tileRef.GridIndices);
        while (anchoredEntities.MoveNext(out var anchored))
        {
            if (_blockQuery.HasComponent(anchored.Value))
                return false;
        }

        return true;
    }

    public bool CanWeatherAffect(Entity<MapGridComponent?, RoofComponent?> ent, TileRef tileRef, WeatherStatusEffectComponent? weather = null)
    {
        if (!Resolve(ent, ref ent.Comp1))
            return false;

        return CanWeatherAffect(ent.Owner, ent.Comp1, tileRef, ent.Comp2, weather);
    }

    public bool CanWeatherAffectEntity(
        EntityUid entity,
        WeatherStatusEffectComponent? weather = null,
        TransformComponent? xform = null)
    {
        var exposure = weather?.ExposureMode ?? WeatherExposureMode.UnroofedOnly;
        var current = entity;

        for (var i = 0; i < 8; i++)
        {
            if (!Resolve(current, ref xform, false))
                return false;

            if (xform.GridUid != null)
            {
                if (!TryComp<MapGridComponent>(xform.GridUid, out var gridComp))
                    return false;

                if (!_mapSystem.TryGetTileRef(xform.GridUid.Value, gridComp, xform.Coordinates, out var tileRef))
                    return false;

                TryComp<RoofComponent>(xform.GridUid, out var roofComp);
                return CanWeatherAffect(xform.GridUid.Value, gridComp, tileRef, roofComp, weather);
            }

            if (!xform.ParentUid.IsValid() || xform.ParentUid == current)
                break;

            current = xform.ParentUid;
            xform = null;
        }

        return exposure != WeatherExposureMode.RoofedOnly;
    }

    private bool IsRoovedForWeather(
        EntityUid uid,
        MapGridComponent grid,
        Vector2i tileIndices,
        RoofComponent? roofComp = null)
    {
        if (HasComp<ImplicitRoofComponent>(uid))
            return true;

        return Resolve(uid, ref roofComp, false) && _roof.IsRooved((uid, grid, roofComp), tileIndices);
    }

    /// <summary>
    /// Calculates the current "strength" of the specified weather based on the duration of the status effect.
    /// Between 0 and 1.
    /// </summary>
    public float GetWeatherPercent(Entity<StatusEffectComponent> ent)
    {
        var elapsed = Timing.CurTime - ent.Comp.StartEffectTime;
        var duration = ent.Comp.Duration;
        var remaining = duration - elapsed;

        if (remaining < ShutdownTime)
            return (float) (remaining / ShutdownTime);

        if (elapsed < StartupTime)
            return (float) (elapsed / StartupTime);

        return 1f;
    }

    public bool TryAddWeather(MapId mapId, EntProtoId weatherProto, [NotNullWhen(true)] out EntityUid? weatherEnt, TimeSpan? duration = null)
    {
        weatherEnt = null;

        if (!_mapSystem.TryGetMap(mapId, out var mapUid))
            return false;

        return TryAddWeather(mapUid.Value, weatherProto, out weatherEnt, duration);
    }

    public bool TryAddWeather(EntityUid mapUid, EntProtoId weatherProto, [NotNullWhen(true)] out EntityUid? weatherEnt, TimeSpan? duration = null)
    {
        return _statusEffects.TrySetStatusEffectDuration(mapUid, weatherProto, out weatherEnt, duration);
    }

    public bool HasWeather(MapId mapId, EntProtoId weatherProto)
    {
        if (!_mapSystem.TryGetMap(mapId, out var mapUid))
            return false;

        return _statusEffects.TryGetStatusEffect(mapUid.Value, weatherProto, out _);
    }

    public bool TryRemoveWeather(MapId mapId, EntProtoId weatherProto)
    {
        if (!_mapSystem.TryGetMap(mapId, out var mapUid))
            return false;

        return TryRemoveWeather(mapUid.Value, weatherProto);
    }

    public bool TryRemoveWeather(EntityUid mapUid, EntProtoId weatherProto)
    {
        if (!_statusEffects.TryGetStatusEffect(mapUid, weatherProto, out var weatherEnt))
            return false;

        if (!_weatherQuery.HasComp(weatherEnt))
            return false;

        return _statusEffects.TrySetStatusEffectDuration(mapUid, weatherProto, ShutdownTime);
    }

    public bool TrySetWeather(MapId mapId, EntProtoId? weatherProto, out EntityUid? weatherEnt, TimeSpan? duration = null)
    {
        weatherEnt = null;

        if (!_mapSystem.TryGetMap(mapId, out var mapUid))
            return false;

        if (_statusEffects.TryEffectsWithComp<WeatherStatusEffectComponent>(mapUid, out var effects))
        {
            foreach (var effect in effects)
            {
                var effectProto = Prototype(effect.Owner);
                if (effectProto is null)
                    continue;

                if (effectProto != weatherProto)
                    TryRemoveWeather(mapUid.Value, effectProto);
                else
                    weatherEnt = effect;
            }
        }

        if (weatherProto is null)
            return true;

        if (weatherEnt != null)
        {
            TryAddWeather(mapUid.Value, weatherProto.Value, out weatherEnt, duration);
            return true;
        }

        return TryAddWeather(mapUid.Value, weatherProto.Value, out weatherEnt, duration);
    }
}
