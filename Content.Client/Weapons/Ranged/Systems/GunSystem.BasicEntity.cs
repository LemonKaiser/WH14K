using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.GameObjects;

namespace Content.Client.Weapons.Ranged.Systems;

public partial class GunSystem
{
    protected override void InitializeBasicEntity()
    {
        base.InitializeBasicEntity();
        SubscribeLocalEvent<BasicEntityAmmoProviderComponent, UpdateAmmoCounterEvent>(OnBasicEntityAmmoCount);
        SubscribeLocalEvent<BasicEntityAmmoProviderComponent, AppearanceChangeEvent>(OnBasicEntityAppearance);
    }

    private void OnBasicEntityAmmoCount(Entity<BasicEntityAmmoProviderComponent> ent, ref UpdateAmmoCounterEvent args)
    {
        if (args.Control is DefaultStatusControl control && ent.Comp.Count != null && ent.Comp.Capacity != null)
        {
            control.Update(ent.Comp.Count.Value, ent.Comp.Capacity.Value);
        }
    }

    private void OnBasicEntityAppearance(Entity<BasicEntityAmmoProviderComponent> ent, ref AppearanceChangeEvent args)
    {
        UpdateAmmoCount(ent);
    }
}
