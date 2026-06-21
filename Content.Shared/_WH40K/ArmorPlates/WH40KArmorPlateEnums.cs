using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ArmorPlates;

[Serializable, NetSerializable]
public enum WH40KArmorPlateType : byte
{
    Laser,
    Bullet,
    Melee,
}

[Flags]
[Serializable, NetSerializable]
public enum WH40KArmorPlateDamageMask : byte
{
    None = 0,
    Laser = 1 << 0,
    Bullet = 1 << 1,
    Melee = 1 << 2,
}

[Serializable, NetSerializable]
public enum WH40KArmorPlateVisuals : byte
{
    OverlayVisible,
    OverlayType,
}
