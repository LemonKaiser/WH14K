using System.Collections.Generic;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server._WH40K.Command.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.NPC;
using Content.Shared.Storage;
using Content.Shared._WH40K.Command;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Command;

public sealed partial class WH40KReinforcementAiSystem : EntitySystem
{
    private static readonly ProtoId<HTNCompoundPrototype> ReinforcementRootTask = "SimpleHumanoidHostileCompound";
    private readonly record struct WeaponCandidate(EntityUid Weapon, string? SlotName);

    [Dependency] private  SharedHandsSystem _hands = default!;
    [Dependency] private  HTNSystem _htn = default!;
    [Dependency] private  InventorySystem _inventory = default!;
    [Dependency] private  NPCSystem _npc = default!;
    [Dependency] private  NPCSteeringSystem _steering = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(NPCSteeringSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WH40KReinforcementAiRuntimeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var runtime, out var xform))
        {
            UpdatePreparedWeapon(uid, runtime);
            UpdateLeash((uid, runtime, xform));
        }
    }

    public void Enable(EntityUid uid, EntityCoordinates homeCoordinates)
    {
        var runtime = EnsureComp<WH40KReinforcementAiRuntimeComponent>(uid);
        runtime.HomeCoordinates = homeCoordinates;
        runtime.NextWeaponReadyAttempt = _timing.CurTime;
        runtime.ReturningHome = false;

        EnsureComp<WH40KReinforcementAiStatusIconComponent>(uid);

        var htn = EnsureComp<HTNComponent>(uid);
        htn.RootTask = new HTNCompoundTask { Task = ReinforcementRootTask };
        htn.Blackboard.SetValue(NPCBlackboard.Owner, uid);

        _npc.SetBlackboard(uid, "IdleRange", runtime.IdleRange, htn);
        _npc.SetBlackboard(uid, "VisionRadius", runtime.VisionRadius, htn);
        _npc.SetBlackboard(uid, "AggroVisionRadius", runtime.AggroVisionRadius, htn);
        _npc.SetBlackboard(uid, "RangedRange", runtime.RangedRange, htn);
        _npc.SetBlackboard(uid, "MinimumIdleTime", 1.5f, htn);
        _npc.SetBlackboard(uid, "MaximumIdleTime", 4.5f, htn);

        if (TryComp<NPCSteeringComponent>(uid, out var steering))
            _steering.Unregister(uid, steering);

        _npc.WakeNPC(uid, htn);
        _htn.Replan(htn);
    }

    public bool TryReadyWeapon(EntityUid uid)
    {
        if (TryGetHeldGun(uid, out var heldGun))
        {
            _hands.TrySelect(uid, heldGun);
            return true;
        }

        if (TryComp(uid, out InventoryComponent? inventory) &&
            TryReadyInventoryWeapon(uid, inventory, rangedOnly: true))
        {
            return true;
        }

        if (TryGetHeldMelee(uid, out var heldMelee))
        {
            _hands.TrySelect(uid, heldMelee);
            return true;
        }

        return TryComp(uid, out inventory) && TryReadyInventoryWeapon(uid, inventory, rangedOnly: false);
    }

    public void Disable(EntityUid uid)
    {
        if (TryComp<NPCSteeringComponent>(uid, out var steering))
            _steering.Unregister(uid, steering);

        if (TryComp<HTNComponent>(uid, out var htn))
        {
            _htn.SetHTNEnabled((uid, htn), false);
            RemComp<HTNComponent>(uid);
        }

        RemComp<ActiveNPCComponent>(uid);
        RemComp<WH40KReinforcementAiRuntimeComponent>(uid);
        RemComp<WH40KReinforcementAiStatusIconComponent>(uid);
    }

    private void UpdatePreparedWeapon(EntityUid uid, WH40KReinforcementAiRuntimeComponent runtime)
    {
        if (TryGetHeldGun(uid, out var heldGun))
        {
            _hands.TrySelect(uid, heldGun);
            return;
        }

        if (_timing.CurTime < runtime.NextWeaponReadyAttempt)
        {
            if (TryGetHeldMelee(uid, out var heldMelee))
                _hands.TrySelect(uid, heldMelee);

            return;
        }

        if (!TryReadyWeapon(uid) && TryGetHeldMelee(uid, out var fallbackMelee))
            _hands.TrySelect(uid, fallbackMelee);

        runtime.NextWeaponReadyAttempt = _timing.CurTime + runtime.WeaponReadyRetryInterval;
    }

    private void UpdateLeash(Entity<WH40KReinforcementAiRuntimeComponent, TransformComponent> ent)
    {
        if (ent.Comp1.HomeCoordinates == EntityCoordinates.Invalid || ent.Comp2.MapID == MapId.Nullspace)
            return;

        var currentMap = _transform.ToMapCoordinates(ent.Comp2.Coordinates, logError: false);
        var homeMap = _transform.ToMapCoordinates(ent.Comp1.HomeCoordinates, logError: false);
        if (currentMap.MapId == MapId.Nullspace || homeMap.MapId == MapId.Nullspace)
            return;

        var shouldReturn = currentMap.MapId != homeMap.MapId;
        var distanceSquared = shouldReturn
            ? float.MaxValue
            : (currentMap.Position - homeMap.Position).LengthSquared();

        if (ent.Comp1.ReturningHome)
        {
            if (!shouldReturn && distanceSquared <= ent.Comp1.ReturnRange * ent.Comp1.ReturnRange)
            {
                FinishReturnHome(ent);
                return;
            }

            RefreshReturnHome(ent);
            return;
        }

        if (distanceSquared > ent.Comp1.LeashRange * ent.Comp1.LeashRange)
            StartReturnHome(ent);
    }

    private void StartReturnHome(Entity<WH40KReinforcementAiRuntimeComponent> ent)
    {
        if (ent.Comp.ReturningHome)
            return;

        ent.Comp.ReturningHome = true;

        if (TryComp<HTNComponent>(ent.Owner, out var htn))
            _htn.SetHTNEnabled((ent.Owner, htn), false);

        EnsureComp<ActiveNPCComponent>(ent.Owner);
        var steering = _steering.Register(ent.Owner, ent.Comp.HomeCoordinates, CompOrNull<NPCSteeringComponent>(ent.Owner));
        steering.Range = ent.Comp.ReturnRange;
        steering.ArriveOnLineOfSight = false;
    }

    private void RefreshReturnHome(Entity<WH40KReinforcementAiRuntimeComponent> ent)
    {
        EnsureComp<ActiveNPCComponent>(ent.Owner);
        var steering = TryComp<NPCSteeringComponent>(ent.Owner, out var existing)
            ? existing
            : _steering.Register(ent.Owner, ent.Comp.HomeCoordinates);

        if (!steering.Coordinates.Equals(ent.Comp.HomeCoordinates) || steering.Status == SteeringStatus.NoPath)
            steering = _steering.Register(ent.Owner, ent.Comp.HomeCoordinates, steering);

        steering.Range = ent.Comp.ReturnRange;
        steering.ArriveOnLineOfSight = false;
    }

    private void FinishReturnHome(Entity<WH40KReinforcementAiRuntimeComponent> ent)
    {
        ent.Comp.ReturningHome = false;

        if (TryComp<NPCSteeringComponent>(ent.Owner, out var steering))
            _steering.Unregister(ent.Owner, steering);

        if (!TryComp<HTNComponent>(ent.Owner, out var htn))
            return;

        _npc.WakeNPC(ent.Owner, htn);
        _htn.SetHTNEnabled((ent.Owner, htn), true, 0.2f);
    }

    private bool TryGetHeldGun(EntityUid uid, out EntityUid weapon)
    {
        weapon = EntityUid.Invalid;
        if (!TryComp(uid, out HandsComponent? hands))
            return false;

        foreach (var held in _hands.EnumerateHeld((uid, hands)))
        {
            if (!HasComp<GunComponent>(held))
                continue;

            weapon = held;
            return true;
        }

        return false;
    }

    private bool TryGetHeldMelee(EntityUid uid, out EntityUid weapon)
    {
        weapon = EntityUid.Invalid;
        if (!TryComp(uid, out HandsComponent? hands))
            return false;

        foreach (var held in _hands.EnumerateHeld((uid, hands)))
        {
            if (!HasComp<MeleeWeaponComponent>(held))
                continue;

            weapon = held;
            return true;
        }

        return false;
    }

    private bool TryReadyInventoryWeapon(EntityUid uid, InventoryComponent inventory, bool rangedOnly)
    {
        foreach (var slot in inventory.Slots)
        {
            if (!_inventory.TryGetSlotEntity(uid, slot.Name, out var item, inventory))
                continue;

            if (!TryFindWeaponCandidate(item.Value, slot.Name, rangedOnly, new HashSet<EntityUid>(), out var candidate))
                continue;

            if (TryPickupWeaponCandidate(uid, candidate))
                return true;
        }

        return false;
    }

    private bool TryFindWeaponCandidate(
        EntityUid entity,
        string? slotName,
        bool rangedOnly,
        HashSet<EntityUid> visited,
        out WeaponCandidate candidate)
    {
        candidate = default;
        if (!visited.Add(entity))
            return false;

        if (HasComp<GunComponent>(entity))
        {
            candidate = new WeaponCandidate(entity, slotName);
            return true;
        }

        if (!rangedOnly && HasComp<MeleeWeaponComponent>(entity))
        {
            candidate = new WeaponCandidate(entity, slotName);
            return true;
        }

        if (!TryComp<StorageComponent>(entity, out var storage))
            return false;

        foreach (var contained in storage.Container.ContainedEntities)
        {
            if (TryFindWeaponCandidate(contained, null, rangedOnly, visited, out candidate))
                return true;
        }

        return false;
    }

    private bool TryPickupWeaponCandidate(EntityUid uid, WeaponCandidate candidate)
    {
        if (candidate.SlotName != null)
        {
            _inventory.TryUnequip(uid, uid, candidate.SlotName, out _, silent: true, force: true);
        }

        var pickedUp =
            _hands.TryPickupAnyHand(uid, candidate.Weapon, checkActionBlocker: false, animateUser: false, animate: false) ||
            _hands.TryForcePickupAnyHand(uid, candidate.Weapon, checkActionBlocker: false);

        if (pickedUp)
            _hands.TrySelect(uid, candidate.Weapon);

        return pickedUp;
    }
}
