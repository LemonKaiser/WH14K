using Content.Server.Popups;
using Content.Shared._WH40K.Rangefinder;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Content.Shared.Verbs;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Rangefinder;

public sealed class WH40KRangefinderSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;

    private readonly Dictionary<int, EntityUid> _designatorMarkers = new();
    private int _nextDesignatorId = 100;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KRangefinderComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KRangefinderComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WH40KRangefinderComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WH40KRangefinderComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WH40KRangefinderComponent, ComponentShutdown>(OnRangefinderShutdown);

        SubscribeLocalEvent<WH40KActiveLaserDesignatorComponent, DroppedEvent>(OnActiveDropped);
        SubscribeLocalEvent<WH40KActiveLaserDesignatorComponent, GotUnequippedHandEvent>(OnActiveUnequipped);
        SubscribeLocalEvent<WH40KActiveLaserDesignatorComponent, ItemUnwieldedEvent>(OnActiveUnwielded);
        SubscribeLocalEvent<WH40KActiveLaserDesignatorComponent, ComponentShutdown>(OnActiveShutdown);

        SubscribeLocalEvent<WH40KLaserDesignatorTargetComponent, ComponentShutdown>(OnMarkerShutdown);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var markerQuery = EntityQueryEnumerator<WH40KLaserDesignatorTargetComponent>();
        while (markerQuery.MoveNext(out var markerUid, out var marker))
        {
            if (marker.ExpiresAt == TimeSpan.Zero || now < marker.ExpiresAt)
                continue;

            QueueDel(markerUid);
        }
    }

    public bool TryGetDesignatorTarget(
        int designatorId,
        out EntityUid gridUid,
        out MapGridComponent grid,
        out Vector2i tileIndices)
    {
        gridUid = default;
        grid = default!;
        tileIndices = default;

        if (!_designatorMarkers.TryGetValue(designatorId, out var markerUid))
            return false;

        if (TerminatingOrDeleted(markerUid))
        {
            _designatorMarkers.Remove(designatorId);
            return false;
        }

        if (!TryComp(markerUid, out WH40KLaserDesignatorTargetComponent? marker) ||
            marker.Grid is not { } markerGridUid ||
            marker.ExpiresAt != TimeSpan.Zero && _timing.CurTime >= marker.ExpiresAt)
        {
            _designatorMarkers.Remove(designatorId);
            return false;
        }

        if (!TryComp<MapGridComponent>(markerGridUid, out var markerGrid))
            return false;

        if (!_map.TryGetTileRef(markerGridUid, markerGrid, marker.Tile, out var tileRef) ||
            tileRef.Tile.IsEmpty ||
            _turf.IsSpace(tileRef))
        {
            return false;
        }

        gridUid = markerGridUid;
        grid = markerGrid;
        tileIndices = marker.Tile;
        return true;
    }

    private void OnMapInit(Entity<WH40KRangefinderComponent> rangefinder, ref MapInitEvent args)
    {
        if (rangefinder.Comp.CanDesignate)
            rangefinder.Comp.Id ??= _nextDesignatorId++;
        else
            rangefinder.Comp.Mode = WH40KRangefinderMode.Rangefinder;

        _useDelay.SetLength((rangefinder.Owner, CompOrNull<UseDelayComponent>(rangefinder.Owner)), rangefinder.Comp.TargetDelay, rangefinder.Comp.TargetUseDelayId);
        _useDelay.SetLength((rangefinder.Owner, CompOrNull<UseDelayComponent>(rangefinder.Owner)), rangefinder.Comp.SwitchModeDelay, rangefinder.Comp.SwitchModeUseDelayId);
    }

    private void OnAfterInteract(Entity<WH40KRangefinderComponent> rangefinder, ref AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        if (!CanUseRangefinder(rangefinder, args.User))
            return;

        if (!_useDelay.TryResetDelay(rangefinder.Owner, checkDelayed: true, id: rangefinder.Comp.TargetUseDelayId))
            return;

        if (!TryResolveClickTile(
                args.ClickLocation,
                out var gridUid,
                out _,
                out var tileIndices,
                out var snappedCoordinates,
                out var mapCoordinates))
        {
            _popup.PopupClient(Loc.GetString("wh40k-rangefinder-invalid-target"), args.User, args.User, PopupType.SmallCaution);
            return;
        }

        args.Handled = true;

        if (!_examine.InRangeUnOccluded(args.User, snappedCoordinates, rangefinder.Comp.Range))
        {
            _popup.PopupClient(
                Loc.GetString("wh40k-rangefinder-out-of-range", ("range", rangefinder.Comp.Range)),
                args.User,
                args.User,
                PopupType.SmallCaution);
            return;
        }

        if (rangefinder.Comp.Mode == WH40KRangefinderMode.Designator && rangefinder.Comp.CanDesignate)
        {
            AcquireDesignator(rangefinder, args.User, gridUid, tileIndices, mapCoordinates);
            return;
        }

        AcquireCoordinates(rangefinder, args.User, gridUid, tileIndices);
    }

    private void OnGetVerbs(Entity<WH40KRangefinderComponent> rangefinder, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !rangefinder.Comp.CanDesignate)
            return;

        var user = args.User;
        var nextMode = rangefinder.Comp.Mode == WH40KRangefinderMode.Rangefinder
            ? WH40KRangefinderMode.Designator
            : WH40KRangefinderMode.Rangefinder;
        var modeLoc = nextMode == WH40KRangefinderMode.Designator
            ? "wh40k-rangefinder-mode-designator"
            : "wh40k-rangefinder-mode-rangefinder";

        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 100,
            Text = Loc.GetString("wh40k-rangefinder-verb-switch-mode", ("mode", Loc.GetString(modeLoc))),
            Act = () => TrySwitchMode(rangefinder, user, nextMode),
        });
    }

    private void OnExamined(Entity<WH40KRangefinderComponent> rangefinder, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(WH40KRangefinderComponent)))
        {
            var modeLoc = rangefinder.Comp.Mode == WH40KRangefinderMode.Designator
                ? "wh40k-rangefinder-mode-designator"
                : "wh40k-rangefinder-mode-rangefinder";
            args.PushMarkup(Loc.GetString("wh40k-rangefinder-examine-mode", ("mode", Loc.GetString(modeLoc))));

            if (rangefinder.Comp.Id is { } id)
                args.PushMarkup(Loc.GetString("wh40k-rangefinder-examine-id", ("id", id)));

            if (rangefinder.Comp.LastTarget is { } target)
            {
                args.PushMarkup(Loc.GetString(
                    "wh40k-rangefinder-examine-last-target",
                    ("x", target.X),
                    ("y", target.Y)));
            }

            if (TryComp(rangefinder, out WH40KActiveLaserDesignatorComponent? active) &&
                active.Marker is { } marker &&
                Exists(marker))
            {
                args.PushMarkup(Loc.GetString(
                    "wh40k-rangefinder-examine-active-designator",
                    ("id", active.Id),
                    ("x", active.Tile.X),
                    ("y", active.Tile.Y)));
            }
        }
    }

    private void OnRangefinderShutdown(Entity<WH40KRangefinderComponent> rangefinder, ref ComponentShutdown args)
    {
        if (!TryComp(rangefinder, out WH40KActiveLaserDesignatorComponent? active))
            return;

        ClearDesignatorState(rangefinder.Owner, active);
    }

    private void OnActiveDropped(Entity<WH40KActiveLaserDesignatorComponent> active, ref DroppedEvent args)
    {
        ClearDesignatorState(active.Owner, active.Comp);
    }

    private void OnActiveUnequipped(Entity<WH40KActiveLaserDesignatorComponent> active, ref GotUnequippedHandEvent args)
    {
        ClearDesignatorState(active.Owner, active.Comp);
    }

    private void OnActiveUnwielded(Entity<WH40KActiveLaserDesignatorComponent> active, ref ItemUnwieldedEvent args)
    {
        ClearDesignatorState(active.Owner, active.Comp);
    }

    private void OnActiveShutdown(Entity<WH40KActiveLaserDesignatorComponent> active, ref ComponentShutdown args)
    {
        RemoveActiveMarker(active.Owner, active.Comp);
    }

    private void OnMarkerShutdown(Entity<WH40KLaserDesignatorTargetComponent> marker, ref ComponentShutdown args)
    {
        if (_designatorMarkers.TryGetValue(marker.Comp.Id, out var knownMarker) &&
            knownMarker == marker.Owner)
        {
            _designatorMarkers.Remove(marker.Comp.Id);
        }

        var source = marker.Comp.Source;
        if (!TryComp(source, out WH40KActiveLaserDesignatorComponent? active) ||
            active.Marker != marker.Owner)
        {
            return;
        }

        active.Marker = null;
        active.Grid = null;
        active.Tile = Vector2i.Zero;
        active.ExpiresAt = TimeSpan.Zero;
        RemCompDeferred<WH40KActiveLaserDesignatorComponent>(source);
    }

    private void AcquireCoordinates(
        Entity<WH40KRangefinderComponent> rangefinder,
        EntityUid user,
        EntityUid gridUid,
        Vector2i tileIndices)
    {
        rangefinder.Comp.LastTarget = tileIndices;
        rangefinder.Comp.LastTargetGrid = gridUid;

        _audio.PlayPredicted(rangefinder.Comp.AcquireSound, rangefinder, user);
        _popup.PopupClient(
            Loc.GetString(
                "wh40k-rangefinder-coordinates-acquired",
                ("x", tileIndices.X),
                ("y", tileIndices.Y)),
            user,
            user);
    }

    private void AcquireDesignator(
        Entity<WH40KRangefinderComponent> rangefinder,
        EntityUid user,
        EntityUid gridUid,
        Vector2i tileIndices,
        MapCoordinates mapCoordinates)
    {
        var id = EnsureId(rangefinder);

        var active = EnsureComp<WH40KActiveLaserDesignatorComponent>(rangefinder);
        RemoveActiveMarker(rangefinder.Owner, active);

        var markerUid = Spawn(rangefinder.Comp.MarkerPrototype, mapCoordinates);
        var expiresAt = _timing.CurTime + rangefinder.Comp.MarkerLifetime;

        var marker = EnsureComp<WH40KLaserDesignatorTargetComponent>(markerUid);
        marker.Id = id;
        marker.Source = rangefinder.Owner;
        marker.Grid = gridUid;
        marker.Tile = tileIndices;
        marker.ExpiresAt = expiresAt;

        if (_designatorMarkers.TryGetValue(id, out var existingMarker) &&
            existingMarker != markerUid &&
            Exists(existingMarker))
        {
            QueueDel(existingMarker);
        }

        _designatorMarkers[id] = markerUid;

        active.Id = id;
        active.Marker = markerUid;
        active.Grid = gridUid;
        active.Tile = tileIndices;
        active.ExpiresAt = expiresAt;

        rangefinder.Comp.LastTarget = tileIndices;
        rangefinder.Comp.LastTargetGrid = gridUid;

        var ttlSeconds = Math.Max(1, (int) Math.Ceiling(rangefinder.Comp.MarkerLifetime.TotalSeconds));

        _audio.PlayPredicted(rangefinder.Comp.AcquireSound, rangefinder, user);
        _popup.PopupClient(
            Loc.GetString(
                "wh40k-rangefinder-designator-acquired",
                ("id", id),
                ("x", tileIndices.X),
                ("y", tileIndices.Y),
                ("ttl", ttlSeconds)),
            user,
            user,
            PopupType.Medium);
    }

    private void TrySwitchMode(Entity<WH40KRangefinderComponent> rangefinder, EntityUid user, WH40KRangefinderMode nextMode)
    {
        if (!_useDelay.TryResetDelay(rangefinder.Owner, checkDelayed: true, id: rangefinder.Comp.SwitchModeUseDelayId))
            return;

        if (nextMode == WH40KRangefinderMode.Designator && !rangefinder.Comp.CanDesignate)
            return;

        if (rangefinder.Comp.Mode == nextMode)
            return;

        rangefinder.Comp.Mode = nextMode;
        if (nextMode == WH40KRangefinderMode.Rangefinder &&
            TryComp(rangefinder, out WH40KActiveLaserDesignatorComponent? active))
        {
            ClearDesignatorState(rangefinder.Owner, active);
        }

        _audio.PlayPredicted(rangefinder.Comp.ToggleSound, rangefinder, user);
        _popup.PopupClient(
            Loc.GetString(
                nextMode == WH40KRangefinderMode.Designator
                    ? "wh40k-rangefinder-mode-designator-set"
                    : "wh40k-rangefinder-mode-rangefinder-set"),
            user,
            user);
    }

    private bool CanUseRangefinder(Entity<WH40KRangefinderComponent> rangefinder, EntityUid user)
    {
        if (!rangefinder.Comp.RequireWield)
            return true;

        if (TryComp(rangefinder, out WieldableComponent? wieldable) && wieldable.Wielded)
            return true;

        _popup.PopupClient(Loc.GetString("wh40k-rangefinder-requires-wield"), user, user, PopupType.SmallCaution);
        return false;
    }

    private bool TryResolveClickTile(
        EntityCoordinates clickLocation,
        out EntityUid gridUid,
        out MapGridComponent grid,
        out Vector2i tileIndices,
        out EntityCoordinates snappedCoordinates,
        out MapCoordinates mapCoordinates)
    {
        gridUid = default;
        grid = default!;
        tileIndices = default;
        snappedCoordinates = default;
        mapCoordinates = default;

        var gridEntity = _transform.GetGrid(clickLocation);
        if (gridEntity is not { } clickedGridUid ||
            !TryComp<MapGridComponent>(clickedGridUid, out var clickedGrid))
        {
            return false;
        }

        snappedCoordinates = clickLocation.SnapToGrid(clickedGrid);
        var snappedMapCoordinates = _transform.ToMapCoordinates(snappedCoordinates);
        if (snappedMapCoordinates.MapId == MapId.Nullspace)
            return false;

        tileIndices = _map.WorldToTile(clickedGridUid, clickedGrid, snappedMapCoordinates.Position);
        if (!_map.TryGetTileRef(clickedGridUid, clickedGrid, tileIndices, out var tileRef) ||
            tileRef.Tile.IsEmpty ||
            _turf.IsSpace(tileRef))
        {
            return false;
        }

        gridUid = clickedGridUid;
        grid = clickedGrid;
        mapCoordinates = _transform.ToMapCoordinates(_map.GridTileToLocal(clickedGridUid, clickedGrid, tileIndices));
        return true;
    }

    private int EnsureId(Entity<WH40KRangefinderComponent> rangefinder)
    {
        rangefinder.Comp.Id ??= _nextDesignatorId++;
        return rangefinder.Comp.Id.Value;
    }

    private void ClearDesignatorState(EntityUid rangefinderUid, WH40KActiveLaserDesignatorComponent active)
    {
        RemoveActiveMarker(rangefinderUid, active);
        RemCompDeferred<WH40KActiveLaserDesignatorComponent>(rangefinderUid);
    }

    private void RemoveActiveMarker(EntityUid rangefinderUid, WH40KActiveLaserDesignatorComponent active)
    {
        if (active.Marker is { } markerUid &&
            Exists(markerUid))
        {
            QueueDel(markerUid);
        }

        if (_designatorMarkers.TryGetValue(active.Id, out var knownMarker) &&
            knownMarker == active.Marker)
        {
            _designatorMarkers.Remove(active.Id);
        }

        active.Marker = null;
        active.Grid = null;
        active.Tile = Vector2i.Zero;
        active.ExpiresAt = TimeSpan.Zero;
    }
}
