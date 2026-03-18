using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared._RMC14.MotionDetector;
using Content.Shared.Coordinates;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Server._RMC14.MotionDetector;

public sealed class MotionDetectorSystem : EntitySystem
{
    private static readonly TimeSpan SampleRetention = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SampleInterpolationPadding = TimeSpan.FromSeconds(1);

    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly HashSet<Entity<MotionDetectorTrackedComponent>> _tracked = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<MobStateComponent, MapInitEvent>(OnMobMapInit);
        SubscribeLocalEvent<MotionDetectorTrackedComponent, MoveEvent>(OnTrackedMove);

        SubscribeLocalEvent<MotionDetectorComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<MotionDetectorComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<MotionDetectorComponent, DroppedEvent>(OnDropped);
        SubscribeLocalEvent<MotionDetectorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    private void OnMobMapInit(Entity<MobStateComponent> ent, ref MapInitEvent args)
    {
        var now = _timing.CurTime;
        var tracked = EnsureComp<MotionDetectorTrackedComponent>(ent);
        tracked.LastMove = now;
        tracked.Samples.Clear();

        var coords = _transform.GetMapCoordinates(ent);
        if (coords.MapId != MapId.Nullspace)
            tracked.Samples.Add(new MotionDetectorMoveSample(now, coords.MapId, coords.Position));
    }

    private void OnTrackedMove(Entity<MotionDetectorTrackedComponent> ent, ref MoveEvent args)
    {
        if (args.OldPosition == args.NewPosition)
            return;

        var now = _timing.CurTime;
        ent.Comp.LastMove = now;

        var newMapCoords = _transform.ToMapCoordinates(args.NewPosition, false);
        if (newMapCoords.MapId == MapId.Nullspace)
            return;

        AddMovementSample(ent.Comp, newMapCoords, now);
        PruneOldSamples(ent.Comp, now - SampleRetention);
    }

    private void OnUseInHand(Entity<MotionDetectorComponent> ent, ref UseInHandEvent args)
    {
        if (!ent.Comp.HandToggleable || !_hands.IsHolding(args.User, ent))
            return;

        args.Handled = true;
        Toggle(ent, args.User);
    }

    private void OnActivateInWorld(Entity<MotionDetectorComponent> ent, ref ActivateInWorldEvent args)
    {
        if (!ent.Comp.HandToggleable)
            return;

        if (!_container.TryGetContainingContainer(ent.Owner, out var container))
            return;

        if (!_hands.IsHolding(args.User, ent.Owner) &&
            HasComp<StorageComponent>(container.Owner) &&
            !_container.TryGetContainingContainer(container.Owner, out _))
        {
            return;
        }

        args.Handled = true;
        Toggle(ent, args.User);
    }

    private void OnDropped(Entity<MotionDetectorComponent> ent, ref DroppedEvent args)
    {
        if (!ent.Comp.DeactivateOnDrop || !ent.Comp.Enabled)
            return;

        ent.Comp.Enabled = false;
        ent.Comp.Blips.Clear();
        Dirty(ent);
        UpdateAppearance(ent);
        RaiseUpdated(ent);
    }

    private void OnGetVerbs(Entity<MotionDetectorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !ent.Comp.CanToggleRange)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = ent.Comp.Short
                ? Loc.GetString("rmc-motion-detector-verb-switch-long")
                : Loc.GetString("rmc-motion-detector-verb-switch-short"),
            Act = () =>
            {
                ent.Comp.Short = !ent.Comp.Short;

                if (ent.Comp.Enabled)
                    ent.Comp.NextScanAt = _timing.CurTime + GetRefreshRate(ent.Comp);

                Dirty(ent);
                UpdateAppearance(ent);
                _audio.PlayPredicted(ent.Comp.ToggleSound, ent, user);
            },
        });
    }

    private void Toggle(Entity<MotionDetectorComponent> ent, EntityUid user)
    {
        ent.Comp.Enabled = !ent.Comp.Enabled;
        ent.Comp.LastUser = user;

        if (ent.Comp.Enabled)
            ent.Comp.NextScanAt = _timing.CurTime + GetRefreshRate(ent.Comp);

        ent.Comp.Blips.Clear();
        Dirty(ent);
        UpdateAppearance(ent);
        RaiseUpdated(ent);
        _audio.PlayPredicted(ent.Comp.ToggleSound, ent, user);
    }

    private void RaiseUpdated(Entity<MotionDetectorComponent> ent)
    {
        var ev = new MotionDetectorUpdatedEvent(ent.Comp.Enabled);
        RaiseLocalEvent(ent, ref ev);
    }

    private void UpdateAppearance(Entity<MotionDetectorComponent> ent)
    {
        _appearance.SetData(ent, MotionDetectorLayer.Setting, ent.Comp.Short ? MotionDetectorSetting.Short : MotionDetectorSetting.Long);

        var count = Math.Min(ent.Comp.Blips.Count, 9);
        if (!ent.Comp.Enabled)
            count = -1;

        _appearance.SetData(ent, MotionDetectorLayer.Number, count);
    }

    private static TimeSpan GetRefreshRate(MotionDetectorComponent detector)
    {
        return detector.Short ? detector.ShortRefresh : detector.LongRefresh;
    }

    private static float GetMoveThreshold(MotionDetectorComponent detector)
    {
        return detector.Short ? detector.ShortMoveDistance : detector.LongMoveDistance;
    }

    private static void AddMovementSample(MotionDetectorTrackedComponent tracked, MapCoordinates mapCoords, TimeSpan time)
    {
        if (tracked.Samples.Count > 0)
        {
            var last = tracked.Samples[^1];

            if (last.MapId != mapCoords.MapId)
            {
                tracked.Samples.Clear();
            }
            else if ((last.Position - mapCoords.Position).LengthSquared() <= 0.0001f)
            {
                tracked.Samples[^1] = last with { Time = time };
                return;
            }
        }

        tracked.Samples.Add(new MotionDetectorMoveSample(time, mapCoords.MapId, mapCoords.Position));
    }

    private static void PruneOldSamples(MotionDetectorTrackedComponent tracked, TimeSpan cutoff)
    {
        // Keep one sample before the cutoff so the first segment in-window can still be measured correctly.
        while (tracked.Samples.Count > 1 && tracked.Samples[1].Time < cutoff)
        {
            tracked.Samples.RemoveAt(0);
        }
    }

    private bool TryGetRecentMovement(
        MotionDetectorTrackedComponent tracked,
        TimeSpan now,
        TimeSpan window,
        out float distance,
        out Vector2 direction)
    {
        distance = 0f;
        direction = Vector2.Zero;

        if (tracked.Samples.Count < 2)
            return false;

        var windowStart = now - window;
        PruneOldSamples(tracked, windowStart - SampleInterpolationPadding);

        if (tracked.Samples.Count < 2)
            return false;

        var samples = tracked.Samples;
        var endIndex = samples.Count - 1;
        var mapId = samples[endIndex].MapId;

        if (samples[endIndex].Time < windowStart)
            return false;

        var hasSegment = false;
        var startPosition = Vector2.Zero;
        var endPosition = Vector2.Zero;
        var fallbackDirection = Vector2.Zero;

        for (var i = 1; i <= endIndex; i++)
        {
            var previous = samples[i - 1];
            var current = samples[i];

            if (previous.MapId != mapId || current.MapId != mapId)
                continue;

            if (current.Time < windowStart)
                continue;

            // Movement happens on discrete move events. If the event is in-window, count its full displacement.
            var segmentVector = current.Position - previous.Position;
            var segmentDistance = segmentVector.Length();
            if (segmentDistance <= 0f)
                continue;

            if (!hasSegment)
            {
                startPosition = previous.Position;
                hasSegment = true;
            }

            endPosition = current.Position;
            fallbackDirection = current.Position - previous.Position;
            distance += segmentDistance;
        }

        if (!hasSegment || distance <= 0f)
            return false;

        direction = endPosition - startPosition;
        if (direction.LengthSquared() <= 0.0001f)
            direction = fallbackDirection;

        return direction.LengthSquared() > 0.0001f;
    }

    private bool IsSameTeam(EntityUid first, EntityUid second)
    {
        if (!TryComp<WH40KTeamMemberComponent>(first, out var firstTeam) ||
            !TryComp<WH40KTeamMemberComponent>(second, out var secondTeam))
        {
            return false;
        }

        return string.Equals(firstTeam.TeamId, secondTeam.TeamId, StringComparison.OrdinalIgnoreCase);
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var now = _timing.CurTime;

        var detectors = EntityQueryEnumerator<MotionDetectorComponent>();
        while (detectors.MoveNext(out var uid, out var detector))
        {
            if (!detector.Enabled || now < detector.NextScanAt || detector.LastUser is not { } user)
                continue;

            detector.LastScan = now;
            detector.NextScanAt = now + GetRefreshRate(detector);
            detector.Blips.Clear();

            var range = detector.Short ? detector.ShortRange : detector.LongRange;
            var moveThreshold = GetMoveThreshold(detector);
            _tracked.Clear();
            _entityLookup.GetEntitiesInRange(uid.ToCoordinates(), range, _tracked, LookupFlags.Uncontained);

            foreach (var tracked in _tracked)
            {
                if (tracked.Owner == user)
                    continue;

                if (tracked.Comp.LastMove < now - detector.MoveTime)
                    continue;

                if (!TryGetRecentMovement(tracked.Comp, now, detector.MoveTime, out var moveDistance, out var moveDirection))
                    continue;

                if (moveDistance < moveThreshold)
                    continue;

                if (IsSameTeam(user, tracked.Owner))
                    continue;

                if (moveDirection.LengthSquared() > 0.0001f)
                    moveDirection = Vector2.Normalize(moveDirection);
                else
                    moveDirection = Vector2.UnitY;

                detector.Blips.Add(new Blip(_transform.GetMapCoordinates(tracked), tracked.Comp.Special, moveDirection));
            }

            Dirty(uid, detector);
            UpdateAppearance((uid, detector));

            if (detector.Blips.Count == 0)
            {
                _audio.PlayPvs(detector.ScanEmptySound, uid);
                continue;
            }

            _audio.PlayPvs(detector.ScanSound, uid);
        }
    }
}
