using Content.Shared.Examine;
using Content.Shared._WH40K.WaveDefence;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.WaveDefence;

/// <summary>
/// Adds map-authoring quality-of-life for WaveDefence markers:
/// richer debug names and useful examine info.
/// </summary>
public sealed partial class WH40KWaveDefenceMapAuthoringSystem : EntitySystem
{
    [Dependency] private  MetaDataSystem _metaData = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KWaveSpawnPointComponent, MapInitEvent>(OnSpawnPointMapInit);
        SubscribeLocalEvent<WH40KWaveSpawnPointComponent, ExaminedEvent>(OnSpawnPointExamined);

        SubscribeLocalEvent<WH40KWaveImperiumBaseComponent, MapInitEvent>(OnBaseMarkerMapInit);
        SubscribeLocalEvent<WH40KWaveImperiumBaseComponent, ExaminedEvent>(OnBaseMarkerExamined);

        SubscribeLocalEvent<WH40KWaveDefenceObjectiveComponent, ComponentStartup>(OnObjectiveStartup);
        SubscribeLocalEvent<WH40KWaveDefenceObjectiveComponent, ExaminedEvent>(OnObjectiveExamined);

        SubscribeLocalEvent<WH40KWaveAttackersOnlyBarrierComponent, MapInitEvent>(OnAttackersOnlyBarrierMapInit);
        SubscribeLocalEvent<WH40KWaveAttackersOnlyBarrierComponent, ExaminedEvent>(OnAttackersOnlyBarrierExamined);
    }

    private void OnSpawnPointMapInit(Entity<WH40KWaveSpawnPointComponent> ent, ref MapInitEvent args)
    {
        RefreshSpawnPointDebugName(ent);
    }

    private void OnBaseMarkerMapInit(Entity<WH40KWaveImperiumBaseComponent> ent, ref MapInitEvent args)
    {
        RefreshImperiumBaseDebugName(ent);
    }

    private void OnObjectiveStartup(Entity<WH40KWaveDefenceObjectiveComponent> ent, ref ComponentStartup args)
    {
        RefreshObjectiveDebugName(ent);
    }

    private void OnAttackersOnlyBarrierMapInit(Entity<WH40KWaveAttackersOnlyBarrierComponent> ent, ref MapInitEvent args)
    {
        RefreshAttackersOnlyBarrierDebugName(ent);
    }

    private void RefreshSpawnPointDebugName(Entity<WH40KWaveSpawnPointComponent> ent)
    {
        var meta = MetaData(ent);
        var baseName = GetPrototypeOrCurrentName(meta);
        var spawnId = string.IsNullOrWhiteSpace(ent.Comp.SpawnId) ? "<any>" : Safe(ent.Comp.SpawnId);
        var team = string.IsNullOrWhiteSpace(ent.Comp.TeamId) ? "-" : Safe(ent.Comp.TeamId);

        _metaData.SetEntityName(
            ent.Owner,
            $"{baseName} [{ent.Comp.SpawnType} id={spawnId} team={team} prio={ent.Comp.Priority}]",
            meta);
    }

    private void RefreshImperiumBaseDebugName(Entity<WH40KWaveImperiumBaseComponent> ent)
    {
        var meta = MetaData(ent);
        var baseName = GetPrototypeOrCurrentName(meta);
        _metaData.SetEntityName(ent.Owner, $"{baseName} [team={Safe(ent.Comp.TeamId)}]", meta);
    }

    private void RefreshObjectiveDebugName(Entity<WH40KWaveDefenceObjectiveComponent> ent)
    {
        var meta = MetaData(ent);
        var baseName = GetPrototypeOrCurrentName(meta);
        var primary = ent.Comp.IsPrimaryObjective ? " primary" : string.Empty;
        _metaData.SetEntityName(ent.Owner, $"{baseName} [team={Safe(ent.Comp.TeamId)}{primary}]", meta);
    }

    private void RefreshAttackersOnlyBarrierDebugName(Entity<WH40KWaveAttackersOnlyBarrierComponent> ent)
    {
        var meta = MetaData(ent);
        var baseName = GetPrototypeOrCurrentName(meta);
        _metaData.SetEntityName(ent.Owner, $"{baseName} [wave-attackers-only]", meta);
    }

    private void OnSpawnPointExamined(Entity<WH40KWaveSpawnPointComponent> ent, ref ExaminedEvent args)
    {
        using var _ = args.PushGroup("wh40k-wave-map-authoring", 10);
        args.PushMarkup($"SpawnType: {ent.Comp.SpawnType}");

        if (!string.IsNullOrWhiteSpace(ent.Comp.TeamId))
            args.PushMarkup($"TeamId: {Safe(ent.Comp.TeamId)}");

        if (!string.IsNullOrWhiteSpace(ent.Comp.SpawnId))
            args.PushMarkup($"SpawnId: {Safe(ent.Comp.SpawnId)}");

        args.PushMarkup($"Priority: {ent.Comp.Priority}");
    }

    private void OnBaseMarkerExamined(Entity<WH40KWaveImperiumBaseComponent> ent, ref ExaminedEvent args)
    {
        using var _ = args.PushGroup("wh40k-wave-map-authoring", 10);
        args.PushMarkup($"TeamId: {Safe(ent.Comp.TeamId)}");
        args.PushMarkup("Usage: strategic base marker for WaveDefence layout.");
    }

    private void OnObjectiveExamined(Entity<WH40KWaveDefenceObjectiveComponent> ent, ref ExaminedEvent args)
    {
        using var _ = args.PushGroup("wh40k-wave-map-authoring", 10);
        args.PushMarkup($"TeamId: {Safe(ent.Comp.TeamId)}");
        args.PushMarkup($"Primary: {ent.Comp.IsPrimaryObjective}");
        args.PushMarkup($"MaxHealth: {ent.Comp.MaxHealth}");
        args.PushMarkup($"WarnAtPercent: {ent.Comp.WarnAtPercent:0.##}");
    }

    private void OnAttackersOnlyBarrierExamined(Entity<WH40KWaveAttackersOnlyBarrierComponent> ent, ref ExaminedEvent args)
    {
        using var _ = args.PushGroup("wh40k-wave-map-authoring", 10);
        args.PushMarkup("Blocks: defenders / non-wave entities");
        args.PushMarkup("Passes: WaveDefence attackers");
        args.PushMarkup("Use on attacker spawn exits to prevent spawn-camping.");
    }

    private static string GetPrototypeOrCurrentName(MetaDataComponent meta)
    {
        return meta.EntityPrototype?.Name ?? meta.EntityName;
    }

    private static string Safe(string? value, string fallback = "-")
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return FormattedMessage.EscapeText(value);
    }
}
