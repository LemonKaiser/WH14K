using Robust.Shared.Audio;

namespace Content.Shared._WH40K.Weapons.Mods;

public static class WH40KWeaponModHelper
{
    public static string GetSlotId(string definitionId)
    {
        return $"{WH40KWeaponModHostComponent.SlotIdPrefix}{definitionId}";
    }

    public static string GetOverlayLayerKey(string slotId)
    {
        return $"wh40k-weapon-mod-overlay-{slotId}";
    }

    public static SoundSpecifier GetDefaultInsertSound(WH40KWeaponModSlotType slotType)
    {
        return new SoundPathSpecifier(slotType switch
        {
            WH40KWeaponModSlotType.OpticTop or WH40KWeaponModSlotType.SideUtility
                => "/Audio/Weapons/Guns/MagIn/pistol_magin.ogg",
            WH40KWeaponModSlotType.Underbarrel or
            WH40KWeaponModSlotType.StockRear or
            WH40KWeaponModSlotType.SlingMount or
            WH40KWeaponModSlotType.MuzzleFront or
            WH40KWeaponModSlotType.BarrelFront
                => "/Audio/Weapons/Guns/MagIn/ltrifle_magin.ogg",
            _ => "/Audio/Weapons/Guns/MagIn/revolver_magin.ogg",
        });
    }

    public static SoundSpecifier GetDefaultEjectSound(WH40KWeaponModSlotType slotType)
    {
        return new SoundPathSpecifier(slotType switch
        {
            WH40KWeaponModSlotType.OpticTop or WH40KWeaponModSlotType.SideUtility
                => "/Audio/Weapons/Guns/MagOut/pistol_magout.ogg",
            WH40KWeaponModSlotType.Underbarrel or
            WH40KWeaponModSlotType.StockRear or
            WH40KWeaponModSlotType.SlingMount or
            WH40KWeaponModSlotType.MuzzleFront or
            WH40KWeaponModSlotType.BarrelFront
                => "/Audio/Weapons/Guns/MagOut/ltrifle_magout.ogg",
            _ => "/Audio/Weapons/Guns/MagOut/revolver_magout.ogg",
        });
    }
}
