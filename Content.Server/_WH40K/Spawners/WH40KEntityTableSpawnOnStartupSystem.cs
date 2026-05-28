using System.Numerics;
using Content.Server._WH40K.Spawners.Components;
using Content.Shared.EntityTable;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Spawners;

/// <summary>
/// Runtime-safe table spawner for WH40K markers.
/// Used by timed wave markers that are spawned after map initialization.
/// </summary>
public sealed partial class WH40KEntityTableSpawnOnStartupSystem : EntitySystem
{
    [Dependency] private  EntityTableSystem _entityTable = default!;
    [Dependency] private  IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KEntityTableSpawnOnStartupComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WH40KEntityTableSpawnOnStartupComponent> ent, ref MapInitEvent args)
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

            SpawnAttachedTo(proto, spawnCoords);
        }

        if (ent.Comp.DeleteSpawnerAfterSpawn && !TerminatingOrDeleted(ent) && Exists(ent))
            QueueDel(ent);
    }
}
