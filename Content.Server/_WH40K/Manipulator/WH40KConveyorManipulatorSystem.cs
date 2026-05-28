using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Construction.Components;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Server.Stack;
using Content.Shared._WH40K.Manipulator;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Interaction.Components;
using Content.Shared.Maps;
using Content.Shared.Materials;
using Content.Shared.Physics;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server._WH40K.Localizations;

namespace Content.Server._WH40K.Manipulator;

/// <summary>
/// Rotation-aware left->right item manipulator with smart receiver feeding.
/// </summary>
public sealed partial class WH40KConveyorManipulatorSystem : EntitySystem
{
    private static readonly TimeSpan EmptyInputRetryDelay = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan IncompatibleItemRetryDelay = TimeSpan.FromSeconds(0.75);
    private static readonly TimeSpan ReceiverCapacityRetryDelay = TimeSpan.FromSeconds(0.35);
    private static readonly TimeSpan NoPowerRetryDelay = TimeSpan.FromSeconds(1);

    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  IRobustRandom _random = default!;
    [Dependency] private  SharedMapSystem _map = default!;
    [Dependency] private  EntityLookupSystem _lookup = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;
    [Dependency] private  SharedContainerSystem _container = default!;
    [Dependency] private  EntityWhitelistSystem _whitelist = default!;
    [Dependency] private  TurfSystem _turf = default!;
    [Dependency] private  MaterialStorageSystem _materialStorage = default!;
    [Dependency] private  MaterialReclaimerSystem _materialReclaimer = default!;
    [Dependency] private  StackSystem _stack = default!;
    [Dependency] private  ItemSlotsSystem _itemSlots = default!;
    [Dependency] private  _WH40K.Diagnostics.WH40KNetDiagAttributionSystem _attribution = default!;
    [Dependency] private  WH40KPlayerCultureTracker _culture = default!;

    private readonly HashSet<EntityUid> _tileEntities = new();
    private readonly List<EntityUid> _leftCandidates = new();
    private readonly List<ReceiverCandidate> _rightReceivers = new();
    private readonly List<Vector2i> _placementTiles = new();
    private readonly Dictionary<EntityUid, EntityUid> _claimedItems = new();
    private readonly Dictionary<EntityUid, ActiveTransfer> _activeTransfers = new();

    private readonly CollisionGroup _placementCollisionMask =
        CollisionGroup.Impassable | CollisionGroup.MidImpassable | CollisionGroup.LowImpassable;

    private TimeSpan _nextClaimCleanup;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KConveyorManipulatorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KConveyorManipulatorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KConveyorManipulatorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<WH40KConveyorManipulatorComponent> ent, ref MapInitEvent args)
    {
        var container = _container.EnsureContainer<Container>(ent.Owner, WH40KConveyorManipulatorComponent.TransferContainerId);
        var xform = Transform(ent);

        if (container.ContainedEntities.Count > 0)
        {
            foreach (var item in container.ContainedEntities.ToArray())
            {
                _container.Remove(item, container, destination: xform.Coordinates);
                _transform.DropNextTo(item, ent.Owner);
            }
        }

        ReleaseClaim(ent.Comp.ActiveItem);
        ent.Comp.SelectionCursor = 0;
        var stateDirty = ApplyOperationalState(ent.Owner, ent.Comp, busy: false, activeItem: null, WH40KManipulatorStatus.Idle);

        var cooldown = MathF.Max(0.05f, ent.Comp.TransferCooldown);
        ent.Comp.NextTransferAt = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(0f, cooldown));

        _activeTransfers.Remove(ent.Owner);
        if (!stateDirty)
            Dirty(ent);
    }

    private void OnShutdown(Entity<WH40KConveyorManipulatorComponent> ent, ref ComponentShutdown args)
    {
        _activeTransfers.Remove(ent.Owner);
        ReleaseClaim(ent.Comp.ActiveItem);
        ReleaseClaimsOwnedBy(ent.Owner);

        if (!_container.TryGetContainer(ent.Owner, WH40KConveyorManipulatorComponent.TransferContainerId, out var container))
            return;

        var xform = Transform(ent);
        foreach (var item in container.ContainedEntities.ToArray())
        {
            _container.Remove(item, container, destination: xform.Coordinates);
            _transform.DropNextTo(item, ent.Owner);
        }
    }

    private void OnExamined(Entity<WH40KConveyorManipulatorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using var scope = _culture.CreateScope(args.Examiner);

        var statusKey = ent.Comp.Status switch
        {
            WH40KManipulatorStatus.Busy => "wh40k-manipulator-status-busy",
            WH40KManipulatorStatus.WaitingForItem => "wh40k-manipulator-status-waiting-item",
            WH40KManipulatorStatus.WaitingForCompatibleItem => "wh40k-manipulator-status-waiting-compatible",
            WH40KManipulatorStatus.WaitingForReceiverCapacity => "wh40k-manipulator-status-waiting-capacity",
            WH40KManipulatorStatus.NoPower => "wh40k-manipulator-status-no-power",
            _ => "wh40k-manipulator-status-idle",
        };

        args.PushMarkup(Loc.GetString(
            "wh40k-manipulator-examine-status",
            ("status", Loc.GetString(statusKey))));

        var xform = Transform(ent);
        if (!TryGetSides(xform, out var sideData))
            return;

        args.PushMarkup(Loc.GetString(
            "wh40k-manipulator-examine-flow",
            ("from", Loc.GetString(GetDirectionLocKey(sideData.LeftDirection))),
            ("to", Loc.GetString(GetDirectionLocKey(sideData.RightDirection)))));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        using var scope = _attribution.EnterScope("manipulator.auto_conveyor_manipulator");
        var now = _timing.CurTime;
        CleanupStaleClaims(now);

        var query = EntityQueryEnumerator<WH40KConveyorManipulatorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var manipulator, out var xform))
        {
            if (manipulator.Busy)
            {
                UpdateBusy((uid, manipulator, xform), now);
                continue;
            }

            if (manipulator.NextTransferAt > now)
                continue;

            UpdateIdle((uid, manipulator, xform), now);
        }
    }

    private void UpdateBusy(Entity<WH40KConveyorManipulatorComponent, TransformComponent> manipulator, TimeSpan now)
    {
        var (uid, component, xform) = manipulator;

        if (!component.ActiveItem.HasValue || !_activeTransfers.TryGetValue(uid, out var transfer))
        {
            FinishTransfer(manipulator, WH40KManipulatorStatus.Idle, now);
            return;
        }

        if (component.RequirePowered && !IsPowered(uid))
        {
            SetStatus(manipulator, WH40KManipulatorStatus.NoPower);
            return;
        }

        if (transfer.EndTime > now)
            return;

        if (!Exists(transfer.Item))
        {
            FinishTransfer(manipulator, WH40KManipulatorStatus.Idle, now);
            return;
        }

        switch (transfer.Mode)
        {
            case WH40KManipulatorMode.SmartFeed:
                FinalizeSmartFeed(uid, transfer);
                break;
            case WH40KManipulatorMode.PassThrough:
                FinalizePassThrough(uid, transfer);
                break;
        }

        FinishTransfer(manipulator, WH40KManipulatorStatus.Idle, now);
    }

    private void UpdateIdle(Entity<WH40KConveyorManipulatorComponent, TransformComponent> manipulator, TimeSpan now)
    {
        var (uid, component, xform) = manipulator;

        if (component.RequirePowered && !IsPowered(uid))
        {
            ScheduleNextAttempt(component, now, NoPowerRetryDelay);
            SetStatus(manipulator, WH40KManipulatorStatus.NoPower);
            return;
        }

        if (!TryGetSides(xform, out var sideData))
        {
            ScheduleNextAttempt(component, now, EmptyInputRetryDelay);
            SetStatus(manipulator, WH40KManipulatorStatus.WaitingForItem);
            return;
        }

        CollectLeftCandidates(uid, sideData.LeftTile);
        if (_leftCandidates.Count == 0)
        {
            ScheduleNextAttempt(component, now, EmptyInputRetryDelay);
            SetStatus(manipulator, WH40KManipulatorStatus.WaitingForItem);
            return;
        }

        CollectRightReceivers(sideData.RightTile);
        var mode = _rightReceivers.Count > 0
            ? WH40KManipulatorMode.SmartFeed
            : WH40KManipulatorMode.PassThrough;
        EntityUid selected;
        int selectedIndex;

        if (mode == WH40KManipulatorMode.PassThrough)
        {
            if (!TrySelectPassThroughCandidate(uid, component, out selected, out selectedIndex))
            {
                ScheduleNextAttempt(component, now, EmptyInputRetryDelay);
                SetStatus(manipulator, WH40KManipulatorStatus.WaitingForItem);
                return;
            }

            if (!HasPassThroughOutputSpace(sideData))
            {
                ScheduleNextAttempt(component, now, ReceiverCapacityRetryDelay);
                SetStatus(manipulator, WH40KManipulatorStatus.WaitingForReceiverCapacity);
                return;
            }

            if (!TryBeginTransfer(manipulator, sideData, selected, selectedIndex, mode, now))
            {
                ScheduleNextAttempt(component, now);
                SetStatus(manipulator, WH40KManipulatorStatus.WaitingForItem);
                return;
            }

            return;
        }

        if (!TrySelectSmartFeedCandidate(uid, component, out selected, out selectedIndex, out var hasPotentialCompatibility))
        {
            ScheduleNextAttempt(component, now,
                hasPotentialCompatibility
                    ? ReceiverCapacityRetryDelay
                    : IncompatibleItemRetryDelay);
            SetStatus(manipulator,
                hasPotentialCompatibility
                    ? WH40KManipulatorStatus.WaitingForReceiverCapacity
                    : WH40KManipulatorStatus.WaitingForCompatibleItem);
            return;
        }

        if (!TryBeginTransfer(manipulator, sideData, selected, selectedIndex, mode, now))
        {
            ScheduleNextAttempt(component, now);
            SetStatus(manipulator, WH40KManipulatorStatus.WaitingForItem);
            return;
        }
    }

    private bool TryBeginTransfer(
        Entity<WH40KConveyorManipulatorComponent, TransformComponent> manipulator,
        SideData sideData,
        EntityUid item,
        int selectedIndex,
        WH40KManipulatorMode mode,
        TimeSpan now)
    {
        var (uid, component, _) = manipulator;
        if (!Exists(item))
            return false;

        if (_claimedItems.TryGetValue(item, out var claimedBy) && claimedBy != uid && Exists(claimedBy))
            return false;

        var transferContainer = _container.EnsureContainer<Container>(uid, WH40KConveyorManipulatorComponent.TransferContainerId);
        if (!_container.CanInsert(item, transferContainer))
            return false;

        var startCoords = Transform(item).Coordinates;
        var initialAngle = Transform(item).LocalRotation;

        _claimedItems[item] = uid;
        if (!_container.Insert(item, transferContainer))
        {
            _claimedItems.Remove(item);
            return false;
        }

        var duration = TimeSpan.FromSeconds(MathF.Max(0.05f, component.TransferDuration));
        _activeTransfers[uid] = new ActiveTransfer
        {
            Item = item,
            Grid = sideData.Grid,
            LeftTile = sideData.LeftTile,
            RightTile = sideData.RightTile,
            Facing = sideData.Facing,
            LeftDirection = sideData.LeftDirection,
            RightDirection = sideData.RightDirection,
            EndTime = now + duration,
            Mode = mode,
        };

        component.SelectionCursor = selectedIndex + 1;
        ApplyOperationalState(uid, component, busy: true, activeItem: item, WH40KManipulatorStatus.Busy);

        RaiseNetworkEvent(
            new WH40KManipulatorArcAnimationEvent(
                GetNetEntity(item),
                GetNetCoordinates(startCoords),
                GetNetCoordinates(sideData.RightCoords),
                (float) duration.TotalSeconds,
                MathF.Max(0f, component.ArcHeight),
                initialAngle),
            Filter.Pvs(uid));

        return true;
    }

    private void FinalizePassThrough(EntityUid manipulator, ActiveTransfer transfer)
    {
        if (!Exists(transfer.Item))
            return;

        ReleaseTransferItem(manipulator, transfer.Item);
        PlaceWithFallback(manipulator, transfer, transfer.Item, preferRight: true);
    }

    private void FinalizeSmartFeed(EntityUid manipulator, ActiveTransfer transfer)
    {
        if (!Exists(transfer.Item))
            return;

        CollectRightReceivers(transfer.RightTile);
        foreach (var receiver in _rightReceivers)
        {
            if (!Exists(transfer.Item))
                return;

            switch (receiver.Kind)
            {
                case ReceiverKind.Reclaimer when receiver.Reclaimer is { } reclaimer:
                    if (!_materialReclaimer.CanAcceptItem(receiver.Uid, transfer.Item, reclaimer))
                        continue;

                    if (_materialReclaimer.TryStartProcessItem(receiver.Uid, transfer.Item, reclaimer))
                        return;
                    break;

                case ReceiverKind.Storage when receiver.Storage is { } storage:
                    if (!storage.InsertOnInteract)
                        continue;

                    if (!_materialStorage.CanInsertMaterialEntity(transfer.Item, receiver.Uid, storage))
                        continue;

                    if (_materialStorage.TryInsertMaterialEntityNoFeedback(transfer.Item, receiver.Uid, storage))
                        return;
                    break;

                case ReceiverKind.ItemSlotMachine when receiver.ItemSlots is { } slots:
                    if (!_itemSlots.TryGetAvailableSlot((receiver.Uid, slots), transfer.Item, null, out var slot, emptyOnly: true))
                        continue;

                    if (_itemSlots.TryInsert(receiver.Uid, slot, transfer.Item, null, excludeUserAudio: true))
                        return;
                    break;
            }
        }

        ReleaseTransferItem(manipulator, transfer.Item);
        PlaceWithFallback(manipulator, transfer, transfer.Item, preferRight: false);
    }

    private void FinishTransfer(
        Entity<WH40KConveyorManipulatorComponent, TransformComponent> manipulator,
        WH40KManipulatorStatus status,
        TimeSpan now)
    {
        var (uid, component, _) = manipulator;

        ReleaseClaim(component.ActiveItem);
        ScheduleNextAttempt(component, now);

        ApplyOperationalState(uid, component, busy: false, activeItem: null, status);
        _activeTransfers.Remove(uid);
    }

    private void SetStatus(Entity<WH40KConveyorManipulatorComponent, TransformComponent> manipulator, WH40KManipulatorStatus status)
    {
        if (manipulator.Comp1.Status == status)
            return;

        manipulator.Comp1.Status = status;
        Dirty(manipulator.Owner, manipulator.Comp1);
    }

    private bool ApplyOperationalState(
        EntityUid uid,
        WH40KConveyorManipulatorComponent component,
        bool busy,
        EntityUid? activeItem,
        WH40KManipulatorStatus status)
    {
        var changed = false;

        if (component.Busy != busy)
        {
            component.Busy = busy;
            changed = true;
        }

        if (component.ActiveItem != activeItem)
        {
            component.ActiveItem = activeItem;
            changed = true;
        }

        if (component.Status != status)
        {
            component.Status = status;
            changed = true;
        }

        if (changed)
            Dirty(uid, component);

        return changed;
    }

    private static void ScheduleNextAttempt(
        WH40KConveyorManipulatorComponent component,
        TimeSpan now,
        TimeSpan? minimumDelay = null)
    {
        var cooldown = TimeSpan.FromSeconds(MathF.Max(0.05f, component.TransferCooldown));
        var delay = minimumDelay is { } minimum && minimum > cooldown
            ? minimum
            : cooldown;

        component.NextTransferAt = now + delay;
    }

    private bool TrySelectPassThroughCandidate(
        EntityUid manipulator,
        WH40KConveyorManipulatorComponent component,
        out EntityUid selected,
        out int selectedIndex)
    {
        selected = EntityUid.Invalid;
        selectedIndex = -1;

        if (_leftCandidates.Count == 0)
            return false;

        var startIndex = Math.Max(0, component.SelectionCursor % _leftCandidates.Count);
        for (var i = 0; i < _leftCandidates.Count; i++)
        {
            var idx = (startIndex + i) % _leftCandidates.Count;
            var candidate = _leftCandidates[idx];

            if (!CanUseCandidate(candidate, manipulator))
                continue;

            selected = candidate;
            selectedIndex = idx;
            return true;
        }

        return false;
    }

    private bool TrySelectSmartFeedCandidate(
        EntityUid manipulator,
        WH40KConveyorManipulatorComponent component,
        out EntityUid selected,
        out int selectedIndex,
        out bool hasPotentialCompatibility)
    {
        selected = EntityUid.Invalid;
        selectedIndex = -1;
        hasPotentialCompatibility = false;

        if (_leftCandidates.Count == 0 || _rightReceivers.Count == 0)
            return false;

        var startIndex = Math.Max(0, component.SelectionCursor % _leftCandidates.Count);
        for (var i = 0; i < _leftCandidates.Count; i++)
        {
            var idx = (startIndex + i) % _leftCandidates.Count;
            var candidate = _leftCandidates[idx];
            if (!CanUseCandidate(candidate, manipulator))
                continue;

            foreach (var receiver in _rightReceivers)
            {
                if (CanReceiverAcceptNow(receiver, candidate))
                {
                    selected = candidate;
                    selectedIndex = idx;
                    return true;
                }

                if (TrySplitCandidateForReceiver(receiver, candidate, out var splitCandidate))
                {
                    selected = splitCandidate;
                    selectedIndex = idx;
                    return true;
                }

                if (IsPotentiallyCompatible(receiver, candidate))
                    hasPotentialCompatibility = true;
            }
        }

        return false;
    }

    private bool CanReceiverAcceptNow(ReceiverCandidate receiver, EntityUid item)
    {
        switch (receiver.Kind)
        {
            case ReceiverKind.Reclaimer when receiver.Reclaimer is { } reclaimer:
                return _materialReclaimer.CanAcceptItem(receiver.Uid, item, reclaimer);

            case ReceiverKind.Storage when receiver.Storage is { } storage:
                if (!storage.InsertOnInteract)
                    return false;
                return _materialStorage.CanInsertMaterialEntity(item, receiver.Uid, storage);

            case ReceiverKind.ItemSlotMachine when receiver.ItemSlots is { } slots:
                return _itemSlots.TryGetAvailableSlot((receiver.Uid, slots), item, null, out _, emptyOnly: true);

            default:
                return false;
        }
    }

    private bool TrySplitCandidateForReceiver(ReceiverCandidate receiver, EntityUid item, out EntityUid splitItem)
    {
        splitItem = EntityUid.Invalid;

        if (receiver.Kind != ReceiverKind.Storage || receiver.Storage is not { } storage)
            return false;

        if (!storage.InsertOnInteract)
            return false;

        if (!TryComp<StackComponent>(item, out var stackComp) || stackComp.Count <= 1)
            return false;

        if (!TryComp<MaterialComponent>(item, out _) ||
            !TryComp<PhysicalCompositionComponent>(item, out var compositionComp))
        {
            return false;
        }

        var maxSplit = GetMaximumStorageSplitCount(receiver.Uid, stackComp, storage, compositionComp);
        if (maxSplit <= 0)
            return false;

        for (var count = maxSplit; count >= 1; count--)
        {
            if (!CanStorageAcceptCount(item, receiver.Uid, storage, compositionComp, count))
                continue;

            var split = _stack.Split((item, stackComp), count, Transform(item).Coordinates);
            if (split is not { } splitUid)
                continue;

            splitItem = splitUid;
            return true;
        }

        return false;
    }

    private bool IsPotentiallyCompatible(ReceiverCandidate receiver, EntityUid item)
    {
        switch (receiver.Kind)
        {
            case ReceiverKind.Storage when receiver.Storage is { } storage:
                if (!storage.InsertOnInteract)
                    return false;

                if (!TryComp<MaterialComponent>(item, out _) ||
                    !TryComp<PhysicalCompositionComponent>(item, out var composition))
                {
                    return false;
                }

                if (_whitelist.IsWhitelistFail(storage.Whitelist, item))
                    return false;

                if (HasComp<UnremoveableComponent>(item))
                    return false;

                var perUnitVolume = 0;
                foreach (var (materialId, amount) in composition.MaterialComposition)
                {
                    if (!_materialStorage.IsMaterialWhitelisted((receiver.Uid, storage), materialId))
                        return false;

                    perUnitVolume += amount;
                }

                if (perUnitVolume <= 0)
                    return false;

                // If one unit can never fit into local storage, this is not a capacity wait case.
                if (storage.StorageLimit is { } limit && perUnitVolume > limit)
                    return false;

                return true;

            case ReceiverKind.Reclaimer when receiver.Reclaimer is { } reclaimer:
                if (_whitelist.IsWhitelistFail(reclaimer.Whitelist, item) ||
                    _whitelist.IsWhitelistPass(reclaimer.Blacklist, item))
                {
                    return false;
                }

                if (HasComp<Content.Shared.Mobs.Components.MobStateComponent>(item) &&
                    !_materialReclaimer.CanGib(receiver.Uid, item, reclaimer))
                {
                    return false;
                }

                return true;

            case ReceiverKind.ItemSlotMachine when receiver.ItemSlots is { } slots:
                foreach (var slot in slots.Slots.Values)
                {
                    if (slot.Locked)
                        continue;

                    if (_whitelist.IsWhitelistFail(slot.Whitelist, item) ||
                        _whitelist.IsWhitelistPass(slot.Blacklist, item))
                    {
                        continue;
                    }

                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private bool CanStorageAcceptCount(
        EntityUid item,
        EntityUid receiver,
        MaterialStorageComponent storage,
        PhysicalCompositionComponent composition,
        int count)
    {
        if (count <= 0)
            return false;

        if (_whitelist.IsWhitelistFail(storage.Whitelist, item))
            return false;

        if (HasComp<UnremoveableComponent>(item))
            return false;

        if (TryComp<ApcPowerReceiverComponent>(receiver, out var power) && !power.Powered)
            return false;

        var totalVolume = 0;
        foreach (var (mat, vol) in composition.MaterialComposition)
        {
            if (!_materialStorage.CanChangeMaterialAmount(receiver, mat, vol * count, storage))
                return false;

            totalVolume += vol * count;
        }

        return _materialStorage.CanTakeVolume(receiver, totalVolume, storage, localOnly: true);
    }

    private int GetMaximumStorageSplitCount(
        EntityUid receiver,
        StackComponent stackComp,
        MaterialStorageComponent storage,
        PhysicalCompositionComponent composition)
    {
        if (stackComp.Count <= 1)
            return 0;

        var maxByStorage = stackComp.Count - 1;

        var perUnitVolume = 0;
        foreach (var (_, vol) in composition.MaterialComposition)
        {
            perUnitVolume += vol;
        }

        if (perUnitVolume <= 0)
            return 0;

        if (storage.StorageLimit is { } limit)
        {
            var current = _materialStorage.GetTotalMaterialAmount(receiver, storage, localOnly: true);
            var remaining = Math.Max(0, limit - current);
            maxByStorage = Math.Min(maxByStorage, remaining / perUnitVolume);
        }

        return Math.Max(0, maxByStorage);
    }

    private bool CanUseCandidate(EntityUid item, EntityUid manipulator)
    {
        if (!Exists(item) || item == manipulator)
            return false;

        if (HasComp<UnremoveableComponent>(item) ||
            HasComp<WH40KConveyorManipulatorComponent>(item) ||
            HasComp<Content.Shared.Mobs.Components.MobStateComponent>(item))
        {
            return false;
        }

        if (_claimedItems.TryGetValue(item, out var claimedBy) && claimedBy != manipulator && Exists(claimedBy))
            return false;

        if (Transform(item).Anchored)
            return false;

        if (!TryComp<PhysicsComponent>(item, out var physics) || physics.BodyType == BodyType.Static)
            return false;

        if (_container.TryGetContainingContainer((item, null, null), out _))
            return false;

        return true;
    }

    private void CollectLeftCandidates(EntityUid manipulator, TileKey tile)
    {
        _leftCandidates.Clear();
        _tileEntities.Clear();

        _lookup.GetLocalEntitiesIntersecting(
            tile.Grid,
            tile.Indices,
            _tileEntities,
            0f,
            flags: LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Approximate);

        foreach (var entity in _tileEntities)
        {
            if (!CanUseCandidate(entity, manipulator))
                continue;

            _leftCandidates.Add(entity);
        }

        _leftCandidates.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    private bool HasPassThroughOutputSpace(SideData sideData)
    {
        _placementTiles.Clear();

        var preferred = sideData.RightTile.Indices;
        var facing = sideData.Facing.ToIntVec();
        var sideways = sideData.RightDirection.ToIntVec();

        AddPlacementTile(preferred);
        AddPlacementTile(preferred + facing);
        AddPlacementTile(preferred - facing);
        AddPlacementTile(preferred + sideways);
        AddPlacementTile(preferred - sideways);

        foreach (var indices in _placementTiles)
        {
            if (CanPlaceOnTile(sideData.Grid, indices))
                return true;
        }

        return false;
    }

    private void CollectRightReceivers(TileKey tile)
    {
        _rightReceivers.Clear();
        _tileEntities.Clear();

        _lookup.GetLocalEntitiesIntersecting(
            tile.Grid,
            tile.Indices,
            _tileEntities,
            0f,
            flags: LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Approximate);

        foreach (var entity in _tileEntities)
        {
            if (TryComp<MaterialReclaimerComponent>(entity, out var reclaimer))
                _rightReceivers.Add(ReceiverCandidate.FromReclaimer(entity, reclaimer));

            if (TryComp<MaterialStorageComponent>(entity, out var storage))
                _rightReceivers.Add(ReceiverCandidate.FromStorage(entity, storage));

            if (HasComp<MachineComponent>(entity) &&
                TryComp<ItemSlotsComponent>(entity, out var itemSlots))
            {
                _rightReceivers.Add(ReceiverCandidate.FromItemSlots(entity, itemSlots));
            }
        }

        _rightReceivers.Sort((a, b) =>
        {
            var uidCompare = a.Uid.Id.CompareTo(b.Uid.Id);
            return uidCompare != 0 ? uidCompare : a.Kind.CompareTo(b.Kind);
        });
    }

    private void PlaceWithFallback(EntityUid manipulator, ActiveTransfer transfer, EntityUid item, bool preferRight)
    {
        if (!Exists(item))
            return;

        _placementTiles.Clear();

        var preferred = preferRight ? transfer.RightTile.Indices : transfer.LeftTile.Indices;
        var secondary = preferRight ? transfer.LeftTile.Indices : transfer.RightTile.Indices;
        var facing = transfer.Facing.ToIntVec();
        var sideways = transfer.RightDirection.ToIntVec();

        AddPlacementTile(preferred);
        AddPlacementTile(preferred + facing);
        AddPlacementTile(preferred - facing);
        AddPlacementTile(preferred + sideways);
        AddPlacementTile(preferred - sideways);
        AddPlacementTile(secondary);
        AddPlacementTile(secondary + facing);
        AddPlacementTile(secondary - facing);

        foreach (var indices in _placementTiles)
        {
            if (TryPlaceOnTile(item, transfer.Grid, indices))
                return;
        }

        _transform.DropNextTo(item, manipulator);
    }

    private void AddPlacementTile(Vector2i tile)
    {
        if (!_placementTiles.Contains(tile))
            _placementTiles.Add(tile);
    }

    private bool TryPlaceOnTile(EntityUid item, EntityUid gridUid, Vector2i indices)
    {
        if (!CanPlaceOnTile(gridUid, indices))
            return false;

        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        if (!_map.TryGetTileRef(gridUid, grid, indices, out var tileRef))
            return false;

        _transform.SetCoordinates(item, _turf.GetTileCenter(tileRef));
        return true;
    }

    private bool CanPlaceOnTile(EntityUid gridUid, Vector2i indices)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        if (!_map.TryGetTileRef(gridUid, grid, indices, out var tileRef))
            return false;

        return !_turf.IsTileBlocked(tileRef, _placementCollisionMask);
    }

    private void ReleaseTransferItem(EntityUid manipulator, EntityUid item)
    {
        if (!Exists(item))
            return;

        if (!_container.TryGetContainingContainer((item, null, null), out var container))
            return;

        if (container.Owner != manipulator || container.ID != WH40KConveyorManipulatorComponent.TransferContainerId)
            return;

        var destination = Transform(manipulator).Coordinates;
        _container.Remove(item, container, destination: destination);
    }

    private bool IsPowered(EntityUid uid)
    {
        return !TryComp<ApcPowerReceiverComponent>(uid, out var power) || power.Powered;
    }

    private static string GetDirectionLocKey(Direction direction)
    {
        return direction switch
        {
            Direction.North => "wh40k-manipulator-direction-north",
            Direction.South => "wh40k-manipulator-direction-south",
            Direction.East => "wh40k-manipulator-direction-east",
            Direction.West => "wh40k-manipulator-direction-west",
            _ => "wh40k-manipulator-direction-unknown",
        };
    }

    private bool TryGetSides(TransformComponent xform, out SideData data)
    {
        data = default;

        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        if (!_map.TryGetTileRef(gridUid, grid, xform.Coordinates, out var centerTile))
            return false;

        // The imported WH40K manipulator sprite pack is visually rotated 180 degrees
        // relative to the machine flow contract. Align logical flow with what players see.
        var facing = xform.LocalRotation.GetCardinalDir().GetOpposite();
        var rightDir = facing.GetClockwise90Degrees();
        var leftDir = rightDir.GetOpposite();

        var leftIndices = centerTile.GridIndices + leftDir.ToIntVec();
        var rightIndices = centerTile.GridIndices + rightDir.ToIntVec();

        data = new SideData
        {
            Grid = gridUid,
            LeftTile = new TileKey(gridUid, leftIndices),
            RightTile = new TileKey(gridUid, rightIndices),
            LeftCoords = new EntityCoordinates(gridUid, leftIndices + grid.TileSizeHalfVector),
            RightCoords = new EntityCoordinates(gridUid, rightIndices + grid.TileSizeHalfVector),
            Facing = facing,
            LeftDirection = leftDir,
            RightDirection = rightDir,
        };

        return true;
    }

    private void CleanupStaleClaims(TimeSpan now)
    {
        if (_nextClaimCleanup > now)
            return;

        _nextClaimCleanup = now + TimeSpan.FromSeconds(1);
        if (_claimedItems.Count == 0)
            return;

        _leftCandidates.Clear();
        foreach (var (item, owner) in _claimedItems)
        {
            if (!Exists(item) || !Exists(owner) ||
                !TryComp<WH40KConveyorManipulatorComponent>(owner, out var manipulator) ||
                !manipulator.ActiveItem.HasValue ||
                manipulator.ActiveItem.Value != item)
            {
                _leftCandidates.Add(item);
            }
        }

        foreach (var item in _leftCandidates)
        {
            _claimedItems.Remove(item);
        }

        _leftCandidates.Clear();
    }

    private void ReleaseClaimsOwnedBy(EntityUid owner)
    {
        if (_claimedItems.Count == 0)
            return;

        _leftCandidates.Clear();
        foreach (var (item, claimOwner) in _claimedItems)
        {
            if (claimOwner == owner)
                _leftCandidates.Add(item);
        }

        foreach (var item in _leftCandidates)
        {
            _claimedItems.Remove(item);
        }

        _leftCandidates.Clear();
    }

    private void ReleaseClaim(EntityUid? item)
    {
        if (!item.HasValue)
            return;

        _claimedItems.Remove(item.Value);
    }

    private readonly record struct TileKey(EntityUid Grid, Vector2i Indices);

    private enum ReceiverKind : byte
    {
        Reclaimer = 0,
        Storage = 1,
        ItemSlotMachine = 2,
    }

    private readonly struct SideData
    {
        public EntityUid Grid { get; init; }
        public TileKey LeftTile { get; init; }
        public TileKey RightTile { get; init; }
        public EntityCoordinates LeftCoords { get; init; }
        public EntityCoordinates RightCoords { get; init; }
        public Direction Facing { get; init; }
        public Direction LeftDirection { get; init; }
        public Direction RightDirection { get; init; }
    }

    private readonly struct ReceiverCandidate
    {
        public readonly EntityUid Uid { get; }
        public readonly ReceiverKind Kind { get; }
        public readonly MaterialStorageComponent? Storage { get; }
        public readonly MaterialReclaimerComponent? Reclaimer { get; }
        public readonly ItemSlotsComponent? ItemSlots { get; }

        private ReceiverCandidate(
            EntityUid uid,
            ReceiverKind kind,
            MaterialStorageComponent? storage = null,
            MaterialReclaimerComponent? reclaimer = null,
            ItemSlotsComponent? itemSlots = null)
        {
            Uid = uid;
            Kind = kind;
            Storage = storage;
            Reclaimer = reclaimer;
            ItemSlots = itemSlots;
        }

        public static ReceiverCandidate FromStorage(EntityUid uid, MaterialStorageComponent storage)
            => new(uid, ReceiverKind.Storage, storage: storage);

        public static ReceiverCandidate FromReclaimer(EntityUid uid, MaterialReclaimerComponent reclaimer)
            => new(uid, ReceiverKind.Reclaimer, reclaimer: reclaimer);

        public static ReceiverCandidate FromItemSlots(EntityUid uid, ItemSlotsComponent itemSlots)
            => new(uid, ReceiverKind.ItemSlotMachine, itemSlots: itemSlots);
    }

    private sealed class ActiveTransfer
    {
        public EntityUid Item;
        public EntityUid Grid;
        public TileKey LeftTile;
        public TileKey RightTile;
        public Direction Facing;
        public Direction LeftDirection;
        public Direction RightDirection;
        public TimeSpan EndTime;
        public WH40KManipulatorMode Mode;
    }
}
