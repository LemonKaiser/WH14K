using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Weapons.Mods;

[Serializable, NetSerializable]
public enum WH40KWeaponModVisuals : byte
{
    OverlaySprites,
    OverlayStates,
    PresentationActive,
    PresentationSprite,
    PresentationState,
    PresentationItemSprite,
}
