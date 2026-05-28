using System.Numerics;
using Content.Shared.Buckle.Components;
using Content.Shared.Buckle;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WH40K.HeavyBolter;

/// <summary>
/// Blocks hand usage and combat actions while a user is operating a deployed WH40K heavy bolter.
/// </summary>
public sealed partial class SharedWH40KHeavyBolterOperatorSystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";
    private static readonly ProtoId<TagPrototype> AirlockTag = "Airlock";
    private static readonly TimeSpan ClientPopupSpamCooldown = TimeSpan.FromSeconds(1);

    [Dependency] private  SharedBuckleSystem _buckle = default!;
    [Dependency] private  EntityLookupSystem _lookup = default!;
    [Dependency] private  INetManager _net = default!;
    [Dependency] private  SharedPopupSystem _popup = default!;
    [Dependency] private  TagSystem _tag = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;
    private readonly Dictionary<(EntityUid User, string Key), TimeSpan> _popupCooldowns = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KHeavyBolterComponent, InteractHandEvent>(
            OnBolterInteractHand,
            before: [typeof(SharedBuckleSystem)]);

        // Client-side prediction guard:
        // hard-cancel buckle attempt before visual "one-frame buckle then rollback".
        if (_net.IsClient)
            SubscribeLocalEvent<WH40KHeavyBolterComponent, StrapAttemptEvent>(OnStrapAttempt);

        SubscribeLocalEvent<BuckleComponent, UseAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<BuckleComponent, PickupAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<BuckleComponent, DropAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<BuckleComponent, ThrowAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<BuckleComponent, IsEquippingAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<BuckleComponent, IsUnequippingAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<BuckleComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<BuckleComponent, CanAttackFromContainerEvent>(OnCanAttackFromContainer);
        SubscribeLocalEvent<BuckleComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<BuckleComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<BuckleComponent, GetMeleeWeaponEvent>(OnGetMeleeWeapon);
    }

    private void OnBolterInteractHand(Entity<WH40KHeavyBolterComponent> ent, ref InteractHandEvent args)
    {

        // Allow operator click-to-unbuckle regardless of local deployed sync
        // so client prediction state cannot block unbuckle interaction.
        if (TryComp<BuckleComponent>(args.User, out var buckle) && buckle.BuckledTo == ent.Owner)
        {
            // Requested behavior: clicking bolter while already operating it unbuckles the operator.
            _buckle.TryUnbuckle((args.User, buckle), args.User, popup: true);
            args.Handled = true;
            return;
        }

        if (args.Handled)
            return;

        if (!ent.Comp.Deployed)
            return;
    }

    private void OnCancellableAttempt(EntityUid uid, BuckleComponent component, CancellableEntityEventArgs args)
    {
        if (!IsOperatingHeavyBolter(component, out _))
            return;

        args.Cancel();
    }

    private void OnInteractionAttempt(EntityUid uid, BuckleComponent component, ref InteractionAttemptEvent args)
    {
        if (!IsOperatingHeavyBolter(component, out var bolterUid))
            return;

        // Allow self-interaction with mounted bolter so repeated click can unbuckle.
        if (args.Target == bolterUid)
        {
            return;
        }

        args.Cancelled = true;
    }

    private void OnCanAttackFromContainer(EntityUid uid, BuckleComponent component, ref CanAttackFromContainerEvent args)
    {
        if (!IsOperatingHeavyBolter(component, out _))
            return;

        args.CanAttack = false;
    }

    private void OnShotAttempted(EntityUid uid, BuckleComponent component, ref ShotAttemptedEvent args)
    {
        if (!IsOperatingHeavyBolter(component, out var bolterUid))
            return;

        // While operating emplacement, only emplacement gun fire is allowed.
        if (args.Used.Owner != bolterUid)
        {
            args.Cancel();
            return;
        }
    }

    private void OnAttackAttempt(EntityUid uid, BuckleComponent component, AttackAttemptEvent args)
    {
        if (!IsOperatingHeavyBolter(component, out var bolterUid))
            return;

        var allowMountedGunPath =
            args.Weapon == null &&
            !args.Disarm &&
            args.Target == null &&
            HasComp<GunComponent>(bolterUid);

        if (allowMountedGunPath)
        {
            return;
        }

        args.Cancel();
    }

    private void OnGetMeleeWeapon(EntityUid uid, BuckleComponent component, GetMeleeWeaponEvent args)
    {
        if (!IsOperatingHeavyBolter(component, out _))
            return;

        // Hard-stop melee/fists while operating emplacement.
        args.Weapon = null;
        args.Handled = true;
    }

    private void OnStrapAttempt(Entity<WH40KHeavyBolterComponent> ent, ref StrapAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!IsOperatorSpotOccupied(ent.Owner, args.Buckle.Owner, args.Strap.Comp.BuckleOffset))
            return;

        args.Cancelled = true;

        if (args.Popup && args.User is { } user)
        {
            if (TryTakePopupCooldown(user, "wh40k-heavy-bolter-operator-space-blocked-wall"))
                _popup.PopupClient(Loc.GetString("wh40k-heavy-bolter-operator-space-blocked-wall"), ent, user);
        }
    }

    private bool IsOperatorSpotOccupied(EntityUid bolterUid, EntityUid operatorUid, Vector2 rearLocalOffset)
    {
        var operatorCoordinates = new EntityCoordinates(bolterUid, rearLocalOffset);
        if (!operatorCoordinates.IsValid(EntityManager))
            return true;

        var operatorMapCoordinates = _transform.ToMapCoordinates(operatorCoordinates);
        if (operatorMapCoordinates.MapId == MapId.Nullspace)
            return true;

        var operatorBounds = _lookup.GetAABBNoContainer(operatorUid, operatorMapCoordinates.Position, Angle.Zero);
        var intersecting = _lookup.GetEntitiesIntersecting(
            operatorMapCoordinates.MapId,
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

    private bool IsOperatingHeavyBolter(BuckleComponent buckle, out EntityUid bolterUid)
    {
        bolterUid = default;

        if (!buckle.Buckled || buckle.BuckledTo is not { } strappedTo)
            return false;

        if (!TryComp<WH40KHeavyBolterComponent>(strappedTo, out var bolterComp))
            return false;

        // Only treat as active operation while the emplacement is truly operable.
        if (!bolterComp.Deployed)
            return false;

        if (!TryComp<StrapComponent>(strappedTo, out var strapComp) ||
            !strapComp.Enabled)
        {
            return false;
        }

        if (!Transform(strappedTo).Anchored)
            return false;

        bolterUid = strappedTo;
        return true;
    }

    private bool TryTakePopupCooldown(EntityUid user, string key)
    {
        var now = _timing.CurTime;
        var userKey = (user, key);

        if (_popupCooldowns.TryGetValue(userKey, out var nextAllowedAt) && nextAllowedAt > now)
            return false;

        _popupCooldowns[userKey] = now + ClientPopupSpamCooldown;
        return true;
    }

    private bool IsRearRotationObstacle(EntityUid entity)
    {
        return _tag.HasTag(entity, WallTag) ||
               _tag.HasTag(entity, WindowTag) ||
               _tag.HasTag(entity, AirlockTag);
    }
}

