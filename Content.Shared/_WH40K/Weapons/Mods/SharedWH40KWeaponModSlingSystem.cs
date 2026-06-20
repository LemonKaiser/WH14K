using Content.Shared.Standing;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.Weapons.Mods;

/// <summary>
///     A weapon with a Sling mod installed cannot be dropped from hands when the wielder falls,
///     is stunned, slips, or enters critical/dead state. The sling also prevents the weapon from
///     being thrown (it is secured to the wielder via the sling strap).
///     Manual drop (right-click → drop) and hotswap/verb-based removal remain available so the
///     player can still manage the weapon outside of combat mishaps.
/// </summary>
public abstract partial class SharedWH40KWeaponModSlingSystem : EntitySystem
{
    private bool _initialized;

    public override void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        base.Initialize();

        // Raised on the held item (weapon) by Server HandsSystem.OnDropHandItems when the wielder
        // falls, is stunned, slips, or enters crit/dead. Cancelling keeps the item in the hand.
        SubscribeLocalEvent<WH40KWeaponModHostComponent, FellDownThrowAttemptEvent>(OnFellDownThrowAttempt);

        // Raised on the item when the user tries to throw it. Cancelling blocks the throw.
        SubscribeLocalEvent<WH40KWeaponModHostComponent, ThrowItemAttemptEvent>(OnThrowItemAttempt);
    }

    private void OnFellDownThrowAttempt(Entity<WH40KWeaponModHostComponent> ent, ref FellDownThrowAttemptEvent args)
    {
        if (HasSlingInstalled(ent))
            args.Cancelled = true;
    }

    private void OnThrowItemAttempt(Entity<WH40KWeaponModHostComponent> ent, ref ThrowItemAttemptEvent args)
    {
        if (HasSlingInstalled(ent))
            args.Cancelled = true;
    }

    /// <summary>
    ///     Checks whether the weapon has a Sling mod installed in any of its SlingMount slots.
    /// </summary>
    private bool HasSlingInstalled(Entity<WH40KWeaponModHostComponent> ent)
    {
        // Delegate to the host system which owns GetInstalledMods.
        var weaponMods = EntitySystem.Get<SharedWH40KWeaponModSystem>();
        return weaponMods.TryGetInstalledSling(ent);
    }
}
