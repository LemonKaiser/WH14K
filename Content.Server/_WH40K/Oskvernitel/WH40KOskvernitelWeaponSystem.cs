using Content.Shared._WH40K.Oskvernitel;
using Content.Shared.UserInterface;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;

namespace Content.Server._WH40K.Oskvernitel;

public sealed partial class WH40KOskvernitelWeaponSystem : SharedWH40KOskvernitelWeaponSystem
{
    private const string MinigunLocKey = "wh40k-oskvernitel-weapon-minigun-name";
    private const string AutogunLocKey = "wh40k-oskvernitel-weapon-autogun-name";

    [Dependency] private  SharedContainerSystem _container = default!;
    [Dependency] private  SharedGunSystem _gun = default!;
    [Dependency] private  UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KOskvernitelWeaponComponent, MapInitEvent>(OnMapInit);

        Subs.BuiEvents<WH40KOskvernitelWeaponComponent>(WH40KOskvernitelWeaponUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<WH40KOskvernitelWeaponSelectMessage>(OnWeaponSelected);
        });
    }

    private void OnMapInit(Entity<WH40KOskvernitelWeaponComponent> ent, ref MapInitEvent args)
    {
        EnsureWeapons(ent);
        Dirty(ent);
    }

    private void OnUiOpened(Entity<WH40KOskvernitelWeaponComponent> ent, ref BoundUIOpenedEvent args)
    {
        EnsureWeapons(ent);
        UpdateUi(ent);
    }

    private void OnWeaponSelected(Entity<WH40KOskvernitelWeaponComponent> ent, ref WH40KOskvernitelWeaponSelectMessage args)
    {
        EnsureWeapons(ent);

        var slot = ToWeaponSlot(args.Entry);
        if (ent.Comp.SelectedWeapon == slot)
            return;

        ent.Comp.SelectedWeapon = slot;
        Dirty(ent);
        UpdateUi(ent);
    }

    private void EnsureWeapons(Entity<WH40KOskvernitelWeaponComponent> ent)
    {
        var container = _container.EnsureContainer<Container>(ent.Owner, ent.Comp.WeaponContainerId);

        ent.Comp.MinigunEntity = EnsureWeapon(container, ent.Comp.MinigunEntity, ent.Comp.MinigunPrototype, ent.Owner);
        ent.Comp.AutogunEntity = EnsureWeapon(container, ent.Comp.AutogunEntity, ent.Comp.AutogunPrototype, ent.Owner);

        if (!IsWeaponValid(ent.Comp, ent.Comp.SelectedWeapon))
            ent.Comp.SelectedWeapon = ent.Comp.MinigunEntity != null
                ? WH40KOskvernitelWeaponSlot.Minigun
                : WH40KOskvernitelWeaponSlot.Autogun;
    }

    private EntityUid? EnsureWeapon(Container container, EntityUid? weaponUid, string prototypeId, EntityUid owner)
    {
        if (weaponUid is { } existing &&
            !TerminatingOrDeleted(existing))
        {
            if (!container.Contains(existing))
                _container.Insert(existing, container);

            return existing;
        }

        var spawned = Spawn(prototypeId, Transform(owner).Coordinates);
        _container.Insert(spawned, container);
        return spawned;
    }

    private bool IsWeaponValid(WH40KOskvernitelWeaponComponent component, WH40KOskvernitelWeaponSlot slot)
    {
        var selected = GetSelectedWeaponUid(component);
        return selected != null && !TerminatingOrDeleted(selected.Value);
    }

    private void UpdateUi(Entity<WH40KOskvernitelWeaponComponent> ent)
    {
        var entries = new[]
        {
            BuildEntry(ent, WH40KOskvernitelWeaponSlot.Minigun, MinigunLocKey, ent.Comp.MinigunPrototype),
            BuildEntry(ent, WH40KOskvernitelWeaponSlot.Autogun, AutogunLocKey, ent.Comp.AutogunPrototype),
        };

        _ui.SetUiState(ent.Owner, WH40KOskvernitelWeaponUiKey.Key, new WH40KOskvernitelWeaponBuiState(entries));
    }

    private WH40KOskvernitelWeaponEntryState BuildEntry(
        Entity<WH40KOskvernitelWeaponComponent> ent,
        WH40KOskvernitelWeaponSlot slot,
        string locKey,
        string prototypeId)
    {
        var weapon = slot switch
        {
            WH40KOskvernitelWeaponSlot.Minigun => ent.Comp.MinigunEntity,
            WH40KOskvernitelWeaponSlot.Autogun => ent.Comp.AutogunEntity,
            _ => null,
        };

        var currentAmmo = weapon is { } uid ? _gun.GetAmmoCount(uid) : 0;
        var maxAmmo = weapon is { } capacityUid ? _gun.GetAmmoCapacity(capacityUid) : 0;

        return new WH40KOskvernitelWeaponEntryState(
            ToEntryId(slot),
            prototypeId,
            locKey,
            currentAmmo,
            maxAmmo,
            ent.Comp.SelectedWeapon == slot);
    }
}
