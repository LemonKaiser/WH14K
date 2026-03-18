using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Fulton;

[Serializable, NetSerializable]
public sealed partial class WH40KPrepareFultonDoAfterEvent : SimpleDoAfterEvent;
