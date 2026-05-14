using Content.Shared._WH40K.Psyker;
using Content.Shared._WH40K.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Server._WH40K.Weapons.Ranged;

public sealed class WH40KPsykerForceStaffSystem : EntitySystem
{
    private const string StaffShotSourceKey = "psyker.staff";

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerForceStaffComponent, GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(Entity<WH40KPsykerForceStaffComponent> ent, ref GunShotEvent args)
    {
        if (ent.Comp.ShotInstability <= 0f ||
            args.Ammo.Count == 0 ||
            !HasComp<WH40KPsykerRoleComponent>(args.User))
        {
            return;
        }

        RaiseLocalEvent(new WH40KWarpInstabilityContributionEvent(args.User, ent.Comp.ShotInstability, StaffShotSourceKey));
    }
}
