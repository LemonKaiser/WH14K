using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Audio;
using Content.Shared._WH40K.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Audio;

public sealed partial class WH40KAmbientFieldSystem : EntitySystem
{
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  IRobustRandom _random = default!;
    [Dependency] private  SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private  SharedAudioSystem _audio = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    private static readonly TimeSpan RebuildInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextRebuildAt = TimeSpan.Zero;
    private bool _rebuildQueued = true;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KAmbientFieldSourceComponent, ComponentStartup>(OnSourceStartup);
        SubscribeLocalEvent<WH40KAmbientFieldSourceComponent, ComponentShutdown>(OnSourceShutdown);
        SubscribeLocalEvent<WH40KAmbientFieldSourceComponent, MoveEvent>(OnSourceMoved);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_rebuildQueued || _timing.CurTime >= _nextRebuildAt)
        {
            RebuildEmitters();
            _rebuildQueued = false;
            _nextRebuildAt = _timing.CurTime + RebuildInterval;
        }

        TickOneShotEmitters();
    }

    private void OnSourceStartup(Entity<WH40KAmbientFieldSourceComponent> ent, ref ComponentStartup args)
    {
        _rebuildQueued = true;
    }

    private void OnSourceShutdown(Entity<WH40KAmbientFieldSourceComponent> ent, ref ComponentShutdown args)
    {
        _rebuildQueued = true;
    }

    private void OnSourceMoved(Entity<WH40KAmbientFieldSourceComponent> ent, ref MoveEvent args)
    {
        _rebuildQueued = true;
    }

    private void RebuildEmitters()
    {
        var groups = new Dictionary<SourceConfigKey, List<EmitterCandidate>>();
        var query = EntityQueryEnumerator<WH40KAmbientFieldSourceComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var source, out var xform))
        {
            if (!source.Enabled || xform.MapID == MapId.Nullspace)
                continue;

            var range = NormalizeRange(source.Range);
            var spacing = NormalizeEmitterSpacing(source.EmitterSpacing);
            var minInterval = NormalizeOneShotMinInterval(source.OneShotMinIntervalSeconds);
            var maxInterval = NormalizeOneShotMaxInterval(minInterval, source.OneShotMaxIntervalSeconds);
            var worldPosition = _transform.GetWorldPosition(xform);
            var qx = QuantizeAxis(worldPosition.X);
            var qy = QuantizeAxis(worldPosition.Y);
            var priority = StablePriority(xform.MapID, qx, qy);
            var isManagedEmitter = HasComp<WH40KAmbientFieldEmitterComponent>(uid);
            var key = BuildSourceConfigKey(source, xform.MapID, range, spacing, minInterval, maxInterval);
            var config = new SelectedEmitterConfiguration(source.Sound, range, source.Volume, spacing, source.Loop, minInterval, maxInterval);
            var candidate = new EmitterCandidate(uid, worldPosition, qx, qy, priority, isManagedEmitter, config);

            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<EmitterCandidate>();
                groups[key] = list;
            }

            list.Add(candidate);
        }

        var selectedLoop = new Dictionary<EntityUid, SelectedEmitterConfiguration>();
        var selectedOneShot = new Dictionary<EntityUid, SelectedEmitterConfiguration>();

        foreach (var (_, candidates) in groups)
        {
            SelectEmittersForGroup(candidates, selectedLoop, selectedOneShot);
        }

        CleanupDeselectedEmitters(selectedLoop, selectedOneShot);

        foreach (var (uid, config) in selectedLoop)
        {
            EnsureLoopEmitter(uid, config);
        }

        foreach (var (uid, config) in selectedOneShot)
        {
            EnsureOneShotEmitter(uid, config);
        }
    }

    private void SelectEmittersForGroup(
        List<EmitterCandidate> candidates,
        Dictionary<EntityUid, SelectedEmitterConfiguration> selectedLoop,
        Dictionary<EntityUid, SelectedEmitterConfiguration> selectedOneShot)
    {
        if (candidates.Count == 0)
            return;

        candidates.Sort(static (a, b) =>
        {
            var existing = b.IsManagedEmitter.CompareTo(a.IsManagedEmitter);
            if (existing != 0)
                return existing;

            var priority = a.Priority.CompareTo(b.Priority);
            if (priority != 0)
                return priority;

            var x = a.QuantizedX.CompareTo(b.QuantizedX);
            if (x != 0)
                return x;

            var y = a.QuantizedY.CompareTo(b.QuantizedY);
            if (y != 0)
                return y;

            return a.Uid.CompareTo(b.Uid);
        });

        var spacing = candidates[0].Config.EmitterSpacing;
        var spacingSquared = spacing * spacing;
        var chosenByCell = new Dictionary<CellKey, List<EmitterCandidate>>();

        foreach (var candidate in candidates)
        {
            var cell = GetCellKey(candidate.WorldPosition, spacing);
            var blocked = false;

            for (var x = cell.X - 1; x <= cell.X + 1 && !blocked; x++)
            {
                for (var y = cell.Y - 1; y <= cell.Y + 1 && !blocked; y++)
                {
                    var neighborCell = new CellKey(x, y);
                    if (!chosenByCell.TryGetValue(neighborCell, out var neighborList))
                        continue;

                    foreach (var other in neighborList)
                    {
                        if (Vector2.DistanceSquared(candidate.WorldPosition, other.WorldPosition) < spacingSquared)
                        {
                            blocked = true;
                            break;
                        }
                    }
                }
            }

            if (blocked)
                continue;

            if (!chosenByCell.TryGetValue(cell, out var cellList))
            {
                cellList = new List<EmitterCandidate>();
                chosenByCell[cell] = cellList;
            }

            cellList.Add(candidate);
            if (candidate.Config.Loop)
                selectedLoop[candidate.Uid] = candidate.Config;
            else
                selectedOneShot[candidate.Uid] = candidate.Config;
        }
    }

    private void CleanupDeselectedEmitters(
        Dictionary<EntityUid, SelectedEmitterConfiguration> selectedLoop,
        Dictionary<EntityUid, SelectedEmitterConfiguration> selectedOneShot)
    {
        var toRemove = new List<EntityUid>();
        var query = EntityQueryEnumerator<WH40KAmbientFieldEmitterComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            if (!selectedLoop.ContainsKey(uid) && !selectedOneShot.ContainsKey(uid))
                toRemove.Add(uid);
        }

        foreach (var uid in toRemove)
        {
            RemComp<AmbientSoundComponent>(uid);
            RemComp<WH40KAmbientFieldEmitterComponent>(uid);
        }
    }

    private void EnsureLoopEmitter(EntityUid uid, SelectedEmitterConfiguration config)
    {
        var runtime = EnsureComp<WH40KAmbientFieldEmitterComponent>(uid);
        runtime.Loop = true;
        runtime.NextOneShotAt = TimeSpan.Zero;

        var ambient = EnsureComp<AmbientSoundComponent>(uid);
        _ambient.SetSound(uid, config.Sound, ambient);
        _ambient.SetRange(uid, config.Range, ambient);
        _ambient.SetVolume(uid, config.Volume, ambient);
        _ambient.SetAmbience(uid, true, ambient);
    }

    private void EnsureOneShotEmitter(EntityUid uid, SelectedEmitterConfiguration config)
    {
        var runtime = EnsureComp<WH40KAmbientFieldEmitterComponent>(uid);
        var resetTimer = runtime.Loop || runtime.NextOneShotAt == TimeSpan.Zero;
        runtime.Loop = false;

        if (resetTimer)
            runtime.NextOneShotAt = _timing.CurTime + TimeSpan.FromSeconds(GetRandomOneShotInterval(config));

        RemComp<AmbientSoundComponent>(uid);
    }

    private void TickOneShotEmitters()
    {
        var query = EntityQueryEnumerator<WH40KAmbientFieldEmitterComponent, WH40KAmbientFieldSourceComponent>();

        while (query.MoveNext(out var uid, out var emitter, out var source))
        {
            if (emitter.Loop || !source.Enabled || Paused(uid) || _timing.CurTime < emitter.NextOneShotAt)
                continue;

            _audio.PlayPvs(source.Sound, uid, BuildOneShotParams(source));
            var minInterval = NormalizeOneShotMinInterval(source.OneShotMinIntervalSeconds);
            var maxInterval = NormalizeOneShotMaxInterval(minInterval, source.OneShotMaxIntervalSeconds);
            emitter.NextOneShotAt = _timing.CurTime + TimeSpan.FromSeconds(GetRandomOneShotInterval(minInterval, maxInterval));
        }
    }

    private AudioParams BuildOneShotParams(WH40KAmbientFieldSourceComponent source)
    {
        return source.Sound.Params
            .AddVolume(source.Volume)
            .WithLoop(false)
            .WithMaxDistance(NormalizeRange(source.Range));
    }

    private SourceConfigKey BuildSourceConfigKey(
        WH40KAmbientFieldSourceComponent source,
        MapId mapId,
        float range,
        float spacing,
        float minInterval,
        float maxInterval)
    {
        return new SourceConfigKey(
            mapId,
            GetSoundKey(source.Sound),
            source.Loop,
            QuantizeFloat(range),
            QuantizeFloat(source.Volume),
            QuantizeFloat(spacing),
            QuantizeFloat(minInterval),
            QuantizeFloat(maxInterval));
    }

    private string GetSoundKey(SoundSpecifier sound)
    {
        var id = sound switch
        {
            SoundPathSpecifier path => $"path:{path.Path}",
            SoundCollectionSpecifier collection => $"collection:{collection.Collection}",
            _ => sound.ToString()
        };

        var @params = sound.Params;
        return
            $"{id}|pv={QuantizeFloat(@params.Volume)}|pp={QuantizeFloat(@params.Pitch)}|pm={QuantizeFloat(@params.MaxDistance)}|pr={QuantizeFloat(@params.ReferenceDistance)}|pro={QuantizeFloat(@params.RolloffFactor)}|pl={@params.Loop}|po={QuantizeFloat(@params.PlayOffsetSeconds)}|vv={QuantizeNullableFloat(@params.Variation)}";
    }

    private static int StablePriority(MapId mapId, int quantizedX, int quantizedY)
    {
        unchecked
        {
            var map = (uint) (int) mapId;
            var x = (uint) quantizedX;
            var y = (uint) quantizedY;
            return (int) ((x * 73856093u) ^ (y * 19349663u) ^ (map * 83492791u));
        }
    }

    private float GetRandomOneShotInterval(SelectedEmitterConfiguration config)
    {
        return GetRandomOneShotInterval(config.OneShotMinIntervalSeconds, config.OneShotMaxIntervalSeconds);
    }

    private float GetRandomOneShotInterval(float minInterval, float maxInterval)
    {
        if (MathHelper.CloseToPercent(minInterval, maxInterval))
            return minInterval;

        return _random.NextFloat(minInterval, maxInterval);
    }

    private static float NormalizeRange(float range)
    {
        return MathF.Max(0.5f, range);
    }

    private static float NormalizeEmitterSpacing(float spacing)
    {
        return MathF.Max(0.5f, spacing);
    }

    private static float NormalizeOneShotMinInterval(float interval)
    {
        return MathF.Max(0.1f, interval);
    }

    private static float NormalizeOneShotMaxInterval(float minInterval, float maxInterval)
    {
        return MathF.Max(minInterval, maxInterval);
    }

    private static int QuantizeAxis(float value)
    {
        return (int) MathF.Round(value * 8f);
    }

    private static int QuantizeFloat(float value)
    {
        return (int) MathF.Round(value * 1000f);
    }

    private static int QuantizeNullableFloat(float? value)
    {
        return value.HasValue ? QuantizeFloat(value.Value) : int.MinValue;
    }

    private static CellKey GetCellKey(Vector2 worldPosition, float spacing)
    {
        return new CellKey(
            (int) MathF.Floor(worldPosition.X / spacing),
            (int) MathF.Floor(worldPosition.Y / spacing));
    }

    private readonly record struct SourceConfigKey(
        MapId MapId,
        string SoundKey,
        bool Loop,
        int Range,
        int Volume,
        int EmitterSpacing,
        int OneShotMinInterval,
        int OneShotMaxInterval);

    private readonly record struct CellKey(int X, int Y);

    private readonly record struct SelectedEmitterConfiguration(
        SoundSpecifier Sound,
        float Range,
        float Volume,
        float EmitterSpacing,
        bool Loop,
        float OneShotMinIntervalSeconds,
        float OneShotMaxIntervalSeconds);

    private readonly record struct EmitterCandidate(
        EntityUid Uid,
        Vector2 WorldPosition,
        int QuantizedX,
        int QuantizedY,
        int Priority,
        bool IsManagedEmitter,
        SelectedEmitterConfiguration Config);
}
