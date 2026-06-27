using Content.Shared.Examine;
using Content.Shared._WH40K.GunGame;

namespace Content.Server._WH40K.GunGame;

public sealed partial class WH40KGunGameSpawnPointAuthoringSystem : EntitySystem
{
    [Dependency] private MetaDataSystem _metaData = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KGunGameSpawnPointComponent, MapInitEvent>(OnSpawnPointMapInit);
        SubscribeLocalEvent<WH40KGunGameSpawnPointComponent, ExaminedEvent>(OnSpawnPointExamined);
    }

    private void OnSpawnPointMapInit(Entity<WH40KGunGameSpawnPointComponent> ent, ref MapInitEvent args)
    {
        var meta = MetaData(ent);
        var baseName = GetPrototypeOrCurrentName(meta);
        _metaData.SetEntityName(ent.Owner, $"{baseName} [prio={ent.Comp.Priority}]", meta);
    }

    private void OnSpawnPointExamined(Entity<WH40KGunGameSpawnPointComponent> ent, ref ExaminedEvent args)
    {
        using var _ = args.PushGroup("wh40k-gungame-map-authoring", 10);
        args.PushMarkup($"Priority: {ent.Comp.Priority}");
    }

    private static string GetPrototypeOrCurrentName(MetaDataComponent meta)
    {
        return meta.EntityPrototype?.Name ?? meta.EntityName;
    }
}
