using Content.Server.Light.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Map.Events;
using Robust.Shared.Map.Components;

namespace Content.Server.Light.EntitySystems;

/// <inheritdoc/>
public sealed class RoofSystem : SharedRoofSystem
{
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    private EntityQuery<MapGridComponent> _gridQuery;
    private ISawmill _sawmill = default!;
    private readonly HashSet<Entity<IsRoofComponent>> _roofEntities = new();

    public override void Initialize()
    {
        base.Initialize();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _sawmill = Logger.GetSawmill("roof");
        SubscribeLocalEvent<SetRoofComponent, ComponentStartup>(OnFlagStartup);
        SubscribeLocalEvent<SetRoofComponent, MapInitEvent>(OnFlagMapInit);
        SubscribeLocalEvent<SetRoofComponent, AnchorStateChangedEvent>(OnFlagAnchorChanged);
        SubscribeLocalEvent<BeforeSerializationEvent>(OnBeforeSerialization);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SetRoofComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            TryApplyAndDelete((uid, marker), xform, source: "update", logPendingFailure: false);
        }
    }

    private void OnFlagStartup(Entity<SetRoofComponent> ent, ref ComponentStartup args)
    {
        TryApplyAndDelete(ent, source: "component-startup");
    }

    private void OnFlagMapInit(Entity<SetRoofComponent> ent, ref MapInitEvent args)
    {
        TryApplyAndDelete(ent, source: "map-init");
    }

    private void OnFlagAnchorChanged(Entity<SetRoofComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored || args.Detaching)
            return;

        TryApplyAndDelete(ent, args.Transform, source: "anchor-changed");
    }

    private void OnBeforeSerialization(BeforeSerializationEvent ev)
    {
        List<EntityUid>? bakedMarkers = null;
        var query = EntityQueryEnumerator<SetRoofComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            if (!ev.MapIds.Contains(xform.MapID))
                continue;

            if (!TryApplyAndDelete((uid, marker), xform, queueDelete: false, source: "before-serialization"))
                continue;

            bakedMarkers ??= new List<EntityUid>();
            bakedMarkers.Add(uid);
        }

        if (bakedMarkers == null)
            return;

        foreach (var uid in bakedMarkers)
        {
            Del(uid);
        }
    }

    private bool TryApplyAndDelete(
        Entity<SetRoofComponent> ent,
        TransformComponent? xform = null,
        bool queueDelete = true,
        string source = "unknown",
        bool logPendingFailure = true)
    {
        xform ??= Transform(ent.Owner);
        var markerProto = MetaData(ent.Owner).EntityPrototype?.ID ?? "<unknown>";
        var markerAction = ent.Comp.Value ? "set-roof" : "clear-roof";

        if (xform.GridUid is not { } gridUid)
        {
            if (logPendingFailure)
            {
                _sawmill.Info(
                    $"Roof marker pending: source={source} marker={markerProto} action={markerAction} uid={ent.Owner} " +
                    $"map={xform.MapID} coords={xform.Coordinates} reason=no-grid");
            }

            return false;
        }

        if (!_gridQuery.TryComp(gridUid, out var grid))
        {
            if (logPendingFailure)
            {
                _sawmill.Info(
                    $"Roof marker pending: source={source} marker={markerProto} action={markerAction} uid={ent.Owner} " +
                    $"map={xform.MapID} grid={gridUid} coords={xform.Coordinates} reason=grid-missing");
            }

            return false;
        }

        var index = _maps.LocalToTile(gridUid, grid, xform.Coordinates);
        var before = CaptureTileState(gridUid, grid, index);

        SetRoof((gridUid, grid, null), index, ent.Comp.Value);

        var after = CaptureTileState(gridUid, grid, index);
        var result = DescribeResult(before, after, ent.Comp.Value);

        _sawmill.Info(
            $"Roof marker processed: source={source} marker={markerProto} action={markerAction} uid={ent.Owner} " +
            $"map={xform.MapID} grid={gridUid} coords={xform.Coordinates} tile={index} " +
            $"before=[implicit={before.ImplicitRoof}, explicit={before.ExplicitRoof}, tileRoofed={before.TileRoofed}, " +
            $"bitRoofed={before.BitRoofed}, chunk={before.ChunkOrigin}, chunkPresent={before.ChunkPresent}, " +
            $"chunkData={before.ChunkData}, roofEntities={before.RoofEntities}] " +
            $"after=[implicit={after.ImplicitRoof}, explicit={after.ExplicitRoof}, tileRoofed={after.TileRoofed}, " +
            $"bitRoofed={after.BitRoofed}, chunk={after.ChunkOrigin}, chunkPresent={after.ChunkPresent}, " +
            $"chunkData={after.ChunkData}, roofEntities={after.RoofEntities}] " +
            $"changed={result.changed} reason={result.reason}");

        if (queueDelete)
            QueueDel(ent.Owner);

        return true;
    }

    private TileRoofState CaptureTileState(EntityUid gridUid, MapGridComponent grid, Vector2i index)
    {
        var implicitRoof = HasComp<ImplicitRoofComponent>(gridUid);
        var explicitRoof = TryComp<RoofComponent>(gridUid, out var roofComp);
        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, RoofComponent.ChunkSize);
        var chunkPresent = false;
        var chunkData = 0UL;
        var bitRoofed = false;

        if (explicitRoof && roofComp!.Data.TryGetValue(chunkOrigin, out var data))
        {
            chunkPresent = true;
            chunkData = data;
            var chunkRelative = SharedMapSystem.GetChunkRelative(index, RoofComponent.ChunkSize);
            var bitFlag = (ulong) 1 << (chunkRelative.X + chunkRelative.Y * RoofComponent.ChunkSize);
            bitRoofed = (data & bitFlag) == bitFlag;
        }

        var tileRoofed = implicitRoof ||
                         (explicitRoof && IsRooved((gridUid, grid, roofComp!), index)) ||
                         HasEnabledRoofEntities(gridUid, index);

        return new TileRoofState(
            ImplicitRoof: implicitRoof,
            ExplicitRoof: explicitRoof,
            TileRoofed: tileRoofed,
            BitRoofed: bitRoofed,
            ChunkOrigin: chunkOrigin,
            ChunkPresent: chunkPresent,
            ChunkData: chunkData,
            RoofEntities: DescribeRoofEntities(gridUid, index));
    }

    private bool HasEnabledRoofEntities(EntityUid gridUid, Vector2i index)
    {
        _roofEntities.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, index, _roofEntities);

        foreach (var roofEntity in _roofEntities)
        {
            if (roofEntity.Comp.Enabled)
                return true;
        }

        return false;
    }

    private string DescribeRoofEntities(EntityUid gridUid, Vector2i index)
    {
        _roofEntities.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, index, _roofEntities);

        if (_roofEntities.Count == 0)
            return "none";

        var entities = new List<string>();
        foreach (var roofEntity in _roofEntities)
        {
            entities.Add($"{ToPrettyString(roofEntity.Owner)}:enabled={roofEntity.Comp.Enabled}");
        }

        return string.Join(",", entities);
    }

    private static (bool changed, string reason) DescribeResult(TileRoofState before, TileRoofState after, bool requestedValue)
    {
        if (before.TileRoofed != after.TileRoofed || before.BitRoofed != after.BitRoofed)
            return (true, "state-changed");

        if (!requestedValue && after.RoofEntities != "none")
            return (false, "unchanged-blocked-by-isroof-entity");

        if (requestedValue && before.TileRoofed)
            return (false, "unchanged-already-roofed");

        if (!requestedValue && !before.TileRoofed && !before.ImplicitRoof && !before.ExplicitRoof)
            return (false, "unchanged-grid-has-no-roof-component");

        if (!requestedValue && !before.TileRoofed)
            return (false, "unchanged-already-unroofed");

        if (before.ImplicitRoof && !after.ImplicitRoof)
            return (false, "unchanged-implicit-roof-converted-without-visible-delta");

        return (false, "unchanged-no-visible-delta");
    }

    private readonly record struct TileRoofState(
        bool ImplicitRoof,
        bool ExplicitRoof,
        bool TileRoofed,
        bool BitRoofed,
        Vector2i ChunkOrigin,
        bool ChunkPresent,
        ulong ChunkData,
        string RoofEntities);
}
