using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._WH40K.Audio;
using Content.Shared.Audio;
using Content.Shared.Maps;
using Content.Shared._WH40K.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WH40K.Audio;

[TestFixture]
[NonParallelizable]
public sealed class WH40KLavaAmbientFieldTests : GameTest
{
    private static readonly ProtoId<ContentTileDefinition> SnowTileId = "FloorSnow";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        Dirty = true,
        Fresh = true,
    };

    [Test]
    public async Task DenseLavaClusterSelectsSingleManagedEmitter()
    {
        EntityUid gridUid = default;
        MapGridComponent gridComp = default!;

        await Server.WaitAssertion(() =>
        {
            (gridUid, gridComp) = CreateFilledGrid();

            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    SpawnLava(gridUid, gridComp, new Vector2i(x, y));
                }
            }
        });

        await Server.WaitRunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var emitters = GetManagedEmitters().ToArray();
            Assert.That(emitters.Length, Is.EqualTo(1));
            Assert.That(SEntMan.TryGetComponent<AmbientSoundComponent>(emitters[0], out _), Is.True);
            Assert.That(SEntMan.TryGetComponent<WH40KAmbientFieldSourceComponent>(emitters[0], out _), Is.True);
        });
    }

    [Test]
    public async Task LavaLineUsesSparseEmittersWithConfiguredSpacing()
    {
        EntityUid gridUid = default;
        MapGridComponent gridComp = default!;

        await Server.WaitAssertion(() =>
        {
            (gridUid, gridComp) = CreateFilledGrid();

            for (var x = 0; x < 14; x++)
            {
                SpawnLava(gridUid, gridComp, new Vector2i(x, 0));
            }
        });

        await Server.WaitRunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var emitters = GetManagedEmitters().ToArray();
            Assert.That(emitters.Length, Is.GreaterThan(1));
            Assert.That(emitters.Length, Is.LessThan(14));

            var positions = emitters
                .Select(uid => Server.System<SharedTransformSystem>().GetWorldPosition(uid))
                .ToArray();

            for (var i = 0; i < positions.Length; i++)
            {
                for (var j = i + 1; j < positions.Length; j++)
                {
                    Assert.That(Vector2.Distance(positions[i], positions[j]), Is.GreaterThanOrEqualTo(6f));
                }
            }
        });
    }

    [Test]
    public async Task RemovingLavaCleansUpManagedEmitter()
    {
        EntityUid lava = default;
        EntityUid gridUid = default;
        MapGridComponent gridComp = default!;

        await Server.WaitAssertion(() =>
        {
            (gridUid, gridComp) = CreateFilledGrid();
            lava = SpawnLava(gridUid, gridComp, new Vector2i(0, 0));
        });

        await Server.WaitRunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(GetManagedEmitters().Count(), Is.EqualTo(1));
        });

        await Server.WaitPost(() => SEntMan.DeleteEntity(lava));
        await Server.WaitRunTicks(20);

        await Server.WaitAssertion(() =>
        {
            Assert.That(GetManagedEmitters(), Is.Empty);
        });
    }

    [Test]
    public async Task IncrementalGrowthPreservesExistingManagedEmitter()
    {
        EntityUid firstLava = default;
        EntityUid replacementLava = default;
        EntityUid gridUid = default;
        MapGridComponent gridComp = default!;
        Vector2i firstTile = default;
        Vector2i replacementTile = default;

        await Server.WaitAssertion(() =>
        {
            (gridUid, gridComp) = CreateFilledGrid();
            var mapId = SComp<TransformComponent>(gridUid).MapID;
            (firstTile, replacementTile) = FindEmitterReplacementPair(mapId);
            firstLava = SpawnLava(gridUid, gridComp, firstTile);
        });

        await Server.WaitRunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(GetManagedEmitters().Single(), Is.EqualTo(firstLava));
        });

        await Server.WaitPost(() =>
        {
            replacementLava = SpawnLava(gridUid, gridComp, replacementTile);
        });

        await Server.WaitRunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var emitters = GetManagedEmitters().ToArray();
            Assert.That(emitters.Length, Is.EqualTo(1));
            Assert.That(emitters[0], Is.EqualTo(firstLava));
            Assert.That(emitters, Does.Not.Contain(replacementLava));
        });
    }

    private (EntityUid GridUid, MapGridComponent GridComp) CreateFilledGrid()
    {
        var mapManager = Server.ResolveDependency<IMapManager>();
        var mapSystem = Server.System<SharedMapSystem>();
        var fillTile = SProtoMan.Index(SnowTileId);
        mapSystem.CreateMap(out var mapId);
        var gridUid = mapManager.CreateGridEntity(mapId);
        var gridComp = SComp<MapGridComponent>(gridUid);

        for (var x = -6; x <= 20; x++)
        {
            for (var y = -6; y <= 20; y++)
            {
                mapSystem.SetTile(gridUid, gridComp, new Vector2i(x, y), new Tile(fillTile.TileId));
            }
        }

        return (gridUid, gridComp);
    }

    private EntityUid SpawnLava(EntityUid gridUid, MapGridComponent gridComp, Vector2i tile)
    {
        var mapSystem = Server.System<SharedMapSystem>();
        return SEntMan.SpawnEntity("FloorLavaEntity", mapSystem.ToCenterCoordinates(gridUid, tile, gridComp));
    }

    private static (Vector2i InitialTile, Vector2i ReplacementTile) FindEmitterReplacementPair(MapId mapId)
    {
        for (var originX = 0; originX <= 4; originX++)
        {
            for (var originY = 0; originY <= 4; originY++)
            {
                var initialTile = new Vector2i(originX, originY);
                var initialPriority = StablePriority(mapId, initialTile);

                for (var dx = -5; dx <= 5; dx++)
                {
                    for (var dy = -5; dy <= 5; dy++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        var replacementTile = initialTile + new Vector2i(dx, dy);
                        var distance = Vector2.Distance(
                            new Vector2(initialTile.X + 0.5f, initialTile.Y + 0.5f),
                            new Vector2(replacementTile.X + 0.5f, replacementTile.Y + 0.5f));
                        if (distance >= 6f)
                            continue;

                        if (StablePriority(mapId, replacementTile) < initialPriority)
                            return (initialTile, replacementTile);
                    }
                }
            }
        }

        throw new InvalidOperationException("Failed to find a deterministic managed-emitter replacement pair for the ambient-field test.");
    }

    private static int StablePriority(MapId mapId, Vector2i tile)
    {
        unchecked
        {
            var map = (uint) (int) mapId;
            var x = (uint) QuantizeAxis(tile.X + 0.5f);
            var y = (uint) QuantizeAxis(tile.Y + 0.5f);
            return (int) ((x * 73856093u) ^ (y * 19349663u) ^ (map * 83492791u));
        }
    }

    private static int QuantizeAxis(float value)
    {
        return (int) MathF.Round(value * 8f);
    }

    private IEnumerable<EntityUid> GetManagedEmitters()
    {
        var query = Server.EntMan.EntityQueryEnumerator<WH40KAmbientFieldEmitterComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            yield return uid;
        }
    }
}
