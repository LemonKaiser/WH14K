using Content.Shared._WH40K.Psyker;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Localization;

namespace Content.Shared._WH40K.Weapons.Ranged;

public sealed class SharedWH40KPsykerForceStaffSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerForceStaffComponent, AttemptShootEvent>(OnAttemptShoot);
    }

    private void OnAttemptShoot(Entity<WH40KPsykerForceStaffComponent> ent, ref AttemptShootEvent args)
    {
        if (args.Cancelled)
            return;

        var user = args.User;
        if (HasComp<WH40KPsykerRoleComponent>(user))
            return;

        args.Cancelled = true;
        args.Message = Loc.GetString(ent.Comp.Popup);
    }
}
