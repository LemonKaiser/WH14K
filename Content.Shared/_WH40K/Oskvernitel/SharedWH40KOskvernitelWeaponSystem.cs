using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared._WH40K.Oskvernitel;

public abstract class SharedWH40KOskvernitelWeaponSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KOskvernitelWeaponComponent, GetActiveWeaponEvent>(OnGetActiveWeapon);
    }

    protected EntityUid? GetSelectedWeaponUid(WH40KOskvernitelWeaponComponent component)
    {
        return component.SelectedWeapon switch
        {
            WH40KOskvernitelWeaponSlot.Minigun => component.MinigunEntity,
            WH40KOskvernitelWeaponSlot.Autogun => component.AutogunEntity,
            _ => null,
        };
    }

    protected static WH40KOskvernitelWeaponUiEntryId ToEntryId(WH40KOskvernitelWeaponSlot slot)
    {
        return slot switch
        {
            WH40KOskvernitelWeaponSlot.Minigun => WH40KOskvernitelWeaponUiEntryId.Minigun,
            WH40KOskvernitelWeaponSlot.Autogun => WH40KOskvernitelWeaponUiEntryId.Autogun,
            _ => WH40KOskvernitelWeaponUiEntryId.Minigun,
        };
    }

    protected static WH40KOskvernitelWeaponSlot ToWeaponSlot(WH40KOskvernitelWeaponUiEntryId entry)
    {
        return entry switch
        {
            WH40KOskvernitelWeaponUiEntryId.Minigun => WH40KOskvernitelWeaponSlot.Minigun,
            WH40KOskvernitelWeaponUiEntryId.Autogun => WH40KOskvernitelWeaponSlot.Autogun,
            _ => WH40KOskvernitelWeaponSlot.Minigun,
        };
    }

    private void OnGetActiveWeapon(Entity<WH40KOskvernitelWeaponComponent> ent, ref GetActiveWeaponEvent args)
    {
        if (args.Handled)
            return;

        var selected = GetSelectedWeaponUid(ent.Comp);
        if (selected == null || !TryComp<GunComponent>(selected.Value, out _))
            return;

        args.Weapon = selected.Value;
        args.Handled = true;
    }
}
