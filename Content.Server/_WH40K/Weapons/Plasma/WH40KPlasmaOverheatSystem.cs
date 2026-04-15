using Content.Server.Explosion.EntitySystems;
using Content.Shared._WH40K.Weapons.Plasma;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Weapons.Plasma;

public sealed class WH40KPlasmaOverheatSystem : EntitySystem
{
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KPlasmaOverheatComponent, GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(Entity<WH40KPlasmaOverheatComponent> ent, ref GunShotEvent args)
    {
        if (args.Ammo.Count == 0 ||
            ent.Comp.Chance <= 0f ||
            !Exists(args.User) ||
            !_random.Prob(ent.Comp.Chance))
        {
            return;
        }

        _explosion.QueueExplosion(args.User,
            ent.Comp.ExplosionType,
            ent.Comp.TotalIntensity,
            ent.Comp.IntensitySlope,
            ent.Comp.MaxTileIntensity,
            ent.Comp.TileBreakScale,
            ent.Comp.MaxTileBreak,
            ent.Comp.CanCreateVacuum,
            args.User);

        if (ent.Comp.DeleteWeapon)
            QueueDel(ent.Owner);
    }
}
