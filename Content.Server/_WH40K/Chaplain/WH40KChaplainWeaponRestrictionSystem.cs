using Content.Server.Popups;
using Content.Shared._WH40K.Chaplain;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server._WH40K.Chaplain;

public sealed partial class WH40KChaplainWeaponRestrictionSystem : EntitySystem
{
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KChaplainRoleComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    private void OnShotAttempted(Entity<WH40KChaplainRoleComponent> ent, ref ShotAttemptedEvent args)
    {
        _popup.PopupEntity(
            Loc.GetString("wh40k-chaplain-cannot-use-firearms"),
            ent,
            ent,
            PopupType.SmallCaution);

        args.Cancel();
    }
}
