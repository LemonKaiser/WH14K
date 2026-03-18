using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.TacticalMap;

[Serializable, NetSerializable]
public enum WH40KTacticalMapUiKey : byte
{
    Key
}

public abstract class SharedWH40KTacticalMapSystem : EntitySystem
{
}
