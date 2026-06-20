using Content.Client.Items;
using Content.Client.Weapons.Ranged.Systems;
using Content.Shared._WH40K.Weapons.Mods;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;

namespace Content.Client._WH40K.Weapons.Mods;

public sealed partial class WH40KWeaponModStatusSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KWeaponModHostComponent, ItemStatusCollectMessage>(
            OnItemStatusCollect,
            after: [typeof(GunSystem)]);
    }

    private void OnItemStatusCollect(Entity<WH40KWeaponModHostComponent> ent, ref ItemStatusCollectMessage args)
    {
        if (!TryGetPresentedLauncher(ent, out var launcherUid))
            return;

        args.Controls.Clear();

        var forwarded = new ItemStatusCollectMessage();
        RaiseLocalEvent(launcherUid, forwarded);

        foreach (var control in forwarded.Controls)
        {
            args.Controls.Add(control);
        }
    }

    private bool TryGetPresentedLauncher(Entity<WH40KWeaponModHostComponent> hostEnt, out EntityUid launcherUid)
    {
        launcherUid = default;

        if (!TryGetHoldingUser(hostEnt.Owner, out var user) ||
            !_hands.TryGetActiveItem(user, out var activeItem) ||
            activeItem != hostEnt.Owner)
        {
            return false;
        }

        foreach (var slot in hostEnt.Comp.ModSlots.Values)
        {
            if (slot.Item is not { } modUid ||
                !TryComp(modUid, out WH40KWeaponModGrenadeLauncherComponent? grenadeLauncher) ||
                !grenadeLauncher.Active)
            {
                continue;
            }

            launcherUid = modUid;
            return true;
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
}
