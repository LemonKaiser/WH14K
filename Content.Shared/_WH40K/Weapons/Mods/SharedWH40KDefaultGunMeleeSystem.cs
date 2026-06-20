using Content.Shared.Damage;
using Content.Shared.Item;
using Content.Shared.FixedPoint;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;

namespace Content.Shared._WH40K.Weapons.Mods;

public sealed partial class SharedWH40KDefaultGunMeleeSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    private bool _initialized;

    public override void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        base.Initialize();

        if (!_net.IsServer)
            return;

        SubscribeLocalEvent<ItemComponent, MapInitEvent>(OnItemMapInit);
    }

    private void OnItemMapInit(Entity<ItemComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp(ent.Owner, out GunComponent? gun))
            return;

        EnsureDefaultMelee(ent.Owner, gun);
    }

    public bool EnsureDefaultMelee(EntityUid uid, GunComponent? gun = null)
    {
        if (!_net.IsServer)
            return false;

        if (!Resolve(uid, ref gun, false) || !HasComp<ItemComponent>(uid))
            return false;

        var changed = false;

        if (!TryComp(uid, out MeleeWeaponComponent? melee))
        {
            melee = AddComp<MeleeWeaponComponent>(uid);
            melee.Damage = new DamageSpecifier();
            melee.Damage.DamageDict["Blunt"] = FixedPoint2.New(10);
            melee.AttackRate = 1f;
            melee.Range = 1.5f;
            melee.Animation = "WeaponArcSlash";
            melee.WideAnimation = "WeaponArcSlash";
            melee.WideAnimationRotation = Angle.Zero;
            melee.Hidden = true;
            Dirty(uid, melee);
            changed = true;
        }

        if (gun.UseKey && !HasComp<AltFireMeleeComponent>(uid))
        {
            AddComp<AltFireMeleeComponent>(uid);
            changed = true;
        }

        return changed;
    }
}
