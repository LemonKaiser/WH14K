using Content.Shared.Speech;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Voice;

[RegisterComponent]
public sealed partial class WH40KSpeechSoundOverrideComponent : Component
{
    [DataField]
    public ProtoId<SpeechSoundsPrototype> SpeechSounds = "WH40KKriegMask";
}
