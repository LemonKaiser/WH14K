using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.GameTicking.Rules;

[Serializable, NetSerializable]
public sealed partial class WH40KFactionRecruitmentDoAfterEvent : SimpleDoAfterEvent;
