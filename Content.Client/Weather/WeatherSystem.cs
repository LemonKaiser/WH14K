using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.Weather;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Client.Weather;

public sealed partial class WeatherSystem : SharedWeatherSystem
{
    [Dependency] private  IPlayerManager _playerManager = default!;
    [Dependency] private  AudioSystem _audio = default!;
    [Dependency] private  SharedMapSystem _mapSystem = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    [Dependency] private  EntityQuery<AudioComponent> _audioQuery = default!;
    [Dependency] private  EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private  EntityQuery<RoofComponent> _roofQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeatherStatusEffectComponent, ComponentShutdown>(OnComponentShutdown);
    }

    private void OnComponentShutdown(Entity<WeatherStatusEffectComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Stream = _audio.Stop(ent.Comp.Stream);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!Timing.IsFirstTimePredicted)
            return;

        var player = _playerManager.LocalEntity;
        if (player == null)
            return;

        var playerXform = Transform(player.Value);
        var query = EntityQueryEnumerator<WeatherStatusEffectComponent, StatusEffectComponent>();
        while (query.MoveNext(out var uid, out var weather, out var status))
        {
            if (weather.Sound == null || status.AppliedTo != playerXform.MapUid)
            {
                weather.Stream = _audio.Stop(weather.Stream);
                continue;
            }

            weather.Stream ??= _audio.PlayGlobal(weather.Sound, Filter.Local(), true)?.Entity;
            if (!_audioQuery.TryComp(weather.Stream, out var audio))
                continue;

            var alpha = GetWeatherPercent((uid, status));
            alpha *= SharedAudioSystem.VolumeToGain(weather.Sound.Params.Volume);
            _audio.SetGain(weather.Stream, alpha, audio);
            audio.Occlusion = GetWeatherOcclusion(playerXform, weather);
        }
    }

    private float GetWeatherOcclusion(TransformComponent playerXform, WeatherStatusEffectComponent weather)
    {
        if (!_gridQuery.TryComp(playerXform.GridUid, out var grid))
            return 0f;

        _roofQuery.TryComp(playerXform.GridUid, out var roofComp);
        var gridId = playerXform.GridUid!.Value;
        var seed = _mapSystem.GetTileRef(gridId, grid, playerXform.Coordinates);
        var frontier = new Queue<TileRef>();
        frontier.Enqueue(seed);

        EntityCoordinates? nearestNode = null;
        var visited = new HashSet<Vector2i>();

        while (frontier.TryDequeue(out var node))
        {
            if (!visited.Add(node.GridIndices))
                continue;

            if (!CanWeatherAffect((gridId, grid, roofComp), node, weather))
            {
                for (var x = -1; x <= 1; x++)
                {
                    for (var y = -1; y <= 1; y++)
                    {
                        if ((Math.Abs(x) == 1 && Math.Abs(y) == 1) ||
                            (x == 0 && y == 0) ||
                            (new Vector2(x, y) + node.GridIndices - seed.GridIndices).Length() > 3)
                        {
                            continue;
                        }

                        frontier.Enqueue(_mapSystem.GetTileRef(gridId, grid, new Vector2i(x, y) + node.GridIndices));
                    }
                }

                continue;
            }

            nearestNode = new EntityCoordinates(gridId, node.GridIndices + grid.TileSizeHalfVector);
            break;
        }

        if (nearestNode == null)
            return 3f;

        var entPos = _transform.GetMapCoordinates(playerXform);
        var nodePosition = _transform.ToMapCoordinates(nearestNode.Value).Position;
        var delta = nodePosition - entPos.Position;
        var distance = delta.Length();
        return _audio.GetOcclusion(entPos, delta, distance);
    }
}
