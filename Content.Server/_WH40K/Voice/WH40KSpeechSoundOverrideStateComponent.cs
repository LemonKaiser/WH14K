using Content.Shared.Speech;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Voice;

[RegisterComponent]
public sealed partial class WH40KSpeechSoundOverrideStateComponent : Component
{
    [DataField]
    public ProtoId<SpeechSoundsPrototype>? OriginalSpeechSounds;

    [DataField]
    public EntityUid? Source;
}
