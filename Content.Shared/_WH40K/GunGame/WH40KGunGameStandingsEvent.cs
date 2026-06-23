using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.GunGame;

[Serializable, NetSerializable]
public sealed class WH40KGunGameStandingsEvent : EntityEventArgs
{
    public List<WH40KGunGameStandingEntry> Entries { get; }

    public WH40KGunGameStandingsEvent(List<WH40KGunGameStandingEntry> entries)
    {
        Entries = entries;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KGunGameStandingEntry
{
    public NetUserId UserId { get; }
    public string UserName { get; }
    public int Level { get; }
    public int Kills { get; }

    public WH40KGunGameStandingEntry(NetUserId userId, string userName, int level, int kills)
    {
        UserId = userId;
        UserName = userName;
        Level = level;
        Kills = kills;
    }
}
