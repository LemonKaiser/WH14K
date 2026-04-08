using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Popups;
using Content.Server._WH40K.Localizations;
using Content.Shared._WH40K.Tank;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Actions.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Repairable;
using Content.Shared.Tools.Systems;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Tank;

public sealed class WH40KTankSystem : EntitySystem
{
    private const float MotionVisualThreshold = 0.05f;
    private const float DiagnosticsRefreshSeconds = 0.25f;

    private static readonly WH40KTankCrewRole[] EntryOrder =
    [
        WH40KTankCrewRole.Driver,
        WH40KTankCrewRole.Gunner,
        WH40KTankCrewRole.Commander,
        WH40KTankCrewRole.Loader,
    ];

    private static readonly WH40KTankModuleType[] ModuleOrder =
    [
        WH40KTankModuleType.Engine,
        WH40KTankModuleType.Tracks,
        WH40KTankModuleType.Turret,
        WH40KTankModuleType.MainGun,
        WH40KTankModuleType.Coaxial,
    ];

    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly WH40KPlayerCultureTracker _culture = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly RotateToFaceSystem _rotateToFace = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedToolSystem _toolSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly Robust.Server.GameObjects.UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KTankComponent, MapInitEvent>(OnTankMapInit);
        SubscribeLocalEvent<WH40KTankComponent, InteractHandEvent>(OnTankInteractHand);
        SubscribeLocalEvent<WH40KTankComponent, InteractUsingEvent>(OnTankInteractUsing);
        SubscribeLocalEvent<WH40KTankComponent, WH40KTankEnterDoAfterEvent>(OnTankEnterDoAfter);
        SubscribeLocalEvent<WH40KTankComponent, WH40KTankExitDoAfterEvent>(OnTankExitDoAfter);
        SubscribeLocalEvent<WH40KTankComponent, ExaminedEvent>(OnTankExamined);
        SubscribeLocalEvent<WH40KTankComponent, WH40KTankAimActionEvent>(OnTankAimAction);
        SubscribeLocalEvent<WH40KTankComponent, WH40KTankFireMainGunActionEvent>(OnTankFireMainGunAction);
        SubscribeLocalEvent<WH40KTankComponent, WH40KTankFireCoaxialActionEvent>(OnTankFireCoaxialAction);
        SubscribeLocalEvent<WH40KTankComponent, WH40KTankReloadMainGunActionEvent>(OnTankReloadMainGunAction);
        SubscribeLocalEvent<WH40KTankComponent, WH40KTankReloadCoaxialActionEvent>(OnTankReloadCoaxialAction);
        SubscribeLocalEvent<WH40KTankComponent, BoundUIOpenedEvent>(OnTankUiOpened);
        SubscribeLocalEvent<WH40KTankComponent, DamageChangedEvent>(OnTankDamageChanged);
        SubscribeLocalEvent<WH40KTankComponent, DestructionEventArgs>(OnTankDestroyed);
        SubscribeLocalEvent<WH40KTankComponent, GetVerbsEvent<AlternativeVerb>>(OnTankGetAlternativeVerbs);
        SubscribeLocalEvent<WH40KTankComponent, EntityTerminatingEvent>(OnTankTerminating);
        SubscribeNetworkEvent<WH40KTankAimRequestEvent>(OnTankAimRequest);
        SubscribeNetworkEvent<WH40KTankFireMainGunRequestEvent>(OnTankFireMainGunRequest);

        SubscribeLocalEvent<WH40KTankStationComponent, StrappedEvent>(OnStationStrapped);
        SubscribeLocalEvent<WH40KTankStationComponent, UnstrapAttemptEvent>(OnStationUnstrapAttempt);
        SubscribeLocalEvent<WH40KTankStationComponent, UnstrappedEvent>(OnStationUnstrapped);

        SubscribeLocalEvent<MobStateChangedEvent>(OnCrewMobStateChanged);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WH40KTankComponent, PhysicsComponent, InputMoverComponent, WH40KTankDriveComponent>();

        while (query.MoveNext(out var uid, out var tank, out var physics, out var mover, out var drive))
        {
            if (!TryComp(uid, out WH40KTankEngineComponent? engine) || !TryComp(uid, out WH40KTankFuelComponent? fuel))
                continue;

            EnsureTankHierarchy((uid, tank));
            UpdateTankMotion(uid, tank, engine, fuel, drive, physics, mover, frameTime);
            UpdateTankReloads((uid, tank));
            UpdateTankTurretAndWeapons((uid, tank), frameTime);
            UpdateTankDiagnosticsUi((uid, tank));
        }
    }

    private void OnTankMapInit(Entity<WH40KTankComponent> tank, ref MapInitEvent args)
    {
        EnsureTankHierarchy(tank);
        EnsureGunnerActionEntities(tank);
        ResetModuleDamage(tank.Comp);
        ResetReloadState(tank.Comp);
        ResetTankAudioState(tank.Comp);
        RefreshCrewRegistry(tank);
        tank.Comp.NextUiRefreshAt = TimeSpan.Zero;
        SetTrackVisual(tank.Owner, tank.Comp, WH40KTankVisualState.Idle, force: true);
    }

    private void OnTankGetAlternativeVerbs(Entity<WH40KTankComponent> tank, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract)
            return;

        RefreshCrewRegistry(tank);

        using var scope = _culture.CreateScope(args.User);

        if (!IsCurrentCrew(tank.Comp, args.User) &&
            (!TryComp<BuckleComponent>(args.User, out var buckleComp) || !buckleComp.Buckled))
        {
            foreach (var role in EntryOrder)
            {
                if (!TryGetAvailableStation(tank, role, out _))
                    continue;

                var reservedRole = role;
                var actor = args.User;
                args.Verbs.Add(new AlternativeVerb
                {
                    Text = Loc.GetString(
                        "wh40k-tank-entry-verb",
                        ("role", Loc.GetString(GetRoleLocKey(reservedRole)))),
                    Priority = 40,
                    Act = () => TryStartEntryDoAfter(tank, actor, reservedRole),
                });
            }
        }

        if (IsCurrentCrew(tank.Comp, args.User))
        {
            var diagnosticUser = args.User;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wh40k-tank-diagnostics-verb"),
                Priority = 30,
                Act = () =>
                {
                    if (_ui.TryToggleUi(tank.Owner, WH40KTankUiKey.Key, diagnosticUser))
                    {
                        tank.Comp.NextUiRefreshAt = TimeSpan.Zero;
                        UpdateTankDiagnosticsUi(tank, force: true);
                    }
                }
            });
        }

        if (tank.Comp.DriverOccupant != args.User)
            return;

        if (!TryComp(tank.Owner, out WH40KTankEngineComponent? engine) ||
            !TryComp(tank.Owner, out WH40KTankFuelComponent? fuel))
        {
            return;
        }

        var driverUser = args.User;

        if (engine.State == WH40KTankEngineState.Running)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wh40k-tank-engine-verb-stop"),
                Act = () => StopEngine(tank.Owner, engine, driverUser, popup: true)
            });
            return;
        }

        var startVerb = new AlternativeVerb
        {
            Text = Loc.GetString("wh40k-tank-engine-verb-start"),
            Act = () => TryStartEngine(tank.Owner, tank.Comp, engine, fuel, driverUser),
        };

        if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.Engine))
        {
            startVerb.Disabled = true;
            startVerb.Message = Loc.GetString("wh40k-tank-engine-disabled");
        }
        else if (!HasFuelForStartup(tank.Owner, fuel))
        {
            startVerb.Disabled = true;
            startVerb.Message = Loc.GetString("wh40k-tank-engine-empty");
        }

        args.Verbs.Add(startVerb);
    }

    private void OnTankInteractHand(Entity<WH40KTankComponent> tank, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        EnsureTankHierarchy(tank);

        if (TryComp<BuckleComponent>(args.User, out var buckleComp) &&
            buckleComp.BuckledTo is { } currentStation &&
            TryComp<WH40KTankStationComponent>(currentStation, out var stationComp) &&
            stationComp.Tank == tank.Owner)
        {
            args.Handled = TryStartExitDoAfter(tank, args.User, currentStation);
            return;
        }

        args.Handled = TryStartEntryDoAfter(tank, args.User);
    }

    private void OnTankEnterDoAfter(Entity<WH40KTankComponent> tank, ref WH40KTankEnterDoAfterEvent args)
    {
        if (args.Used is not { } stationUid)
            return;

        if (args.Cancelled || args.Handled)
        {
            ClearPendingEntryReservation(stationUid, args.User);
            return;
        }

        args.Handled = true;
        CompleteTankEntry(tank, args.User, stationUid);
    }

    private void OnTankExitDoAfter(Entity<WH40KTankComponent> tank, ref WH40KTankExitDoAfterEvent args)
    {
        if (args.Used is not { } stationUid)
            return;

        if (args.Cancelled || args.Handled)
        {
            ClearPendingExit(stationUid, args.User);
            return;
        }

        args.Handled = true;
        CompleteTankExit(args.User, stationUid);
    }

    private void OnTankInteractUsing(Entity<WH40KTankComponent> tank, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            !TryComp<RepairableComponent>(tank.Owner, out var repairable) ||
            !_toolSystem.HasQuality(args.Used, repairable.QualityNeeded))
        {
            return;
        }

        if (!TryComp<DamageableComponent>(tank.Owner, out var damageable) ||
            _damageable.GetTotalDamage((tank.Owner, damageable)) <= FixedPoint2.Zero)
        {
            return;
        }

        if (TryComp<WH40KTankEngineComponent>(tank.Owner, out var engine) &&
            engine.State == WH40KTankEngineState.Running)
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.User, "wh40k-tank-repair-engine-running"), tank.Owner, args.User);
            args.Handled = true;
            return;
        }

        if (IsTankMoving(tank.Owner))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.User, "wh40k-tank-repair-moving"), tank.Owner, args.User);
            args.Handled = true;
        }
    }

    private void OnTankExamined(Entity<WH40KTankComponent> tank, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        RefreshCrewRegistry(tank);

        using var scope = _culture.CreateScope(args.Examiner);

        using (args.PushGroup(nameof(WH40KTankComponent)))
        {
            args.PushMarkup(Loc.GetString("wh40k-tank-examine-header"));

            foreach (var role in EntryOrder)
            {
                var roleName = Loc.GetString(GetRoleLocKey(role));
                var occupant = GetOccupantUid(tank.Comp, role);

                if (occupant is { } occupantUid && Exists(occupantUid))
                {
                    args.PushMarkup(Loc.GetString(
                        "wh40k-tank-examine-occupied",
                        ("role", roleName),
                        ("occupant", Name(occupantUid))));
                }
                else
                {
                    args.PushMarkup(Loc.GetString(
                        "wh40k-tank-examine-empty",
                        ("role", roleName)));
                }
            }
        }
    }

    private void OnTankUiOpened(Entity<WH40KTankComponent> tank, ref BoundUIOpenedEvent args)
    {
        RefreshCrewRegistry(tank);
        if (!IsCurrentCrew(tank.Comp, args.Actor))
        {
            _ui.CloseUi(tank.Owner, WH40KTankUiKey.Key, args.Actor);
            return;
        }

        tank.Comp.NextUiRefreshAt = TimeSpan.Zero;
        UpdateTankDiagnosticsUi(tank, force: true);
    }

    private void OnTankDamageChanged(Entity<WH40KTankComponent> tank, ref DamageChangedEvent args)
    {
        if (args.DamageDelta == null)
            return;

        if (args.DamageIncreased)
        {
            var damage = GetPositiveDamageAmount(args.DamageDelta);
            if (damage <= 0f)
                return;

            ApplyModuleDamage(tank, damage);
        }
        else
        {
            var repair = GetHealingAmount(args.DamageDelta);
            if (repair <= 0f)
                return;

            ApplyModuleRepair(tank, repair);
        }

        EnforceModuleFailures(tank);
        tank.Comp.NextUiRefreshAt = TimeSpan.Zero;
    }

    private void OnTankDestroyed(Entity<WH40KTankComponent> tank, ref DestructionEventArgs args)
    {
        StopTankAudio(tank.Comp);
        _audio.PlayPvs(tank.Comp.DestroyedSound, tank.Owner);
    }

    private void OnTankTerminating(Entity<WH40KTankComponent> tank, ref EntityTerminatingEvent args)
    {
        StopTankAudio(tank.Comp);

        RefreshCrewRegistry(tank);
        TryUnbuckleOccupant(tank.Comp.DriverOccupant);
        TryUnbuckleOccupant(tank.Comp.GunnerOccupant);
        TryUnbuckleOccupant(tank.Comp.CommanderOccupant);
        TryUnbuckleOccupant(tank.Comp.LoaderOccupant);

        foreach (var child in EnumerateChildren(tank.Comp))
        {
            if (child is not { } childUid || !Exists(childUid))
                continue;

            QueueDel(childUid);
        }
    }

    private void OnStationStrapped(Entity<WH40KTankStationComponent> station, ref StrappedEvent args)
    {
        station.Comp.PendingEntrant = null;
        station.Comp.PendingExitOccupant = null;

        if (station.Comp.Tank is not { } tankUid || !TryComp<WH40KTankComponent>(tankUid, out var tankComp))
            return;

        GrantDiagnosticsAction((tankUid, tankComp), station, args.Buckle.Owner);
        RefreshCrewRegistry((tankUid, tankComp));

        if (station.Comp.Role == WH40KTankCrewRole.Driver)
            SetDriverRelay(tankUid, args.Buckle.Owner);

        if (station.Comp.Role == WH40KTankCrewRole.Gunner)
            GrantGunnerActions((tankUid, tankComp), args.Buckle.Owner);

        if (station.Comp.Role == WH40KTankCrewRole.Loader)
            GrantLoaderActions((tankUid, tankComp), args.Buckle.Owner);

        tankComp.NextUiRefreshAt = TimeSpan.Zero;
        UpdateTankDiagnosticsUi((tankUid, tankComp), force: true);
    }

    private void OnStationUnstrapAttempt(Entity<WH40KTankStationComponent> station, ref UnstrapAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (args.User is not { } user)
            return;

        if (user != args.Buckle.Owner)
        {
            args.Cancelled = true;
            return;
        }

        if (station.Comp.PendingExitOccupant != args.Buckle.Owner)
        {
            if (station.Comp.Tank is not { } tankUid || !TryComp<WH40KTankComponent>(tankUid, out var tankComp))
            {
                args.Cancelled = true;
                return;
            }

            if (tankComp.ExitDelaySeconds > 0f)
            {
                args.Cancelled = true;
                TryStartExitDoAfter((tankUid, tankComp), user, station.Owner);
                return;
            }

            station.Comp.PendingExitOccupant = args.Buckle.Owner;
        }

        if (!TryGetExitCoordinates(station, out var exitCoordinates))
            return;

        if (!IsExitBlocked(station, args.Buckle.Owner, exitCoordinates))
            return;

        args.Cancelled = true;
        station.Comp.PendingExitOccupant = null;
        _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-tank-exit-blocked"), station.Comp.Tank ?? station.Owner, user);
    }

    private void OnStationUnstrapped(Entity<WH40KTankStationComponent> station, ref UnstrappedEvent args)
    {
        station.Comp.PendingExitOccupant = null;

        if (station.Comp.Tank is { } tankUid && TryComp<WH40KTankComponent>(tankUid, out var tankComp))
        {
            RemoveDiagnosticsAction(station, args.Buckle.Owner);

            if (station.Comp.Role == WH40KTankCrewRole.Gunner)
            {
                RemoveGunnerActions(tankComp, args.Buckle.Owner);
                ClearGunnerCombatState(tankComp);
            }

            if (station.Comp.Role == WH40KTankCrewRole.Driver)
            {
                ClearDriverRelay(args.Buckle.Owner, tankUid);

                if (TryComp<WH40KTankEngineComponent>(tankUid, out var engine))
                    StopEngine(tankUid, engine);
            }

            if (station.Comp.Role == WH40KTankCrewRole.Loader)
            {
                RemoveLoaderActions(tankComp, args.Buckle.Owner);
                ResetReloadState(tankComp);
            }

            RefreshCrewRegistry((tankUid, tankComp));
            tankComp.NextUiRefreshAt = TimeSpan.Zero;
            UpdateTankDiagnosticsUi((tankUid, tankComp), force: true);
        }

        MoveOccupantToExitIfClear(station, args.Buckle.Owner);
    }

    private void OnCrewMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        if (!TryComp<BuckleComponent>(args.Target, out var buckleComp))
            return;

        if (!buckleComp.Buckled || buckleComp.BuckledTo is not { } stationUid)
            return;

        if (!HasComp<WH40KTankStationComponent>(stationUid))
            return;

        _buckle.Unbuckle((args.Target, buckleComp), null);
    }

    private bool TryStartEntryDoAfter(Entity<WH40KTankComponent> tank, EntityUid user, WH40KTankCrewRole? preferredRole = null)
    {
        if (!TryComp<BuckleComponent>(user, out var buckleComp) || buckleComp.Buckled)
            return false;

        if (HasPendingEntryReservation(tank.Comp, user))
            return true;

        EntityUid stationUid;
        var reserved = preferredRole is { } role
            ? TryReserveAvailableStation(tank, user, role, out stationUid)
            : TryReserveFirstAvailableStation(tank, user, out stationUid);

        if (!reserved)
        {
            _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-tank-entry-full"), tank.Owner, user);
            return true;
        }

        if (tank.Comp.EntryDelaySeconds <= 0f)
        {
            CompleteTankEntry(tank, user, stationUid);
            return true;
        }

        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            tank.Comp.EntryDelaySeconds,
            new WH40KTankEnterDoAfterEvent(),
            tank.Owner,
            target: tank.Owner,
            used: stationUid)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            DistanceThreshold = 2f,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            ClearPendingEntryReservation(stationUid, user);
            return false;
        }

        _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-tank-entry-start"), tank.Owner, user);
        return true;
    }

    private void CompleteTankEntry(Entity<WH40KTankComponent> tank, EntityUid user, EntityUid stationUid)
    {
        try
        {
            if (!Exists(tank.Owner) || !Exists(user) || !Exists(stationUid))
                return;

            if (!TryComp<WH40KTankStationComponent>(stationUid, out var stationComp) ||
                stationComp.Tank != tank.Owner ||
                stationComp.PendingEntrant != user ||
                !TryComp<StrapComponent>(stationUid, out var strapComp) ||
                !strapComp.Enabled ||
                strapComp.BuckledEntities.Count != 0 ||
                !TryComp<BuckleComponent>(user, out var userBuckle) ||
                userBuckle.Buckled)
            {
                if (Exists(user) && Exists(tank.Owner))
                    _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-tank-entry-full"), tank.Owner, user);

                return;
            }

            var originalCoords = _transform.GetMapCoordinates(user);
            var stationCoords = _transform.GetMapCoordinates(stationUid);
            _transform.SetMapCoordinates(user, stationCoords);

            if (_buckle.TryBuckle(user, user, stationUid, userBuckle, popup: true))
                return;

            _transform.SetMapCoordinates(user, originalCoords);
        }
        finally
        {
            ClearPendingEntryReservation(stationUid, user);
        }
    }

    private bool TryStartExitDoAfter(Entity<WH40KTankComponent> tank, EntityUid user, EntityUid stationUid)
    {
        if (!TryComp<WH40KTankStationComponent>(stationUid, out var stationComp) ||
            stationComp.Tank != tank.Owner ||
            !TryComp<BuckleComponent>(user, out var buckleComp) ||
            !buckleComp.Buckled ||
            buckleComp.BuckledTo != stationUid)
        {
            return false;
        }

        if (stationComp.PendingExitOccupant == user)
            return true;

        if (!TryGetExitCoordinates((stationUid, stationComp), out var exitCoordinates))
            return false;

        if (IsExitBlocked((stationUid, stationComp), user, exitCoordinates))
        {
            _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-tank-exit-blocked"), tank.Owner, user);
            return true;
        }

        stationComp.PendingExitOccupant = user;

        if (tank.Comp.ExitDelaySeconds <= 0f)
        {
            return CompleteTankExit(user, stationUid);
        }

        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            tank.Comp.ExitDelaySeconds,
            new WH40KTankExitDoAfterEvent(),
            tank.Owner,
            target: tank.Owner,
            used: stationUid)
        {
            BreakOnDamage = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            ClearPendingExit(stationUid, user);
            return false;
        }

        _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-tank-exit-start"), tank.Owner, user);
        return true;
    }

    private bool CompleteTankExit(EntityUid user, EntityUid stationUid)
    {
        try
        {
            if (!Exists(user) || !Exists(stationUid) ||
                !TryComp<BuckleComponent>(user, out var buckleComp) ||
                !buckleComp.Buckled ||
                buckleComp.BuckledTo != stationUid)
            {
                return false;
            }

            if (TryComp<WH40KTankStationComponent>(stationUid, out var stationComp))
                stationComp.PendingExitOccupant = user;

            return _buckle.TryUnbuckle((user, buckleComp), user, popup: true);
        }
        finally
        {
            ClearPendingExit(stationUid, user);
        }
    }

    private void OnTankAimAction(Entity<WH40KTankComponent> tank, ref WH40KTankAimActionEvent args)
    {
        args.Handled = TrySetAimTarget(tank, args.Performer, _transform.ToMapCoordinates(args.Target));
    }

    private void OnTankFireMainGunAction(Entity<WH40KTankComponent> tank, ref WH40KTankFireMainGunActionEvent args)
    {
        args.Handled = TryQueueMainGunFire(tank, args.Performer);
    }

    private void OnTankAimRequest(WH40KTankAimRequestEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } user ||
            !TryGetControlledTank(user, out var tank))
        {
            return;
        }

        TrySetAimTarget(tank, user, ev.Target, popup: false);
    }

    private void OnTankFireMainGunRequest(WH40KTankFireMainGunRequestEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } user ||
            !TryGetControlledTank(user, out var tank))
        {
            return;
        }

        if (!TrySetAimTarget(tank, user, ev.Target))
            return;

        TryQueueMainGunFire(tank, user);
    }

    private void OnTankFireCoaxialAction(Entity<WH40KTankComponent> tank, ref WH40KTankFireCoaxialActionEvent args)
    {
        if (!IsCurrentGunner(tank.Comp, args.Performer) || tank.Comp.CoaxialGun is not { } coaxialGun || !Exists(coaxialGun))
            return;

        if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.Turret))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Performer, "wh40k-tank-turret-disabled"), tank.Owner, args.Performer);
            return;
        }

        if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.Coaxial))
        {
            _popup.PopupEntity(
                _culture.GetPlayerString(
                    args.Performer,
                    "wh40k-tank-weapon-disabled",
                    ("weapon", GetWeaponDisplayName(tank.Comp, WH40KTankModuleType.Coaxial))),
                tank.Owner,
                args.Performer);
            return;
        }

        if (!HasAimTarget(tank.Comp))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Performer, "wh40k-tank-aim-missing"), tank.Owner, args.Performer);
            return;
        }

        tank.Comp.PendingCoaxialFire = true;
        tank.Comp.NextUiRefreshAt = TimeSpan.Zero;
        args.Handled = true;
    }

    private void OnTankReloadMainGunAction(Entity<WH40KTankComponent> tank, ref WH40KTankReloadMainGunActionEvent args)
    {
        if (!IsCurrentLoader(tank.Comp, args.Performer))
            return;

        args.Handled = TryStartWeaponReload(tank, args.Performer, WH40KTankModuleType.MainGun);
    }

    private void OnTankReloadCoaxialAction(Entity<WH40KTankComponent> tank, ref WH40KTankReloadCoaxialActionEvent args)
    {
        if (!IsCurrentLoader(tank.Comp, args.Performer))
            return;

        args.Handled = TryStartWeaponReload(tank, args.Performer, WH40KTankModuleType.Coaxial);
    }

    private void UpdateTankReloads(Entity<WH40KTankComponent> tank)
    {
        CompleteWeaponReload(tank, WH40KTankModuleType.MainGun);

        if (tank.Comp.CoaxialGunPrototype != null)
            CompleteWeaponReload(tank, WH40KTankModuleType.Coaxial);
    }

    private void UpdateTankTurretAndWeapons(Entity<WH40KTankComponent> tank, float frameTime)
    {
        if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.Turret))
        {
            ClearGunnerCombatState(tank.Comp);
            return;
        }

        if (tank.Comp.GunnerOccupant is not { } gunner || !Exists(gunner))
        {
            ClearQueuedFire(tank.Comp);
            return;
        }

        var aligned = UpdateTurretAim(tank, frameTime);
        EnsureTankHierarchy(tank);
        if (!aligned)
            return;

        if (tank.Comp.PendingMainGunFire)
        {
            if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.MainGun))
            {
                tank.Comp.PendingMainGunFire = false;
            }
            else if (tank.Comp.MainGun is not { } mainGun || !Exists(mainGun))
            {
                tank.Comp.PendingMainGunFire = false;
            }
            else if (!IsWeaponReloading(tank.Comp, WH40KTankModuleType.MainGun))
            {
                if (_gun.GetAmmoCount(mainGun) <= 0)
                {
                    _popup.PopupEntity(
                        Loc.GetString(
                            "wh40k-tank-weapon-empty",
                            ("weapon", GetWeaponDisplayName(tank.Comp, WH40KTankModuleType.MainGun))),
                        tank.Owner,
                        gunner);
                    tank.Comp.PendingMainGunFire = false;
                }
                else
                {
                    tank.Comp.PendingMainGunFire = !TryFireWeapon(tank, mainGun, gunner);
                }
            }
        }

        if (tank.Comp.PendingCoaxialFire)
        {
            if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.Coaxial))
            {
                tank.Comp.PendingCoaxialFire = false;
            }
            else if (tank.Comp.CoaxialGun is not { } coaxialGun || !Exists(coaxialGun))
            {
                tank.Comp.PendingCoaxialFire = false;
            }
            else if (!IsWeaponReloading(tank.Comp, WH40KTankModuleType.Coaxial))
            {
                if (_gun.GetAmmoCount(coaxialGun) <= 0)
                {
                    _popup.PopupEntity(
                        Loc.GetString(
                            "wh40k-tank-weapon-empty",
                            ("weapon", GetWeaponDisplayName(tank.Comp, WH40KTankModuleType.Coaxial))),
                        tank.Owner,
                        gunner);
                    tank.Comp.PendingCoaxialFire = false;
                }
                else
                {
                    tank.Comp.PendingCoaxialFire = !TryFireWeapon(tank, coaxialGun, gunner);
                }
            }
        }
    }

    private bool UpdateTurretAim(Entity<WH40KTankComponent> tank, float frameTime)
    {
        if (!HasAimTarget(tank.Comp) || tank.Comp.Turret is not { } turretUid || !Exists(turretUid))
            return false;

        var traverseFactor = GetPerformanceFactor(tank.Comp, WH40KTankModuleType.Turret);
        if (traverseFactor <= 0f)
            return false;

        var turretCoords = _transform.GetMapCoordinates(turretUid);
        if (turretCoords.MapId == MapId.Nullspace || turretCoords.MapId != tank.Comp.AimTarget.MapId)
            return false;

        var direction = tank.Comp.AimTarget.Position - turretCoords.Position;
        if (direction.LengthSquared() <= 0.0001f)
            return true;

        return _rotateToFace.TryRotateTo(
            turretUid,
            Angle.FromWorldVec(direction),
            frameTime,
            Angle.FromDegrees(tank.Comp.TurretAlignmentTolerance),
            Angle.FromDegrees(tank.Comp.TurretTraverseSpeed * traverseFactor).Theta);
    }

    private bool TryFireWeapon(Entity<WH40KTankComponent> tank, EntityUid gunUid, EntityUid user)
    {
        if (!HasAimTarget(tank.Comp) || !Exists(gunUid) || !TryComp<GunComponent>(gunUid, out var gunComp))
            return false;

        var targetCoords = new EntityCoordinates(_map.GetMapOrInvalid(tank.Comp.AimTarget.MapId), tank.Comp.AimTarget.Position);
        return _gun.AttemptShoot(user, (gunUid, gunComp), targetCoords);
    }

    private void EnsureGunnerActionEntities(Entity<WH40KTankComponent> tank)
    {
        if (tank.Comp.CoaxialGunPrototype is not null && tank.Comp.FireCoaxialAction is { } coaxialAction)
            _actionContainer.EnsureAction(tank, ref tank.Comp.FireCoaxialActionEntity, coaxialAction);
    }

    private void EnsureLoaderActionEntities(Entity<WH40KTankComponent> tank)
    {
        _actionContainer.EnsureAction(tank, ref tank.Comp.ReloadMainGunActionEntity, tank.Comp.ReloadMainGunAction);

        if (tank.Comp.CoaxialGunPrototype is not null && tank.Comp.ReloadCoaxialAction is { } coaxialAction)
            _actionContainer.EnsureAction(tank, ref tank.Comp.ReloadCoaxialActionEntity, coaxialAction);
    }

    private void GrantGunnerActions(Entity<WH40KTankComponent> tank, EntityUid user)
    {
        EnsureGunnerActionEntities(tank);
        ClearGunnerCombatState(tank.Comp);

        if (tank.Comp.CoaxialGunPrototype is not null && tank.Comp.FireCoaxialAction is { } coaxialAction)
            _actions.AddAction(user, ref tank.Comp.FireCoaxialActionEntity, coaxialAction, tank);
    }

    private void GrantLoaderActions(Entity<WH40KTankComponent> tank, EntityUid user)
    {
        EnsureLoaderActionEntities(tank);
        _actions.AddAction(user, ref tank.Comp.ReloadMainGunActionEntity, tank.Comp.ReloadMainGunAction, tank);

        if (tank.Comp.CoaxialGunPrototype is not null && tank.Comp.ReloadCoaxialAction is { } coaxialAction)
            _actions.AddAction(user, ref tank.Comp.ReloadCoaxialActionEntity, coaxialAction, tank);
    }

    private void GrantDiagnosticsAction(
        Entity<WH40KTankComponent> tank,
        Entity<WH40KTankStationComponent> station,
        EntityUid user)
    {
        _actions.AddAction(user, ref station.Comp.DiagnosticsActionEntity, tank.Comp.DiagnosticsAction, tank);
    }

    private void RemoveGunnerActions(WH40KTankComponent tank, EntityUid user)
    {
        if (tank.FireCoaxialActionEntity is { } coaxialAction)
            TryRemoveAction(user, coaxialAction);
    }

    private void RemoveLoaderActions(WH40KTankComponent tank, EntityUid user)
    {
        if (tank.ReloadMainGunActionEntity is { } reloadMainAction)
            TryRemoveAction(user, reloadMainAction);

        if (tank.ReloadCoaxialActionEntity is { } reloadCoaxialAction)
            TryRemoveAction(user, reloadCoaxialAction);
    }

    private void RemoveDiagnosticsAction(Entity<WH40KTankStationComponent> station, EntityUid user)
    {
        if (station.Comp.DiagnosticsActionEntity is not { } action)
            return;

        TryRemoveAction(user, action);
    }

    private void TryRemoveAction(EntityUid user, EntityUid actionUid)
    {
        if (!TryComp<ActionComponent>(actionUid, out var actionComp) || actionComp.AttachedEntity != user)
            return;

        _actions.RemoveAction(user, actionUid);
    }

    private bool TryGetControlledTank(EntityUid user, out Entity<WH40KTankComponent> tank)
    {
        tank = default;

        if (!TryComp<BuckleComponent>(user, out var buckle) ||
            buckle.BuckledTo is not { } stationUid ||
            !TryComp<WH40KTankStationComponent>(stationUid, out var stationComp) ||
            stationComp.Role != WH40KTankCrewRole.Gunner ||
            stationComp.Tank is not { } tankUid ||
            !TryComp<WH40KTankComponent>(tankUid, out var tankComp))
        {
            return false;
        }

        tank = (tankUid, tankComp);
        return true;
    }

    private bool TrySetAimTarget(Entity<WH40KTankComponent> tank, EntityUid user, MapCoordinates target, bool popup = true)
    {
        if (!IsCurrentGunner(tank.Comp, user))
            return false;

        if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.Turret))
        {
            if (popup)
                _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-tank-turret-disabled"), tank.Owner, user);

            return false;
        }

        var tankCoords = _transform.GetMapCoordinates(tank.Owner);
        if (target.MapId == MapId.Nullspace || target.MapId != tankCoords.MapId)
            return false;

        tank.Comp.AimTarget = target;
        tank.Comp.NextUiRefreshAt = TimeSpan.Zero;
        return true;
    }

    private bool TryQueueMainGunFire(Entity<WH40KTankComponent> tank, EntityUid user)
    {
        if (!IsCurrentGunner(tank.Comp, user) || tank.Comp.MainGun is not { } mainGun || !Exists(mainGun))
            return false;

        if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.Turret))
        {
            _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-tank-turret-disabled"), tank.Owner, user);
            return false;
        }

        if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.MainGun))
        {
            _popup.PopupEntity(
                _culture.GetPlayerString(
                    user,
                    "wh40k-tank-weapon-disabled",
                    ("weapon", GetWeaponDisplayName(tank.Comp, WH40KTankModuleType.MainGun))),
                tank.Owner,
                user);
            return false;
        }

        if (!HasAimTarget(tank.Comp))
        {
            _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-tank-aim-missing"), tank.Owner, user);
            return false;
        }

        tank.Comp.PendingMainGunFire = true;
        tank.Comp.NextUiRefreshAt = TimeSpan.Zero;
        return true;
    }

    private void TryUnbuckleOccupant(EntityUid? occupantUid)
    {
        if (occupantUid is not { } occupant || !TryComp<BuckleComponent>(occupant, out var buckle))
            return;

        _buckle.TryUnbuckle((occupant, buckle), null, popup: false);
    }

    private static bool HasAimTarget(WH40KTankComponent tank)
    {
        return tank.AimTarget.MapId != MapId.Nullspace;
    }

    private static bool IsCurrentLoader(WH40KTankComponent tank, EntityUid user)
    {
        return tank.LoaderOccupant == user;
    }

    private static bool IsCurrentGunner(WH40KTankComponent tank, EntityUid user)
    {
        return tank.GunnerOccupant == user;
    }

    private static void ClearGunnerCombatState(WH40KTankComponent tank)
    {
        tank.AimTarget = MapCoordinates.Nullspace;
        ClearQueuedFire(tank);
    }

    private static void ClearQueuedFire(WH40KTankComponent tank)
    {
        tank.PendingMainGunFire = false;
        tank.PendingCoaxialFire = false;
    }

    private bool TryStartWeaponReload(Entity<WH40KTankComponent> tank, EntityUid user, WH40KTankModuleType module)
    {
        if (GetWeaponEntity(tank.Comp, module) is not { } weaponUid ||
            !Exists(weaponUid) ||
            !TryComp<BasicEntityAmmoProviderComponent>(weaponUid, out var ammoProvider) ||
            ammoProvider.Capacity is not { } capacity ||
            capacity <= 0)
        {
            return false;
        }

        if (IsModuleDestroyed(tank.Comp, module))
        {
            _popup.PopupEntity(
                _culture.GetPlayerString(
                    user,
                    "wh40k-tank-weapon-disabled",
                    ("weapon", GetWeaponDisplayName(tank.Comp, module))),
                tank.Owner,
                user);
            return false;
        }

        if (IsWeaponReloading(tank.Comp, module))
        {
            _popup.PopupEntity(
                _culture.GetPlayerString(
                    user,
                    "wh40k-tank-weapon-reloading",
                    ("weapon", GetWeaponDisplayName(tank.Comp, module))),
                tank.Owner,
                user);
            return false;
        }

        if (_gun.GetAmmoCount(weaponUid) >= capacity)
        {
            _popup.PopupEntity(
                _culture.GetPlayerString(
                    user,
                    "wh40k-tank-weapon-already-loaded",
                    ("weapon", GetWeaponDisplayName(tank.Comp, module))),
                tank.Owner,
                user);
            return false;
        }

        SetReloadCompleteAt(
            tank.Comp,
            module,
            _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0.1f, GetReloadSeconds(tank.Comp, module))));
        tank.Comp.NextUiRefreshAt = TimeSpan.Zero;
        _audio.PlayPvs(tank.Comp.ReloadSound, tank.Owner);
        _popup.PopupEntity(
            Loc.GetString(
                "wh40k-tank-weapon-reload-start",
                ("weapon", GetWeaponDisplayName(tank.Comp, module))),
            tank.Owner,
            user);
        return true;
    }

    private void UpdateTankMotion(
        EntityUid uid,
        WH40KTankComponent tank,
        WH40KTankEngineComponent engine,
        WH40KTankFuelComponent fuel,
        WH40KTankDriveComponent drive,
        PhysicsComponent physics,
        InputMoverComponent mover,
        float frameTime)
    {
        var driver = tank.DriverOccupant;
        var hasDriver = driver is { } driverUid && Exists(driverUid);

        if (!hasDriver && engine.State == WH40KTankEngineState.Running)
            StopEngine(uid, engine);

        if (engine.State == WH40KTankEngineState.Running && IsModuleDestroyed(tank, WH40KTankModuleType.Engine))
            StopEngine(uid, engine, driver, stalled: true, popup: hasDriver);

        if (engine.State == WH40KTankEngineState.Running && !HasFuel(uid, fuel))
            StopEngine(uid, engine, driver, stalled: true, popup: hasDriver);

        var movementFactor = MathF.Min(
            GetPerformanceFactor(tank, WH40KTankModuleType.Engine),
            GetPerformanceFactor(tank, WH40KTankModuleType.Tracks));

        var buttons = hasDriver && engine.State == WH40KTankEngineState.Running
            ? mover.HeldMoveButtons
            : MoveButtons.None;

        if (movementFactor <= 0f)
            buttons = MoveButtons.None;

        var throttle = 0f;
        var steering = 0f;

        if ((buttons & MoveButtons.Up) != 0)
            throttle += 1f;

        if ((buttons & MoveButtons.Down) != 0)
            throttle -= 1f;

        if ((buttons & MoveButtons.Right) != 0)
            steering -= 1f;

        if ((buttons & MoveButtons.Left) != 0)
            steering += 1f;

        var worldRotation = _transform.GetWorldRotation(uid);
        var forward = worldRotation.Opposite().ToWorldVec();
        var right = worldRotation.RotateVec(Vector2.UnitX);

        var currentForwardSpeed = Vector2.Dot(physics.LinearVelocity, forward);
        var currentLateralSpeed = Vector2.Dot(physics.LinearVelocity, right);

        var targetForwardSpeed = throttle switch
        {
            > 0f => drive.ForwardSpeed * movementFactor,
            < 0f => -drive.ReverseSpeed * movementFactor,
            _ => 0f,
        };

        var targetAngularVelocity = steering * (throttle == 0f ? drive.PivotTurnSpeed : drive.TurnSpeed) * movementFactor;

        var forwardStep = (throttle == 0f ? drive.BrakeDeceleration : drive.Acceleration) * frameTime;
        var lateralStep = drive.LateralDamping * frameTime;
        var angularStep = (steering == 0f ? drive.AngularDeceleration : drive.AngularAcceleration) * frameTime;

        var newForwardSpeed = MoveTowards(currentForwardSpeed, targetForwardSpeed, forwardStep);
        var newLateralSpeed = MoveTowards(currentLateralSpeed, 0f, lateralStep);
        var newAngularVelocity = MoveTowards(physics.AngularVelocity, targetAngularVelocity, angularStep);

        var activeMotion = MathF.Abs(targetForwardSpeed) > MotionVisualThreshold ||
                           MathF.Abs(targetAngularVelocity) > MotionVisualThreshold ||
                           MathF.Abs(newForwardSpeed) > MotionVisualThreshold ||
                           MathF.Abs(newAngularVelocity) > MotionVisualThreshold;

        if (engine.State == WH40KTankEngineState.Running)
        {
            var consumption = fuel.IdleConsumption * frameTime;

            if (activeMotion)
                consumption += fuel.MovementConsumption * frameTime;

            if (consumption > FixedPoint2.Zero && !TryDrainFuel(uid, fuel, consumption))
            {
                StopEngine(uid, engine, driver, stalled: true, popup: hasDriver);
                newForwardSpeed = MoveTowards(currentForwardSpeed, 0f, drive.BrakeDeceleration * frameTime);
                newLateralSpeed = MoveTowards(currentLateralSpeed, 0f, lateralStep);
                newAngularVelocity = MoveTowards(physics.AngularVelocity, 0f, drive.AngularDeceleration * frameTime);
            }
        }

        var newVelocity = forward * newForwardSpeed + right * newLateralSpeed;
        _physics.SetLinearVelocity(uid, newVelocity, body: physics);
        _physics.SetAngularVelocity(uid, newAngularVelocity, body: physics);

        var moving = MathF.Abs(newForwardSpeed) > MotionVisualThreshold ||
                     MathF.Abs(newLateralSpeed) > MotionVisualThreshold ||
                     MathF.Abs(newAngularVelocity) > MotionVisualThreshold;

        SetTrackVisual(uid, tank, moving ? WH40KTankVisualState.Moving : WH40KTankVisualState.Idle);
        UpdateTankAudio(uid, tank, engine.State == WH40KTankEngineState.Running, moving);
    }

    private void SetDriverRelay(EntityUid tankUid, EntityUid driverUid)
    {
        _mover.SetRelay(driverUid, tankUid);
    }

    private void ClearDriverRelay(EntityUid driverUid, EntityUid tankUid)
    {
        if (TryComp<RelayInputMoverComponent>(driverUid, out var relay) && relay.RelayEntity == tankUid)
            RemComp<RelayInputMoverComponent>(driverUid);
    }

    private bool TryStartEngine(EntityUid uid, WH40KTankComponent tank, WH40KTankEngineComponent engine, WH40KTankFuelComponent fuel, EntityUid user)
    {
        if (engine.State == WH40KTankEngineState.Running)
            return true;

        if (IsModuleDestroyed(tank, WH40KTankModuleType.Engine))
        {
            _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-tank-engine-disabled"), uid, user);
            return false;
        }

        if (!HasFuelForStartup(uid, fuel) || !TryDrainFuel(uid, fuel, fuel.StartupConsumption))
        {
            _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-tank-engine-empty"), uid, user);
            return false;
        }

        engine.State = WH40KTankEngineState.Running;
        StartEngineAudio(uid, tank);
        _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-tank-engine-started"), uid, user);
        return true;
    }

    private void StopEngine(EntityUid uid, WH40KTankEngineComponent engine, EntityUid? user = null, bool stalled = false, bool popup = false)
    {
        if (engine.State == WH40KTankEngineState.Off)
            return;

        engine.State = WH40KTankEngineState.Off;

        if (TryComp<WH40KTankComponent>(uid, out var tank))
            StartEngineStopAudio(uid, tank);

        if (!popup || user is not { } userUid)
            return;

        var message = stalled
            ? Loc.GetString("wh40k-tank-engine-stalled")
            : Loc.GetString("wh40k-tank-engine-stopped");

        _popup.PopupEntity(message, uid, userUid);
    }

    private bool HasFuel(EntityUid uid, WH40KTankFuelComponent fuel)
    {
        if (!TryGetFuelSolution(uid, fuel, out _, out var solution) || solution is null)
            return false;

        return solution.Volume > FixedPoint2.Zero;
    }

    private bool HasFuelForStartup(EntityUid uid, WH40KTankFuelComponent fuel)
    {
        if (!TryGetFuelSolution(uid, fuel, out _, out var solution) || solution is null)
            return false;

        return solution.Volume >= fuel.StartupConsumption;
    }

    private bool TryDrainFuel(EntityUid uid, WH40KTankFuelComponent fuel, FixedPoint2 amount)
    {
        if (amount <= FixedPoint2.Zero)
            return true;

        if (!TryGetFuelSolution(uid, fuel, out var solutionEntity, out var solution) ||
            solutionEntity is not { } fuelSolutionEntity ||
            solution is null)
        {
            return false;
        }

        var drainedAmount = FixedPoint2.Min(solution.Volume, amount);
        if (drainedAmount <= FixedPoint2.Zero)
            return false;

        _solution.SplitSolution(fuelSolutionEntity, drainedAmount);
        return drainedAmount >= amount;
    }

    private bool TryGetFuelSolution(EntityUid uid, WH40KTankFuelComponent fuel, out Entity<SolutionComponent>? solutionEntity, out Solution? solution)
    {
        solutionEntity = null;
        solution = null;

        if (!TryComp<SolutionContainerManagerComponent>(uid, out var manager))
            return false;

        return _solution.TryGetSolution((uid, manager), fuel.Solution, out solutionEntity, out solution);
    }

    private void SetTrackVisual(EntityUid uid, WH40KTankComponent tank, WH40KTankVisualState state, bool force = false)
    {
        if (!force && tank.TrackVisualState == state)
            return;

        tank.TrackVisualState = state;

        if (TryComp<AppearanceComponent>(uid, out var appearance))
            _appearance.SetData(uid, WH40KTankVisuals.State, state, appearance);
    }

    private void UpdateTankAudio(EntityUid uid, WH40KTankComponent tank, bool engineRunning, bool moving)
    {
        if (!engineRunning)
        {
            if (tank.AudioState == WH40KTankAudioState.Stopping)
            {
                if (_timing.CurTime >= tank.AudioTransitionAt)
                {
                    StopTransitionAudio(tank);
                    ResetTankAudioState(tank);
                }
            }
            else if (tank.AudioState != WH40KTankAudioState.Off ||
                     tank.AudioLoopStream != null ||
                     tank.AudioTransitionStream != null)
            {
                StopTankAudio(tank);
                ResetTankAudioState(tank);
            }

            return;
        }

        switch (tank.AudioState)
        {
            case WH40KTankAudioState.Off:
                if (moving)
                    StartMovementAccelerationAudio(uid, tank);
                else
                    StartIdleLoopAudio(uid, tank);
                break;
            case WH40KTankAudioState.Starting:
                if (_timing.CurTime >= tank.AudioTransitionAt)
                {
                    if (moving)
                        StartMovementAccelerationAudio(uid, tank);
                    else
                        StartIdleLoopAudio(uid, tank);
                }
                break;
            case WH40KTankAudioState.Idle:
                if (moving)
                    StartMovementAccelerationAudio(uid, tank);
                break;
            case WH40KTankAudioState.Accelerating:
                if (_timing.CurTime >= tank.AudioTransitionAt)
                {
                    if (moving)
                        StartMovementLoopAudio(uid, tank);
                    else
                        StartIdleLoopAudio(uid, tank);
                }
                break;
            case WH40KTankAudioState.Moving:
                if (!moving)
                    StartMovementDecelerationAudio(uid, tank);
                break;
            case WH40KTankAudioState.Decelerating:
                if (moving)
                {
                    StartMovementAccelerationAudio(uid, tank);
                }
                else if (_timing.CurTime >= tank.AudioTransitionAt)
                {
                    StartIdleLoopAudio(uid, tank);
                }
                break;
            case WH40KTankAudioState.Stopping:
                if (_timing.CurTime >= tank.AudioTransitionAt)
                {
                    if (moving)
                        StartMovementAccelerationAudio(uid, tank);
                    else
                        StartIdleLoopAudio(uid, tank);
                }
                break;
        }
    }

    private void StartEngineAudio(EntityUid uid, WH40KTankComponent tank)
    {
        StopTankAudio(tank);
        PlayTransitionAudio(uid, tank, tank.EngineStartSound, WH40KTankAudioState.Starting);
    }

    private void StartEngineStopAudio(EntityUid uid, WH40KTankComponent tank)
    {
        StopTankAudio(tank);
        PlayTransitionAudio(uid, tank, tank.EngineStopSound, WH40KTankAudioState.Stopping);
    }

    private void StartIdleLoopAudio(EntityUid uid, WH40KTankComponent tank)
    {
        StopTankAudio(tank);
        tank.AudioState = WH40KTankAudioState.Idle;
        tank.AudioTransitionAt = TimeSpan.Zero;
        tank.AudioLoopStream = PlayLoopAudio(uid, tank.IdleSound);
    }

    private void StartMovementAccelerationAudio(EntityUid uid, WH40KTankComponent tank)
    {
        StopTankAudio(tank);
        PlayTransitionAudio(uid, tank, tank.MovementStartSound, WH40KTankAudioState.Accelerating);
    }

    private void StartMovementLoopAudio(EntityUid uid, WH40KTankComponent tank)
    {
        StopTankAudio(tank);
        tank.AudioState = WH40KTankAudioState.Moving;
        tank.AudioTransitionAt = TimeSpan.Zero;
        tank.AudioLoopStream = PlayLoopAudio(uid, tank.MovementLoopSound);
    }

    private void StartMovementDecelerationAudio(EntityUid uid, WH40KTankComponent tank)
    {
        StopTankAudio(tank);
        PlayTransitionAudio(uid, tank, tank.MovementStopSound, WH40KTankAudioState.Decelerating);
    }

    private EntityUid? PlayLoopAudio(EntityUid uid, SoundSpecifier sound)
    {
        return _audio.PlayPvs(sound, uid, sound.Params.WithLoop(true))?.Entity;
    }

    private void PlayTransitionAudio(EntityUid uid, WH40KTankComponent tank, SoundSpecifier sound, WH40KTankAudioState state)
    {
        var resolved = _audio.ResolveSound(sound);
        tank.AudioState = state;
        tank.AudioTransitionAt = _timing.CurTime + _audio.GetAudioLength(resolved);
        tank.AudioTransitionStream = _audio.PlayPvs(sound, uid)?.Entity;
    }

    private void StopTankAudio(WH40KTankComponent tank)
    {
        StopLoopAudio(tank);
        StopTransitionAudio(tank);
    }

    private void StopLoopAudio(WH40KTankComponent tank)
    {
        tank.AudioLoopStream = _audio.Stop(tank.AudioLoopStream);
    }

    private void StopTransitionAudio(WH40KTankComponent tank)
    {
        tank.AudioTransitionStream = _audio.Stop(tank.AudioTransitionStream);
    }

    private static void ResetTankAudioState(WH40KTankComponent tank)
    {
        tank.AudioState = WH40KTankAudioState.Off;
        tank.AudioTransitionAt = TimeSpan.Zero;
        tank.AudioLoopStream = null;
        tank.AudioTransitionStream = null;
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (maxDelta <= 0f)
            return current;

        var delta = target - current;
        if (MathF.Abs(delta) <= maxDelta)
            return target;

        return current + MathF.Sign(delta) * maxDelta;
    }

    private void EnsureTankHierarchy(Entity<WH40KTankComponent> tank)
    {
        var turretReference = tank.Comp.Turret ?? tank.Owner;
        var turretOffset = ResolveDirectionalOffset(
            turretReference,
            tank.Comp.TurretDirectionalOffsets,
            tank.Comp.TurretOffset);

        tank.Comp.Turret = EnsureChildEntity(tank.Owner, tank.Comp.Turret, tank.Comp.TurretPrototype, turretOffset);

        var hardpointParent = tank.Comp.Turret ?? tank.Owner;
        var mainHardpointReference = tank.Comp.MainHardpoint ?? hardpointParent;
        var mainHardpointOffset = ResolveDirectionalOffset(
            mainHardpointReference,
            tank.Comp.MainHardpointDirectionalOffsets,
            tank.Comp.MainHardpointOffset);

        tank.Comp.MainHardpoint = EnsureChildEntity(
            hardpointParent,
            tank.Comp.MainHardpoint,
            tank.Comp.MainHardpointPrototype,
            mainHardpointOffset);

        if (tank.Comp.CoaxialHardpointPrototype is { } coaxialPrototype)
        {
            tank.Comp.CoaxialHardpoint = EnsureChildEntity(
                hardpointParent,
                tank.Comp.CoaxialHardpoint,
                coaxialPrototype,
                tank.Comp.CoaxialHardpointOffset);
        }

        var mainGunParent = tank.Comp.MainHardpoint ?? hardpointParent;
        tank.Comp.MainGun = EnsureChildEntity(
            mainGunParent,
            tank.Comp.MainGun,
            tank.Comp.MainGunPrototype,
            tank.Comp.MainGunOffset);

        if (tank.Comp.CoaxialGunPrototype is { } coaxialGunPrototype)
        {
            var coaxialGunParent = tank.Comp.CoaxialHardpoint ?? hardpointParent;
            tank.Comp.CoaxialGun = EnsureChildEntity(
                coaxialGunParent,
                tank.Comp.CoaxialGun,
                coaxialGunPrototype,
                tank.Comp.CoaxialGunOffset);
        }

        tank.Comp.DriverStation = EnsureStationEntity(
            tank.Owner,
            tank.Comp.DriverStation,
            tank.Comp.DriverStationPrototype,
            tank.Comp.DriverStationOffset,
            tank.Owner);

        tank.Comp.GunnerStation = EnsureStationEntity(
            tank.Owner,
            tank.Comp.GunnerStation,
            tank.Comp.GunnerStationPrototype,
            tank.Comp.GunnerStationOffset,
            tank.Owner);

        tank.Comp.CommanderStation = EnsureStationEntity(
            tank.Owner,
            tank.Comp.CommanderStation,
            tank.Comp.CommanderStationPrototype,
            tank.Comp.CommanderStationOffset,
            tank.Owner);

        tank.Comp.LoaderStation = EnsureStationEntity(
            tank.Owner,
            tank.Comp.LoaderStation,
            tank.Comp.LoaderStationPrototype,
            tank.Comp.LoaderStationOffset,
            tank.Owner);
    }

    private EntityUid EnsureStationEntity(
        EntityUid parent,
        EntityUid? existing,
        string prototype,
        Vector2 offset,
        EntityUid tankUid)
    {
        var stationUid = EnsureChildEntity(parent, existing, prototype, offset);

        if (TryComp<WH40KTankStationComponent>(stationUid, out var stationComp))
        {
            if (stationComp.Tank != tankUid)
            {
                stationComp.Tank = tankUid;
                Dirty(stationUid, stationComp);
            }
        }

        return stationUid;
    }

    private EntityUid EnsureChildEntity(EntityUid parent, EntityUid? existing, string prototype, Vector2 offset)
    {
        if (existing is { } existingUid && Exists(existingUid))
        {
            var existingXform = Transform(existingUid);

            if (existingXform.ParentUid != parent)
            {
                _transform.SetCoordinates(
                    existingUid,
                    existingXform,
                    new EntityCoordinates(parent, offset),
                    existingXform.LocalRotation);
            }
            else
            {
                _transform.SetLocalPosition(existingUid, offset, existingXform);
            }

            return existingUid;
        }

        return Spawn(prototype, new EntityCoordinates(parent, offset));
    }

    private Vector2 ResolveDirectionalOffset(
        EntityUid reference,
        WH40KTankDirectionalOffsetSet? directionalOffsets,
        Vector2 fallback)
    {
        if (directionalOffsets == null || !Exists(reference))
            return fallback;

        var direction = _transform.GetWorldRotation(reference).GetCardinalDir();
        return directionalOffsets.Resolve(direction, fallback);
    }

    private void RefreshCrewRegistry(Entity<WH40KTankComponent> tank)
    {
        tank.Comp.DriverOccupant = GetStationOccupant(tank.Comp.DriverStation);
        tank.Comp.GunnerOccupant = GetStationOccupant(tank.Comp.GunnerStation);
        tank.Comp.CommanderOccupant = GetStationOccupant(tank.Comp.CommanderStation);
        tank.Comp.LoaderOccupant = GetStationOccupant(tank.Comp.LoaderStation);
    }

    private void UpdateTankDiagnosticsUi(Entity<WH40KTankComponent> tank, bool force = false)
    {
        if (!_ui.IsUiOpen(tank.Owner, WH40KTankUiKey.Key))
            return;

        if (!force && _timing.CurTime < tank.Comp.NextUiRefreshAt)
            return;

        RefreshCrewRegistry(tank);
        tank.Comp.NextUiRefreshAt = _timing.CurTime + TimeSpan.FromSeconds(DiagnosticsRefreshSeconds);
        _ui.SetUiState(tank.Owner, WH40KTankUiKey.Key, BuildDiagnosticsState(tank));
    }

    private WH40KTankBuiState BuildDiagnosticsState(Entity<WH40KTankComponent> tank)
    {
        var fuelCurrent = 0f;
        var fuelCapacity = 0f;
        GetWeaponAmmoStatus(tank.Comp.MainGun, out var mainGunAmmoCount, out var mainGunAmmoCapacity);
        GetWeaponAmmoStatus(tank.Comp.CoaxialGun, out var coaxialAmmoCount, out var coaxialAmmoCapacity);

        if (TryComp<WH40KTankFuelComponent>(tank.Owner, out var fuelComp) &&
            TryGetFuelSolution(tank.Owner, fuelComp, out _, out var fuelSolution) &&
            fuelSolution != null)
        {
            fuelCurrent = fuelSolution.Volume.Float();
            fuelCapacity = fuelSolution.MaxVolume.Float();
        }

        var fuelFraction = fuelCapacity <= 0f ? 0f : Math.Clamp(fuelCurrent / fuelCapacity, 0f, 1f);
        var hullDamage = TryComp<DamageableComponent>(tank.Owner, out var damageable)
            ? _damageable.GetTotalDamage((tank.Owner, damageable)).Float()
            : 0f;
        var hullIntegrity = tank.Comp.HullMaxIntegrity <= 0f
            ? 1f
            : Math.Clamp(1f - (hullDamage / tank.Comp.HullMaxIntegrity), 0f, 1f);
        var crew = new WH40KTankCrewEntry[EntryOrder.Length];

        for (var i = 0; i < EntryOrder.Length; i++)
        {
            var role = EntryOrder[i];
            var occupant = GetOccupantUid(tank.Comp, role);
            crew[i] = new WH40KTankCrewEntry(role, occupant is { } occupantUid && Exists(occupantUid) ? Name(occupantUid) : string.Empty, occupant != null);
        }

        var moduleEntries = new List<WH40KTankModuleEntry>();
        foreach (var module in ModuleOrder)
        {
            if (!HasModule(tank.Comp, module))
                continue;

            moduleEntries.Add(new WH40KTankModuleEntry(module, GetModuleIntegrityFraction(tank.Comp, module), GetModuleStatus(tank.Comp, module)));
        }

        return new WH40KTankBuiState(
            Name(tank.Owner),
            tank.Comp.MainWeaponLocKey,
            tank.Comp.CoaxialWeaponLocKey,
            tank.Comp.MainAmmoLocKey,
            tank.Comp.CoaxialAmmoLocKey,
            hullIntegrity,
            TryComp<WH40KTankEngineComponent>(tank.Owner, out var engine) && engine.State == WH40KTankEngineState.Running,
            fuelCurrent,
            fuelCapacity,
            fuelFraction,
            HasAimTarget(tank.Comp),
            tank.Comp.PendingMainGunFire,
            tank.Comp.PendingCoaxialFire,
            tank.Comp.CoaxialGunPrototype != null,
            mainGunAmmoCount,
            mainGunAmmoCapacity,
            GetReloadSecondsRemaining(tank.Comp, WH40KTankModuleType.MainGun),
            coaxialAmmoCount,
            coaxialAmmoCapacity,
            GetReloadSecondsRemaining(tank.Comp, WH40KTankModuleType.Coaxial),
            crew,
            moduleEntries.ToArray());
    }

    private void ResetModuleDamage(WH40KTankComponent tank)
    {
        tank.EngineDamage = 0f;
        tank.TracksDamage = 0f;
        tank.TurretDamage = 0f;
        tank.MainGunDamage = 0f;
        tank.CoaxialDamage = 0f;
    }

    private void ResetReloadState(WH40KTankComponent tank)
    {
        tank.MainGunReloadCompleteAt = TimeSpan.Zero;
        tank.CoaxialReloadCompleteAt = TimeSpan.Zero;
    }

    private void ApplyModuleDamage(Entity<WH40KTankComponent> tank, float damage)
    {
        while (damage > 0.001f)
        {
            var candidates = new List<WH40KTankModuleType>();
            var totalWeight = 0f;

            foreach (var module in ModuleOrder)
            {
                if (!HasModule(tank.Comp, module))
                    continue;

                var remaining = GetModuleMaxDamage(tank.Comp, module) - GetModuleDamage(tank.Comp, module);
                if (remaining <= 0f)
                    continue;

                candidates.Add(module);
                totalWeight += GetModuleMaxDamage(tank.Comp, module);
            }

            if (candidates.Count == 0 || totalWeight <= 0f)
                break;

            var roll = _random.NextFloat() * totalWeight;
            var selected = candidates[^1];
            foreach (var module in candidates)
            {
                roll -= GetModuleMaxDamage(tank.Comp, module);
                if (roll > 0f)
                    continue;

                selected = module;
                break;
            }

            var current = GetModuleDamage(tank.Comp, selected);
            var remainingDamage = GetModuleMaxDamage(tank.Comp, selected) - current;
            var applied = MathF.Min(damage, remainingDamage);
            SetModuleDamage(tank.Comp, selected, current + applied);
            HandleModuleTransition(tank, selected, current, current + applied);
            damage -= applied;
        }
    }

    private void ApplyModuleRepair(Entity<WH40KTankComponent> tank, float repair)
    {
        while (repair > 0.001f)
        {
            WH40KTankModuleType? selected = null;
            var bestRatio = 0f;

            foreach (var module in ModuleOrder)
            {
                if (!HasModule(tank.Comp, module))
                    continue;

                var current = GetModuleDamage(tank.Comp, module);
                if (current <= 0f)
                    continue;

                var ratio = current / MathF.Max(GetModuleMaxDamage(tank.Comp, module), 1f);
                if (ratio <= bestRatio)
                    continue;

                bestRatio = ratio;
                selected = module;
            }

            if (selected == null)
                break;

            var moduleType = selected.Value;
            var currentDamage = GetModuleDamage(tank.Comp, moduleType);
            var applied = MathF.Min(repair, currentDamage);
            SetModuleDamage(tank.Comp, moduleType, currentDamage - applied);
            HandleModuleTransition(tank, moduleType, currentDamage, currentDamage - applied);
            repair -= applied;
        }
    }

    private void HandleModuleTransition(Entity<WH40KTankComponent> tank, WH40KTankModuleType module, float oldDamage, float newDamage)
    {
        var maxDamage = GetModuleMaxDamage(tank.Comp, module);
        var oldStatus = GetModuleStatus(oldDamage, maxDamage);
        var newStatus = GetModuleStatus(newDamage, maxDamage);

        if (oldStatus == newStatus)
            return;

        if (newStatus == WH40KTankModuleStatus.Destroyed)
        {
            var message = module switch
            {
                WH40KTankModuleType.MainGun or WH40KTankModuleType.Coaxial => Loc.GetString(
                    "wh40k-tank-weapon-module-destroyed",
                    ("weapon", GetWeaponDisplayName(tank.Comp, module))),
                _ => Loc.GetString(GetModuleDisabledLocKey(module)),
            };
            NotifyCrew(tank, message);
        }
        else if (oldStatus == WH40KTankModuleStatus.Destroyed)
        {
            var message = module switch
            {
                WH40KTankModuleType.MainGun or WH40KTankModuleType.Coaxial => Loc.GetString(
                    "wh40k-tank-weapon-module-restored",
                    ("weapon", GetWeaponDisplayName(tank.Comp, module))),
                _ => Loc.GetString(GetModuleRestoredLocKey(module)),
            };
            NotifyCrew(tank, message);
        }
    }

    private void EnforceModuleFailures(Entity<WH40KTankComponent> tank)
    {
        if (TryComp<WH40KTankEngineComponent>(tank.Owner, out var engine) && IsModuleDestroyed(tank.Comp, WH40KTankModuleType.Engine))
            StopEngine(tank.Owner, engine);

        if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.Turret))
            ClearGunnerCombatState(tank.Comp);

        if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.MainGun))
        {
            tank.Comp.PendingMainGunFire = false;
            SetReloadCompleteAt(tank.Comp, WH40KTankModuleType.MainGun, TimeSpan.Zero);
        }

        if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.Coaxial))
        {
            tank.Comp.PendingCoaxialFire = false;
            SetReloadCompleteAt(tank.Comp, WH40KTankModuleType.Coaxial, TimeSpan.Zero);
        }

        if (IsModuleDestroyed(tank.Comp, WH40KTankModuleType.Tracks) && TryComp<PhysicsComponent>(tank.Owner, out var physics))
        {
            _physics.SetLinearVelocity(tank.Owner, Vector2.Zero, body: physics);
            _physics.SetAngularVelocity(tank.Owner, 0f, body: physics);
            SetTrackVisual(tank.Owner, tank.Comp, WH40KTankVisualState.Idle);
        }
    }

    private void NotifyCrew(Entity<WH40KTankComponent> tank, string message)
    {
        foreach (var role in EntryOrder)
        {
            var occupant = GetOccupantUid(tank.Comp, role);
            if (occupant is not { } occupantUid || !Exists(occupantUid))
                continue;

            _popup.PopupEntity(message, tank.Owner, occupantUid);
        }
    }

    private bool IsTankMoving(EntityUid uid)
    {
        if (!TryComp<PhysicsComponent>(uid, out var physics))
            return false;

        return physics.LinearVelocity.LengthSquared() > 0.01f || MathF.Abs(physics.AngularVelocity) > 0.05f;
    }

    private void TryRemoveDiagnosticsAction(EntityUid? stationUid, EntityUid? occupantUid)
    {
        if (stationUid is not { } station || occupantUid is not { } occupant)
            return;

        if (!TryComp<WH40KTankStationComponent>(station, out var stationComp))
            return;

        RemoveDiagnosticsAction((station, stationComp), occupant);
    }

    private void CompleteWeaponReload(Entity<WH40KTankComponent> tank, WH40KTankModuleType module)
    {
        var completeAt = GetReloadCompleteAt(tank.Comp, module);
        if (completeAt == TimeSpan.Zero || _timing.CurTime < completeAt)
            return;

        SetReloadCompleteAt(tank.Comp, module, TimeSpan.Zero);
        tank.Comp.NextUiRefreshAt = TimeSpan.Zero;

        if (IsModuleDestroyed(tank.Comp, module))
            return;

        if (GetWeaponEntity(tank.Comp, module) is not { } weaponUid ||
            !Exists(weaponUid) ||
            !TryComp<BasicEntityAmmoProviderComponent>(weaponUid, out var ammoProvider) ||
            ammoProvider.Capacity is not { } capacity)
        {
            return;
        }

        _gun.UpdateBasicEntityAmmoCount((weaponUid, ammoProvider), capacity);
    }

    private void GetWeaponAmmoStatus(EntityUid? weaponUid, out int count, out int capacity)
    {
        count = 0;
        capacity = 0;

        if (weaponUid is not { } resolvedWeapon || !Exists(resolvedWeapon))
            return;

        count = Math.Max(0, _gun.GetAmmoCount(resolvedWeapon));
        capacity = Math.Max(0, _gun.GetAmmoCapacity(resolvedWeapon));
    }

    private TimeSpan GetReloadCompleteAt(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        return module switch
        {
            WH40KTankModuleType.MainGun => tank.MainGunReloadCompleteAt,
            WH40KTankModuleType.Coaxial => tank.CoaxialReloadCompleteAt,
            _ => TimeSpan.Zero,
        };
    }

    private void SetReloadCompleteAt(WH40KTankComponent tank, WH40KTankModuleType module, TimeSpan value)
    {
        switch (module)
        {
            case WH40KTankModuleType.MainGun:
                tank.MainGunReloadCompleteAt = value;
                break;
            case WH40KTankModuleType.Coaxial:
                tank.CoaxialReloadCompleteAt = value;
                break;
        }
    }

    private float GetReloadSeconds(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        return module switch
        {
            WH40KTankModuleType.MainGun => tank.MainGunReloadSeconds,
            WH40KTankModuleType.Coaxial => tank.CoaxialReloadSeconds,
            _ => 0f,
        };
    }

    private float GetReloadSecondsRemaining(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        var completeAt = GetReloadCompleteAt(tank, module);
        if (completeAt == TimeSpan.Zero || completeAt <= _timing.CurTime)
            return 0f;

        return (float) (completeAt - _timing.CurTime).TotalSeconds;
    }

    private bool IsWeaponReloading(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        var completeAt = GetReloadCompleteAt(tank, module);
        return completeAt != TimeSpan.Zero && completeAt > _timing.CurTime;
    }

    private static EntityUid? GetWeaponEntity(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        return module switch
        {
            WH40KTankModuleType.MainGun => tank.MainGun,
            WH40KTankModuleType.Coaxial => tank.CoaxialGun,
            _ => null,
        };
    }

    private string GetWeaponDisplayName(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        return Loc.GetString(GetWeaponLocKey(tank, module));
    }

    private static string GetWeaponLocKey(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        return module switch
        {
            WH40KTankModuleType.MainGun => tank.MainWeaponLocKey,
            WH40KTankModuleType.Coaxial => tank.CoaxialWeaponLocKey,
            _ => "wh40k-tank-ui-module-main-gun",
        };
    }

    private static float GetPositiveDamageAmount(DamageSpecifier damage)
    {
        var total = 0f;
        foreach (var value in damage.DamageDict.Values)
        {
            if (value > FixedPoint2.Zero)
                total += value.Float();
        }

        return total;
    }

    private static float GetHealingAmount(DamageSpecifier damage)
    {
        var total = 0f;
        foreach (var value in damage.DamageDict.Values)
        {
            if (value < FixedPoint2.Zero)
                total += -value.Float();
        }

        return total;
    }

    private static float GetModuleMaxDamage(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        return module switch
        {
            WH40KTankModuleType.Engine => tank.EngineMaxIntegrity,
            WH40KTankModuleType.Tracks => tank.TracksMaxIntegrity,
            WH40KTankModuleType.Turret => tank.TurretMaxIntegrity,
            WH40KTankModuleType.MainGun => tank.MainGunMaxIntegrity,
            WH40KTankModuleType.Coaxial => tank.CoaxialGunPrototype == null ? 0f : tank.CoaxialMaxIntegrity,
            _ => 1f,
        };
    }

    private static bool HasModule(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        return module != WH40KTankModuleType.Coaxial || tank.CoaxialGunPrototype != null;
    }

    private static float GetModuleDamage(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        return module switch
        {
            WH40KTankModuleType.Engine => tank.EngineDamage,
            WH40KTankModuleType.Tracks => tank.TracksDamage,
            WH40KTankModuleType.Turret => tank.TurretDamage,
            WH40KTankModuleType.MainGun => tank.MainGunDamage,
            WH40KTankModuleType.Coaxial => tank.CoaxialDamage,
            _ => 0f,
        };
    }

    private static void SetModuleDamage(WH40KTankComponent tank, WH40KTankModuleType module, float value)
    {
        value = MathF.Max(0f, value);

        switch (module)
        {
            case WH40KTankModuleType.Engine:
                tank.EngineDamage = value;
                break;
            case WH40KTankModuleType.Tracks:
                tank.TracksDamage = value;
                break;
            case WH40KTankModuleType.Turret:
                tank.TurretDamage = value;
                break;
            case WH40KTankModuleType.MainGun:
                tank.MainGunDamage = value;
                break;
            case WH40KTankModuleType.Coaxial:
                tank.CoaxialDamage = value;
                break;
        }
    }

    private static float GetModuleIntegrityFraction(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        var max = MathF.Max(GetModuleMaxDamage(tank, module), 1f);
        return Math.Clamp(1f - (GetModuleDamage(tank, module) / max), 0f, 1f);
    }

    private static WH40KTankModuleStatus GetModuleStatus(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        return GetModuleStatus(GetModuleDamage(tank, module), GetModuleMaxDamage(tank, module));
    }

    private static WH40KTankModuleStatus GetModuleStatus(float currentDamage, float maxDamage)
    {
        if (maxDamage <= 0f)
            return WH40KTankModuleStatus.Operational;

        var integrity = Math.Clamp(1f - (currentDamage / maxDamage), 0f, 1f);
        if (integrity <= 0f)
            return WH40KTankModuleStatus.Destroyed;

        if (integrity <= 0.35f)
            return WH40KTankModuleStatus.Critical;

        if (integrity <= 0.7f)
            return WH40KTankModuleStatus.Damaged;

        return WH40KTankModuleStatus.Operational;
    }

    private static float GetPerformanceFactor(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        return GetModuleStatus(tank, module) switch
        {
            WH40KTankModuleStatus.Operational => 1f,
            WH40KTankModuleStatus.Damaged => 0.78f,
            WH40KTankModuleStatus.Critical => 0.45f,
            WH40KTankModuleStatus.Destroyed => 0f,
            _ => 1f,
        };
    }

    private static bool IsModuleDestroyed(WH40KTankComponent tank, WH40KTankModuleType module)
    {
        return GetModuleStatus(tank, module) == WH40KTankModuleStatus.Destroyed;
    }

    private static bool IsCurrentCrew(WH40KTankComponent tank, EntityUid user)
    {
        return tank.DriverOccupant == user ||
               tank.GunnerOccupant == user ||
               tank.CommanderOccupant == user ||
               tank.LoaderOccupant == user;
    }

    private static string GetModuleDisabledLocKey(WH40KTankModuleType module)
    {
        return module switch
        {
            WH40KTankModuleType.Engine => "wh40k-tank-module-engine-destroyed",
            WH40KTankModuleType.Tracks => "wh40k-tank-module-tracks-destroyed",
            WH40KTankModuleType.Turret => "wh40k-tank-module-turret-destroyed",
            WH40KTankModuleType.MainGun => "wh40k-tank-module-main-gun-destroyed",
            WH40KTankModuleType.Coaxial => "wh40k-tank-module-coaxial-destroyed",
            _ => "wh40k-tank-module-engine-destroyed",
        };
    }

    private static string GetModuleRestoredLocKey(WH40KTankModuleType module)
    {
        return module switch
        {
            WH40KTankModuleType.Engine => "wh40k-tank-module-engine-restored",
            WH40KTankModuleType.Tracks => "wh40k-tank-module-tracks-restored",
            WH40KTankModuleType.Turret => "wh40k-tank-module-turret-restored",
            WH40KTankModuleType.MainGun => "wh40k-tank-module-main-gun-restored",
            WH40KTankModuleType.Coaxial => "wh40k-tank-module-coaxial-restored",
            _ => "wh40k-tank-module-engine-restored",
        };
    }

    private EntityUid? GetStationOccupant(EntityUid? stationUid)
    {
        if (stationUid is not { } resolvedStation || !TryComp<StrapComponent>(resolvedStation, out var strapComp))
            return null;

        foreach (var occupant in strapComp.BuckledEntities)
        {
            if (Exists(occupant))
                return occupant;
        }

        return null;
    }

    private bool TryGetFirstAvailableStation(Entity<WH40KTankComponent> tank, out EntityUid stationUid)
    {
        foreach (var role in EntryOrder)
        {
            if (TryGetAvailableStation(tank, role, out stationUid))
                return true;
        }

        stationUid = default;
        return false;
    }

    private bool TryGetAvailableStation(Entity<WH40KTankComponent> tank, WH40KTankCrewRole role, out EntityUid stationUid)
    {
        var station = GetStationUid(tank.Comp, role);
        if (station is not { } stationCandidate ||
            !TryComp<WH40KTankStationComponent>(stationCandidate, out var stationComp) ||
            stationComp.PendingEntrant != null ||
            !TryComp<StrapComponent>(stationCandidate, out var strapComp) ||
            !strapComp.Enabled ||
            strapComp.BuckledEntities.Count != 0)
        {
            stationUid = default;
            return false;
        }

        stationUid = stationCandidate;
        return true;
    }

    private bool TryReserveFirstAvailableStation(Entity<WH40KTankComponent> tank, EntityUid user, out EntityUid stationUid)
    {
        if (!TryGetFirstAvailableStation(tank, out stationUid) ||
            !TryComp<WH40KTankStationComponent>(stationUid, out var stationComp))
        {
            stationUid = default;
            return false;
        }

        stationComp.PendingEntrant = user;
        return true;
    }

    private bool TryReserveAvailableStation(
        Entity<WH40KTankComponent> tank,
        EntityUid user,
        WH40KTankCrewRole role,
        out EntityUid stationUid)
    {
        if (!TryGetAvailableStation(tank, role, out stationUid) ||
            !TryComp<WH40KTankStationComponent>(stationUid, out var stationComp))
        {
            stationUid = default;
            return false;
        }

        stationComp.PendingEntrant = user;
        return true;
    }

    private bool HasPendingEntryReservation(WH40KTankComponent tank, EntityUid user)
    {
        foreach (var role in EntryOrder)
        {
            var station = GetStationUid(tank, role);
            if (station is not { } stationUid || !TryComp<WH40KTankStationComponent>(stationUid, out var stationComp))
                continue;

            if (stationComp.PendingEntrant == user)
                return true;
        }

        return false;
    }

    private void ClearPendingEntryReservation(EntityUid stationUid, EntityUid user)
    {
        if (!TryComp<WH40KTankStationComponent>(stationUid, out var stationComp) || stationComp.PendingEntrant != user)
            return;

        stationComp.PendingEntrant = null;
    }

    private void ClearPendingExit(EntityUid stationUid, EntityUid user)
    {
        if (!TryComp<WH40KTankStationComponent>(stationUid, out var stationComp) || stationComp.PendingExitOccupant != user)
            return;

        stationComp.PendingExitOccupant = null;
    }

    private void MoveOccupantToExitIfClear(Entity<WH40KTankStationComponent> station, EntityUid occupantUid)
    {
        if (!TryGetExitCoordinates(station, out var exitCoordinates))
            return;

        var exitMap = _map.GetMapOrInvalid(exitCoordinates.MapId);
        if (!Exists(exitMap) ||
            !TryComp(exitMap, out MetaDataComponent? metaData) ||
            metaData.EntityLifeStage >= EntityLifeStage.Terminating ||
            !TryComp<BroadphaseComponent>(exitMap, out _))
        {
            return;
        }

        if (IsExitBlocked(station, occupantUid, exitCoordinates))
            return;

        _transform.SetMapCoordinates(occupantUid, exitCoordinates);

        var occupantXform = Transform(occupantUid);
        occupantXform.ActivelyLerping = false;

        if (!TryComp<PhysicsComponent>(occupantUid, out var physics))
            return;

        _physics.SetLinearVelocity(occupantUid, Vector2.Zero, body: physics);
        _physics.SetAngularVelocity(occupantUid, 0f, body: physics);
    }

    private bool TryGetExitCoordinates(Entity<WH40KTankStationComponent> station, out MapCoordinates exitCoordinates)
    {
        exitCoordinates = MapCoordinates.Nullspace;

        if (station.Comp.Tank is not { } tankUid || !Exists(tankUid))
            return false;

        var exitCoords = new EntityCoordinates(tankUid, station.Comp.ExitOffset);
        if (!exitCoords.IsValid(EntityManager))
            return false;

        exitCoordinates = _transform.ToMapCoordinates(exitCoords);
        return exitCoordinates.MapId != MapId.Nullspace;
    }

    private bool IsExitBlocked(Entity<WH40KTankStationComponent> station, EntityUid occupantUid, MapCoordinates exitCoordinates)
    {
        var occupantBounds = _lookup.GetAABBNoContainer(occupantUid, exitCoordinates.Position, Angle.Zero);
        var intersecting = _lookup.GetEntitiesIntersecting(
            exitCoordinates.MapId,
            occupantBounds,
            LookupFlags.Dynamic | LookupFlags.Static);

        foreach (var entity in intersecting)
        {
            if (entity == occupantUid)
                continue;

            if (station.Comp.Tank is { } tankUid && IsPartOfTank(entity, tankUid))
                continue;

            if (!TryComp<FixturesComponent>(entity, out var fixturesComp))
                continue;

            foreach (var fixture in fixturesComp.Fixtures.Values)
            {
                if (fixture.Hard)
                    return true;
            }
        }

        return false;
    }

    private bool IsPartOfTank(EntityUid entity, EntityUid tankUid)
    {
        var current = entity;

        while (Exists(current))
        {
            if (current == tankUid)
                return true;

            var parent = Transform(current).ParentUid;
            if (!parent.IsValid() || parent == current)
                break;

            current = parent;
        }

        return false;
    }

    private static EntityUid? GetStationUid(WH40KTankComponent tank, WH40KTankCrewRole role)
    {
        return role switch
        {
            WH40KTankCrewRole.Driver => tank.DriverStation,
            WH40KTankCrewRole.Gunner => tank.GunnerStation,
            WH40KTankCrewRole.Commander => tank.CommanderStation,
            WH40KTankCrewRole.Loader => tank.LoaderStation,
            _ => null,
        };
    }

    private static EntityUid? GetOccupantUid(WH40KTankComponent tank, WH40KTankCrewRole role)
    {
        return role switch
        {
            WH40KTankCrewRole.Driver => tank.DriverOccupant,
            WH40KTankCrewRole.Gunner => tank.GunnerOccupant,
            WH40KTankCrewRole.Commander => tank.CommanderOccupant,
            WH40KTankCrewRole.Loader => tank.LoaderOccupant,
            _ => null,
        };
    }

    private static string GetRoleLocKey(WH40KTankCrewRole role)
    {
        return role switch
        {
            WH40KTankCrewRole.Driver => "wh40k-tank-role-driver",
            WH40KTankCrewRole.Gunner => "wh40k-tank-role-gunner",
            WH40KTankCrewRole.Commander => "wh40k-tank-role-commander",
            WH40KTankCrewRole.Loader => "wh40k-tank-role-loader",
            _ => "wh40k-tank-role-driver",
        };
    }

    private static EntityUid?[] EnumerateChildren(WH40KTankComponent tank)
    {
        return
        [
            tank.Turret,
            tank.MainHardpoint,
            tank.CoaxialHardpoint,
            tank.MainGun,
            tank.CoaxialGun,
            tank.DriverStation,
            tank.GunnerStation,
            tank.CommanderStation,
            tank.LoaderStation,
        ];
    }
}
