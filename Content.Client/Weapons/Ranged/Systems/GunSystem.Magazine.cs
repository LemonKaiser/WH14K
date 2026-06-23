using Content.Shared.Weapons.Ranged;

using Robust.Client.GameObjects;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    protected override void InitializeMagazine()
    {
        base.InitializeMagazine();
        SubscribeLocalEvent<MagazineAmmoProviderComponent, UpdateAmmoCounterEvent>(OnMagazineAmmoUpdate);
        SubscribeLocalEvent<MagazineAmmoProviderComponent, AmmoCounterControlEvent>(OnMagazineControl);
        SubscribeLocalEvent<MagazineAmmoProviderComponent, AppearanceChangeEvent>(OnMagazineAppearance);
    }

    private void OnMagazineAmmoUpdate(Entity<MagazineAmmoProviderComponent> ent, ref UpdateAmmoCounterEvent args)
    {
        var magEnt = GetMagazineEntity(ent);

        if (magEnt == null)
        {
            if (args.Control is DefaultStatusControl control)
            {
                control.Update(0, 0);
            }

            return;
        }

        RaiseLocalEvent(magEnt.Value, args, false);
    }

    private void OnMagazineControl(Entity<MagazineAmmoProviderComponent> ent, ref AmmoCounterControlEvent args)
    {
        var magEnt = GetMagazineEntity(ent);
        if (magEnt == null)
            return;
        RaiseLocalEvent(magEnt.Value, args, false);
    }

    private void OnMagazineAppearance(Entity<MagazineAmmoProviderComponent> ent, ref AppearanceChangeEvent args)
    {
        UpdateAmmoCount(ent);
    }
}
