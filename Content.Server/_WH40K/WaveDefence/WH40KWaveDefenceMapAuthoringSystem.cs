using System.Linq;
using Content.Shared.Examine;
using Content.Shared._WH40K.WaveDefence;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.WaveDefence;

/// <summary>
/// Adds map-authoring quality-of-life for WaveDefence markers:
/// auto-order lane points, richer debug names, and useful examine info.
/// </summary>
public sealed class WH40KWaveDefenceMapAuthoringSystem : EntitySystem
{
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KWaveLanePointComponent, MapInitEvent>(OnLanePointMapInit);
        SubscribeLocalEvent<WH40KWaveLanePointComponent, ExaminedEvent>(OnLanePointExamined);

        SubscribeLocalEvent<WH40KWaveSpawnPointComponent, MapInitEvent>(OnSpawnPointMapInit);
        SubscribeLocalEvent<WH40KWaveSpawnPointComponent, ExaminedEvent>(OnSpawnPointExamined);

        SubscribeLocalEvent<WH40KWaveImperiumBaseComponent, MapInitEvent>(OnBaseMarkerMapInit);
        SubscribeLocalEvent<WH40KWaveImperiumBaseComponent, ExaminedEvent>(OnBaseMarkerExamined);

        SubscribeLocalEvent<WH40KWaveDefenceObjectiveComponent, ComponentStartup>(OnObjectiveStartup);
        SubscribeLocalEvent<WH40KWaveDefenceObjectiveComponent, ExaminedEvent>(OnObjectiveExamined);

        SubscribeLocalEvent<WH40KWaveAttackersOnlyBarrierComponent, MapInitEvent>(OnAttackersOnlyBarrierMapInit);
        SubscribeLocalEvent<WH40KWaveAttackersOnlyBarrierComponent, ExaminedEvent>(OnAttackersOnlyBarrierExamined);
    }

    private void OnLanePointMapInit(Entity<WH40KWaveLanePointComponent> ent, ref MapInitEvent args)
    {
        var mapId = Transform(ent).MapID;
        if (mapId == MapId.Nullspace)
        {
            RefreshLanePointDebugName(ent);
            return;
        }

        NormalizeLaneAutoOrders(mapId, ent.Comp.LaneId);
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

    private void NormalizeLaneAutoOrders(MapId mapId, string laneId)
    {
        if (mapId == MapId.Nullspace || string.IsNullOrWhiteSpace(laneId))
            return;

        var lanePoints = new List<(EntityUid Uid, WH40KWaveLanePointComponent Point, MetaDataComponent Meta)>();
        var query = EntityQueryEnumerator<WH40KWaveLanePointComponent, TransformComponent, MetaDataComponent>();

        while (query.MoveNext(out var uid, out var point, out var xform, out var meta))
        {
            if (xform.MapID != mapId ||
                !string.Equals(point.LaneId, laneId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lanePoints.Add((uid, point, meta));
        }

        if (lanePoints.Count == 0)
            return;

        var autoPoints = lanePoints
            .Where(entry => entry.Point.AutoOrder)
            .OrderBy(entry => entry.Uid.Id)
            .ToList();

        if (autoPoints.Count > 0)
        {
            var usedOrders = lanePoints
                .Where(entry => !entry.Point.AutoOrder && entry.Point.Order >= 0)
                .Select(entry => entry.Point.Order)
                .ToHashSet();

            var nextOrder = 0;
            foreach (var entry in autoPoints)
            {
                while (usedOrders.Contains(nextOrder))
                {
                    nextOrder++;
                }

                if (entry.Point.Order != nextOrder)
                    entry.Point.Order = nextOrder;

                usedOrders.Add(nextOrder);
                nextOrder++;
            }
        }

        foreach (var entry in lanePoints)
        {
            RefreshLanePointDebugName((entry.Uid, entry.Point, entry.Meta));
        }
    }

    private void RefreshLanePointDebugName(Entity<WH40KWaveLanePointComponent> ent)
    {
        RefreshLanePointDebugName((ent.Owner, ent.Comp, MetaData(ent)));
    }

    private void RefreshLanePointDebugName((EntityUid Uid, WH40KWaveLanePointComponent Point, MetaDataComponent Meta) ent)
    {
        var baseName = GetPrototypeOrCurrentName(ent.Meta);
        var laneId = Safe(ent.Point.LaneId, "<no-lane>");
        var order = ent.Point.Order;
        var pointIdSuffix = string.IsNullOrWhiteSpace(ent.Point.PointId)
            ? string.Empty
            : $" [{Safe(ent.Point.PointId)}]";
        var autoSuffix = ent.Point.AutoOrder ? " auto" : string.Empty;
        var pointType = ent.Point.PointType.ToString();

        _metaData.SetEntityName(
            ent.Uid,
            $"{baseName} [{laneId} #{order} {pointType}{autoSuffix}{pointIdSuffix}]",
            ent.Meta);
    }

    private void RefreshSpawnPointDebugName(Entity<WH40KWaveSpawnPointComponent> ent)
    {
        var meta = MetaData(ent);
        var baseName = GetPrototypeOrCurrentName(meta);
        var spawnId = string.IsNullOrWhiteSpace(ent.Comp.SpawnId) ? "<any>" : Safe(ent.Comp.SpawnId);
        var laneIds = ent.Comp.LaneIds.Count == 0
            ? "all"
            : string.Join(",", ent.Comp.LaneIds.Select(id => Safe(id)));
        var team = string.IsNullOrWhiteSpace(ent.Comp.TeamId) ? "-" : Safe(ent.Comp.TeamId);

        _metaData.SetEntityName(
            ent.Owner,
            $"{baseName} [{ent.Comp.SpawnType} id={spawnId} lanes={laneIds} team={team} prio={ent.Comp.Priority}]",
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

    private void OnLanePointExamined(Entity<WH40KWaveLanePointComponent> ent, ref ExaminedEvent args)
    {
        using var _ = args.PushGroup("wh40k-wave-map-authoring", 10);
        args.PushMarkup($"LaneId: {Safe(ent.Comp.LaneId, "<no-lane>")}");
        args.PushMarkup($"Order: {ent.Comp.Order} ({(ent.Comp.AutoOrder ? "auto" : "manual")})");
        args.PushMarkup($"PointType: {ent.Comp.PointType}");

        if (!string.IsNullOrWhiteSpace(ent.Comp.PointId))
            args.PushMarkup($"PointId: {Safe(ent.Comp.PointId)}");

        if (ent.Comp.ArrivalRange > 0.05f)
            args.PushMarkup($"ArrivalRange: {ent.Comp.ArrivalRange:0.##}");

        if (ent.Comp.SegmentWidth > 0.05f)
            args.PushMarkup($"SegmentWidth: {ent.Comp.SegmentWidth:0.##}");

        if (ent.Comp.ProgressGateWidth > 0.05f)
            args.PushMarkup($"ProgressGateWidth: {ent.Comp.ProgressGateWidth:0.##}");

        if (ent.Comp.FallbackAnchor)
            args.PushMarkup("FallbackAnchor: true");

        if (ent.Comp.AllowedRoles.Count > 0)
            args.PushMarkup($"AllowedRoles: {string.Join(", ", ent.Comp.AllowedRoles)}");
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
        args.PushMarkup(
            ent.Comp.LaneIds.Count == 0
                ? "LaneIds: all"
                : $"LaneIds: {string.Join(", ", ent.Comp.LaneIds.Select(id => Safe(id)))}");
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
