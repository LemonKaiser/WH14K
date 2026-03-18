using Content.Shared.Examine;

namespace Content.Shared._WH40K.Combat;

/// <summary>
/// Shared examine support for WH40K turret profile metadata.
/// </summary>
public sealed class SharedWH40KTurretProfileExamineSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KTurretProfileComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<WH40KTurretProfileComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || string.IsNullOrWhiteSpace(ent.Comp.SupportedAmmo))
            return;

        args.PushMarkup(Loc.GetString("wh40k-turret-supported-ammo", ("ammo", ent.Comp.SupportedAmmo)));
    }
}

