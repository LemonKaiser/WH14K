using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.Light.Components;
using Content.Server.Popups;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.IgnitionSource;
using Content.Shared.Item;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.Signals.Flare;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using Content.Server._WH40K.Localizations;

namespace Content.Server._WH40K.Signals.Flare;

public sealed partial class WH40KFlareSignalSystem : EntitySystem
{
    [Dependency] private  SharedAudioSystem _audio = default!;
    [Dependency] private  IChatManager _chat = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  IMapManager _mapManager = default!;
    [Dependency] private  SharedMapSystem _map = default!;
    [Dependency] private  IPlayerManager _players = default!;
    [Dependency] private  PopupSystem _popup = default!;
    [Dependency] private  IPrototypeManager _proto = default!;
    [Dependency] private  WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;
    [Dependency] private  TurfSystem _turf = default!;
    [Dependency] private  WH40KPlayerCultureTracker _culture = default!;

    private readonly Dictionary<int, EntityUid> _signalMarkers = new();
    private int _nextSignalId = 400;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KFlareSignalComponent, IgnitionEvent>(OnIgnition);
        SubscribeLocalEvent<WH40KFlareSignalComponent, GettingPickedUpAttemptEvent>(OnPickupAttempt);
        SubscribeLocalEvent<WH40KFlareSignalComponent, ContainerGettingInsertedAttemptEvent>(OnContainerInsertAttempt);
        SubscribeLocalEvent<WH40KFlareSignalComponent, DroppedEvent>(OnDropped);
        SubscribeLocalEvent<WH40KFlareSignalComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<WH40KFlareSignalComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WH40KFlareSignalComponent, ComponentShutdown>(OnFlareShutdown);
        SubscribeLocalEvent<WH40KSignalFlareTargetComponent, ComponentShutdown>(OnMarkerShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent _)
    {
        _signalMarkers.Clear();
        _nextSignalId = 400;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WH40KActiveFlareSignalComponent, WH40KFlareSignalComponent, TransformComponent>();
        while (query.MoveNext(out var flareUid, out var active, out var signal, out var xform))
        {
            if (!IsFlareLit(flareUid))
            {
                RemCompDeferred<WH40KActiveFlareSignalComponent>(flareUid);
                continue;
            }

            var mapCoordinates = _transform.ToMapCoordinates(xform.Coordinates);
            if (mapCoordinates.MapId == MapId.Nullspace)
                continue;

            active.LastCoordinates.Enqueue(mapCoordinates);
            var sampleCount = Math.Max(2, signal.GroundedSampleCount);
            while (active.LastCoordinates.Count > sampleCount)
            {
                active.LastCoordinates.Dequeue();
            }

            if (active.LastCoordinates.Count < sampleCount)
                continue;

            if (!IsStationary(active.LastCoordinates, Math.Max(0.01f, signal.GroundedTolerance)))
                continue;

            if (!TryResolveGroundTile(mapCoordinates, out var gridUid, out var grid, out var tileIndices))
                continue;

            ActivateSignalMarker((flareUid, signal), active, gridUid, grid, tileIndices);
            RemCompDeferred<WH40KActiveFlareSignalComponent>(flareUid);
        }
    }

    public bool TryGetSignalTarget(
        int signalId,
        EntityUid user,
        out EntityUid gridUid,
        out MapGridComponent grid,
        out Vector2i tileIndices)
    {
        gridUid = default;
        grid = default!;
        tileIndices = default;

        if (!TryResolveSignalMarker(signalId, out _, out var marker))
            return false;

        if (!string.IsNullOrWhiteSpace(marker.TeamId))
        {
            if (!_teamRule.TryGetTeamIdFromEntity(user, out var userTeamId))
                return false;

            if (!string.Equals(userTeamId, marker.TeamId, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return TryResolveSignalTile(marker, out gridUid, out grid, out tileIndices);
    }

    private void OnIgnition(Entity<WH40KFlareSignalComponent> flare, ref IgnitionEvent args)
    {
        if (args.Ignite)
            return;

        RemCompDeferred<WH40KActiveFlareSignalComponent>(flare);
    }

    private void OnPickupAttempt(Entity<WH40KFlareSignalComponent> flare, ref GettingPickedUpAttemptEvent args)
    {
        if (args.Cancelled || !IsFlareLit(flare))
            return;

        args.Cancel();
        using var scope = _culture.CreateScope(args.User);
        _popup.PopupEntity(
            Loc.GetString("wh40k-signal-flare-popup-pickup-blocked"),
            flare,
            args.User,
            PopupType.SmallCaution);
    }

    private void OnContainerInsertAttempt(Entity<WH40KFlareSignalComponent> flare, ref ContainerGettingInsertedAttemptEvent args)
    {
        if (args.Cancelled || !IsFlareLit(flare))
            return;

        args.Cancel();
    }

    private void OnDropped(Entity<WH40KFlareSignalComponent> flare, ref DroppedEvent args)
    {
        TryStartTracking(flare, args.User);
    }

    private void OnThrown(Entity<WH40KFlareSignalComponent> flare, ref ThrownEvent args)
    {
        TryStartTracking(flare, args.User);
    }

    private void OnExamined(Entity<WH40KFlareSignalComponent> flare, ref ExaminedEvent args)
    {
        using var scope = _culture.CreateScope(args.Examiner);
        using (args.PushGroup(nameof(WH40KFlareSignalComponent)))
        {
            args.PushMarkup(Loc.GetString(
                "wh40k-signal-flare-examine-policy",
                ("seconds", Math.Max(1, (int) Math.Ceiling(flare.Comp.UserCooldown.TotalSeconds))),
                ("window", Math.Max(1, (int) Math.Ceiling(flare.Comp.RateLimitWindow.TotalSeconds))),
                ("count", flare.Comp.MaxSignalsPerWindow),
                ("active", flare.Comp.MaxActiveMarkersPerTeam)));

            if (TryComp(flare, out WH40KActiveFlareSignalComponent? active))
            {
                args.PushMarkup(Loc.GetString("wh40k-signal-flare-examine-arming", ("id", active.SignalId)));
            }
        }
    }

    private void OnFlareShutdown(Entity<WH40KFlareSignalComponent> flare, ref ComponentShutdown args)
    {
        RemCompDeferred<WH40KActiveFlareSignalComponent>(flare);
    }

    private void OnMarkerShutdown(Entity<WH40KSignalFlareTargetComponent> marker, ref ComponentShutdown args)
    {
        if (_signalMarkers.TryGetValue(marker.Comp.Id, out var knownMarker) &&
            knownMarker == marker.Owner)
        {
            _signalMarkers.Remove(marker.Comp.Id);
        }
    }

    private void TryStartTracking(Entity<WH40KFlareSignalComponent> flare, EntityUid? user)
    {
        if (!IsFlareLit(flare))
            return;

        if (HasComp<WH40KActiveFlareSignalComponent>(flare))
            return;

        if (user == null || !Exists(user.Value))
            return;

        if (!TryResolveAuthorizedTeamId(user.Value, flare.Comp, out var teamId))
            return;

        if (!TryConsumeUserThrottle(user.Value, flare.Comp))
            return;

        var active = EnsureComp<WH40KActiveFlareSignalComponent>(flare);
        active.User = user;
        active.TeamId = teamId;
        active.SignalId = ComputeNextSignalId();
        active.LastCoordinates.Clear();

        var mapCoordinates = _transform.GetMapCoordinates(flare);
        if (mapCoordinates.MapId != MapId.Nullspace)
            active.LastCoordinates.Enqueue(mapCoordinates);

        using var scope = _culture.CreateScope(user.Value);
        _popup.PopupEntity(
            Loc.GetString("wh40k-signal-flare-popup-armed", ("id", active.SignalId)),
            user.Value,
            user.Value);
    }

    private void ActivateSignalMarker(
        Entity<WH40KFlareSignalComponent> flare,
        WH40KActiveFlareSignalComponent active,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tileIndices)
    {
        if (string.IsNullOrWhiteSpace(active.TeamId))
            return;

        if (!_proto.HasIndex<EntityPrototype>(flare.Comp.MarkerPrototype))
        {
            PopupToOwner(active.User, "wh40k-signal-flare-popup-marker-unavailable", PopupType.SmallCaution);
            return;
        }

        var activeCap = Math.Max(1, flare.Comp.MaxActiveMarkersPerTeam);
        if (CountActiveMarkers(active.TeamId) >= activeCap)
        {
            PopupToOwner(
                active.User,
                "wh40k-signal-flare-popup-team-cap",
                PopupType.SmallCaution,
                ("count", activeCap));
            return;
        }

        var markerCoordinates = _transform.ToMapCoordinates(_map.GridTileToLocal(gridUid, grid, tileIndices));
        var markerUid = Spawn(flare.Comp.MarkerPrototype, markerCoordinates);

        var despawn = EnsureComp<TimedDespawnComponent>(markerUid);
        despawn.Lifetime = Math.Max(1f, (float) flare.Comp.MarkerLifetime.TotalSeconds);

        var visual = EnsureComp<WH40KMissionObjectiveVisualComponent>(markerUid);
        visual.TeamId = active.TeamId;
        visual.Label = flare.Comp.MarkerLabel;
        visual.Radius = flare.Comp.MarkerRadius;
        visual.Pulse = true;
        visual.Color = flare.Comp.MarkerColor;
        Dirty(markerUid, visual);

        var marker = EnsureComp<WH40KSignalFlareTargetComponent>(markerUid);
        marker.Id = active.SignalId;
        marker.TeamId = active.TeamId;
        marker.Source = flare.Owner;
        marker.Grid = gridUid;
        marker.Tile = tileIndices;
        marker.ExpiresAt = _timing.CurTime + flare.Comp.MarkerLifetime;
        Dirty(markerUid, marker);

        if (_signalMarkers.TryGetValue(active.SignalId, out var existingMarker) &&
            existingMarker != markerUid &&
            Exists(existingMarker))
        {
            QueueDel(existingMarker);
        }

        _signalMarkers[active.SignalId] = markerUid;
        _audio.PlayPvs(flare.Comp.ActivateSound, markerUid);

        PopupToOwner(
            active.User,
            "wh40k-signal-flare-popup-activated",
            PopupType.Small,
            ("id", active.SignalId),
            ("x", tileIndices.X),
            ("y", tileIndices.Y));

        var userName = active.User != null && Exists(active.User.Value)
            ? Name(active.User.Value)
            : Loc.GetString("wh40k-signal-flare-user-unknown");

        DispatchTeamSignalMessage(
            active.TeamId,
            "wh40k-signal-flare-team-message",
            ("user", userName),
            ("id", active.SignalId),
            ("x", tileIndices.X),
            ("y", tileIndices.Y));
    }

    private bool TryResolveSignalMarker(
        int signalId,
        out EntityUid markerUid,
        out WH40KSignalFlareTargetComponent marker)
    {
        markerUid = default;
        marker = default!;

        if (!_signalMarkers.TryGetValue(signalId, out markerUid))
        {
            var query = EntityQueryEnumerator<WH40KSignalFlareTargetComponent>();
            while (query.MoveNext(out var candidateUid, out var candidateMarker))
            {
                if (candidateMarker.Id != signalId)
                    continue;

                markerUid = candidateUid;
                marker = candidateMarker;
                _signalMarkers[signalId] = markerUid;
                break;
            }
        }

        if (markerUid == default || Deleted(markerUid))
        {
            _signalMarkers.Remove(signalId);
            return false;
        }

        if (!TryComp(markerUid, out WH40KSignalFlareTargetComponent? resolvedMarker))
        {
            _signalMarkers.Remove(signalId);
            return false;
        }
        marker = resolvedMarker;

        if (marker.ExpiresAt != TimeSpan.Zero &&
            _timing.CurTime >= marker.ExpiresAt)
        {
            QueueDel(markerUid);
            _signalMarkers.Remove(signalId);
            return false;
        }

        return true;
    }

    private bool TryResolveSignalTile(
        WH40KSignalFlareTargetComponent marker,
        out EntityUid gridUid,
        out MapGridComponent grid,
        out Vector2i tileIndices)
    {
        gridUid = default;
        grid = default!;
        tileIndices = default;

        if (marker.Grid is not { } markerGridUid ||
            !TryComp<MapGridComponent>(markerGridUid, out var markerGrid))
        {
            return false;
        }

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

    private int CountActiveMarkers(string teamId)
    {
        var count = 0;
        var query = EntityQueryEnumerator<WH40KSignalFlareTargetComponent>();
        while (query.MoveNext(out _, out var marker))
        {
            if (!string.Equals(marker.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (marker.ExpiresAt != TimeSpan.Zero && _timing.CurTime >= marker.ExpiresAt)
                continue;

            count++;
        }

        return count;
    }

    private bool TryResolveAuthorizedTeamId(
        EntityUid user,
        WH40KFlareSignalComponent flare,
        out string teamId)
    {
        teamId = string.Empty;
        var hasTeam = _teamRule.TryGetTeamIdFromEntity(user, out teamId);
        if (!hasTeam)
        {
            if (!flare.RequireTeam && flare.AllowedTeamIds.Count == 0)
                return true;

            _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-signal-flare-popup-no-team"), user, user, PopupType.SmallCaution);
            return false;
        }

        if (flare.AllowedTeamIds.Count == 0)
            return true;

        var resolvedTeamId = teamId;
        var allowed = flare.AllowedTeamIds.Any(allowedTeamId =>
            string.Equals(allowedTeamId, resolvedTeamId, StringComparison.OrdinalIgnoreCase));
        if (allowed)
            return true;

        _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-signal-flare-popup-wrong-team"), user, user, PopupType.SmallCaution);
        return false;
    }

    private bool TryConsumeUserThrottle(EntityUid user, WH40KFlareSignalComponent flare)
    {
        var throttle = EnsureComp<WH40KFlareSignalUserThrottleComponent>(user);
        var now = _timing.CurTime;

        if (throttle.NextAllowedSignalAt > now)
        {
            var seconds = Math.Max(1, (int) Math.Ceiling((throttle.NextAllowedSignalAt - now).TotalSeconds));
            _popup.PopupEntity(
                Loc.GetString("wh40k-signal-flare-popup-user-cooldown", ("seconds", seconds)),
                user,
                user,
                PopupType.SmallCaution);
            return false;
        }

        while (throttle.RecentSignals.Count > 0 &&
               now - throttle.RecentSignals.Peek() > flare.RateLimitWindow)
        {
            throttle.RecentSignals.Dequeue();
        }

        if (throttle.RecentSignals.Count >= flare.MaxSignalsPerWindow)
        {
            var nextAt = throttle.RecentSignals.Peek() + flare.RateLimitWindow;
            var seconds = Math.Max(1, (int) Math.Ceiling((nextAt - now).TotalSeconds));
            _popup.PopupEntity(
                Loc.GetString(
                    "wh40k-signal-flare-popup-rate-limit",
                    ("seconds", seconds),
                    ("count", flare.MaxSignalsPerWindow)),
                user,
                user,
                PopupType.SmallCaution);
            return false;
        }

        throttle.RecentSignals.Enqueue(now);
        throttle.NextAllowedSignalAt = now + flare.UserCooldown;
        return true;
    }

    private bool IsStationary(Queue<MapCoordinates> coordinates, float tolerance)
    {
        if (coordinates.Count == 0)
            return false;

        MapCoordinates latest = default;
        var hasSample = false;
        foreach (var sample in coordinates)
        {
            latest = sample;
            hasSample = true;
        }

        if (!hasSample || latest.MapId == MapId.Nullspace)
            return false;

        foreach (var sample in coordinates)
        {
            if (sample.MapId == MapId.Nullspace || sample.MapId != latest.MapId)
                return false;

            if ((sample.Position - latest.Position).LengthSquared() > tolerance * tolerance)
                return false;
        }

        return true;
    }

    private bool TryResolveGroundTile(
        MapCoordinates coordinates,
        out EntityUid gridUid,
        out MapGridComponent grid,
        out Vector2i tileIndices)
    {
        gridUid = default;
        grid = default!;
        tileIndices = default;

        if (coordinates.MapId == MapId.Nullspace)
            return false;

        if (!_mapManager.TryFindGridAt(coordinates, out gridUid, out var maybeGrid) ||
            maybeGrid == null)
        {
            return false;
        }

        grid = maybeGrid;
        tileIndices = _map.WorldToTile(gridUid, grid, coordinates.Position);
        if (!_map.TryGetTileRef(gridUid, grid, tileIndices, out var tileRef))
            return false;

        return !tileRef.Tile.IsEmpty && !_turf.IsSpace(tileRef);
    }

    private int ComputeNextSignalId()
    {
        return _nextSignalId++;
    }

    private bool IsFlareLit(EntityUid flareUid)
    {
        return TryComp(flareUid, out ExpendableLightComponent? expendable) &&
               expendable.Activated;
    }

    private void DispatchTeamSignalMessage(
        string teamId,
        string messageKey,
        params (string, object)[] args)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return;

        var message = Loc.GetString(messageKey, args);
        foreach (var session in _players.Sessions)
        {
            if (!_teamRule.TryGetTeamIdForUser(session.UserId, out var sessionTeamId))
                continue;

            if (!string.Equals(sessionTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            _chat.DispatchServerMessage(session, message);
        }
    }

    private void PopupToOwner(
        EntityUid? user,
        string messageKey,
        PopupType type = PopupType.Small,
        params (string, object)[] args)
    {
        if (user == null || !Exists(user.Value))
            return;

        _popup.PopupEntity(_culture.GetPlayerString(user.Value, messageKey, args), user.Value, user.Value, type);
    }
}
