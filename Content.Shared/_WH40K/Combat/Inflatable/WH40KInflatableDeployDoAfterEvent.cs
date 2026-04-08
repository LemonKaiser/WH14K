using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Combat.Inflatable;

[Serializable, NetSerializable]
public sealed partial class WH40KInflatableDeployDoAfterEvent : SimpleDoAfterEvent;
