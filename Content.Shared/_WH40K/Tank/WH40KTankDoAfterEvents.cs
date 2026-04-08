using System;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Tank;

[Serializable, NetSerializable]
public sealed partial class WH40KTankEnterDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class WH40KTankExitDoAfterEvent : SimpleDoAfterEvent;
