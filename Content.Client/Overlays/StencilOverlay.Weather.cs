using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.Weather;
using Robust.Client.Graphics;
using Robust.Shared.Map.Components;

namespace Content.Client.Overlays;

public sealed partial class StencilOverlay
{
    private List<Entity<MapGridComponent>> _grids = new();

    private void DrawWeather(
        in OverlayDrawArgs args,
        CachedResources res,
        HashSet<Entity<WeatherStatusEffectComponent, StatusEffectComponent>> weathers,
        Matrix3x2 invMatrix)
    {
        var worldHandle = args.WorldHandle;
        var mapId = args.MapId;
        var worldAABB = args.WorldAABB;
        var worldBounds = args.WorldBounds;
        var position = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;
        var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
        var curTime = _timing.RealTime;

        foreach (var (uid, weather, status) in weathers)
        {
            worldHandle.RenderInRenderTarget(res.Blep!,
                () =>
                {
                    _grids.Clear();
                    _mapManager.FindGridsIntersecting(mapId, worldAABB, ref _grids);

                    foreach (var grid in _grids)
                    {
                        var matrix = _transform.GetWorldMatrix(grid, xformQuery);
                        var matty = Matrix3x2.Multiply(matrix, invMatrix);
                        worldHandle.SetTransform(matty);
                        _entManager.TryGetComponent<RoofComponent>(grid.Owner, out var roofComp);

                        foreach (var tile in _map.GetTilesIntersecting(grid.Owner, grid, worldAABB))
                        {
                            if (!_weather.CanWeatherAffect(grid.Owner, grid.Comp, tile, roofComp, weather))
                                continue;

                            var gridTile = new Box2(tile.GridIndices * grid.Comp.TileSize,
                                (tile.GridIndices + Vector2i.One) * grid.Comp.TileSize);

                            worldHandle.DrawRect(gridTile, Color.White);
                        }
                    }
                },
                Color.Transparent);

            worldHandle.SetTransform(Matrix3x2.Identity);
            worldHandle.UseShader(_protoManager.Index(StencilMask).Instance());
            worldHandle.DrawTextureRect(res.Blep!.Texture, worldBounds);

            var alpha = _weather.GetWeatherPercent((uid, status));
            var sprite = _sprite.GetFrame(weather.Sprite, curTime);

            // Weather should render on the masked tiles themselves, not on the inverse of the mask.
            worldHandle.UseShader(_protoManager.Index(WeatherStencilDraw).Instance());
            _parallax.DrawParallax(worldHandle,
                worldAABB,
                sprite,
                curTime,
                position,
                weather.Scrolling ?? Vector2.Zero,
                modulate: (weather.Color ?? Color.White).WithAlpha(alpha));
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
