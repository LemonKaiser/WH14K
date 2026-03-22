using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Popups;
using Content.Server._WH40K.Command;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.Intel.Detector;
using Content.Shared.Coordinates;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Intel.Detector;

/// <summary>
/// Mission-recon scanner for active WH40K runtime objective markers.
/// The detector only reports mission-tagged visuals and keeps faction-safe visibility rules.
/// </summary>
public sealed class WH40KIntelDetectorSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly WH40KCommandEventMissionRuntimeSystem _runtime = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly HashSet<Entity<WH40KMissionObjectiveVisualComponent>> _trackedMarkers = new();

    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<WH40KIntelDetectorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KIntelDetectorComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WH40KIntelDetectorComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<WH40KIntelDetectorComponent, DroppedEvent>(OnDropped);
        SubscribeLocalEvent<WH40KIntelDetectorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WH40KIntelDetectorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<WH40KIntelDetectorComponent> ent, ref MapInitEvent args)
    {
        UpdateAppearance(ent);
    }

    private void OnUseInHand(Entity<WH40KIntelDetectorComponent> ent, ref UseInHandEvent args)
    {
        if (!_hands.IsHolding(args.User, ent.Owner))
            return;

        args.Handled = true;
        Toggle(ent, args.User);
    }

    private void OnActivateInWorld(Entity<WH40KIntelDetectorComponent> ent, ref ActivateInWorldEvent args)
    {
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

    private void OnDropped(Entity<WH40KIntelDetectorComponent> ent, ref DroppedEvent args)
    {
        if (!ent.Comp.DeactivateOnDrop || !ent.Comp.Enabled)
            return;

        ent.Comp.Enabled = false;
        ent.Comp.LastScan = _timing.CurTime;
        ent.Comp.Blips.Clear();
        Dirty(ent);
        UpdateAppearance(ent);
    }

    private void OnGetVerbs(Entity<WH40KIntelDetectorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !ent.Comp.CanToggleRange)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 100,
            Text = ent.Comp.Short
                ? Loc.GetString("wh40k-intel-detector-verb-switch-long")
                : Loc.GetString("wh40k-intel-detector-verb-switch-short"),
            Act = () =>
            {
                ent.Comp.Short = !ent.Comp.Short;
                if (ent.Comp.Enabled)
                    ent.Comp.NextScanAt = _timing.CurTime + GetRefreshRate(ent.Comp);

                Dirty(ent);
                _audio.PlayPredicted(ent.Comp.ToggleSound, ent, user);
                _popup.PopupEntity(
                    Loc.GetString(
                        ent.Comp.Short
                            ? "wh40k-intel-detector-popup-mode-short"
                            : "wh40k-intel-detector-popup-mode-long"),
                    user,
                    user,
                    PopupType.Small);
            }
        });
    }

    private void OnExamined(Entity<WH40KIntelDetectorComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(WH40KIntelDetectorComponent)))
        {
            var modeKey = ent.Comp.Short
                ? "wh40k-intel-detector-mode-short"
                : "wh40k-intel-detector-mode-long";
            var range = ent.Comp.Short ? ent.Comp.ShortRange : ent.Comp.LongRange;
            var refresh = ent.Comp.Short ? ent.Comp.ShortRefresh : ent.Comp.LongRefresh;
            var stateKey = ent.Comp.Enabled
                ? "wh40k-intel-detector-state-enabled"
                : "wh40k-intel-detector-state-disabled";

            args.PushMarkup(Loc.GetString(
                "wh40k-intel-detector-examine-state",
                ("state", Loc.GetString(stateKey))));
            args.PushMarkup(Loc.GetString(
                "wh40k-intel-detector-examine-mode",
                ("mode", Loc.GetString(modeKey)),
                ("range", range),
                ("seconds", Math.Max(1, (int) Math.Ceiling(refresh.TotalSeconds)))));

            if (!TryResolveDetectorTeam(ent.Owner, ent.Comp, out var teamId))
            {
                args.PushMarkup(Loc.GetString("wh40k-intel-detector-examine-feed-no-team"));
                return;
            }

            var missionState = _runtime.BuildTeamMissionRuntimeState(teamId);
            if (!missionState.IsActive)
                missionState = _runtime.BuildGlobalMissionRuntimeState();

            if (!missionState.IsActive)
            {
                args.PushMarkup(Loc.GetString("wh40k-intel-detector-examine-feed-none"));
                return;
            }

            var missionTitle = ResolveLocalizedOrRaw(missionState.MissionTitle);
            args.PushMarkup(Loc.GetString(
                "wh40k-intel-detector-examine-feed-active",
                ("mission", missionTitle),
                ("seconds", missionState.RemainingSeconds)));
        }
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        var detectors = EntityQueryEnumerator<WH40KIntelDetectorComponent>();
        while (detectors.MoveNext(out var uid, out var detector))
        {
            if (!detector.Enabled || now < detector.NextScanAt)
                continue;

            detector.NextScanAt = now + GetRefreshRate(detector);
            var newBlips = new List<WH40KIntelDetectorBlip>();

            if (!TryResolveScannerUser(uid, detector, out var scanner, out var scannerTeamId, out var hasTeam))
            {
                CommitScanResults((uid, detector), newBlips, now);
                _audio.PlayPvs(detector.ScanEmptySound, uid);
                continue;
            }

            var scannerCoords = _transform.GetMapCoordinates(scanner);
            if (scannerCoords.MapId == MapId.Nullspace)
            {
                CommitScanResults((uid, detector), newBlips, now);
                _audio.PlayPvs(detector.ScanEmptySound, uid);
                continue;
            }

            var range = detector.Short ? detector.ShortRange : detector.LongRange;
            _trackedMarkers.Clear();
            _lookup.GetEntitiesInRange(scanner.ToCoordinates(), range, _trackedMarkers, LookupFlags.Uncontained);

            foreach (var marker in _trackedMarkers)
            {
                if (!CanTrackMarker(marker.Comp, scannerTeamId, hasTeam))
                    continue;

                var markerCoords = _transform.GetMapCoordinates(marker);
                if (markerCoords.MapId != scannerCoords.MapId)
                    continue;

                var direction = markerCoords.Position - scannerCoords.Position;
                if (direction.LengthSquared() <= 0.0001f)
                    continue;

                newBlips.Add(new WH40KIntelDetectorBlip(
                    markerCoords,
                    Special: false,
                    Direction: Vector2.Normalize(direction)));
            }

            CommitScanResults((uid, detector), newBlips, now);
            _audio.PlayPvs(newBlips.Count == 0 ? detector.ScanEmptySound : detector.ScanSound, uid);
        }
    }

    private void Toggle(Entity<WH40KIntelDetectorComponent> ent, EntityUid user)
    {
        if (!IsUserAuthorized((user, ent.Comp), showPopup: true))
            return;

        ent.Comp.Enabled = !ent.Comp.Enabled;
        ent.Comp.LastUser = user;
        ent.Comp.LastScan = _timing.CurTime;
        ent.Comp.Blips.Clear();
        ent.Comp.NextScanAt = ent.Comp.Enabled
            ? _timing.CurTime + GetRefreshRate(ent.Comp)
            : TimeSpan.Zero;

        Dirty(ent);
        UpdateAppearance(ent);
        _audio.PlayPredicted(ent.Comp.ToggleSound, ent, user);
        _popup.PopupEntity(
            Loc.GetString(
                ent.Comp.Enabled
                    ? "wh40k-intel-detector-popup-enabled"
                    : "wh40k-intel-detector-popup-disabled"),
            user,
            user,
            PopupType.Small);
    }

    private static TimeSpan GetRefreshRate(WH40KIntelDetectorComponent detector)
    {
        return detector.Short ? detector.ShortRefresh : detector.LongRefresh;
    }

    private void CommitScanResults(
        Entity<WH40KIntelDetectorComponent> detector,
        List<WH40KIntelDetectorBlip> newBlips,
        TimeSpan now)
    {
        newBlips.Sort(CompareBlips);

        var currentBlips = detector.Comp.Blips;
        var hadBlips = currentBlips.Count > 0;
        var hasBlips = newBlips.Count > 0;

        if (!hadBlips && !hasBlips)
            return;

        detector.Comp.LastScan = now;

        if (!BlipListsEqual(currentBlips, newBlips))
            detector.Comp.Blips = newBlips;

        Dirty(detector);
        UpdateAppearance(detector);
    }

    private static bool BlipListsEqual(
        IReadOnlyList<WH40KIntelDetectorBlip> currentBlips,
        IReadOnlyList<WH40KIntelDetectorBlip> newBlips)
    {
        if (currentBlips.Count != newBlips.Count)
            return false;

        for (var i = 0; i < currentBlips.Count; i++)
        {
            if (!currentBlips[i].Equals(newBlips[i]))
                return false;
        }

        return true;
    }

    private static int CompareBlips(WH40KIntelDetectorBlip left, WH40KIntelDetectorBlip right)
    {
        var positionOrder = left.Coordinates.Position.X.CompareTo(right.Coordinates.Position.X);
        if (positionOrder != 0)
            return positionOrder;

        positionOrder = left.Coordinates.Position.Y.CompareTo(right.Coordinates.Position.Y);
        if (positionOrder != 0)
            return positionOrder;

        var specialOrder = left.Special.CompareTo(right.Special);
        if (specialOrder != 0)
            return specialOrder;

        var directionOrder = left.Direction.X.CompareTo(right.Direction.X);
        if (directionOrder != 0)
            return directionOrder;

        return left.Direction.Y.CompareTo(right.Direction.Y);
    }

    private void UpdateAppearance(Entity<WH40KIntelDetectorComponent> detector)
    {
        _appearance.SetData(
            detector,
            WH40KIntelDetectorLayer.State,
            detector.Comp.Enabled);
    }

    private bool TryResolveScannerUser(
        EntityUid detectorUid,
        WH40KIntelDetectorComponent detector,
        out EntityUid scanner,
        out string scannerTeamId,
        out bool hasTeam)
    {
        scanner = EntityUid.Invalid;
        scannerTeamId = string.Empty;
        hasTeam = false;

        if (TryResolveHolderByTransform(detectorUid, out var holderByTransform))
            scanner = holderByTransform;
        else if (detector.LastUser is { } lastUser && Exists(lastUser))
            scanner = lastUser;

        if (scanner == EntityUid.Invalid)
            return false;

        detector.LastUser = scanner;

        hasTeam = _teamRule.TryGetTeamIdFromEntity(scanner, out scannerTeamId);
        if (!hasTeam && (detector.RequireTeam || detector.AllowedTeamIds.Count > 0))
            return false;

        if (detector.AllowedTeamIds.Count == 0)
            return true;

        if (!hasTeam)
            return false;

        var scannerTeam = scannerTeamId;
        return detector.AllowedTeamIds.Any(allowed =>
            string.Equals(allowed, scannerTeam, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsUserAuthorized(Entity<WH40KIntelDetectorComponent> ent, bool showPopup)
    {
        if (!TryResolveUserTeam(ent.Owner, out var teamId))
        {
            if (ent.Comp.RequireTeam || ent.Comp.AllowedTeamIds.Count > 0)
            {
                if (showPopup)
                {
                    _popup.PopupEntity(
                        Loc.GetString("wh40k-intel-detector-popup-no-team"),
                        ent.Owner,
                        ent.Owner,
                        PopupType.SmallCaution);
                }

                return false;
            }

            return true;
        }

        if (ent.Comp.AllowedTeamIds.Count == 0)
            return true;

        var allowed = ent.Comp.AllowedTeamIds.Any(allowedId =>
            string.Equals(allowedId, teamId, StringComparison.OrdinalIgnoreCase));
        if (allowed)
            return true;

        if (showPopup)
        {
            _popup.PopupEntity(
                Loc.GetString("wh40k-intel-detector-popup-wrong-team"),
                ent.Owner,
                ent.Owner,
                PopupType.SmallCaution);
        }

        return false;
    }

    private bool TryResolveDetectorTeam(
        EntityUid detectorUid,
        WH40KIntelDetectorComponent detector,
        out string teamId)
    {
        teamId = string.Empty;

        if (TryResolveHolderByTransform(detectorUid, out var holder) &&
            _teamRule.TryGetTeamIdFromEntity(holder, out teamId))
        {
            detector.LastUser = holder;
            return true;
        }

        if (detector.LastUser is { } lastUser &&
            Exists(lastUser) &&
            _teamRule.TryGetTeamIdFromEntity(lastUser, out teamId))
        {
            return true;
        }

        return false;
    }

    private bool TryResolveUserTeam(EntityUid user, out string teamId)
    {
        teamId = string.Empty;
        return _teamRule.TryGetTeamIdFromEntity(user, out teamId);
    }

    private bool TryResolveHolderByTransform(EntityUid detectorUid, out EntityUid holderUid)
    {
        holderUid = EntityUid.Invalid;

        if (!_xformQuery.TryGetComponent(detectorUid, out var detectorXform))
            return false;

        var current = detectorXform.ParentUid;
        var depth = 0;
        while (current != EntityUid.Invalid && depth < 12)
        {
            if (_teamRule.TryGetTeamIdFromEntity(current, out _))
            {
                holderUid = current;
                return true;
            }

            if (!_xformQuery.TryGetComponent(current, out var currentXform))
                return false;

            if (currentXform.ParentUid == EntityUid.Invalid || currentXform.ParentUid == current)
                return false;

            current = currentXform.ParentUid;
            depth++;
        }

        return false;
    }

    private static bool CanTrackMarker(
        WH40KMissionObjectiveVisualComponent marker,
        string scannerTeamId,
        bool scannerHasTeam)
    {
        if (string.IsNullOrWhiteSpace(marker.TeamId))
            return true;

        if (!scannerHasTeam || string.IsNullOrWhiteSpace(scannerTeamId))
            return false;

        return string.Equals(marker.TeamId, scannerTeamId, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveLocalizedOrRaw(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (Loc.TryGetString(value, out var localized) && !string.IsNullOrWhiteSpace(localized))
            return localized!;

        return value;
    }
}
