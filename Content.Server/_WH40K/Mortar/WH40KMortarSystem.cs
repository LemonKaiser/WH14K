using System.Numerics;
using Content.Server.Popups;
using Content.Server._WH40K.Rangefinder;
using Content.Server._WH40K.Signals.Flare;
using Content.Server.Light.Components;
using Content.Server.Light.EntitySystems;
using Content.Shared._WH40K.Mortar;
using Content.Shared._WH40K.Rangefinder;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.Explosion.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Trigger.Systems;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Mortar;

public sealed class WH40KMortarSystem : EntitySystem
{
    private static readonly TimeSpan PopupSpamCooldown = TimeSpan.FromSeconds(1);

    [Dependency] private readonly ISharedAdminLogManager _adminLogs = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly FixtureSystem _fixture = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly ExpendableLightSystem _expLight = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly WH40KRangefinderSystem _rangefinder = default!;
    [Dependency] private readonly WH40KFlareSignalSystem _flareSignal = default!;
    [Dependency] private readonly SharedRoofSystem _roof = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private readonly Dictionary<EntityUid, int> _lastUiCooldownSeconds = new();
    private readonly Dictionary<(EntityUid User, string Key), TimeSpan> _popupCooldowns = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KMortarComponent, HandheldEntityPlacementAttemptEvent>(OnPlacementAttempt);
        SubscribeLocalEvent<WH40KMortarComponent, HandheldEntityPlacementCompleteEvent>(OnPlacementComplete);
        SubscribeLocalEvent<WH40KMortarComponent, HandheldEntityFoldAttemptEvent>(OnFoldAttempt);
        SubscribeLocalEvent<WH40KMortarComponent, HandheldEntityFoldCompleteEvent>(OnFoldComplete);
        SubscribeLocalEvent<WH40KMortarComponent, ActivateInWorldEvent>(OnActivateInWorld, before: [typeof(ActivatableUISystem)]);
        SubscribeLocalEvent<WH40KMortarComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WH40KMortarComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<WH40KMortarComponent, WH40KLoadMortarShellDoAfterEvent>(OnLoadDoAfter);
        SubscribeLocalEvent<WH40KMortarComponent, WH40KUnloadMortarShellDoAfterEvent>(OnUnloadDoAfter);
        SubscribeLocalEvent<WH40KMortarComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
        SubscribeLocalEvent<WH40KMortarComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WH40KMortarComponent, ComponentShutdown>(OnMortarShutdown);
        SubscribeLocalEvent<WH40KMortarComponent, DestructionEventArgs>(OnDestruction);

        Subs.BuiEvents<WH40KMortarComponent>(WH40KMortarUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<WH40KMortarSetTargetMessage>(OnSetTarget);
            subs.Event<WH40KMortarSetDialMessage>(OnSetDial);
            subs.Event<WH40KMortarSetLinkedDesignatorMessage>(OnSetLinkedDesignator);
            subs.Event<WH40KMortarToggleLaserModeMessage>(OnToggleLaserMode);
            subs.Event<WH40KMortarFireMessage>(OnFire);
        });
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KActiveMortarShellComponent, WH40KMortarShellComponent>();
        while (query.MoveNext(out var uid, out var active, out var shell))
        {
            if (!active.Warned && now >= active.WarnAt)
            {
                active.Warned = true;
                var warningText = Loc.GetString("wh40k-mortar-shell-warning");
                _popup.PopupCoordinates(warningText, active.Coordinates, PopupType.MediumCaution);
                _audio.PlayPvs(active.WarnSound, active.Coordinates);
            }

            if (!active.ImpactWarned && now >= active.ImpactWarnAt)
            {
                active.ImpactWarned = true;
                var impactText = Loc.GetString("wh40k-mortar-shell-impact-warning");
                _popup.PopupCoordinates(impactText, active.Coordinates, PopupType.LargeCaution);
            }

            if (now < active.LandAt)
                continue;

            if (_container.TryGetContainingContainer(uid, out var containing))
                _container.Remove(uid, containing);

            _transform.SetCoordinates(uid, active.Coordinates);

            if (shell.SpawnOnLand is { } spawnOnLand)
            {
                var landedUid = Spawn(spawnOnLand, active.Coordinates);
                if (TryComp<ExpendableLightComponent>(landedUid, out var expendableLight))
                    _expLight.TryActivate((landedUid, expendableLight));

                if (!string.IsNullOrWhiteSpace(shell.SpawnOnLandTriggerKey))
                    _trigger.Trigger(landedUid, null, shell.SpawnOnLandTriggerKey);
            }

            if (shell.TriggerExplosion)
                _explosion.TriggerExplosive(uid);

            if (!EntityManager.IsQueuedForDeletion(uid))
                QueueDel(uid);
        }

        var mortarQuery = EntityQueryEnumerator<WH40KMortarComponent>();
        while (mortarQuery.MoveNext(out var mortarUid, out var mortarComp))
        {
            if (!_ui.IsUiOpen(mortarUid, WH40KMortarUiKey.Key))
            {
                _lastUiCooldownSeconds.Remove(mortarUid);
                continue;
            }

            var mortar = (mortarUid, mortarComp);
            if (!CanOperateMortar(mortar))
            {
                _ui.CloseUi(mortarUid, WH40KMortarUiKey.Key);
                _lastUiCooldownSeconds.Remove(mortarUid);
                continue;
            }

            var remaining = GetCooldownRemainingSeconds(mortar, now);
            if (_lastUiCooldownSeconds.TryGetValue(mortarUid, out var last) && last == remaining)
                continue;

            _lastUiCooldownSeconds[mortarUid] = remaining;
            UpdateUi(mortar);
        }
    }

    private void OnMortarShutdown(Entity<WH40KMortarComponent> mortar, ref ComponentShutdown args)
    {
        _lastUiCooldownSeconds.Remove(mortar);
    }

    private void OnPlacementAttempt(Entity<WH40KMortarComponent> mortar, ref HandheldEntityPlacementAttemptEvent args)
    {
        NormalizeDeployedState(mortar);
        if (mortar.Comp.Deployed)
        {
            args.Cancel();
            return;
        }

        args.DeployDelay = mortar.Comp.DeployDelay;
        args.BreakOnMove = true;
        args.BreakOnHandChange = true;
        args.NeedHand = true;
    }

    private void OnPlacementComplete(Entity<WH40KMortarComponent> mortar, ref HandheldEntityPlacementCompleteEvent args)
    {
        if (args.Handled)
            return;

        if (mortar.Comp.Deployed)
            return;

        var coords = args.Coordinates;
        var rotation = args.Direction.ToAngle();

        mortar.Comp.Deployed = true;
        Dirty(mortar);

        if (_fixture.GetFixtureOrNull(mortar, mortar.Comp.FixtureId) is { } fixture)
            _physics.SetHard(mortar, fixture, true);

        _appearance.SetData(mortar, WH40KMortarVisualLayers.State, WH40KMortarVisuals.Deployed);

        var xform = Transform(mortar);

        _transform.SetCoordinates(mortar, xform, coords, rotation);
        _transform.AnchorEntity((mortar, xform));

        _audio.PlayPredicted(mortar.Comp.DeploySound, mortar, args.User);
        UpdateUi(mortar);

        _popup.PopupPredicted(
            Loc.GetString("wh40k-mortar-deploy-finish-self", ("mortar", mortar)),
            Loc.GetString("wh40k-mortar-deploy-finish-others", ("user", args.User), ("mortar", mortar)),
            mortar,
            args.User);

        args.Handled = true;
    }

    private void OnActivateInWorld(Entity<WH40KMortarComponent> mortar, ref ActivateInWorldEvent args)
    {
        NormalizeDeployedState(mortar);
        if (CanOperateMortar(mortar))
            return;

        // Folded mortar should ignore world activation interactions.
        args.Handled = true;
    }

    private void OnGetVerbs(Entity<WH40KMortarComponent> mortar, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        NormalizeDeployedState(mortar);
        if (!CanOperateMortar(mortar))
            return;

        var user = args.User;
        var uiVerb = new AlternativeVerb
        {
            Text = Loc.GetString("wh40k-mortar-verb-ui"),
            Act = () => _ui.TryOpenUi(mortar.Owner, WH40KMortarUiKey.Key, user),
        };

        var foldVerb = new AlternativeVerb
        {
            Text = Loc.GetString("wh40k-mortar-verb-fold"),
            Act = () => TryStartFold(mortar, user),
        };

        args.Verbs.Add(uiVerb);
        args.Verbs.Add(foldVerb);

        if (TryGetLoadedShell(mortar, out _, out _))
        {
            var unloadVerb = new AlternativeVerb
            {
                Text = Loc.GetString("wh40k-mortar-verb-unload-shell"),
                Act = () => TryStartUnloadShell(mortar, user),
            };

            args.Verbs.Add(unloadVerb);
        }
    }

    private void TryStartUnloadShell(Entity<WH40KMortarComponent> mortar, EntityUid user)
    {
        if (!CanOperateMortar(mortar))
            return;

        if (!TryGetLoadedShell(mortar, out var shellId, out var shellComp))
        {
            if (TryTakeUserPopupCooldown(user, "wh40k-mortar-no-shell-loaded"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-no-shell-loaded", ("mortar", mortar)), mortar, user);

            UpdateUi(mortar);
            return;
        }

        var ev = new WH40KUnloadMortarShellDoAfterEvent();
        var doAfter = new DoAfterArgs(EntityManager, user, shellComp!.LoadDelay, ev, mortar, mortar, shellId)
        {
            BreakOnMove = true,
            BreakOnHandChange = true,
            NeedHand = true,
            // The shell is inside mortar's internal container.
            // Keep interaction checks on the mortar itself, but skip direct distance checks to `used` shell entity.
            DistanceThreshold = null,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        _popup.PopupPredicted(
            Loc.GetString("wh40k-mortar-shell-unload-start-self", ("shell", shellId), ("mortar", mortar)),
            Loc.GetString("wh40k-mortar-shell-unload-start-others", ("user", user), ("shell", shellId), ("mortar", mortar)),
            mortar,
            user);
    }

    private void OnUnloadDoAfter(Entity<WH40KMortarComponent> mortar, ref WH40KUnloadMortarShellDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } shellId)
            return;

        args.Handled = true;

        if (!CanOperateMortar(mortar))
            return;

        if (!TryGetLoadedShell(mortar, out var loadedShellId, out _))
        {
            UpdateUi(mortar);
            return;
        }

        // DoAfter stores the intended shell in args.Used; unload only if it is still loaded.
        if (loadedShellId != shellId)
        {
            UpdateUi(mortar);
            return;
        }

        if (!_container.TryGetContainer(mortar, mortar.Comp.ContainerId, out var container))
            return;

        if (!_container.Remove(shellId, container))
            return;

        _hands.PickupOrDrop(args.User, shellId, dropNear: true);

        _popup.PopupPredicted(
            Loc.GetString("wh40k-mortar-shell-unload-self", ("shell", shellId), ("mortar", mortar)),
            Loc.GetString("wh40k-mortar-shell-unload-others", ("user", args.User), ("shell", shellId), ("mortar", mortar)),
            mortar,
            args.User);

        UpdateUi(mortar);
    }

    private void TryStartFold(Entity<WH40KMortarComponent> mortar, EntityUid user)
    {
        if (!CanOperateMortar(mortar))
            return;

        var foldRequest = new HandheldEntityFoldRequestEvent(user);
        RaiseLocalEvent(mortar.Owner, foldRequest);
        if (!foldRequest.Handled)
            return;

        _popup.PopupClient(Loc.GetString("wh40k-mortar-fold-start", ("mortar", mortar)), user, user);
    }

    private void OnInteractHand(Entity<WH40KMortarComponent> mortar, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        NormalizeDeployedState(mortar);
        if (!CanOperateMortar(mortar))
            return;

        if (_ui.TryOpenUi(mortar.Owner, WH40KMortarUiKey.Key, args.User))
            args.Handled = true;
    }

    private void OnFoldAttempt(Entity<WH40KMortarComponent> mortar, ref HandheldEntityFoldAttemptEvent args)
    {
        if (!CanOperateMortar(mortar))
        {
            args.Cancel();
            return;
        }

        args.FoldDelay = mortar.Comp.FoldDelay;
        args.BreakOnMove = true;
        args.BreakOnHandChange = true;
        args.NeedHand = true;
    }

    private void OnFoldComplete(Entity<WH40KMortarComponent> mortar, ref HandheldEntityFoldCompleteEvent args)
    {
        if (args.Handled)
            return;

        FoldMortar(mortar, args.User);
        args.Handled = true;
    }

    private void FoldMortar(Entity<WH40KMortarComponent> mortar, EntityUid user)
    {
        if (!mortar.Comp.Deployed)
            return;

        mortar.Comp.Deployed = false;
        Dirty(mortar);

        if (_fixture.GetFixtureOrNull(mortar, mortar.Comp.FixtureId) is { } fixture)
            _physics.SetHard(mortar, fixture, false);

        _appearance.SetData(mortar, WH40KMortarVisualLayers.State, WH40KMortarVisuals.Item);

        var xform = Transform(mortar);
        if (xform.Anchored)
            _transform.Unanchor(mortar, xform);

        _ui.CloseUi(mortar.Owner, WH40KMortarUiKey.Key, user);
        UpdateUi(mortar);

        _popup.PopupPredicted(
            Loc.GetString("wh40k-mortar-fold-finish-self", ("mortar", mortar)),
            Loc.GetString("wh40k-mortar-fold-finish-others", ("user", user), ("mortar", mortar)),
            mortar,
            user);
    }

    private void OnAnchorStateChanged(Entity<WH40KMortarComponent> mortar, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored || !mortar.Comp.Deployed)
            return;

        mortar.Comp.Deployed = false;
        Dirty(mortar);

        if (_fixture.GetFixtureOrNull(mortar, mortar.Comp.FixtureId) is { } fixture)
            _physics.SetHard(mortar, fixture, false);

        _appearance.SetData(mortar, WH40KMortarVisualLayers.State, WH40KMortarVisuals.Item);
        _ui.CloseUi(mortar.Owner, WH40KMortarUiKey.Key);
        UpdateUi(mortar);
    }

    private void OnUiOpened(Entity<WH40KMortarComponent> mortar, ref BoundUIOpenedEvent args)
    {
        NormalizeDeployedState(mortar);
        if (!CanOperateMortar(mortar))
        {
            _ui.CloseUi(mortar.Owner, WH40KMortarUiKey.Key, args.Actor);
            return;
        }

        UpdateUi(mortar);
    }

    private void OnSetTarget(Entity<WH40KMortarComponent> mortar, ref WH40KMortarSetTargetMessage args)
    {
        if (!CanOperateMortar(mortar))
        {
            _ui.CloseUi(mortar.Owner, WH40KMortarUiKey.Key, args.Actor);
            return;
        }

        var target = new Vector2i(ClampSigned(args.Target.X, mortar.Comp.MaxTarget), ClampSigned(args.Target.Y, mortar.Comp.MaxTarget));

        mortar.Comp.Target = target;
        Dirty(mortar);
        UpdateUi(mortar);

        _popup.PopupEntity(Loc.GetString("wh40k-mortar-target-set"), mortar, args.Actor);
    }

    private void OnSetDial(Entity<WH40KMortarComponent> mortar, ref WH40KMortarSetDialMessage args)
    {
        if (!CanOperateMortar(mortar))
        {
            _ui.CloseUi(mortar.Owner, WH40KMortarUiKey.Key, args.Actor);
            return;
        }

        var dial = new Vector2i(ClampSigned(args.Dial.X, mortar.Comp.MaxDial), ClampSigned(args.Dial.Y, mortar.Comp.MaxDial));

        mortar.Comp.Dial = dial;
        Dirty(mortar);
        UpdateUi(mortar);

        _popup.PopupEntity(Loc.GetString("wh40k-mortar-dial-set"), mortar, args.Actor);
    }

    private void OnSetLinkedDesignator(Entity<WH40KMortarComponent> mortar, ref WH40KMortarSetLinkedDesignatorMessage args)
    {
        if (!CanOperateMortar(mortar))
        {
            _ui.CloseUi(mortar.Owner, WH40KMortarUiKey.Key, args.Actor);
            return;
        }

        var designatorId = args.DesignatorId > 0 ? args.DesignatorId : (int?) null;
        mortar.Comp.LinkedDesignatorId = designatorId;
        Dirty(mortar);
        UpdateUi(mortar);

        if (designatorId == null)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-mortar-laser-link-cleared"), mortar, args.Actor);
            return;
        }

        _popup.PopupEntity(
            Loc.GetString("wh40k-mortar-laser-link-set", ("id", designatorId.Value)),
            mortar,
            args.Actor);
    }

    private void OnToggleLaserMode(Entity<WH40KMortarComponent> mortar, ref WH40KMortarToggleLaserModeMessage args)
    {
        if (!CanOperateMortar(mortar))
        {
            _ui.CloseUi(mortar.Owner, WH40KMortarUiKey.Key, args.Actor);
            return;
        }

        mortar.Comp.LaserTargetingMode = !mortar.Comp.LaserTargetingMode;
        Dirty(mortar);
        UpdateUi(mortar);

        var loc = mortar.Comp.LaserTargetingMode
            ? "wh40k-mortar-laser-mode-enabled"
            : "wh40k-mortar-laser-mode-disabled";
        _popup.PopupEntity(Loc.GetString(loc), mortar, args.Actor);
    }

    private void OnInteractUsing(Entity<WH40KMortarComponent> mortar, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!CanOperateMortar(mortar))
            return;

        if (TryComp(args.Used, out WH40KRangefinderComponent? rangefinder))
        {
            args.Handled = true;

            if (rangefinder.Id is not { } designatorId || designatorId <= 0)
            {
                if (TryTakeUserPopupCooldown(args.User, "wh40k-mortar-laser-link-invalid"))
                    _popup.PopupEntity(Loc.GetString("wh40k-mortar-laser-link-invalid"), mortar, args.User);
                return;
            }

            mortar.Comp.LinkedDesignatorId = designatorId;
            Dirty(mortar);
            UpdateUi(mortar);

            _popup.PopupEntity(Loc.GetString("wh40k-mortar-laser-link-set", ("id", designatorId)), mortar, args.User);
            return;
        }

        if (!TryComp(args.Used, out WH40KMortarShellComponent? shell))
            return;

        args.Handled = true;

        if (TryGetLoadedShell(mortar, out _, out _))
        {
            if (TryTakeUserPopupCooldown(args.User, "wh40k-mortar-shell-busy"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-shell-busy", ("mortar", mortar)), mortar, args.User);

            return;
        }

        var ev = new WH40KLoadMortarShellDoAfterEvent();
        var doAfter = new DoAfterArgs(EntityManager, args.User, shell.LoadDelay, ev, mortar, mortar, args.Used)
        {
            BreakOnMove = true,
            BreakOnHandChange = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        _audio.PlayPvs(mortar.Comp.ReloadSound, mortar);
        _popup.PopupPredicted(
            Loc.GetString("wh40k-mortar-shell-load-start-self", ("shell", args.Used), ("mortar", mortar)),
            Loc.GetString("wh40k-mortar-shell-load-start-others", ("user", args.User), ("shell", args.Used), ("mortar", mortar)),
            mortar,
            args.User);
    }

    private void OnLoadDoAfter(Entity<WH40KMortarComponent> mortar, ref WH40KLoadMortarShellDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } shellId)
            return;

        args.Handled = true;

        if (!TryComp(shellId, out WH40KMortarShellComponent? _))
            return;

        if (TryGetLoadedShell(mortar, out _, out _))
        {
            if (TryTakeUserPopupCooldown(args.User, "wh40k-mortar-shell-busy"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-shell-busy", ("mortar", mortar)), mortar, args.User);

            return;
        }

        var container = _container.EnsureContainer<Container>(mortar, mortar.Comp.ContainerId);
        if (!_container.Insert(shellId, container))
        {
            if (TryTakeUserPopupCooldown(args.User, "wh40k-mortar-cant-insert"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-cant-insert", ("shell", shellId), ("mortar", mortar)), mortar, args.User);

            return;
        }

        _popup.PopupPredicted(
            Loc.GetString("wh40k-mortar-shell-load-finish-self", ("shell", shellId), ("mortar", mortar)),
            Loc.GetString("wh40k-mortar-shell-load-finish-others", ("user", args.User), ("shell", shellId), ("mortar", mortar)),
            mortar,
            args.User);

        UpdateUi(mortar);
    }

    private void OnFire(Entity<WH40KMortarComponent> mortar, ref WH40KMortarFireMessage args)
    {
        if (!CanOperateMortar(mortar))
        {
            _ui.CloseUi(mortar.Owner, WH40KMortarUiKey.Key, args.Actor);
            return;
        }

        if (!TryGetLoadedShell(mortar, out var shellId, out _))
        {
            if (TryTakeUserPopupCooldown(args.Actor, "wh40k-mortar-no-shell-loaded"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-no-shell-loaded", ("mortar", mortar)), mortar, args.Actor);

            UpdateUi(mortar);
            return;
        }

        if (!TryComp(shellId, out WH40KMortarShellComponent? shell))
        {
            UpdateUi(mortar);
            return;
        }

        if (!TryPrepareShot(mortar, args.Actor, out var targetCoordinates))
        {
            UpdateUi(mortar);
            return;
        }

        var now = _timing.CurTime;
        mortar.Comp.LastFiredAt = now;
        Dirty(mortar);

        var landAt = now + shell.TravelDelay + shell.ImpactDelay;
        var warnAt = landAt - shell.IncomingSoundLeadTime;
        if (warnAt <= now)
            warnAt = now + TimeSpan.FromSeconds(0.1);

        var active = new WH40KActiveMortarShellComponent
        {
            Coordinates = _transform.ToCoordinates(targetCoordinates),
            WarnAt = warnAt,
            ImpactWarnAt = now + shell.TravelDelay + shell.ImpactWarningDelay,
            LandAt = landAt,
            WarnSound = mortar.Comp.TravelSound,
        };

        AddComp(shellId, active);

        _adminLogs.Add(LogType.Explosion, LogImpact.High, $"WH40K mortar {ToPrettyString(mortar)} fired {ToPrettyString(shellId)} by {ToPrettyString(args.Actor)} to {targetCoordinates}");

        _audio.PlayPvs(mortar.Comp.FireSound, mortar);
        RaiseNetworkEvent(new WH40KMortarFiredEvent(GetNetEntity(mortar)), Filter.Pvs(mortar));
        _popup.PopupEntity(Loc.GetString("wh40k-mortar-shell-fire", ("mortar", mortar)), mortar, PopupType.MediumCaution);
        UpdateUi(mortar);
    }

    private bool TryPrepareShot(Entity<WH40KMortarComponent> mortar, EntityUid user, out MapCoordinates targetCoordinates)
    {
        targetCoordinates = default;

        if (!CanOperateMortar(mortar))
            return false;

        var now = _timing.CurTime;
        if (now < mortar.Comp.LastFiredAt + mortar.Comp.FireDelay)
        {
            if (TryTakeUserPopupCooldown(user, "wh40k-mortar-fire-cooldown"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-fire-cooldown", ("mortar", mortar)), mortar, user);

            return false;
        }

        var mortarCoordinates = _transform.GetMapCoordinates(mortar);
        if (!TryGetGroundTile(mortarCoordinates, out var mortarGridUid, out var mortarGrid, out var mortarTile))
        {
            if (TryTakeUserPopupCooldown(user, "wh40k-mortar-bad-origin"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-bad-origin", ("mortar", mortar)), mortar, user);

            return false;
        }

        if (IsRoovedTile(mortarGridUid, mortarGrid, mortarTile))
        {
            if (TryTakeUserPopupCooldown(user, "wh40k-mortar-origin-roofed"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-origin-roofed", ("mortar", mortar)), mortar, user);

            return false;
        }

        var resolvedTarget = mortar.Comp.Target;
        if (mortar.Comp.LaserTargetingMode)
        {
            if (mortar.Comp.LinkedDesignatorId is not { } linkedDesignatorId || linkedDesignatorId <= 0)
            {
                if (TryTakeUserPopupCooldown(user, "wh40k-mortar-laser-no-designator"))
                    _popup.PopupEntity(Loc.GetString("wh40k-mortar-laser-no-designator"), mortar, user);
                return false;
            }

            EntityUid designatorGridUid = default;
            Vector2i designatorTile = default;
            var resolvedLinkedTarget = _rangefinder.TryGetDesignatorTarget(linkedDesignatorId, out designatorGridUid, out _, out designatorTile) ||
                                       _flareSignal.TryGetSignalTarget(linkedDesignatorId, user, out designatorGridUid, out _, out designatorTile);
            if (!resolvedLinkedTarget)
            {
                if (TryTakeUserPopupCooldown(user, "wh40k-mortar-laser-no-target"))
                    _popup.PopupEntity(Loc.GetString("wh40k-mortar-laser-no-target", ("id", linkedDesignatorId)), mortar, user);
                return false;
            }

            if (designatorGridUid != mortarGridUid)
            {
                if (TryTakeUserPopupCooldown(user, "wh40k-mortar-laser-different-grid"))
                    _popup.PopupEntity(Loc.GetString("wh40k-mortar-laser-different-grid"), mortar, user);
                return false;
            }

            resolvedTarget = designatorTile;
        }

        if (!TryValidateAimSelection(
                mortar,
                mortarGridUid,
                mortarGrid,
                mortarTile,
                resolvedTarget,
                mortar.Comp.Dial,
                user,
                out targetCoordinates))
            return false;

        if (mortar.Comp.UseRandomScatter &&
            !mortar.Comp.LaserTargetingMode &&
            mortar.Comp.FireRandomOffset.Length > 0)
        {
            var dx = _random.Pick(mortar.Comp.FireRandomOffset);
            var dy = _random.Pick(mortar.Comp.FireRandomOffset);
            var scattered = targetCoordinates.Offset(new Vector2(dx, dy));

            if (TryGetGroundTile(scattered, out var scatterGridUid, out var scatterGrid, out var scatterTile) &&
                !IsRoovedTile(scatterGridUid, scatterGrid, scatterTile))
            {
                targetCoordinates = scattered;
            }
        }

        return true;
    }

    private bool TryValidateAimSelection(
        Entity<WH40KMortarComponent> mortar,
        EntityUid mortarGridUid,
        MapGridComponent mortarGrid,
        Vector2i mortarTile,
        Vector2i target,
        Vector2i dial,
        EntityUid user,
        out MapCoordinates targetCoordinates)
    {
        targetCoordinates = default;

        if (target == Vector2i.Zero)
        {
            if (TryTakeUserPopupCooldown(user, "wh40k-mortar-not-aimed"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-not-aimed", ("mortar", mortar)), mortar, user);

            return false;
        }

        var finalTile = target + dial;
        if (!_map.TryGetTileRef(mortarGridUid, mortarGrid, finalTile, out var tileRef) ||
            tileRef.Tile.IsEmpty ||
            _turf.IsSpace(tileRef))
        {
            if (TryTakeUserPopupCooldown(user, "wh40k-mortar-target-invalid"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-target-invalid"), mortar, user);

            return false;
        }

        var delta = finalTile - mortarTile;
        var distance = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        if (distance < mortar.Comp.MinimumRange)
        {
            if (TryTakeUserPopupCooldown(user, "wh40k-mortar-target-too-close"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-target-too-close"), mortar, user);

            return false;
        }

        if (distance > mortar.Comp.MaximumRange)
        {
            if (TryTakeUserPopupCooldown(user, "wh40k-mortar-target-too-far"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-target-too-far"), mortar, user);

            return false;
        }

        if (IsRoovedTile(mortarGridUid, mortarGrid, finalTile))
        {
            if (TryTakeUserPopupCooldown(user, "wh40k-mortar-target-roofed"))
                _popup.PopupEntity(Loc.GetString("wh40k-mortar-target-roofed"), mortar, user);

            return false;
        }

        targetCoordinates = _transform.ToMapCoordinates(_map.GridTileToLocal(mortarGridUid, mortarGrid, finalTile));
        return true;
    }

    private bool TryGetGroundTile(
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

        if (!_mapManager.TryFindGridAt(coordinates, out gridUid, out var maybeGrid) || maybeGrid == null)
            return false;

        grid = maybeGrid;
        tileIndices = _map.WorldToTile(gridUid, grid, coordinates.Position);
        if (!_map.TryGetTileRef(gridUid, grid, tileIndices, out var tileRef))
            return false;

        return !tileRef.Tile.IsEmpty && !_turf.IsSpace(tileRef);
    }

    private bool IsRoovedTile(EntityUid gridUid, MapGridComponent grid, Vector2i tileIndices)
    {
        if (HasComp<ImplicitRoofComponent>(gridUid))
            return true;

        if (!TryComp<RoofComponent>(gridUid, out var roofComp))
            return false;

        return _roof.IsRooved((gridUid, grid, roofComp), tileIndices);
    }

    private void OnDestruction(Entity<WH40KMortarComponent> mortar, ref DestructionEventArgs args)
    {
        if (!_container.TryGetContainer(mortar, mortar.Comp.ContainerId, out var container))
            return;

        var containedEntities = new List<EntityUid>(container.ContainedEntities);
        foreach (var contained in containedEntities)
        {
            _container.Remove(contained, container);
            QueueDel(contained);
        }
    }

    private bool TryGetLoadedShell(
        Entity<WH40KMortarComponent> mortar,
        out EntityUid shell,
        out WH40KMortarShellComponent? shellComp)
    {
        shell = default;
        shellComp = null;

        if (!_container.TryGetContainer(mortar, mortar.Comp.ContainerId, out var container))
            return false;

        foreach (var contained in container.ContainedEntities)
        {
            if (!TryComp<WH40KMortarShellComponent>(contained, out var containedShell))
                continue;

            if (HasComp<WH40KActiveMortarShellComponent>(contained))
                continue;

            shell = contained;
            shellComp = containedShell;
            return true;
        }

        return false;
    }

    private bool TryTakeUserPopupCooldown(EntityUid user, string key, TimeSpan? cooldown = null)
    {
        var now = _timing.CurTime;
        var resolvedCooldown = cooldown ?? PopupSpamCooldown;
        var userKey = (user, key);

        if (_popupCooldowns.TryGetValue(userKey, out var nextAllowedAt) && nextAllowedAt > now)
            return false;

        _popupCooldowns[userKey] = now + resolvedCooldown;
        return true;
    }

    private bool CanOperateMortar(Entity<WH40KMortarComponent> mortar)
    {
        if (!mortar.Comp.Deployed)
            return false;

        var xform = Transform(mortar);
        if (!xform.Anchored)
            return false;

        return !_container.IsEntityInContainer(mortar);
    }

    private void NormalizeDeployedState(Entity<WH40KMortarComponent> mortar)
    {
        if (!mortar.Comp.Deployed)
            return;

        var xform = Transform(mortar);
        if (xform.Anchored && !_container.IsEntityInContainer(mortar))
            return;

        mortar.Comp.Deployed = false;
        Dirty(mortar);

        if (_fixture.GetFixtureOrNull(mortar, mortar.Comp.FixtureId) is { } fixture)
            _physics.SetHard(mortar, fixture, false);

        _appearance.SetData(mortar, WH40KMortarVisualLayers.State, WH40KMortarVisuals.Item);
        _ui.CloseUi(mortar.Owner, WH40KMortarUiKey.Key);
        UpdateUi(mortar);
    }

    private void UpdateUi(Entity<WH40KMortarComponent> mortar)
    {
        var position = Vector2i.Zero;
        EntityUid? mortarGridUid = null;
        var mortarCoordinates = _transform.GetMapCoordinates(mortar);
        if (TryGetGroundTile(mortarCoordinates, out var currentGridUid, out _, out var tileIndices))
        {
            position = tileIndices;
            mortarGridUid = currentGridUid;
        }

        var deployed = CanOperateMortar(mortar);
        var loaded = TryGetLoadedShell(mortar, out _, out var loadedShellComp);
        var loadedShellType = loadedShellComp?.UiShellType ?? string.Empty;
        var cooldownRemaining = GetCooldownRemainingSeconds(mortar, _timing.CurTime);
        var resolvedLinkedDesignatorId = mortar.Comp.LinkedDesignatorId ?? 0;
        var linkedDesignatorAssigned = resolvedLinkedDesignatorId > 0;

        var linkedTarget = Vector2i.Zero;
        var hasLinkedTarget = false;
        var linkedTargetSameGrid = false;
        if (linkedDesignatorAssigned &&
            _rangefinder.TryGetDesignatorTarget(resolvedLinkedDesignatorId, out var designatorGridUid, out _, out var designatorTile))
        {
            linkedTarget = designatorTile;
            hasLinkedTarget = true;
            linkedTargetSameGrid = mortarGridUid != null && designatorGridUid == mortarGridUid.Value;
        }

        var state = new WH40KMortarBuiState(
            mortar.Comp.Target,
            mortar.Comp.Dial,
            position,
            linkedTarget,
            mortar.Comp.MaxTarget,
            mortar.Comp.MaxDial,
            mortar.Comp.MinimumRange,
            mortar.Comp.MaximumRange,
            Math.Max(1, (int) Math.Ceiling(mortar.Comp.FireDelay.TotalSeconds)),
            cooldownRemaining,
            resolvedLinkedDesignatorId,
            deployed,
            loaded,
            mortar.Comp.LaserTargetingMode,
            linkedDesignatorAssigned,
            hasLinkedTarget,
            linkedTargetSameGrid,
            loadedShellType);

        _ui.SetUiState(mortar.Owner, WH40KMortarUiKey.Key, state);
    }

    private int GetCooldownRemainingSeconds(Entity<WH40KMortarComponent> mortar, TimeSpan now)
    {
        var nextReadyAt = mortar.Comp.LastFiredAt + mortar.Comp.FireDelay;
        if (nextReadyAt <= now)
            return 0;

        return Math.Max(0, (int) Math.Ceiling((nextReadyAt - now).TotalSeconds));
    }

    private static int ClampSigned(int value, int absLimit)
    {
        absLimit = Math.Abs(absLimit);
        return Math.Clamp(value, -absLimit, absLimit);
    }
}
