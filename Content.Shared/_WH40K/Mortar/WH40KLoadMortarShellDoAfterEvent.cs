using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Mortar;

[Serializable, NetSerializable]
public sealed partial class WH40KLoadMortarShellDoAfterEvent : SimpleDoAfterEvent;
