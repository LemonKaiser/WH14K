using System.Linq;
using Content.Shared.ActionBlocker;
using Content.Shared.Armor;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Explosion;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.ArmorPlates;

public sealed partial class SharedWH40KArmorPlateSystem : EntitySystem
{
    private static readonly VerbCategory PlateSlotsCategory = new("wh40k-armor-plate-slots-category", null);

    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedToolSystem _tool = default!;

    private readonly Dictionary<EntityUid, GameTick> _recentExplosionWearTicks = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, ComponentInit>(OnHolderInit);
        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, MapInitEvent>(OnHolderMapInit);
        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, ComponentRemove>(OnHolderRemove);
        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, EntInsertedIntoContainerMessage>(OnHolderInserted);
        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, EntRemovedFromContainerMessage>(OnHolderRemoved);
        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, ItemSlotInsertAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, ExaminedEvent>(OnHolderExamined);
        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, ArmorExamineEvent>(OnHolderArmorExamine);
        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, GetVerbsEvent<ActivationVerb>>(OnGetActivationVerbs);
        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, InventoryRelayedEvent<GetVerbsEvent<EquipmentVerb>>>(OnRelayedEquipmentVerbs);
        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshMoveSpeed);
        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnRelayedDamageModify);
        SubscribeLocalEvent<WH40KArmorPlateHolderComponent, InventoryRelayedEvent<GetExplosionResistanceEvent>>(OnRelayedExplosionResistance);

        SubscribeLocalEvent<WH40KArmorPlateComponent, MapInitEvent>(OnPlateMapInit);
        SubscribeLocalEvent<WH40KArmorPlateComponent, InteractUsingEvent>(OnPlateInteractUsing);
        SubscribeLocalEvent<WH40KArmorPlateComponent, WH40KArmorPlateRepairDoAfterEvent>(OnPlateRepairDoAfter);
        SubscribeLocalEvent<WH40KArmorPlateComponent, ExaminedEvent>(OnPlateExamined);

    }

    private void OnHolderInit(Entity<WH40KArmorPlateHolderComponent> ent, ref ComponentInit args)
    {
        ent.Comp.SlotCount = Math.Clamp(ent.Comp.SlotCount, 1, WH40KArmorPlateHolderComponent.MaxSlots);
        EnsureBaseModifiers(ent);

        if (ent.Comp.PlateSlots.Count == 0)
        {
            for (var i = 1; i <= ent.Comp.SlotCount; i++)
            {
                var slotId = WH40KArmorPlateHelper.GetSlotId(i);
                var slot = CreatePlateSlot(i);
                ent.Comp.PlateSlots[slotId] = slot;
                _itemSlots.AddItemSlot(ent.Owner, slotId, slot);
            }
        }

        UpdateAppearance(ent);
    }

    private void OnHolderMapInit(Entity<WH40KArmorPlateHolderComponent> ent, ref MapInitEvent args)
    {
        EnsureBaseModifiers(ent);
        RefreshArmorModifiers(ent);
        UpdateAppearance(ent);
    }

    private void OnHolderRemove(Entity<WH40KArmorPlateHolderComponent> ent, ref ComponentRemove args)
    {
        foreach (var slot in ent.Comp.PlateSlots.Values.ToArray())
        {
            _itemSlots.RemoveItemSlot(ent.Owner, slot);
        }

        ent.Comp.PlateSlots.Clear();

        if (_net.IsClient || !ent.Comp.BaseModifiersInitialized || !TryComp(ent.Owner, out ArmorComponent? armor))
            return;

        armor.Modifiers = WH40KArmorPlateHelper.CloneModifierSet(ent.Comp.BaseModifiers);
        Dirty(ent.Owner, armor);
    }

    private void OnHolderInserted(Entity<WH40KArmorPlateHolderComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!IsPlateSlot(ent.Comp, args.Container.ID))
            return;

        UpdateAppearance(ent);
        RefreshArmorModifiers(ent);
        RefreshWearerMovement(ent.Owner);
    }

    private void OnHolderRemoved(Entity<WH40KArmorPlateHolderComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!IsPlateSlot(ent.Comp, args.Container.ID))
            return;

        UpdateAppearance(ent);
        RefreshArmorModifiers(ent);
        RefreshWearerMovement(ent.Owner);
    }

    private void OnInsertAttempt(Entity<WH40KArmorPlateHolderComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Slot.ID == null || !IsPlateSlot(ent.Comp, args.Slot.ID))
            return;

        if (!TryComp(args.Item, out WH40KArmorPlateComponent? incomingPlate))
            return;

        foreach (var (_, _, plateUid, plate) in GetInstalledPlates(ent))
        {
            if (plateUid == args.Item)
                continue;

            if (plate.PlateType != incomingPlate.PlateType)
                continue;

            args.Cancelled = true;

            if (args.User != null)
            {
                _popup.PopupClient(
                    Loc.GetString("wh40k-armor-plate-duplicate-type"),
                    ent.Owner,
                    args.User.Value);
            }

            return;
        }
    }

    private void OnHolderExamined(Entity<WH40KArmorPlateHolderComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(WH40KArmorPlateHolderComponent)))
        {
            var filled = GetInstalledPlates(ent).Count();
            args.PushMarkup(Loc.GetString(
                "wh40k-armor-plate-holder-examine",
                ("filled", filled),
                ("total", ent.Comp.SlotCount)));

            if (!args.IsInDetailsRange)
                return;

            foreach (var (slotIndex, _, plateUid, plate) in GetInstalledPlates(ent))
            {
                args.PushMarkup(Loc.GetString(
                    "wh40k-armor-plate-holder-examine-entry",
                    ("slot", slotIndex),
                    ("plate", Name(plateUid)),
                    ("current", plate.CurrentDurability),
                    ("max", plate.MaxDurability)));
            }
        }
    }

    private void OnHolderArmorExamine(Entity<WH40KArmorPlateHolderComponent> ent, ref ArmorExamineEvent args)
    {
        var filled = GetInstalledPlates(ent).Count();
        args.Msg.PushNewline();
        args.Msg.AddMarkupOrThrow(Loc.GetString(
            "wh40k-armor-plate-holder-examine",
            ("filled", filled),
            ("total", ent.Comp.SlotCount)));

        foreach (var (slotIndex, _, plateUid, plate) in GetInstalledPlates(ent))
        {
            args.Msg.PushNewline();
            args.Msg.AddMarkupOrThrow(Loc.GetString(
                "wh40k-armor-plate-holder-examine-entry",
                ("slot", slotIndex),
                ("plate", Name(plateUid)),
                ("current", plate.CurrentDurability),
                ("max", plate.MaxDurability)));
        }
    }

    private void OnGetActivationVerbs(Entity<WH40KArmorPlateHolderComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        AddActivationSlotOverviewVerbs(ent, args.User, args.Verbs);
    }

    private void OnRelayedEquipmentVerbs(Entity<WH40KArmorPlateHolderComponent> ent, ref InventoryRelayedEvent<GetVerbsEvent<EquipmentVerb>> args)
    {
        if (!args.Args.CanAccess || !args.Args.CanInteract)
            return;

        if (args.Args.Hands != null &&
            args.Args.Using != null &&
            TryComp(args.Args.Using.Value, out WH40KArmorPlateComponent? _))
        {
            foreach (var (_, slot) in GetInsertableSlots(ent, args.Args.Using.Value, args.Args.User))
            {
                var owner = ent.Owner;
                var user = args.Args.User;
                var hands = args.Args.Hands;
                var localSlot = slot;
                args.Args.Verbs.Add(new EquipmentVerb
                {
                    Category = VerbCategory.Insert,
                    IconEntity = GetNetEntity(args.Args.Using),
                    Priority = slot.Priority,
                    TextLocId = slot.Name,
                    Act = () => _itemSlots.TryInsertFromHand(owner, localSlot, user, hands, excludeUserAudio: true),
                });
            }
        }

        AddEquipmentSlotOverviewVerbs(ent, args.Args.User, args.Args.Verbs);
    }

    private void OnRefreshMoveSpeed(Entity<WH40KArmorPlateHolderComponent> ent, ref InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        var modifier = GetSpeedModifier(ent);
        if (MathHelper.CloseTo(modifier, 1f))
            return;

        args.Args.ModifySpeed(modifier, modifier, MovementSpeedModifierLayer.Equipment);
    }

    private void OnPlateMapInit(Entity<WH40KArmorPlateComponent> ent, ref MapInitEvent args)
    {
        if (_net.IsClient)
            return;

        ent.Comp.MaxDurability = Math.Max(0, ent.Comp.MaxDurability);
        ent.Comp.CurrentDurability = ent.Comp.CurrentDurability <= 0
            ? ent.Comp.MaxDurability
            : Math.Clamp(ent.Comp.CurrentDurability, 0, ent.Comp.MaxDurability);

        Dirty(ent);
    }

    private void OnPlateInteractUsing(Entity<WH40KArmorPlateComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.CurrentDurability >= ent.Comp.MaxDurability)
            return;

        if (TryGetContainingHolder(ent.Owner, out _, out _))
        {
            _popup.PopupClient(
                Loc.GetString("wh40k-armor-plate-repair-remove-first"),
                ent.Owner,
                args.User);
            return;
        }

        args.Handled = _tool.UseTool(
            args.Used,
            args.User,
            ent.Owner,
            ent.Comp.RepairDelay,
            ent.Comp.RepairQuality,
            new WH40KArmorPlateRepairDoAfterEvent(),
            ent.Comp.RepairFuelCost);
    }

    private void OnPlateRepairDoAfter(Entity<WH40KArmorPlateComponent> ent, ref WH40KArmorPlateRepairDoAfterEvent args)
    {
        if (args.Cancelled || ent.Comp.CurrentDurability >= ent.Comp.MaxDurability)
            return;

        var wasBroken = ent.Comp.Broken;
        ent.Comp.CurrentDurability = Math.Min(ent.Comp.MaxDurability, ent.Comp.CurrentDurability + 1);
        Dirty(ent);

        args.Repeat = ent.Comp.CurrentDurability < ent.Comp.MaxDurability;
        args.Args.Event.Repeat = args.Repeat;
        args.Handled = true;

        if (!wasBroken || ent.Comp.Broken)
            return;

        if (!TryGetContainingHolder(ent.Owner, out var holder, out _))
            return;

        RefreshArmorModifiers(holder);
    }

    private void OnPlateExamined(Entity<WH40KArmorPlateComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(WH40KArmorPlateComponent)))
        {
            args.PushMarkup(Loc.GetString("wh40k-armor-plate-tier", ("tier", ent.Comp.Tier)));
            args.PushMarkup(Loc.GetString(
                "wh40k-armor-plate-type",
                ("type", Loc.GetString(GetTypeLocKey(ent.Comp.PlateType)))));
            args.PushMarkup(Loc.GetString(
                "wh40k-armor-plate-durability",
                ("current", ent.Comp.CurrentDurability),
                ("max", ent.Comp.MaxDurability)));
            args.PushMarkup(Loc.GetString(
                "wh40k-armor-plate-bonus",
                ("bonus", ent.Comp.BonusPercent)));

            var penaltyPercent = MathF.Round((1f - ent.Comp.SpeedModifier) * 100f, 1);
            if (penaltyPercent > 0)
            {
                args.PushMarkup(Loc.GetString(
                    "wh40k-armor-plate-speed-penalty",
                    ("penalty", penaltyPercent)));
            }

            if (ent.Comp.Broken)
                args.PushMarkup(Loc.GetString("wh40k-armor-plate-broken"));
        }
    }

    private void OnRelayedDamageModify(Entity<WH40KArmorPlateHolderComponent> ent, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        if (_net.IsClient || !args.Args.OriginalDamage.AnyPositive())
            return;

        if (_recentExplosionWearTicks.TryGetValue(ent.Owner, out var explosionTick))
        {
            if (explosionTick == _timing.CurTick)
            {
                _recentExplosionWearTicks.Remove(ent.Owner);
                return;
            }

            if (explosionTick < _timing.CurTick)
                _recentExplosionWearTicks.Remove(ent.Owner);
        }

        var damageMask = WH40KArmorPlateHelper.GetDamageMask(args.Args.OriginalDamage);
        ApplyWearForDamage(ent, damageMask);
    }

    private void OnRelayedExplosionResistance(Entity<WH40KArmorPlateHolderComponent> ent, ref InventoryRelayedEvent<GetExplosionResistanceEvent> args)
    {
        if (_net.IsClient)
            return;

        if (WearAllInstalledPlates(ent))
            _recentExplosionWearTicks[ent.Owner] = _timing.CurTick;
    }

    private void EnsureBaseModifiers(Entity<WH40KArmorPlateHolderComponent> ent)
    {
        if (ent.Comp.BaseModifiersInitialized || !TryComp(ent.Owner, out ArmorComponent? armor))
            return;

        ent.Comp.BaseModifiers = WH40KArmorPlateHelper.CloneModifierSet(armor.Modifiers);
        ent.Comp.BaseModifiersInitialized = true;
    }

    private ItemSlot CreatePlateSlot(int slotIndex)
    {
        return new ItemSlot
        {
            Whitelist = new EntityWhitelist
            {
                Components = ["WH40KArmorPlate"],
            },
            InsertOnInteract = true,
            EjectOnInteract = false,
            DisableEject = true,
            Swap = false,
            Priority = WH40KArmorPlateHolderComponent.MaxSlots - slotIndex,
            Name = $"wh40k-armor-plate-slot-name-{slotIndex}",
            LockedFailPopup = "wh40k-armor-plate-slot-locked",
            WhitelistFailPopup = "wh40k-armor-plate-slot-invalid",
            InsertSound = new SoundPathSpecifier("/Audio/Weapons/Guns/MagIn/revolver_magin.ogg"),
            EjectSound = new SoundPathSpecifier("/Audio/Weapons/Guns/MagOut/revolver_magout.ogg"),
        };
    }

    private void RefreshArmorModifiers(Entity<WH40KArmorPlateHolderComponent> ent)
    {
        if (_net.IsClient || !TryComp(ent.Owner, out ArmorComponent? armor))
            return;

        EnsureBaseModifiers(ent);
        var modifiers = WH40KArmorPlateHelper.CloneModifierSet(ent.Comp.BaseModifiers);

        foreach (var (_, _, _, plate) in GetInstalledPlates(ent))
        {
            if (plate.Broken)
                continue;

            foreach (var damageType in WH40KArmorPlateHelper.GetProtectedDamageTypes(plate.PlateType))
            {
                var baseCoefficient = modifiers.Coefficients.GetValueOrDefault(damageType, 1f);
                modifiers.Coefficients[damageType] =
                    WH40KArmorPlateHelper.ApplyBonusToCoefficient(baseCoefficient, plate.BonusPercent);
            }
        }

        armor.Modifiers = modifiers;
        Dirty(ent.Owner, armor);
    }

    private void UpdateAppearance(Entity<WH40KArmorPlateHolderComponent> ent)
    {
        var overlayType = default(WH40KArmorPlateType?);

        foreach (var (_, _, _, plate) in GetInstalledPlates(ent))
        {
            overlayType = plate.PlateType;
            break;
        }

        _appearance.SetData(ent.Owner, WH40KArmorPlateVisuals.OverlayVisible, overlayType != null);

        if (overlayType != null)
            _appearance.SetData(ent.Owner, WH40KArmorPlateVisuals.OverlayType, overlayType.Value);
    }

    private float GetSpeedModifier(Entity<WH40KArmorPlateHolderComponent> ent)
    {
        var penalty = 0f;

        foreach (var (_, _, _, plate) in GetInstalledPlates(ent))
        {
            penalty += 1f - plate.SpeedModifier;
        }

        return Math.Clamp(1f - penalty, 0.1f, 1f);
    }

    private void RefreshWearerMovement(EntityUid armorUid)
    {
        if (!_container.TryGetContainingContainer((armorUid, null, null), out var container))
            return;

        _movement.RefreshMovementSpeedModifiers(container.Owner);
    }

    private IEnumerable<(int SlotIndex, ItemSlot Slot, EntityUid PlateUid, WH40KArmorPlateComponent Plate)> GetInstalledPlates(
        Entity<WH40KArmorPlateHolderComponent> ent)
    {
        for (var slotIndex = 1; slotIndex <= ent.Comp.SlotCount; slotIndex++)
        {
            var slotId = WH40KArmorPlateHelper.GetSlotId(slotIndex);

            if (!ent.Comp.PlateSlots.TryGetValue(slotId, out var slot) ||
                slot.Item is not { } plateUid ||
                !TryComp(plateUid, out WH40KArmorPlateComponent? plate))
            {
                continue;
            }

            yield return (slotIndex, slot, plateUid, plate);
        }
    }

    private IEnumerable<(int SlotIndex, ItemSlot Slot)> GetInsertableSlots(
        Entity<WH40KArmorPlateHolderComponent> ent,
        EntityUid plateUid,
        EntityUid user)
    {
        for (var slotIndex = 1; slotIndex <= ent.Comp.SlotCount; slotIndex++)
        {
            var slotId = WH40KArmorPlateHelper.GetSlotId(slotIndex);
            if (!_itemSlots.TryGetSlot(ent.Owner, slotId, out var slot) ||
                !_itemSlots.CanInsert(ent.Owner, plateUid, user, slot))
            {
                continue;
            }

            yield return (slotIndex, slot);
        }
    }

    private void AddActivationSlotOverviewVerbs(
        Entity<WH40KArmorPlateHolderComponent> ent,
        EntityUid user,
        SortedSet<ActivationVerb> verbs)
    {
        for (var slotIndex = 1; slotIndex <= ent.Comp.SlotCount; slotIndex++)
        {
            var slotId = WH40KArmorPlateHelper.GetSlotId(slotIndex);
            if (!ent.Comp.PlateSlots.TryGetValue(slotId, out var slot))
                continue;

            var verb = new ActivationVerb
            {
                Category = PlateSlotsCategory,
                Priority = WH40KArmorPlateHolderComponent.MaxSlots - slotIndex,
            };

            if (slot.Item is not { } plateUid || !TryComp(plateUid, out WH40KArmorPlateComponent? plate))
            {
                verb.Text = Loc.GetString("wh40k-armor-plate-slot-empty-verb", ("slot", slotIndex));
                verb.Disabled = true;
                verbs.Add(verb);
                continue;
            }

            verb.Text = GetSlotStatusText(slotIndex, plateUid, plate);
            verb.IconEntity = GetNetEntity(plateUid);

            if (!_actionBlocker.CanPickup(user, plateUid))
            {
                verb.Disabled = true;
                verbs.Add(verb);
                continue;
            }

            var owner = ent.Owner;
            var localSlot = slot;
            verb.Act = () => _itemSlots.TryEjectToHands(owner, localSlot, user, excludeUserAudio: true);
            verbs.Add(verb);
        }
    }

    private void AddEquipmentSlotOverviewVerbs(
        Entity<WH40KArmorPlateHolderComponent> ent,
        EntityUid user,
        SortedSet<EquipmentVerb> verbs)
    {
        for (var slotIndex = 1; slotIndex <= ent.Comp.SlotCount; slotIndex++)
        {
            var slotId = WH40KArmorPlateHelper.GetSlotId(slotIndex);
            if (!ent.Comp.PlateSlots.TryGetValue(slotId, out var slot))
                continue;

            var verb = new EquipmentVerb
            {
                Category = PlateSlotsCategory,
                Priority = WH40KArmorPlateHolderComponent.MaxSlots - slotIndex,
            };

            if (slot.Item is not { } plateUid || !TryComp(plateUid, out WH40KArmorPlateComponent? plate))
            {
                verb.Text = Loc.GetString("wh40k-armor-plate-slot-empty-verb", ("slot", slotIndex));
                verb.Disabled = true;
                verbs.Add(verb);
                continue;
            }

            verb.Text = GetSlotStatusText(slotIndex, plateUid, plate);
            verb.IconEntity = GetNetEntity(plateUid);

            if (!_actionBlocker.CanPickup(user, plateUid))
            {
                verb.Disabled = true;
                verbs.Add(verb);
                continue;
            }

            var owner = ent.Owner;
            var localSlot = slot;
            verb.Act = () => _itemSlots.TryEjectToHands(owner, localSlot, user, excludeUserAudio: true);
            verbs.Add(verb);
        }
    }

    private bool ApplyWearForDamage(Entity<WH40KArmorPlateHolderComponent> ent, WH40KArmorPlateDamageMask damageMask)
    {
        var installed = GetInstalledPlates(ent).ToArray();
        if (installed.Length == 0)
            return false;

        var targets = damageMask == WH40KArmorPlateDamageMask.None
            ? installed
            : installed.Where(x => WH40KArmorPlateHelper.MatchesDamage(x.Plate.PlateType, damageMask)).ToArray();

        if (targets.Length == 0)
            targets = installed;

        var changed = false;

        foreach (var (_, _, plateUid, plate) in targets)
        {
            if (plate.CurrentDurability <= 0)
                continue;

            plate.CurrentDurability = Math.Max(0, plate.CurrentDurability - 1);
            Dirty(plateUid, plate);
            changed = true;
        }

        if (changed)
            RefreshArmorModifiers(ent);

        return changed;
    }

    private bool WearAllInstalledPlates(Entity<WH40KArmorPlateHolderComponent> ent)
    {
        var changed = false;

        foreach (var (_, _, plateUid, plate) in GetInstalledPlates(ent))
        {
            if (plate.CurrentDurability <= 0)
                continue;

            plate.CurrentDurability = Math.Max(0, plate.CurrentDurability - 1);
            Dirty(plateUid, plate);
            changed = true;
        }

        if (changed)
            RefreshArmorModifiers(ent);

        return changed;
    }

    private bool TryGetContainingHolder(
        EntityUid plateUid,
        out Entity<WH40KArmorPlateHolderComponent> holder,
        out string slotId)
    {
        holder = default;
        slotId = string.Empty;

        if (!_container.TryGetContainingContainer((plateUid, null, null), out var container) ||
            !TryComp(container.Owner, out WH40KArmorPlateHolderComponent? holderComp) ||
            !IsPlateSlot(holderComp, container.ID))
        {
            return false;
        }

        holder = (container.Owner, holderComp);
        slotId = container.ID;
        return true;
    }

    private static bool IsPlateSlot(WH40KArmorPlateHolderComponent holder, string slotId)
    {
        return holder.PlateSlots.ContainsKey(slotId);
    }

    private string GetSlotStatusText(
        int slotIndex,
        EntityUid plateUid,
        WH40KArmorPlateComponent plate)
    {
        return Loc.GetString(
            "wh40k-armor-plate-slot-filled-verb",
            ("slot", slotIndex),
            ("plate", Name(plateUid)),
            ("current", plate.CurrentDurability),
            ("max", plate.MaxDurability));
    }

    private static string GetTypeLocKey(WH40KArmorPlateType type)
    {
        return type switch
        {
            WH40KArmorPlateType.Laser => "wh40k-armor-plate-type-laser",
            WH40KArmorPlateType.Bullet => "wh40k-armor-plate-type-bullet",
            WH40KArmorPlateType.Melee => "wh40k-armor-plate-type-melee",
            _ => "wh40k-armor-plate-type-bullet",
        };
    }
}
