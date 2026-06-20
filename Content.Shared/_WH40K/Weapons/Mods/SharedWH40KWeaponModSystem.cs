using System.Linq;
using Content.Shared.ActionBlocker;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Movement.Systems;
using Content.Shared.Tag;
using Content.Shared._WH40K.Aiming;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Misc;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Content.Shared.Wieldable;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._WH40K.Weapons.Mods;

public sealed partial class SharedWH40KWeaponModSystem : EntitySystem
{
    private static readonly VerbCategory WeaponModSlotsCategory = new("wh40k-weapon-mod-slots-category", null);

    [Dependency] private INetManager _net = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private ClothingSystem _clothing = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedWH40KDefaultGunMeleeSystem _defaultMelee = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IComponentFactory _compFactory = default!;
    private bool _initialized;

    public override void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        base.Initialize();

        SubscribeLocalEvent<WH40KWeaponModHostComponent, ComponentInit>(OnHostInit);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, MapInitEvent>(OnHostMapInit);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, ComponentRemove>(OnHostRemove);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, EntInsertedIntoContainerMessage>(OnHostInserted);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, EntRemovedFromContainerMessage>(OnHostRemoved);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, ItemSlotInsertAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, GetVerbsEvent<ActivationVerb>>(OnGetActivationVerbs);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, InventoryRelayedEvent<GetVerbsEvent<EquipmentVerb>>>(OnRelayedEquipmentVerbs);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, GunMuzzleFlashAttemptEvent>(OnGunMuzzleFlashAttempt);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, GotEquippedHandEvent>(OnGotEquippedHand);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, GotUnequippedHandEvent>(OnGotUnequippedHand);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, HandSelectedEvent>(OnHandSelected);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, HandDeselectedEvent>(OnHandDeselected);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, ItemWieldedEvent>(OnItemWielded);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, ItemUnwieldedEvent>(OnItemUnwielded);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnHeldRefreshMovementSpeedModifiers);
        SubscribeLocalEvent<WH40KWeaponModComponent, AfterAutoHandleStateEvent>(OnModAutoHandleState);
        SubscribeLocalEvent<WH40KWeaponModComponent, ExaminedEvent>(OnModExamined);
    }

    /// <summary>
    /// The networked OverlayState on a mod (e.g. folding-stock "folded") arrived on the client.
    /// Rebuild the host's appearance from the freshly-applied state so the overlay matches the
    /// server without the client having to predict the toggle. This is what keeps a folded
    /// stock visually folded after the weapon is dropped.
    /// </summary>
    private void OnModAutoHandleState(Entity<WH40KWeaponModComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_net.IsServer)
            return;

        if (TryComp(ent.Owner, out TransformComponent? xform) &&
            TryComp(xform.ParentUid, out WH40KWeaponModHostComponent? host))
        {
            // Use the force variant: this fires DURING state application where SetData no-ops.
            ForceRebuildHostOverlayClient((xform.ParentUid, host));
        }
    }

    /// <summary>
    /// Client-side appearance rebuild from the current (server-replicated) mod OverlayState.
    /// Mirrors the server UpdateAppearance but is safe to run on the client because it only
    /// reads networked fields. Called by OnModAutoHandleState when the mod component state
    /// arrives, and by the folding-stock system when the replicated Folded state arrives, so a
    /// folded stock stays visually folded after the weapon is dropped and re-enters PVS.
    /// </summary>
    public void RebuildHostOverlayClient(Entity<WH40KWeaponModHostComponent> ent)
    {
        var overlaySprites = new Dictionary<string, string>();
        var overlayStates = new Dictionary<string, string>();

        foreach (var (slotId, _, modUid, mod) in GetInstalledMods(ent))
        {
            if (string.IsNullOrWhiteSpace(mod.OverlaySprite))
                continue;

            overlaySprites[slotId] = mod.OverlaySprite!;
            overlayStates[slotId] = mod.OverlayState;
        }

        _appearance.SetData(ent.Owner, WH40KWeaponModVisuals.OverlaySprites, overlaySprites);
        _appearance.SetData(ent.Owner, WH40KWeaponModVisuals.OverlayStates, overlayStates);
    }

    /// <summary>
    /// Force-rebuild the host's overlay appearance on the client, bypassing the
    /// <see cref="SharedAppearanceSystem"/> SetData guard that no-ops during state application
    /// (<c>CheckIfApplyingState</c>). This defers the rebuild to the next frame (after state
    /// application completes) using <c>Timer.Spawn(0, ...)</c>, so <c>SetData</c> is no longer a
    /// no-op and the visualizer re-renders with the correct folded/unfolded state even when the
    /// networked Folded state arrives in the middle of state application (PVS re-entry,
    /// prediction rollback).
    /// </summary>
    public void ForceRebuildHostOverlayClient(Entity<WH40KWeaponModHostComponent> ent)
    {
        if (_net.IsServer)
            return;

        if (!TryComp(ent.Owner, out AppearanceComponent? appearance))
            return;

        var hostUid = ent.Owner;
        // Defer to the next frame: SetData no-ops while _timing.ApplyingState is true (this handler
        // fires during state application), so run the rebuild once state application has finished.
        Timer.Spawn(TimeSpan.Zero, () =>
        {
            if (!TryComp(hostUid, out WH40KWeaponModHostComponent? host))
                return;

            RebuildHostOverlayClient((hostUid, host));
        });
    }

    private void OnHostInit(Entity<WH40KWeaponModHostComponent> ent, ref ComponentInit args)
    {
        if (ent.Comp.ModSlots.Count != 0)
            return;

        foreach (var definition in ent.Comp.SlotDefinitions)
        {
            var slotId = WH40KWeaponModHelper.GetSlotId(definition.Id);
            var slot = CreateModSlot(definition, ent.Comp.StartingMods.TryGetValue(definition.Id, out var startingMod)
                ? startingMod
                : definition.StartingItem);
            ent.Comp.ModSlots[slotId] = slot;
            _itemSlots.AddItemSlot(ent.Owner, slotId, slot);
        }

        UpdateAppearance(ent);
    }

    private void OnHostMapInit(Entity<WH40KWeaponModHostComponent> ent, ref MapInitEvent args)
    {
        UpdateAppearance(ent);
        RefreshHostState(ent);
    }

    private void OnHostRemove(Entity<WH40KWeaponModHostComponent> ent, ref ComponentRemove args)
    {
        foreach (var slot in ent.Comp.ModSlots.Values.ToArray())
        {
            _itemSlots.RemoveItemSlot(ent.Owner, slot);
        }

        ent.Comp.ModSlots.Clear();

        if (_net.IsClient)
            return;

        if (ent.Comp.BaseMeleeInitialized && TryComp(ent.Owner, out MeleeWeaponComponent? melee))
        {
            melee.Damage = new DamageSpecifier(ent.Comp.BaseMeleeDamage);
            melee.AttackRate = ent.Comp.BaseMeleeAttackRate;
            melee.Range = ent.Comp.BaseMeleeRange;
            melee.Animation = ent.Comp.BaseMeleeAnimation;
            melee.WideAnimation = ent.Comp.BaseMeleeWideAnimation;
            melee.WideAnimationRotation = ent.Comp.BaseMeleeWideAnimationRotation;
            Dirty(ent.Owner, melee);
        }

        if (ent.Comp.BaseAimInitialized && TryComp(ent.Owner, out AimingCameraComponent? aiming))
        {
            aiming.MaxOffset = ent.Comp.BaseAimMaxOffset;
            Dirty(ent.Owner, aiming);
        }

        if (ent.Comp.BaseCombatSightInitialized && TryComp(ent.Owner, out CombatSightComponent? combatSight))
        {
            combatSight.Sight = ent.Comp.BaseCombatSight;
            combatSight.Unavailable = ent.Comp.BaseCombatSightUnavailable;
            Dirty(ent.Owner, combatSight);
        }

        if (ent.Comp.BaseClothingSlotsInitialized && TryComp(ent.Owner, out ClothingComponent? clothing))
            _clothing.SetSlots(ent.Owner, ent.Comp.BaseClothingSlots, clothing);

        if (ent.Comp.BaseItemShapeInitialized && TryComp(ent.Owner, out ItemComponent? item))
            _item.SetShape(ent.Owner, ent.Comp.BaseItemShape, item);
    }

    private void OnHostInserted(Entity<WH40KWeaponModHostComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!IsModSlot(ent.Comp, args.Container.ID))
            return;

        UpdateAppearance(ent);
        RefreshHostState(ent);
    }

    private void OnHostRemoved(Entity<WH40KWeaponModHostComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!IsModSlot(ent.Comp, args.Container.ID))
            return;

        UpdateAppearance(ent);
        RefreshHostState(ent);
    }

    private void OnInsertAttempt(Entity<WH40KWeaponModHostComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Slot.ID == null || !IsModSlot(ent.Comp, args.Slot.ID))
            return;

        if (!TryGetSlotDefinition(ent.Comp, args.Slot.ID, out var definition) ||
            !TryComp(args.Item, out WH40KWeaponModComponent? mod))
        {
            args.Cancelled = true;
            return;
        }

        if (mod.SlotType == definition.SlotType)
            return;

        args.Cancelled = true;
    }

    private void OnExamined(Entity<WH40KWeaponModHostComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(WH40KWeaponModHostComponent)))
        {
            var installed = GetInstalledMods(ent).Count();
            args.PushMarkup(Loc.GetString(
                "wh40k-weapon-mod-host-examine",
                ("filled", installed),
                ("total", ent.Comp.SlotDefinitions.Count)));

            if (!args.IsInDetailsRange)
                return;

            foreach (var definition in ent.Comp.SlotDefinitions.OrderByDescending(x => x.Priority))
            {
                var slotName = Loc.GetString(definition.Name);
                var slotId = WH40KWeaponModHelper.GetSlotId(definition.Id);

                if (!ent.Comp.ModSlots.TryGetValue(slotId, out var slot) ||
                    slot.Item is not { } modUid ||
                    !TryComp(modUid, out WH40KWeaponModComponent? _))
                {
                    args.PushMarkup(Loc.GetString(
                        "wh40k-weapon-mod-host-entry-empty",
                        ("slot", slotName)));
                    continue;
                }

                args.PushMarkup(Loc.GetString(
                    "wh40k-weapon-mod-host-entry",
                    ("slot", slotName),
                    ("mod", Name(modUid))));
            }
        }
    }

    /// <summary>
    ///     Appends a dark-gray "Compatible weapons / Modifiers" block to a mod entity's examine text.
    ///     Reads the mod's subcomponents (Optic, Stock, Suppressor, MuzzleBrake, Barrel, MeleeOverride,
    ///     GrenadeLauncher, Foregrip, Bipod, LaserSight, Sling) to produce exact percentage stats,
    ///     and scans all EntityPrototypes with WH40KWeaponModHost to list compatible weapon names.
    /// </summary>
    private void OnModExamined(Entity<WH40KWeaponModComponent> ent, ref ExaminedEvent args)
    {
        // Only show the detailed stats block in the "details" range (close-up examine).
        if (!args.IsInDetailsRange)
            return;

        var weapons = GetCompatibleWeaponNames(ent);
        var statLines = BuildModStatLines(ent);

        using (args.PushGroup(nameof(WH40KWeaponModComponent)))
        {
            if (weapons.Count > 0)
            {
                args.PushMarkup(Loc.GetString(
                    "wh40k-weapon-mod-examine-compatible",
                    ("weapons", string.Join(", ", weapons))));
            }

            foreach (var line in statLines)
            {
                args.PushMarkup(line);
            }
        }
    }

    /// <summary>
    ///     Enumerates all weapon prototypes that have a WH40KWeaponModHost slot whose whitelist
    ///     tags include one of the mod's own tags. Returns localized weapon names.
    /// </summary>
    private List<string> GetCompatibleWeaponNames(Entity<WH40KWeaponModComponent> modEnt)
    {
        var result = new List<string>();

        if (!TryComp<TagComponent>(modEnt, out var tagComp))
            return result;

        var modTags = tagComp.Tags.Select(t => t.Id).ToHashSet();
        if (modTags.Count == 0)
            return result;

        foreach (var proto in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (!proto.TryGetComponent<WH40KWeaponModHostComponent>(out var host, _compFactory))
                continue;

            bool compatible = false;
            foreach (var def in host.SlotDefinitions)
            {
                if (def.SlotType != modEnt.Comp.SlotType)
                    continue;
                if (def.Whitelist is not { Tags: { } defTags })
                    continue;
                if (defTags.Any(t => modTags.Contains(t.Id)))
                {
                    compatible = true;
                    break;
                }
            }

            if (!compatible)
                continue;

            // Localized prototype name (ent-{id} convention; falls back to proto.ID).
            var name = Loc.GetString($"ent-{proto.ID}");
            if (string.IsNullOrEmpty(name) || name == $"ent-{proto.ID}")
                name = proto.ID;
            if (!result.Contains(name))
                result.Add(name);
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>
    ///     Builds the list of dark-gray modifier description lines for the mod, based on which
    ///     behavior subcomponents are present. Numbers are rendered as percentages relative to
    ///     the gun's base value: a multiplier of 0.7 becomes "−30%", 1.2 becomes "+20%".
    /// </summary>
    private List<string> BuildModStatLines(Entity<WH40KWeaponModComponent> modEnt)
    {
        var lines = new List<string>();
        var uid = modEnt.Owner;

        // Optic
        if (TryComp<WH40KWeaponModOpticComponent>(uid, out var optic))
        {
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-aim-range", $"+{optic.AimRangeBonus:0.#}"));
            if (optic.HighlightTargets)
                lines.Add(FormatStatLine("wh40k-weapon-mod-stat-target-highlight", null));
        }

        // Suppressor
        if (TryComp<WH40KWeaponModSuppressorComponent>(uid, out var supp))
        {
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-volume", FormatOffset(supp.VolumeOffset)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-muzzle-flash", null));
        }

        // Muzzle brake
        if (TryComp<WH40KWeaponModMuzzleBrakeComponent>(uid, out var brake))
        {
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-volume", FormatOffset(brake.VolumeOffset)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-spread", FormatMultiplierPercent(brake.SpreadMultiplier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-camera-recoil", FormatMultiplierPercent(brake.CameraRecoilMultiplier)));
        }

        // Barrel (long)
        if (TryComp<WH40KWeaponModBarrelComponent>(uid, out var barrel))
        {
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-projectile-speed", FormatMultiplierPercent(barrel.ProjectileSpeedMultiplier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-spread", FormatMultiplierPercent(barrel.SpreadMultiplier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-camera-recoil", FormatMultiplierPercent(barrel.CameraRecoilMultiplier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-walk-speed", FormatMultiplierPercent(barrel.WalkModifier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-sprint-speed", FormatMultiplierPercent(barrel.SprintModifier)));
        }

        // Short barrel
        if (TryComp<WH40KWeaponModShortBarrelComponent>(uid, out var shortBarrel))
        {
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-projectile-speed", FormatMultiplierPercent(shortBarrel.ProjectileSpeedMultiplier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-spread", FormatMultiplierPercent(shortBarrel.SpreadMultiplier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-camera-recoil", FormatMultiplierPercent(shortBarrel.CameraRecoilMultiplier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-walk-speed", FormatMultiplierPercent(shortBarrel.WalkModifier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-sprint-speed", FormatMultiplierPercent(shortBarrel.SprintModifier)));
        }

        // Bayonet / Serp (MeleeOverride)
        if (TryComp<WH40KWeaponModMeleeOverrideComponent>(uid, out var melee))
        {
            var dmg = melee.Damage.GetTotal().Int();
            string dmgType = "Piercing";
            if (melee.Damage.DamageDict.Count > 0)
                dmgType = melee.Damage.DamageDict.First().Key.Id;
            var typeLabel = Loc.GetString($"wh40k-weapon-mod-stat-damage-type-{dmgType.ToLowerInvariant()}");
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-melee-damage", $"{dmg} {typeLabel}"));
        }

        // Stock
        if (TryComp<WH40KWeaponModStockComponent>(uid, out var stock))
        {
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-spread", FormatMultiplierPercent(stock.SpreadMultiplier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-camera-recoil", FormatMultiplierPercent(stock.CameraRecoilMultiplier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-walk-speed", FormatMultiplierPercent(stock.WalkModifier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-sprint-speed", FormatMultiplierPercent(stock.SprintModifier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-storage-size", null));
            if (TryComp<WH40KWeaponModFoldingStockComponent>(uid, out _))
                lines.Add(FormatStatLine("wh40k-weapon-mod-stat-folding", null));
        }

        // Foregrip
        if (TryComp<WH40KWeaponModForegripComponent>(uid, out var grip))
        {
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-spread", FormatMultiplierPercent(grip.SpreadMultiplier)));
        }

        // Bipod
        if (TryComp<WH40KWeaponModBipodComponent>(uid, out var bipod))
        {
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-spread-deployed", FormatMultiplierPercent(bipod.SpreadMultiplier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-camera-recoil-deployed", FormatMultiplierPercent(bipod.CameraRecoilMultiplier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-walk-speed-deployed", FormatMultiplierPercent(bipod.WalkModifier)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-sprint-speed-deployed", FormatMultiplierPercent(bipod.SprintModifier)));
        }

        // Grenade launcher
        if (TryComp<WH40KWeaponModGrenadeLauncherComponent>(uid, out _))
        {
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-grenade-launcher", null));
        }

        // Laser sight
        if (TryComp<WH40KWeaponModLaserSightComponent>(uid, out var laser))
        {
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-laser-beam", laser.BeamColor.ToHexNoAlpha()));
        }

        // Sling
        if (TryComp<WH40KWeaponModSlingComponent>(uid, out var sling))
        {
            var slotNames = new List<string>();
            if ((sling.AdditionalSlots & SlotFlags.BACK) != 0)
                slotNames.Add(Loc.GetString("wh40k-weapon-mod-stat-slot-back"));
            if ((sling.AdditionalSlots & SlotFlags.BELT) != 0)
                slotNames.Add(Loc.GetString("wh40k-weapon-mod-stat-slot-belt"));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-carry-slots", string.Join(", ", slotNames)));
            lines.Add(FormatStatLine("wh40k-weapon-mod-stat-secured", null));
        }

        return lines;
    }

    /// <summary>
    ///     Renders a single stat line in dark-gray markup. If <paramref name="value"/> is null,
    ///     the line is rendered as a plain label (no value column).
    /// </summary>
    private string FormatStatLine(string key, string? value)
    {
        var label = Loc.GetString(key);
        if (value == null)
            return $"[color=#8090a0]{label}[/color]";
        return $"[color=#8090a0]{label}: {value}[/color]";
    }

    /// <summary>Multiplier 0.7 → "−30%", 1.2 → "+20%".</summary>
    private static string FormatMultiplierPercent(float mult)
    {
        var pct = (mult - 1f) * 100f;
        var sign = pct >= 0 ? "+" : "−";
        return $"{sign}{Math.Abs(pct):0.#}%";
    }

    /// <summary>Offset -20 → "−20", +2 → "+2" (raw number, not a percent).</summary>
    private static string FormatOffset(float offset)
    {
        var sign = offset >= 0 ? "+" : "−";
        return $"{sign}{Math.Abs(offset):0.#}";
    }

    private void OnGetActivationVerbs(Entity<WH40KWeaponModHostComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        AddActivationSlotOverviewVerbs(ent, args.User, args.Verbs);
    }

    private void OnRelayedEquipmentVerbs(Entity<WH40KWeaponModHostComponent> ent, ref InventoryRelayedEvent<GetVerbsEvent<EquipmentVerb>> args)
    {
        if (!args.Args.CanAccess || !args.Args.CanInteract)
            return;

        if (args.Args.Hands != null &&
            args.Args.Using != null &&
            TryComp(args.Args.Using.Value, out WH40KWeaponModComponent? _))
        {
            foreach (var (definition, slot) in GetInsertableSlots(ent, args.Args.Using.Value, args.Args.User))
            {
                var owner = ent.Owner;
                var user = args.Args.User;
                var hands = args.Args.Hands;
                var localSlot = slot;
                var usingUid = args.Args.Using.Value;
                args.Args.Verbs.Add(new EquipmentVerb
                {
                    Category = VerbCategory.Insert,
                    IconEntity = GetNetEntity(args.Args.Using),
                    Priority = slot.Priority,
                    TextLocId = slot.Name,
                    Act = () => TryHotswapInsertFromHand(owner, localSlot, usingUid, user, hands),
                });
            }
        }

        AddEquipmentSlotOverviewVerbs(ent, args.Args.User, args.Args.Verbs);
    }

    private void OnGunRefreshModifiers(Entity<WH40KWeaponModHostComponent> ent, ref GunRefreshModifiersEvent args)
    {
        foreach (var (_, _, modUid, _) in GetInstalledMods(ent))
        {
            RaiseLocalEvent(modUid, ref args);
        }
    }

    private void OnGunMuzzleFlashAttempt(Entity<WH40KWeaponModHostComponent> ent, ref GunMuzzleFlashAttemptEvent args)
    {
        foreach (var (_, _, modUid, _) in GetInstalledMods(ent))
        {
            RaiseLocalEvent(modUid, ref args);

            if (args.Cancelled)
                break;
        }
    }

    private void OnGotEquippedHand(Entity<WH40KWeaponModHostComponent> ent, ref GotEquippedHandEvent args)
    {
        foreach (var (_, _, modUid, _) in GetInstalledMods(ent))
        {
            RaiseLocalEvent(modUid, args);
        }
    }

    private void OnGotUnequippedHand(Entity<WH40KWeaponModHostComponent> ent, ref GotUnequippedHandEvent args)
    {
        foreach (var (_, _, modUid, _) in GetInstalledMods(ent))
        {
            RaiseLocalEvent(modUid, args);
        }
    }

    private void OnHandSelected(Entity<WH40KWeaponModHostComponent> ent, ref HandSelectedEvent args)
    {
        foreach (var (_, _, modUid, _) in GetInstalledMods(ent))
        {
            RaiseLocalEvent(modUid, args);
        }
    }

    private void OnHandDeselected(Entity<WH40KWeaponModHostComponent> ent, ref HandDeselectedEvent args)
    {
        foreach (var (_, _, modUid, _) in GetInstalledMods(ent))
        {
            RaiseLocalEvent(modUid, args);
        }
    }

    private void OnItemWielded(Entity<WH40KWeaponModHostComponent> ent, ref ItemWieldedEvent args)
    {
        foreach (var (_, _, modUid, _) in GetInstalledMods(ent))
        {
            RaiseLocalEvent(modUid, ref args);
        }

        _movementSpeed.RefreshMovementSpeedModifiers(args.User);
    }

    private void OnItemUnwielded(Entity<WH40KWeaponModHostComponent> ent, ref ItemUnwieldedEvent args)
    {
        foreach (var (_, _, modUid, _) in GetInstalledMods(ent))
        {
            RaiseLocalEvent(modUid, ref args);
        }

        _movementSpeed.RefreshMovementSpeedModifiers(args.User);
    }

    private void OnHeldRefreshMovementSpeedModifiers(
        Entity<WH40KWeaponModHostComponent> ent,
        ref HeldRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        foreach (var (_, _, modUid, _) in GetInstalledMods(ent))
        {
            RaiseLocalEvent(modUid, ref args);
        }
    }

    private ItemSlot CreateModSlot(WH40KWeaponModSlotDefinition definition, EntProtoId? startingItem)
    {
        return new ItemSlot
        {
            Whitelist = definition.Whitelist,
            InsertOnInteract = true,
            EjectOnInteract = false,
            DisableEject = true,
            // Hotswap enabled: clicking a filled mod slot with a compatible mod in hand ejects the
            // old mod to a free hand and inserts the new one. Compatibility is still enforced by
            // the slot whitelist + OnInsertAttempt (mod.SlotType == definition.SlotType).
            Swap = true,
            Priority = definition.Priority,
            Name = definition.Name,
            LockedFailPopup = "wh40k-weapon-mod-slot-locked",
            WhitelistFailPopup = "wh40k-weapon-mod-slot-invalid",
            InsertSound = definition.InsertSound ?? WH40KWeaponModHelper.GetDefaultInsertSound(definition.SlotType),
            EjectSound = definition.EjectSound ?? WH40KWeaponModHelper.GetDefaultEjectSound(definition.SlotType),
            StartingItem = startingItem,
        };
    }

    /// <summary>
    ///     Insert a held mod into a weapon mod slot, hotswapping the existing mod if the slot is filled.
    ///     The old mod is picked up into a free hand (or dropped if no free hand) before the new one is inserted.
    ///     Compatibility is enforced by the slot whitelist + OnInsertAttempt (mod.SlotType == definition.SlotType).
    /// </summary>
    public bool TryHotswapInsertFromHand(
        EntityUid owner,
        ItemSlot slot,
        EntityUid usedMod,
        EntityUid user,
        HandsComponent? hands = null)
    {
        if (!Resolve(user, ref hands, false))
            return false;

        // Verify the new mod still passes compatibility (whitelist + OnInsertAttempt) before doing
        // anything destructive. CanInsert with swap=true also checks that the old mod can be ejected.
        if (!_itemSlots.CanInsert(owner, usedMod, user, slot, swap: true))
        {
            _popup.PopupClient(Loc.GetString("wh40k-weapon-mod-hotswap-incompatible"), owner, user);
            return false;
        }

        // If the slot is filled, hotswap: pick up the old mod into a free hand first.
        if (slot.Item is { } oldMod)
        {
            // Need a free hand to receive the old mod (the active hand holds the new mod).
            // TryPickupAnyHand uses an empty hand, so the active hand (holding new mod) is skipped.
            if (!_hands.TryPickupAnyHand(user, oldMod, handsComp: hands))
            {
                // No free hand — drop the new mod's hand so we can pick up the old one, then the
                // caller will need to re-grab the new mod. Simpler: just abort with a popup telling
                // the user to free a hand.
                _popup.PopupClient(Loc.GetString("wh40k-weapon-mod-hotswap-no-free-hand"), owner, user);
                return false;
            }

            // Play the slot's eject sound for the old mod being removed.
            if (slot.EjectSound != null)
                _audio.PlayPredicted(slot.EjectSound, owner, user);

            // Old mod is now in a free hand; the slot is empty. Drop the new mod from the active hand
            // and insert it into the now-empty slot.
            if (!_hands.TryDrop((user, hands), usedMod))
            {
                // Failed to drop the new mod — the old mod is already in hand, so put it back.
                _itemSlots.TryInsert(owner, slot, oldMod, user);
                return false;
            }

            _itemSlots.TryInsert(owner, slot, usedMod, user, excludeUserAudio: true);
            _popup.PopupClient(Loc.GetString(
                "wh40k-weapon-mod-hotswap-swapped",
                ("old", Name(oldMod)),
                ("new", Name(usedMod))),
                owner, user);
            return true;
        }

        // Empty slot: plain insert from hand.
        if (!_hands.TryDrop((user, hands), usedMod))
            return false;

        _itemSlots.TryInsert(owner, slot, usedMod, user, excludeUserAudio: true);
        return true;
    }

    public void RefreshHost(EntityUid uid, WH40KWeaponModHostComponent? host = null)
    {
        if (!Resolve(uid, ref host, false))
            return;

        var ent = (uid, host);
        UpdateAppearance(ent);
        RefreshHostState(ent);
    }

    private void RefreshHostState(Entity<WH40KWeaponModHostComponent> ent)
    {
        if (!_net.IsServer)
            return;

        if (TryComp(ent.Owner, out GunComponent? gun))
            _defaultMelee.EnsureDefaultMelee(ent.Owner, gun);

        RefreshMeleeProfile(ent);
        RefreshOpticProfile(ent);
        RefreshClothingProfile(ent);
        RefreshItemShapeProfile(ent);

        if (gun != null)
            _gun.RefreshModifiers(ent.Owner);

        if (TryGetHoldingUser(ent.Owner, out var user))
            _movementSpeed.RefreshMovementSpeedModifiers(user);
    }

    private void RefreshMeleeProfile(Entity<WH40KWeaponModHostComponent> ent)
    {
        if (!_net.IsServer || !TryComp(ent.Owner, out MeleeWeaponComponent? melee))
            return;

        EnsureBaseMelee(ent, melee);

        melee.Damage = new DamageSpecifier(ent.Comp.BaseMeleeDamage);
        melee.AttackRate = ent.Comp.BaseMeleeAttackRate;
        melee.Range = ent.Comp.BaseMeleeRange;
        melee.Animation = ent.Comp.BaseMeleeAnimation;
        melee.WideAnimation = ent.Comp.BaseMeleeWideAnimation;
        melee.WideAnimationRotation = ent.Comp.BaseMeleeWideAnimationRotation;

        foreach (var (_, _, modUid, _) in GetInstalledMods(ent))
        {
            if (!TryComp(modUid, out WH40KWeaponModMeleeOverrideComponent? overrideComp))
                continue;

            melee.Damage = new DamageSpecifier(overrideComp.Damage);
            melee.AttackRate = overrideComp.AttackRate;
            melee.Range = overrideComp.Range;
            melee.Animation = overrideComp.Animation;
            melee.WideAnimation = overrideComp.WideAnimation;
            melee.WideAnimationRotation = overrideComp.WideAnimationRotation;
            break;
        }

        Dirty(ent.Owner, melee);
    }

    private void RefreshOpticProfile(Entity<WH40KWeaponModHostComponent> ent)
    {
        WH40KWeaponModOpticComponent? optic = null;

        foreach (var (_, _, modUid, _) in GetInstalledMods(ent))
        {
            if (!TryComp(modUid, out WH40KWeaponModOpticComponent? opticComp))
                continue;

            optic = opticComp;
            break;
        }

        if (TryComp(ent.Owner, out AimingCameraComponent? aiming))
        {
            EnsureBaseAiming(ent, aiming);
            var targetOffset = ent.Comp.BaseAimMaxOffset + (optic?.AimRangeBonus ?? 0f);

            if (!MathHelper.CloseTo(aiming.MaxOffset, targetOffset))
            {
                aiming.MaxOffset = targetOffset;
                Dirty(ent.Owner, aiming);
            }
        }

        if (TryComp(ent.Owner, out CombatSightComponent? combatSight))
        {
            EnsureBaseCombatSight(ent, combatSight);

            var nextSight = optic?.Sight ?? ent.Comp.BaseCombatSight;
            var nextUnavailable = optic?.UnavailableSight ?? ent.Comp.BaseCombatSightUnavailable;

            if (!Equals(combatSight.Sight, nextSight) || !Equals(combatSight.Unavailable, nextUnavailable))
            {
                combatSight.Sight = nextSight;
                combatSight.Unavailable = nextUnavailable;
                Dirty(ent.Owner, combatSight);
            }
        }
    }

    private void EnsureBaseMelee(Entity<WH40KWeaponModHostComponent> ent, MeleeWeaponComponent melee)
    {
        if (ent.Comp.BaseMeleeInitialized)
            return;

        ent.Comp.BaseMeleeDamage = new DamageSpecifier(melee.Damage);
        ent.Comp.BaseMeleeAttackRate = melee.AttackRate;
        ent.Comp.BaseMeleeRange = melee.Range;
        ent.Comp.BaseMeleeAnimation = melee.Animation;
        ent.Comp.BaseMeleeWideAnimation = melee.WideAnimation;
        ent.Comp.BaseMeleeWideAnimationRotation = melee.WideAnimationRotation;
        ent.Comp.BaseMeleeInitialized = true;
    }

    private void EnsureBaseAiming(Entity<WH40KWeaponModHostComponent> ent, AimingCameraComponent aiming)
    {
        if (ent.Comp.BaseAimInitialized)
            return;

        ent.Comp.BaseAimMaxOffset = aiming.MaxOffset;
        ent.Comp.BaseAimInitialized = true;
    }

    public void EnsureBaseCombatSight(Entity<WH40KWeaponModHostComponent> ent, CombatSightComponent combatSight)
    {
        if (ent.Comp.BaseCombatSightInitialized)
            return;

        ent.Comp.BaseCombatSight = combatSight.Sight;
        ent.Comp.BaseCombatSightUnavailable = combatSight.Unavailable;
        ent.Comp.BaseCombatSightInitialized = true;
    }

    private void EnsureBaseClothingSlots(Entity<WH40KWeaponModHostComponent> ent, ClothingComponent clothing)
    {
        if (ent.Comp.BaseClothingSlotsInitialized)
            return;

        ent.Comp.BaseClothingSlots = clothing.Slots;
        ent.Comp.BaseClothingSlotsInitialized = true;
    }

    private void UpdateAppearance(Entity<WH40KWeaponModHostComponent> ent)
    {
        // Build the overlay dict from WH40KWeaponModComponent.OverlayState (networked). Runs on BOTH
        // sides: the server authoritatively sets it and replicates via AppearanceComponentState; the
        // client rebuilds it from its networked OverlayState whenever OnHostInserted/OnHostRemoved/
        // RefreshHost fire (e.g. on stock toggle). SetData no-ops during server state application
        // (CheckIfApplyingState), so during PVS re-entry the client relies on the replicated
        // AppearanceComponentState — but for live toggle/drop events (outside state application)
        // this rebuild is what keeps the overlay in sync with the folded state on the client.
        var overlaySprites = new Dictionary<string, string>();
        var overlayStates = new Dictionary<string, string>();

        foreach (var (slotId, _, modUid, mod) in GetInstalledMods(ent))
        {
            if (string.IsNullOrWhiteSpace(mod.OverlaySprite))
                continue;

            overlaySprites[slotId] = mod.OverlaySprite!;
            overlayStates[slotId] = mod.OverlayState;
        }

        _appearance.SetData(ent.Owner, WH40KWeaponModVisuals.OverlaySprites, overlaySprites);
        _appearance.SetData(ent.Owner, WH40KWeaponModVisuals.OverlayStates, overlayStates);
    }

    private void RefreshClothingProfile(Entity<WH40KWeaponModHostComponent> ent)
    {
        if (!_net.IsServer || !TryComp(ent.Owner, out ClothingComponent? clothing))
            return;

        EnsureBaseClothingSlots(ent, clothing);

        var targetSlots = ent.Comp.BaseClothingSlots;
        foreach (var (_, _, modUid, _) in GetInstalledMods(ent))
        {
            if (!TryComp(modUid, out WH40KWeaponModSlingComponent? sling))
                continue;

            targetSlots |= sling.AdditionalSlots;
        }

        if (clothing.Slots != targetSlots)
            _clothing.SetSlots(ent.Owner, targetSlots, clothing);
    }

    /// <summary>
    ///     Recomputes the weapon's <see cref="ItemComponent.Shape"/> based on whether a stock mod is
    ///     installed and (for folding stocks) whether it is unfolded.
    ///     - Fixed stock installed → full base shape (e.g. 2×5).
    ///     - Folding stock installed + unfolded → full base shape.
    ///     - Folding stock installed + folded → width-reduced shape (e.g. 2×4).
    ///     - No stock installed → width-reduced shape (e.g. 2×4).
    ///     The width reduction removes 1 column from every box in the shape (the full-height column
    ///     at the right edge, representing the stock's footprint in the storage grid).
    /// </summary>
    private void RefreshItemShapeProfile(Entity<WH40KWeaponModHostComponent> ent)
    {
        if (!_net.IsServer || !TryComp(ent.Owner, out ItemComponent? item))
            return;

        EnsureBaseItemShape(ent, item);

        // Determine whether an active (deployed) stock is installed.
        bool hasActiveStock = false;
        foreach (var (_, _, modUid, mod) in GetInstalledMods(ent))
        {
            if (mod.SlotType != WH40KWeaponModSlotType.StockRear)
                continue;

            if (!TryComp(modUid, out WH40KWeaponModStockComponent? _))
                continue;

            // Folding stock: only counts as active when unfolded.
            if (TryComp(modUid, out WH40KWeaponModFoldingStockComponent? folding) && folding.Folded)
                continue;

            hasActiveStock = true;
            break;
        }

        var targetShape = hasActiveStock
            ? ent.Comp.BaseItemShape
            : ReduceShapeWidth(ent.Comp.BaseItemShape);

        _item.SetShape(ent.Owner, targetShape, item);
    }

    private void EnsureBaseItemShape(Entity<WH40KWeaponModHostComponent> ent, ItemComponent item)
    {
        if (ent.Comp.BaseItemShapeInitialized)
            return;

        // Snapshot the current shape (or the default for the item's Size if Shape is null).
        ent.Comp.BaseItemShape = item.Shape != null
            ? new List<Box2i>(item.Shape)
            : new List<Box2i>(_item.GetItemShape((ent.Owner, item)));

        ent.Comp.BaseItemShapeInitialized = true;
    }

    /// <summary>
    ///     Returns a copy of <paramref name="shape"/> with every box's right edge shifted left by 1,
    ///     reducing the overall width by one column. Boxes that would collapse to zero or negative
    ///     width are dropped. Used to represent a weapon without its stock in storage.
    /// </summary>
    private static List<Box2i>? ReduceShapeWidth(List<Box2i>? shape)
    {
        if (shape == null || shape.Count == 0)
            return shape;

        var result = new List<Box2i>(shape.Count);
        foreach (var box in shape)
        {
            var newRight = box.Right - 1;
            if (newRight < box.Left)
                continue;
            result.Add(new Box2i(box.Left, box.Bottom, newRight, box.Top));
        }

        return result.Count == 0 ? shape : result;
    }

    /// <summary>
    ///     Returns true if the weapon has a Sling mod installed in any of its SlingMount slots.
    ///     Used by <see cref="SharedWH40KWeaponModSlingSystem"/> to prevent the weapon from
    ///     being dropped on fall/stun/crit/slip and from being thrown.
    /// </summary>
    public bool TryGetInstalledSling(Entity<WH40KWeaponModHostComponent> ent)
    {
        foreach (var (_, _, modUid, mod) in GetInstalledMods(ent))
        {
            if (mod.SlotType != WH40KWeaponModSlotType.SlingMount)
                continue;

            if (TryComp(modUid, out WH40KWeaponModSlingComponent? sling) &&
                (sling.AdditionalSlots & (SlotFlags.BACK | SlotFlags.BELT)) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetHoldingUser(EntityUid item, out EntityUid user)
    {
        user = default;

        if (!TryComp(item, out TransformComponent? xform))
            return false;

        var parent = xform.ParentUid;
        if (parent == EntityUid.Invalid || !HasComp<HandsComponent>(parent))
            return false;

        user = parent;
        return true;
    }

    private IEnumerable<(WH40KWeaponModSlotDefinition Definition, ItemSlot Slot)> GetInsertableSlots(
        Entity<WH40KWeaponModHostComponent> ent,
        EntityUid modUid,
        EntityUid user)
    {
        foreach (var definition in ent.Comp.SlotDefinitions.OrderByDescending(x => x.Priority))
        {
            var slotId = WH40KWeaponModHelper.GetSlotId(definition.Id);
            if (!_itemSlots.TryGetSlot(ent.Owner, slotId, out var slot))
                continue;

            // Hotswap: a filled slot is still insertable if the new mod passes the whitelist and
            // the old mod can be ejected. CanInsert with swap=true covers both the empty and the
            // hotswap cases, so the verb shows up for filled slots too.
            if (!_itemSlots.CanInsert(ent.Owner, modUid, user, slot, swap: true))
                continue;

            yield return (definition, slot);
        }
    }

    private IEnumerable<(string SlotId, WH40KWeaponModSlotDefinition Definition, EntityUid ModUid, WH40KWeaponModComponent Mod)> GetInstalledMods(
        Entity<WH40KWeaponModHostComponent> ent)
    {
        foreach (var definition in ent.Comp.SlotDefinitions.OrderByDescending(x => x.Priority))
        {
            var slotId = WH40KWeaponModHelper.GetSlotId(definition.Id);
            if (!ent.Comp.ModSlots.TryGetValue(slotId, out var slot) ||
                slot.Item is not { } modUid ||
                !TryComp(modUid, out WH40KWeaponModComponent? mod))
            {
                continue;
            }

            yield return (slotId, definition, modUid, mod);
        }
    }

    private void AddActivationSlotOverviewVerbs(
        Entity<WH40KWeaponModHostComponent> ent,
        EntityUid user,
        SortedSet<ActivationVerb> verbs)
    {
        foreach (var definition in ent.Comp.SlotDefinitions.OrderByDescending(x => x.Priority))
        {
            var slotId = WH40KWeaponModHelper.GetSlotId(definition.Id);
            if (!ent.Comp.ModSlots.TryGetValue(slotId, out var slot))
                continue;

            var verb = new ActivationVerb
            {
                Category = WeaponModSlotsCategory,
                Priority = definition.Priority,
            };

            var slotName = Loc.GetString(definition.Name);
            if (slot.Item is not { } modUid)
            {
                verb.Text = Loc.GetString("wh40k-weapon-mod-slot-empty-verb", ("slot", slotName));
                verb.Disabled = true;
                verbs.Add(verb);
                continue;
            }

            verb.Text = Loc.GetString(
                "wh40k-weapon-mod-slot-filled-verb",
                ("slot", slotName),
                ("mod", Name(modUid)));
            verb.IconEntity = GetNetEntity(modUid);

            if (!_actionBlocker.CanPickup(user, modUid))
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
        Entity<WH40KWeaponModHostComponent> ent,
        EntityUid user,
        SortedSet<EquipmentVerb> verbs)
    {
        foreach (var definition in ent.Comp.SlotDefinitions.OrderByDescending(x => x.Priority))
        {
            var slotId = WH40KWeaponModHelper.GetSlotId(definition.Id);
            if (!ent.Comp.ModSlots.TryGetValue(slotId, out var slot))
                continue;

            var verb = new EquipmentVerb
            {
                Category = WeaponModSlotsCategory,
                Priority = definition.Priority,
            };

            var slotName = Loc.GetString(definition.Name);
            if (slot.Item is not { } modUid)
            {
                verb.Text = Loc.GetString("wh40k-weapon-mod-slot-empty-verb", ("slot", slotName));
                verb.Disabled = true;
                verbs.Add(verb);
                continue;
            }

            verb.Text = Loc.GetString(
                "wh40k-weapon-mod-slot-filled-verb",
                ("slot", slotName),
                ("mod", Name(modUid)));
            verb.IconEntity = GetNetEntity(modUid);

            if (!_actionBlocker.CanPickup(user, modUid))
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

    private static bool IsModSlot(WH40KWeaponModHostComponent holder, string slotId)
    {
        return holder.ModSlots.ContainsKey(slotId);
    }

    private static bool TryGetSlotDefinition(
        WH40KWeaponModHostComponent holder,
        string slotId,
        out WH40KWeaponModSlotDefinition definition)
    {
        definition = default!;

        foreach (var candidate in holder.SlotDefinitions)
        {
            if (WH40KWeaponModHelper.GetSlotId(candidate.Id) != slotId)
                continue;

            definition = candidate;
            return true;
        }

        return false;
    }
}
