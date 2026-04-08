using System.Numerics;
using Content.Server.Popups;
using Content.Shared._WH40K.HeavyBolter;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Content.Server._WH40K.Localizations;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.HeavyBolter;

public sealed class WH40KHeavyBolterSystem : EntitySystem
{
    private const float ArcDotEpsilon = 0.001f;
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";
    private static readonly ProtoId<TagPrototype> AirlockTag = "Airlock";
    private static readonly TimeSpan PopupSpamCooldown = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AttemptShootMessageCooldown = TimeSpan.FromSeconds(1);

    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly FixtureSystem _fixture = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly WH40KPlayerCultureTracker _culture = default!;
    private readonly Dictionary<(EntityUid User, string Key), TimeSpan> _popupCooldowns = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KHeavyBolterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, HandheldEntityPlacementAttemptEvent>(OnPlacementAttempt);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, HandheldEntityPlacementCompleteEvent>(OnPlacementComplete);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, HandheldEntityFoldAttemptEvent>(OnFoldAttempt);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, HandheldEntityFoldCompleteEvent>(OnFoldComplete);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, StrapAttemptEvent>(OnStrapAttempt);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<BuckleComponent, MobStateChangedEvent>(OnOperatorMobStateChanged);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, WH40KHeavyBolterRotateLeftActionEvent>(OnRotateLeftAction);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, WH40KHeavyBolterRotateRightActionEvent>(OnRotateRightAction);
        SubscribeLocalEvent<ActionComponent, ActionPerformedEvent>(OnActionPerformed);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WH40KHeavyBolterComponent, StrapComponent>();
        while (query.MoveNext(out var uid, out var bolterComp, out var strapComp))
        {
            if (!bolterComp.Deployed || strapComp.BuckledEntities.Count == 0)
                continue;

            foreach (var buckledUid in strapComp.BuckledEntities)
            {
                if (!TryComp<BuckleComponent>(buckledUid, out var buckleComp) || buckleComp.BuckledTo != uid)
                    continue;

                if (!buckleComp.DontCollide)
                    _buckle.SetDontCollide(buckledUid, true, buckleComp);

                SnapOperatorToRearOffset((uid, bolterComp), (buckledUid, buckleComp), strapComp.BuckleOffset, resetVelocity: true);
            }
        }
    }

    private void OnMapInit(Entity<WH40KHeavyBolterComponent> bolter, ref MapInitEvent args)
    {
        EnsureActionEntities(bolter);
        SyncRotateActionCooldowns(bolter);
        RefreshGunModifiers(bolter);
        NormalizeDeployedState(bolter);
        SyncMagazineSlotLock(bolter);
        SyncMagazineVisualState(bolter);
    }

    private void OnPlacementAttempt(Entity<WH40KHeavyBolterComponent> bolter, ref HandheldEntityPlacementAttemptEvent args)
    {
        NormalizeDeployedState(bolter);
        if (bolter.Comp.Deployed)
        {
            args.Cancel();
            return;
        }

        if (TryGetCooldownRemainingSeconds(bolter, out var remaining))
        {
            if (TryTakeUserPopupCooldown(args.User, "wh40k-heavy-bolter-toggle-cooldown"))
            {
                _popup.PopupEntity(
                    Loc.GetString("wh40k-heavy-bolter-toggle-cooldown", ("seconds", remaining)),
                    bolter,
                    args.User);
            }

            args.Cancel();
            return;
        }

        args.DeployDelay = bolter.Comp.DeployDelay;
        args.BreakOnMove = true;
        args.BreakOnHandChange = true;
        args.NeedHand = true;
    }

    private void OnPlacementComplete(Entity<WH40KHeavyBolterComponent> bolter, ref HandheldEntityPlacementCompleteEvent args)
    {
        if (args.Handled)
            return;

        if (bolter.Comp.Deployed || TryGetCooldownRemainingSeconds(bolter, out _))
            return;

        var coords = args.Coordinates;

        bolter.Comp.Deployed = true;
        bolter.Comp.LastToggleAt = _timing.CurTime;
        Dirty(bolter);

        var xform = Transform(bolter);
        var rotation = args.Direction.ToAngle();
        _transform.SetCoordinates(bolter, xform, coords, rotation);
        _transform.AnchorEntity((bolter, xform));

        if (_fixture.GetFixtureOrNull(bolter, bolter.Comp.FixtureId) is { } fixture)
            _physics.SetHard(bolter, fixture, true);

        _buckle.StrapSetEnabled(bolter, true);
        _appearance.SetData(bolter, WH40KHeavyBolterVisuals.State, WH40KHeavyBolterVisualState.Deployed);
        _audio.PlayPredicted(bolter.Comp.DeploySound, bolter, args.User);
        SyncMagazineSlotLock(bolter);
        SyncMagazineVisualState(bolter);

        _popup.PopupPredicted(
            Loc.GetString("wh40k-heavy-bolter-deploy-finish-self", ("bolter", bolter)),
            Loc.GetString("wh40k-heavy-bolter-deploy-finish-others", ("user", args.User), ("bolter", bolter)),
            bolter,
            args.User);

        args.Handled = true;
    }

    private void OnGetVerbs(Entity<WH40KHeavyBolterComponent> bolter, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        NormalizeDeployedState(bolter);
        if (!bolter.Comp.Deployed)
            return;

        var user = args.User;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wh40k-heavy-bolter-verb-fold"),
            Act = () => TryStartFold(bolter, user),
        });
    }

    private void TryStartFold(Entity<WH40KHeavyBolterComponent> bolter, EntityUid user)
    {
        if (!CanOperateBolter(bolter))
        {
            if (TryTakeUserPopupCooldown(user, "wh40k-heavy-bolter-not-deployed"))
                _popup.PopupEntity(_culture.GetPlayerString(user, "wh40k-heavy-bolter-not-deployed", ("bolter", bolter)), bolter, user);

            return;
        }

        if (TryGetCooldownRemainingSeconds(bolter, out var remaining))
        {
            if (TryTakeUserPopupCooldown(user, "wh40k-heavy-bolter-toggle-cooldown"))
            {
                _popup.PopupEntity(
                    Loc.GetString("wh40k-heavy-bolter-toggle-cooldown", ("seconds", remaining)),
                    bolter,
                    user);
            }

            return;
        }

        var foldRequest = new HandheldEntityFoldRequestEvent(user);
        RaiseLocalEvent(bolter.Owner, foldRequest);
        if (!foldRequest.Handled)
            return;

        _popup.PopupClient(_culture.GetPlayerString(user, "wh40k-heavy-bolter-fold-start", ("bolter", bolter)), user, user);
    }

    private void OnFoldAttempt(Entity<WH40KHeavyBolterComponent> bolter, ref HandheldEntityFoldAttemptEvent args)
    {
        if (!CanOperateBolter(bolter))
        {
            args.Cancel();
            return;
        }

        if (TryGetCooldownRemainingSeconds(bolter, out _))
        {
            args.Cancel();
            return;
        }

        args.FoldDelay = bolter.Comp.FoldDelay;
        args.BreakOnMove = true;
        args.BreakOnHandChange = true;
        args.NeedHand = true;
    }

    private void OnFoldComplete(Entity<WH40KHeavyBolterComponent> bolter, ref HandheldEntityFoldCompleteEvent args)
    {
        if (args.Handled)
            return;

        FoldBolter(bolter, args.User);
        args.Handled = true;
    }

    private void FoldBolter(Entity<WH40KHeavyBolterComponent> bolter, EntityUid user)
    {
        if (!bolter.Comp.Deployed)
            return;

        bolter.Comp.Deployed = false;
        bolter.Comp.LastToggleAt = _timing.CurTime;
        Dirty(bolter);

        _buckle.StrapSetEnabled(bolter, false);

        if (_fixture.GetFixtureOrNull(bolter, bolter.Comp.FixtureId) is { } fixture)
            _physics.SetHard(bolter, fixture, false);

        _appearance.SetData(bolter, WH40KHeavyBolterVisuals.State, WH40KHeavyBolterVisualState.Folded);

        var xform = Transform(bolter);
        if (xform.Anchored)
            _transform.Unanchor(bolter, xform);

        _audio.PlayPredicted(bolter.Comp.FoldSound, bolter, user);
        SyncMagazineSlotLock(bolter);
        SyncMagazineVisualState(bolter);
        _popup.PopupPredicted(
            Loc.GetString("wh40k-heavy-bolter-fold-finish-self", ("bolter", bolter)),
            Loc.GetString("wh40k-heavy-bolter-fold-finish-others", ("user", user), ("bolter", bolter)),
            bolter,
            user);
    }

    private void OnAnchorStateChanged(Entity<WH40KHeavyBolterComponent> bolter, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored || !bolter.Comp.Deployed)
            return;

        bolter.Comp.Deployed = false;
        Dirty(bolter);
        NormalizeDeployedState(bolter);
        SyncMagazineSlotLock(bolter);
        SyncMagazineVisualState(bolter);
    }

    private void OnAttemptShoot(Entity<WH40KHeavyBolterComponent> bolter, ref AttemptShootEvent args)
    {

        if (!CanOperateBolter(bolter))
        {
            args.Cancelled = true;
            if (TryTakeUserPopupCooldown(args.User, "wh40k-heavy-bolter-not-deployed-shoot", AttemptShootMessageCooldown))
                args.Message = Loc.GetString("wh40k-heavy-bolter-not-deployed", ("bolter", bolter));

            return;
        }

        if (bolter.Comp.RequireBuckledOperator &&
            (!TryComp<BuckleComponent>(args.User, out var buckle) || buckle.BuckledTo != bolter.Owner))
        {
            args.Cancelled = true;
            if (TryTakeUserPopupCooldown(args.User, "wh40k-heavy-bolter-operator-required", AttemptShootMessageCooldown))
                args.Message = Loc.GetString("wh40k-heavy-bolter-operator-required");

            return;
        }

        if (!TryComp<GunComponent>(bolter, out var gunComp))
        {
            args.Cancelled = true;
            if (TryTakeUserPopupCooldown(args.User, "wh40k-heavy-bolter-arc-limit", AttemptShootMessageCooldown))
                args.Message = Loc.GetString("wh40k-heavy-bolter-arc-limit");

            return;
        }

        if (!TryGetShotDirection(bolter, gunComp, out var shotDirection))
        {
            args.Cancelled = true;
            if (TryTakeUserPopupCooldown(args.User, "wh40k-heavy-bolter-arc-limit", AttemptShootMessageCooldown))
                args.Message = Loc.GetString("wh40k-heavy-bolter-arc-limit");

            return;
        }

        if (!TryGetForwardDirection(bolter, out var forwardDirection))
        {
            args.Cancelled = true;
            if (TryTakeUserPopupCooldown(args.User, "wh40k-heavy-bolter-arc-limit", AttemptShootMessageCooldown))
                args.Message = Loc.GetString("wh40k-heavy-bolter-arc-limit");

            return;
        }

        var halfArc = Math.Clamp(bolter.Comp.FireArcDegrees, 0.1f, 360f) * 0.5f;
        if (halfArc >= 179.9f)
            return;

        // Gate by predicted shot vector from mounted shot origin (can be barrel-shifted), not by operator/mouse origin.
        var dot = Vector2.Dot(forwardDirection, shotDirection);
        var minDot = MathF.Cos((MathF.PI / 180f) * halfArc);
        if (dot >= minDot + ArcDotEpsilon)
        {
            return;
        }

        args.Cancelled = true;
        if (TryTakeUserPopupCooldown(args.User, "wh40k-heavy-bolter-arc-limit", AttemptShootMessageCooldown))
            args.Message = Loc.GetString("wh40k-heavy-bolter-arc-limit");
    }

    private void OnRotateLeftAction(Entity<WH40KHeavyBolterComponent> bolter, ref WH40KHeavyBolterRotateLeftActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryRotateBolter(bolter, args.Performer, -1f);
    }

    private void OnRotateRightAction(Entity<WH40KHeavyBolterComponent> bolter, ref WH40KHeavyBolterRotateRightActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryRotateBolter(bolter, args.Performer, 1f);
    }

    private void OnActionPerformed(Entity<ActionComponent> action, ref ActionPerformedEvent args)
    {
        if (!IsRotateAction(action.Owner))
            return;

        if (action.Comp.Container is not { } containerUid)
            return;

        if (!TryComp<WH40KHeavyBolterComponent>(containerUid, out var bolterComp))
            return;

        // Re-apply shared rotate cooldown after action pipeline finalizes.
        // Action runtime clears cooldown for the pressed action by default, but
        // heavy bolter requires both rotate buttons to stay in sync.
        SyncRotateActionCooldowns((containerUid, bolterComp), args.Performer);
    }

    private bool TryGetShotDirection(Entity<WH40KHeavyBolterComponent> bolter, GunComponent gun, out Vector2 shotDirection)
    {
        shotDirection = Vector2.Zero;
        if (gun.ShootCoordinates is not { } targetCoordinates)
        {
            return false;
        }

        var fromMap = GetShotOriginMapCoordinates(bolter);
        var toMap = _transform.ToMapCoordinates(targetCoordinates);
        if (fromMap.MapId == MapId.Nullspace || toMap.MapId == MapId.Nullspace || fromMap.MapId != toMap.MapId)
        {
            return false;
        }

        var vector = toMap.Position - fromMap.Position;
        if (vector.LengthSquared() <= 0.0001f)
        {
            return false;
        }

        shotDirection = vector.Normalized();
        return true;
    }

    private MapCoordinates GetShotOriginMapCoordinates(Entity<WH40KHeavyBolterComponent> bolter)
    {
        if (TryComp<BuckleMountedGunComponent>(bolter, out var mounted) &&
            mounted.ShootOriginOffset.LengthSquared() > 0.0001f)
        {
            var localCoords = new EntityCoordinates(bolter, mounted.ShootOriginOffset);
            return _transform.ToMapCoordinates(localCoords);
        }

        return _transform.GetMapCoordinates(bolter);
    }

    private bool TryGetForwardDirection(Entity<WH40KHeavyBolterComponent> bolter, out Vector2 forwardDirection)
    {
        forwardDirection = Vector2.Zero;

        // Primary source: strap rear offset (operator sits at rear, so forward is opposite).
        if (TryComp<StrapComponent>(bolter, out var strap))
        {
            var rearLocal = strap.BuckleOffset;
            if (rearLocal.LengthSquared() > 0.0001f)
            {
                var fromRearToFront = _transform.GetWorldRotation(bolter).RotateVec(-rearLocal);
                if (fromRearToFront.LengthSquared() > 0.0001f)
                {
                    forwardDirection = fromRearToFront.Normalized();
                    return true;
                }
            }
        }

        var fallback = _transform.GetWorldRotation(bolter).ToWorldVec();
        if (fallback.LengthSquared() <= 0.0001f)
        {
            return false;
        }

        forwardDirection = fallback.Normalized();
        return true;
    }

    private bool CanOperateBolter(Entity<WH40KHeavyBolterComponent> bolter)
    {
        if (!bolter.Comp.Deployed)
            return false;

        var xform = Transform(bolter);
        if (!xform.Anchored)
            return false;

        return !_container.IsEntityInContainer(bolter);
    }

    private void NormalizeDeployedState(Entity<WH40KHeavyBolterComponent> bolter)
    {
        if (CanOperateBolter(bolter))
        {
            _buckle.StrapSetEnabled(bolter, true);
            _appearance.SetData(bolter, WH40KHeavyBolterVisuals.State, WH40KHeavyBolterVisualState.Deployed);

            if (_fixture.GetFixtureOrNull(bolter, bolter.Comp.FixtureId) is { } deployedFixture)
                _physics.SetHard(bolter, deployedFixture, true);

            SyncMagazineSlotLock(bolter);
            SyncMagazineVisualState(bolter);
            return;
        }

        if (bolter.Comp.Deployed)
        {
            bolter.Comp.Deployed = false;
            Dirty(bolter);
        }

        _buckle.StrapSetEnabled(bolter, false);
        _appearance.SetData(bolter, WH40KHeavyBolterVisuals.State, WH40KHeavyBolterVisualState.Folded);

        if (_fixture.GetFixtureOrNull(bolter, bolter.Comp.FixtureId) is { } foldedFixture)
            _physics.SetHard(bolter, foldedFixture, false);

        SyncMagazineSlotLock(bolter);
        SyncMagazineVisualState(bolter);
    }

    private void SyncMagazineSlotLock(Entity<WH40KHeavyBolterComponent> bolter)
    {
        if (!TryComp<ItemSlotsComponent>(bolter, out var slots))
            return;

        // While folded / non-operable, magazine interactions are disabled.
        _itemSlots.SetLock(bolter, SharedGunSystem.MagazineSlot, !CanOperateBolter(bolter), slots);
    }

    private void SyncMagazineVisualState(Entity<WH40KHeavyBolterComponent> bolter)
    {
        var showMagazine = CanOperateBolter(bolter) && HasMagazineLoaded(bolter.Owner);
        _appearance.SetData(bolter, AmmoVisuals.MagLoaded, showMagazine);
    }

    private bool HasMagazineLoaded(EntityUid bolterUid)
    {
        if (!_container.TryGetContainer(bolterUid, SharedGunSystem.MagazineSlot, out var container))
            return false;

        return container.ContainedEntities.Count > 0;
    }

    private bool TryGetCooldownRemainingSeconds(Entity<WH40KHeavyBolterComponent> bolter, out int remainingSeconds)
    {
        remainingSeconds = 0;
        var nextReadyAt = bolter.Comp.LastToggleAt + bolter.Comp.ToggleCooldown;
        if (nextReadyAt <= _timing.CurTime)
            return false;

        remainingSeconds = Math.Max(1, (int) Math.Ceiling((nextReadyAt - _timing.CurTime).TotalSeconds));
        return true;
    }

    private void OnStrapped(Entity<WH40KHeavyBolterComponent> bolter, ref StrappedEvent args)
    {

        DropHeldItems(args.Buckle.Owner);
        GrantOperatorActions(bolter, args.Buckle.Owner);
        _buckle.SetDontCollide(args.Buckle, true);
        SnapOperatorToRearOffset(bolter, args.Buckle, args.Strap.Comp.BuckleOffset, resetVelocity: true);
    }

    private void OnStrapAttempt(Entity<WH40KHeavyBolterComponent> bolter, ref StrapAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (IsRearOperatorSpotOccupied(bolter.Owner, args.Buckle.Owner, args.Strap.Comp.BuckleOffset))
        {
            args.Cancelled = true;
            _buckle.SetDontCollide(args.Buckle, false);

            if (args.Popup && args.User is { } user)
            {
                if (TryTakeUserPopupCooldown(user, "wh40k-heavy-bolter-operator-space-blocked-wall"))
                {
                    _popup.PopupEntity(
                        Loc.GetString("wh40k-heavy-bolter-operator-space-blocked-wall"),
                        bolter,
                        user);
                }
            }

            return;
        }

        // Ensure the initial buckle placement is not immediately collision-corrected by the emplacement.
        _buckle.SetDontCollide(args.Buckle, true);
    }

    private void OnUnstrapped(Entity<WH40KHeavyBolterComponent> bolter, ref UnstrappedEvent args)
    {

        _actions.RemoveProvidedActions(args.Buckle.Owner, bolter);
        MoveOperatorToRearExit(bolter, args.Buckle.Owner, args.Strap.Comp.BuckleOffset);
        _buckle.SetDontCollide(args.Buckle, false);
    }

    private void OnOperatorMobStateChanged(Entity<BuckleComponent> buckle, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        if (!buckle.Comp.Buckled || buckle.Comp.BuckledTo is not { } strappedTo)
            return;

        if (!HasComp<WH40KHeavyBolterComponent>(strappedTo))
            return;

        _buckle.TryUnbuckle(buckle.Owner, buckle.Owner, buckle.Comp, popup: false);
    }

    private void SnapOperatorToRearOffset(
        Entity<WH40KHeavyBolterComponent> bolter,
        Entity<BuckleComponent> buckle,
        Vector2 rearLocalOffset,
        bool resetVelocity)
    {
        var buckleXform = Transform(buckle);
        var buckleCoords = new EntityCoordinates(bolter, rearLocalOffset);
        _transform.SetCoordinates(buckle, buckleXform, buckleCoords, Angle.Zero);
        buckleXform.ActivelyLerping = false;

        if (!resetVelocity || !TryComp<PhysicsComponent>(buckle, out var physics))
            return;

        _physics.SetLinearVelocity(buckle, Vector2.Zero, body: physics);
        _physics.SetAngularVelocity(buckle, 0f, body: physics);
    }

    private void MoveOperatorToRearExit(
        Entity<WH40KHeavyBolterComponent> bolter,
        EntityUid operatorUid,
        Vector2 rearLocalOffset)
    {
        if (rearLocalOffset.LengthSquared() <= 0.0001f)
            return;

        var bolterMap = _transform.GetMapCoordinates(bolter);
        if (bolterMap.MapId == MapId.Nullspace)
            return;

        var rearWorld = _transform.GetWorldRotation(bolter).RotateVec(rearLocalOffset);
        if (rearWorld.LengthSquared() <= 0.0001f)
            return;

        var rearDirection = rearWorld.Normalized();
        var exitDistance = MathF.Max(rearLocalOffset.Length() + 0.35f, 0.9f);
        var exitCoordinates = new MapCoordinates(bolterMap.Position + rearDirection * exitDistance, bolterMap.MapId);
        _transform.SetMapCoordinates(operatorUid, exitCoordinates);

        var operatorXform = Transform(operatorUid);
        operatorXform.ActivelyLerping = false;

        if (!TryComp<PhysicsComponent>(operatorUid, out var physics))
            return;

        _physics.SetLinearVelocity(operatorUid, Vector2.Zero, body: physics);
        _physics.SetAngularVelocity(operatorUid, 0f, body: physics);
    }

    private bool IsOperatorControlAllowed(Entity<WH40KHeavyBolterComponent> bolter, EntityUid user)
    {
        if (!CanOperateBolter(bolter))
            return false;

        if (!bolter.Comp.RequireBuckledOperator)
            return true;

        return TryComp<BuckleComponent>(user, out var buckle) &&
               buckle.BuckledTo == bolter.Owner;
    }

    private bool TryRotateBolter(Entity<WH40KHeavyBolterComponent> bolter, EntityUid performer, float directionSign)
    {
        if (!IsOperatorControlAllowed(bolter, performer))
            return false;

        var nextReadyAt = bolter.Comp.LastRotateAt + bolter.Comp.RotateCooldown;
        if (nextReadyAt > _timing.CurTime)
        {
            SyncRotateActionCooldowns(bolter, performer);
            return false;
        }

        if (TryComp<StrapComponent>(bolter, out var currentStrapComp))
        {
            var currentWorldRotation = _transform.GetWorldRotation(bolter);
            var previewStep = Angle.FromDegrees(MathF.Abs(bolter.Comp.RotateStepDegrees) * directionSign);
            var nextWorldRotation = currentWorldRotation + previewStep;

            foreach (var buckledUid in currentStrapComp.BuckledEntities)
            {
                if (!TryComp<BuckleComponent>(buckledUid, out var buckleComp) || buckleComp.BuckledTo != bolter.Owner)
                    continue;

                if (IsRearOperatorSpotOccupied(bolter.Owner, buckledUid, currentStrapComp.BuckleOffset, nextWorldRotation))
                {
                    if (TryTakeUserPopupCooldown(performer, "wh40k-heavy-bolter-rotate-space-blocked"))
                    {
                        _popup.PopupEntity(
                            Loc.GetString("wh40k-heavy-bolter-rotate-space-blocked"),
                            bolter,
                            performer);
                    }

                    return false;
                }
            }
        }

        var xform = Transform(bolter);
        var step = Angle.FromDegrees(MathF.Abs(bolter.Comp.RotateStepDegrees) * directionSign);
        _transform.SetLocalRotation(bolter, xform.LocalRotation + step, xform);

        bolter.Comp.LastRotateAt = _timing.CurTime;
        SyncRotateActionCooldowns(bolter, performer);

        if (TryComp<StrapComponent>(bolter, out var strapComp))
            SnapBuckledOperatorsToRear(bolter, strapComp);

        return true;
    }

    private void SnapBuckledOperatorsToRear(Entity<WH40KHeavyBolterComponent> bolter, StrapComponent strapComp)
    {
        foreach (var buckledUid in strapComp.BuckledEntities)
        {
            if (!TryComp<BuckleComponent>(buckledUid, out var buckleComp) || buckleComp.BuckledTo != bolter.Owner)
                continue;

            if (!buckleComp.DontCollide)
                _buckle.SetDontCollide(buckledUid, true, buckleComp);

            SnapOperatorToRearOffset((bolter.Owner, bolter.Comp), (buckledUid, buckleComp), strapComp.BuckleOffset, resetVelocity: true);
        }
    }

    private void GrantOperatorActions(Entity<WH40KHeavyBolterComponent> bolter, EntityUid user)
    {
        EnsureActionEntities(bolter);

        _actions.AddAction(user, ref bolter.Comp.RotateLeftActionEntity, bolter.Comp.RotateLeftAction, bolter);
        _actions.AddAction(user, ref bolter.Comp.RotateRightActionEntity, bolter.Comp.RotateRightAction, bolter);

        SyncRotateActionCooldowns(bolter, user);
    }

    private void EnsureActionEntities(Entity<WH40KHeavyBolterComponent> bolter)
    {
        _actionContainer.EnsureAction(bolter, ref bolter.Comp.RotateLeftActionEntity, bolter.Comp.RotateLeftAction);
        _actionContainer.EnsureAction(bolter, ref bolter.Comp.RotateRightActionEntity, bolter.Comp.RotateRightAction);
    }

    private void SyncRotateActionCooldowns(Entity<WH40KHeavyBolterComponent> bolter, EntityUid? user = null)
    {
        var start = bolter.Comp.LastRotateAt;
        var end = start + bolter.Comp.RotateCooldown;

        if (end <= _timing.CurTime)
        {
            _actions.ClearCooldown(bolter.Comp.RotateLeftActionEntity);
            _actions.ClearCooldown(bolter.Comp.RotateRightActionEntity);
        }
        else
        {
            _actions.SetCooldown(bolter.Comp.RotateLeftActionEntity, start, end);
            _actions.SetCooldown(bolter.Comp.RotateRightActionEntity, start, end);
        }

        if (user is { } preferredUser)
        {
            SyncRotateActionCooldownsForUser(bolter.Owner, preferredUser, start, end);
            return;
        }

        if (!TryComp<StrapComponent>(bolter, out var strap))
            return;

        foreach (var buckledUid in strap.BuckledEntities)
        {
            if (!TryComp<BuckleComponent>(buckledUid, out var buckle) || buckle.BuckledTo != bolter.Owner)
                continue;

            SyncRotateActionCooldownsForUser(bolter.Owner, buckledUid, start, end);
        }
    }

    private void SyncRotateActionCooldownsForUser(
        EntityUid bolterUid,
        EntityUid user,
        TimeSpan start,
        TimeSpan end)
    {
        foreach (var action in _actions.GetActions(user))
        {
            if (action.Comp.Container != bolterUid || !IsRotateAction(action.Owner))
                continue;

            if (end <= _timing.CurTime)
                _actions.ClearCooldown((action.Owner, action.Comp));
            else
                _actions.SetCooldown((action.Owner, action.Comp), start, end);
        }
    }

    private bool IsRotateAction(EntityUid actionUid)
    {
        if (!TryComp<InstantActionComponent>(actionUid, out var instant) || instant.Event == null)
            return false;

        return instant.Event is WH40KHeavyBolterRotateLeftActionEvent or WH40KHeavyBolterRotateRightActionEvent;
    }

    private void RefreshGunModifiers(Entity<WH40KHeavyBolterComponent> bolter)
    {
        if (!TryComp<GunComponent>(bolter, out var gunComp))
            return;

        _gun.RefreshModifiers((bolter.Owner, gunComp));
    }

    private void DropHeldItems(EntityUid user)
    {
        if (!TryComp<HandsComponent>(user, out var hands))
            return;

        foreach (var hand in _hands.EnumerateHands((user, hands)))
        {
            var held = _hands.GetHeldItem((user, hands), hand);
            if (held == null)
                continue;

            _hands.TryDrop((user, hands), hand, checkActionBlocker: false);
        }
    }

    private bool IsRearOperatorSpotOccupied(
        EntityUid bolterUid,
        EntityUid operatorUid,
        Vector2 rearLocalOffset,
        Angle? worldRotationOverride = null)
    {
        var bolterMapCoordinates = _transform.GetMapCoordinates(bolterUid);
        if (bolterMapCoordinates.MapId == MapId.Nullspace)
            return true;

        var rotation = worldRotationOverride ?? _transform.GetWorldRotation(bolterUid);
        var operatorMapPosition = bolterMapCoordinates.Position + rotation.RotateVec(rearLocalOffset);

        var operatorBounds = _lookup.GetAABBNoContainer(operatorUid, operatorMapPosition, Angle.Zero);
        var intersecting = _lookup.GetEntitiesIntersecting(
            bolterMapCoordinates.MapId,
            operatorBounds,
            LookupFlags.Dynamic | LookupFlags.Static);

        foreach (var entity in intersecting)
        {
            if (entity == bolterUid || entity == operatorUid)
                continue;

            if (IsRearRotationObstacle(entity))
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

    private bool IsRearRotationObstacle(EntityUid entity)
    {
        return _tag.HasTag(entity, WallTag) ||
               _tag.HasTag(entity, WindowTag) ||
               _tag.HasTag(entity, AirlockTag);
    }
}

