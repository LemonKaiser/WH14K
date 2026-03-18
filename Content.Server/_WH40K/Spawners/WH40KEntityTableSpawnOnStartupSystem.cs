using System.Numerics;
using Content.Server._WH40K.Spawners.Components;
using Content.Shared.EntityTable;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Spawners;

/// <summary>
/// Runtime-safe table spawner for WH40K markers.
/// Used by timed wave markers that are spawned after map initialization.
/// </summary>
public sealed class WH40KEntityTableSpawnOnStartupSystem : EntitySystem
{
    [Dependency] private readonly EntityTableSystem _entityTable = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KEntityTableSpawnOnStartupComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<WH40KEntityTableSpawnOnStartupComponent> ent, ref ComponentStartup args)
    {
        if (TerminatingOrDeleted(ent) || !Exists(ent))
            return;

        var coords = Transform(ent).Coordinates;
        var spawns = _entityTable.GetSpawns(ent.Comp.Table);
        foreach (var proto in spawns)
        {
            var xOffset = _random.NextFloat(-ent.Comp.Offset, ent.Comp.Offset);
            var yOffset = _random.NextFloat(-ent.Comp.Offset, ent.Comp.Offset);
            var spawnCoords = coords.Offset(new Vector2(xOffset, yOffset));

            SpawnAtPosition(proto, spawnCoords);
        }

        if (ent.Comp.DeleteSpawnerAfterSpawn && !TerminatingOrDeleted(ent) && Exists(ent))
            QueueDel(ent);
    }
}
