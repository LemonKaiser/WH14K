using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Psyker;

[Serializable, NetSerializable]
public enum WH40KChaosPatron : byte
{
    None = 0,
    Undivided = 1,
    Khorne = 2,
    Nurgle = 3,
    Slaanesh = 4,
    Tzeentch = 5
}
