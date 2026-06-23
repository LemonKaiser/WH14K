using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.GunGame;

public sealed partial class WH40KGunGameRuleSystem
{
    private bool _protectionActive;

    public void InitializeProtection()
    {
        SubscribeLocalEvent<MetaDataComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
    }

    private void OnBeforeDamageChanged(EntityUid uid, MetaDataComponent component, ref BeforeDamageChangedEvent args)
    {
        if (!_protectionActive)
            return;

        if (HasComp<MobStateComponent>(uid))
            return;

        args.Cancelled = true;
    }
}
