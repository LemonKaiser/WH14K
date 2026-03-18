using System;
using System.Numerics;
using Content.Shared._WH40K.TacticalMap;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static Robust.UnitTesting.RobustIntegrationTest;

namespace Content.MapRenderer.Painters;

public sealed class WH40KTacticalMapBlackoutPainter
{
    private readonly IEntityManager _entityManager;
    private readonly EntityQuery<WH40KTacticalMapBlackoutComponent> _blackoutQuery;

    public WH40KTacticalMapBlackoutPainter(ServerIntegrationInstance server)
    {
        _entityManager = server.ResolveDependency<IEntityManager>();
        _blackoutQuery = _entityManager.GetEntityQuery<WH40KTacticalMapBlackoutComponent>();
    }

    public void Run(Image<Rgba32> gridCanvas, EntityUid gridUid, MapGridComponent grid, Vector2 customOffset = default)
    {
        if (!_blackoutQuery.TryComp(gridUid, out var blackout))
            return;

        var bounds = grid.LocalAABB;
        var xOffset = -bounds.Left;
        var yOffset = -bounds.Bottom;
        var tileSize = (int) (grid.TileSize * TilePainter.TileImageSize);
        var chunkSize = WH40KTacticalMapBlackoutComponent.ChunkSize;
        var black = new Rgba32(0, 0, 0, 255);

        foreach (var (chunkOrigin, bitMask) in blackout.Data)
        {
            if (bitMask == 0)
                continue;

            for (var bit = 0; bit < chunkSize * chunkSize; bit++)
            {
                if ((bitMask & ((ulong) 1 << bit)) == 0)
                    continue;

                var tileX = chunkOrigin.X + bit % chunkSize;
                var tileY = chunkOrigin.Y + bit / chunkSize;
                var pixelX = (int) ((tileX + xOffset + customOffset.X) * tileSize);
                var pixelY = (int) ((tileY + yOffset + customOffset.Y) * tileSize);

                FillRect(gridCanvas, pixelX, pixelY, tileSize, tileSize, black);
            }
        }
    }

    private static void FillRect(Image<Rgba32> image, int x, int y, int width, int height, Rgba32 color)
    {
        var minX = Math.Clamp(x, 0, image.Width);
        var minY = Math.Clamp(y, 0, image.Height);
        var maxX = Math.Clamp(x + width, 0, image.Width);
        var maxY = Math.Clamp(y + height, 0, image.Height);
        var fillWidth = maxX - minX;
        var fillHeight = maxY - minY;

        if (fillWidth <= 0 || fillHeight <= 0)
            return;

        image.ProcessPixelRows(accessor =>
        {
            for (var row = minY; row < maxY; row++)
            {
                accessor.GetRowSpan(row).Slice(minX, fillWidth).Fill(color);
            }
        });
    }
}
