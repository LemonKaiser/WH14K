using System.Numerics;
using Robust.Server.GameStates;
using Content.Server.Cargo.Components;
using Content.Server.Station.Systems;
using Content.Server._WH40K.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Cargo;

/// <summary>
/// Decorative train visuals for WH40K delayed cargo batches.
/// Trains are persistent map entities: never deleted, only hidden (Nullspace) and shown back.
/// </summary>
public sealed class WH40KCargoBatchTrainVisualSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private readonly Dictionary<(EntityUid Station, ProtoId<CargoAccountPrototype> Account), CargoTrainTransitState> _transitStates = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KCargoBatchTrainVisualComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KCargoBatchTrainVisualComponent, ComponentShutdown>(OnComponentShutdown);
    }

    private void OnMapInit(Entity<WH40KCargoBatchTrainVisualComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent);

        ent.Comp.HomeInitialized = true;
        ent.Comp.HomeParent = xform.ParentUid;
        ent.Comp.HomeLocalPosition = xform.LocalPosition;

        // Decorative train should stay networked to avoid PVS pop-in while traveling.
        _pvsOverride.AddGlobalOverride(ent.Owner);
    }

    private void OnComponentShutdown(Entity<WH40KCargoBatchTrainVisualComponent> ent, ref ComponentShutdown args)
    {
        _pvsOverride.RemoveGlobalOverride(ent.Owner);

        if (_transitStates.Count == 0)
            return;

        var keysToRemove = new List<(EntityUid Station, ProtoId<CargoAccountPrototype> Account)>();
        foreach (var (key, state) in _transitStates)
        {
            if (state.TrainEntity == ent.Owner)
                keysToRemove.Add(key);
        }

        foreach (var key in keysToRemove)
        {
            _transitStates.Remove(key);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateActiveAnimations();
        UpdateBatchTriggers();
    }

    private void UpdateActiveAnimations()
    {
        var query = EntityQueryEnumerator<WH40KCargoBatchTrainVisualComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var visual, out var xform))
        {
            if (visual.Motion == WH40KCargoBatchTrainMotion.Idle)
                continue;

            if (xform.MapID == MapId.Nullspace)
            {
                visual.Motion = WH40KCargoBatchTrainMotion.Idle;
                continue;
            }

            var duration = MathF.Max(0.01f, visual.AnimationDurationSeconds);
            var elapsed = (float) (_timing.CurTime - visual.MotionStartedAt).TotalSeconds;
            var t = Math.Clamp(elapsed / duration, 0f, 1f);

            var eased = visual.Motion switch
            {
                WH40KCargoBatchTrainMotion.Departing => EaseInCubic(t),
                WH40KCargoBatchTrainMotion.Returning => EaseOutCubic(t),
                _ => t
            };

            var target = Vector2.Lerp(visual.MotionStartPosition, visual.MotionEndPosition, eased);
            SetPositionOnParent(uid, xform, xform.ParentUid, target);

            if (t < 1f)
                continue;

            if (visual.Motion == WH40KCargoBatchTrainMotion.Departing)
            {
                visual.Motion = WH40KCargoBatchTrainMotion.Idle;
                HideTrain(uid, xform);
                continue;
            }

            visual.Motion = WH40KCargoBatchTrainMotion.Idle;

            if (!visual.HomeInitialized || !Exists(visual.HomeParent))
                continue;

            if (!SetPositionOnParent(uid, xform, visual.HomeParent, visual.HomeLocalPosition))
                continue;
        }
    }

    private void UpdateBatchTriggers()
    {
        var activeKeys = new HashSet<(EntityUid Station, ProtoId<CargoAccountPrototype> Account)>();
        var stationQuery = EntityQueryEnumerator<StationCargoOrderBatchComponent>();
        while (stationQuery.MoveNext(out var stationUid, out var batchState))
        {
            foreach (var (account, batch) in batchState.ActiveBatches)
            {
                var key = (stationUid, account);
                activeKeys.Add(key);

                if (!_transitStates.TryGetValue(key, out var transitState) || transitState.BatchId != batch.BatchId)
                {
                    if (TryStartDeparture(stationUid, account, batch.BatchId, batch.DeliverAt, out var startedState))
                        _transitStates[key] = startedState;

                    continue;
                }

                transitState.DeliverAt = batch.DeliverAt;
                _transitStates[key] = transitState;
            }
        }

        if (_transitStates.Count == 0)
            return;

        var keys = new List<(EntityUid Station, ProtoId<CargoAccountPrototype> Account)>(_transitStates.Keys);
        foreach (var key in keys)
        {
            var state = _transitStates[key];
            if (state.ReturnTriggered)
                continue;

            var remaining = (float) (state.DeliverAt - _timing.CurTime).TotalSeconds;

            if (remaining <= state.ReturnLeadSeconds)
            {
                if (TryStartReturn(state))
                    state.ReturnTriggered = true;
            }

            _transitStates[key] = state;
        }

        var staleKeys = new List<(EntityUid Station, ProtoId<CargoAccountPrototype> Account)>();
        foreach (var key in _transitStates.Keys)
        {
            if (!activeKeys.Contains(key))
                staleKeys.Add(key);
        }

        foreach (var key in staleKeys)
        {
            var state = _transitStates[key];
            if (state.ReturnTriggered)
                _transitStates.Remove(key);
        }
    }

    private bool TryStartDeparture(
        EntityUid stationUid,
        ProtoId<CargoAccountPrototype> account,
        int batchId,
        TimeSpan deliverAt,
        out CargoTrainTransitState state)
    {
        state = new CargoTrainTransitState();

        if (!TryGetTrain(stationUid, account, out var train))
            return false;

        EnsureHome((train.Owner, train.Comp1, train.Comp2));
        if (!train.Comp1.HomeInitialized || !Exists(train.Comp1.HomeParent))
            return false;

        if (!SetPositionOnParent(train.Owner, train.Comp2, train.Comp1.HomeParent, train.Comp1.HomeLocalPosition))
            return false;

        train.Comp1.Motion = WH40KCargoBatchTrainMotion.Departing;
        train.Comp1.MotionStartedAt = _timing.CurTime;
        train.Comp1.MotionStartPosition = train.Comp1.HomeLocalPosition;
        train.Comp1.MotionEndPosition = train.Comp1.HomeLocalPosition + new Vector2(train.Comp1.OutboundDistance, 0f);

        _audio.PlayPvs(train.Comp1.DepartureSound, train.Owner, AudioParams.Default.WithVolume(train.Comp1.DepartureSoundVolume));

        state = new CargoTrainTransitState
        {
            BatchId = batchId,
            Account = account,
            Station = stationUid,
            TrainEntity = train.Owner,
            ReturnTriggered = false,
            DeliverAt = deliverAt,
            HomeParent = train.Comp1.HomeParent,
            HomeLocalPosition = train.Comp1.HomeLocalPosition,
            ReturnSpawnDistance = train.Comp1.ReturnSpawnDistance,
            ReturnSpawnDirectionX = train.Comp1.ReturnSpawnDirectionX,
            AnimationDurationSeconds = train.Comp1.AnimationDurationSeconds,
            ReturnLeadSeconds = train.Comp1.ReturnLeadSeconds
        };

        return true;
    }

    private bool TryStartReturn(CargoTrainTransitState state)
    {
        if (!TryResolveTrain(state, out var train))
            return false;

        EnsureHome((train.Owner, train.Comp1, train.Comp2));

        if (!Exists(state.HomeParent))
            return false;

        train.Comp1.HomeInitialized = true;
        train.Comp1.HomeParent = state.HomeParent;
        train.Comp1.HomeLocalPosition = state.HomeLocalPosition;
        train.Comp1.AnimationDurationSeconds = state.AnimationDurationSeconds;
        train.Comp1.ReturnSpawnDistance = state.ReturnSpawnDistance;
        train.Comp1.ReturnSpawnDirectionX = state.ReturnSpawnDirectionX;
        train.Comp1.ReturnLeadSeconds = state.ReturnLeadSeconds;

        var returnDirection = NormalizeDirectionX(state.ReturnSpawnDirectionX, -1f);
        var returnStart = state.HomeLocalPosition + new Vector2(returnDirection * state.ReturnSpawnDistance, 0f);
        if (!SetPositionOnParent(train.Owner, train.Comp2, state.HomeParent, returnStart))
            return false;

        train.Comp1.Motion = WH40KCargoBatchTrainMotion.Returning;
        train.Comp1.MotionStartedAt = _timing.CurTime;
        train.Comp1.MotionStartPosition = returnStart;
        train.Comp1.MotionEndPosition = state.HomeLocalPosition;

        _audio.PlayPvs(train.Comp1.ArrivalSound, train.Owner, AudioParams.Default.WithVolume(train.Comp1.ArrivalSoundVolume));
        return true;
    }

    private bool TryResolveTrain(
        CargoTrainTransitState state,
        out Entity<WH40KCargoBatchTrainVisualComponent, TransformComponent> train)
    {
        if (state.TrainEntity is { } trainUid &&
            TryComp<WH40KCargoBatchTrainVisualComponent>(trainUid, out var visual) &&
            TryComp(trainUid, out TransformComponent? xform) &&
            visual.Account == state.Account)
        {
            train = (trainUid, visual, xform);
            return true;
        }

        return TryGetTrain(state.Station, state.Account, out train);
    }

    private bool TryGetTrain(
        EntityUid stationUid,
        ProtoId<CargoAccountPrototype> account,
        out Entity<WH40KCargoBatchTrainVisualComponent, TransformComponent> train)
    {
        var query = EntityQueryEnumerator<WH40KCargoBatchTrainVisualComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var visual, out var xform))
        {
            if (visual.Account != account)
                continue;

            EnsureHome((uid, visual, xform));

            var ownerStation = visual.HomeInitialized && Exists(visual.HomeParent)
                ? _station.GetOwningStation(visual.HomeParent)
                : _station.GetOwningStation(uid, xform);

            if (ownerStation != stationUid)
                continue;

            train = (uid, visual, xform);
            return true;
        }

        train = default;
        return false;
    }

    private void HideTrain(EntityUid uid, TransformComponent xform)
    {
        if (xform.MapID == MapId.Nullspace)
            return;

        _transform.DetachEntity(uid, xform);
    }

    private void EnsureHome(Entity<WH40KCargoBatchTrainVisualComponent, TransformComponent> ent)
    {
        if (ent.Comp1.HomeInitialized)
            return;

        ent.Comp1.HomeInitialized = true;
        ent.Comp1.HomeParent = ent.Comp2.ParentUid;
        ent.Comp1.HomeLocalPosition = ent.Comp2.LocalPosition;
    }

    private bool SetPositionOnParent(EntityUid uid, TransformComponent xform, EntityUid parent, Vector2 position)
    {
        if (!Exists(parent))
            return false;

        var coords = new EntityCoordinates(parent, position);
        _transform.SetCoordinates(uid, xform, coords);
        return true;
    }

    private static float NormalizeDirectionX(float value, float fallback)
    {
        if (MathF.Abs(value) < 0.001f)
            return fallback;

        return value < 0f ? -1f : 1f;
    }

    private static float EaseInCubic(float t) => t * t * t;

    private static float EaseOutCubic(float t)
    {
        var u = 1f - t;
        return 1f - (u * u * u);
    }

    private sealed class CargoTrainTransitState
    {
        public int BatchId;
        public ProtoId<CargoAccountPrototype> Account = "Cargo";
        public EntityUid Station;
        public EntityUid? TrainEntity;
        public bool ReturnTriggered;
        public TimeSpan DeliverAt;
        public EntityUid HomeParent;
        public Vector2 HomeLocalPosition;
        public float ReturnSpawnDistance;
        public float ReturnSpawnDirectionX;
        public float AnimationDurationSeconds;
        public float ReturnLeadSeconds;
    }
}
