namespace Content.Server._WH40K.GunGame;

[RegisterComponent, Access(typeof(WH40KGunGameRuleSystem))]
public sealed partial class WH40KGunGamePlayerComponent : Component
{
    [ViewVariables]
    public EntityUid? CurrentWeapon;
}
