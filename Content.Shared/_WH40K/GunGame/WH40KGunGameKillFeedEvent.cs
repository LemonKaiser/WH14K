using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.GunGame;

[Serializable, NetSerializable]
public sealed class WH40KGunGameKillFeedEvent : EntityEventArgs
{
    public string KillerName { get; }
    public string VictimName { get; }
    public string? WeaponPrototypeId { get; }
    public bool UseSkullIcon { get; }
    public bool LocalKiller { get; }
    public bool LocalVictim { get; }

    public WH40KGunGameKillFeedEvent(
        string killerName,
        string victimName,
        string? weaponPrototypeId,
        bool useSkullIcon,
        bool localKiller,
        bool localVictim)
    {
        KillerName = killerName;
        VictimName = victimName;
        WeaponPrototypeId = weaponPrototypeId;
        UseSkullIcon = useSkullIcon;
        LocalKiller = localKiller;
        LocalVictim = localVictim;
    }
}
