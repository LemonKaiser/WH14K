using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Administration.Mute;

[Flags]
[Serializable, NetSerializable]
public enum WH40KMuteType : byte
{
    None = 0,
    Chat = 1 << 0,
    AHelp = 1 << 1,
}
