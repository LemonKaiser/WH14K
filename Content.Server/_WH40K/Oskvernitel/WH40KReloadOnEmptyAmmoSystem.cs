using Content.Shared._WH40K.Oskvernitel;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Oskvernitel;

public sealed partial class WH40KReloadOnEmptyAmmoSystem : EntitySystem
{
    [Dependency] private  SharedGunSystem _gun = default!;
    [Dependency] private  IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WH40KReloadOnEmptyAmmoComponent, BasicEntityAmmoProviderComponent>();

        while (query.MoveNext(out var uid, out var reload, out var ammo))
        {
            if (ammo.Capacity is not { } capacity || ammo.Count is not { } count)
                continue;

            if (count == capacity)
            {
                reload.NextReloadTime = null;
                continue;
            }

            if (count > 0)
            {
                reload.NextReloadTime = null;
                continue;
            }

            if (reload.NextReloadTime == null)
            {
                reload.NextReloadTime = _timing.CurTime + TimeSpan.FromSeconds(reload.ReloadCooldown);
                continue;
            }

            if (reload.NextReloadTime > _timing.CurTime)
                continue;

            _gun.UpdateBasicEntityAmmoCount((uid, ammo), capacity);
            reload.NextReloadTime = null;
        }
    }
}
