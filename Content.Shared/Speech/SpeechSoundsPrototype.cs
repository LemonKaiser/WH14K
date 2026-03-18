using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared.Speech
{
    [Prototype]
    public sealed partial class SpeechSoundsPrototype : IPrototype
    {
        [ViewVariables]
        [IdDataField]
        public string ID { get; private set; } = default!;

        //Variation is here instead of in SharedSpeechComponent since some sets of
        //sounds may require more fine tuned pitch variation than others.
        [DataField("variation")]
        public float Variation { get; set; } = 0.1f;

        [DataField("saySound")]
        public SoundSpecifier SaySound { get; set; } = new SoundPathSpecifier("/Audio/Voice/Talk/speak_2.ogg");

        [DataField("askSound")]
        public SoundSpecifier AskSound { get; set; } = new SoundPathSpecifier("/Audio/Voice/Talk/speak_2_ask.ogg");

        [DataField("exclaimSound")]
        public SoundSpecifier ExclaimSound { get; set; } = new SoundPathSpecifier("/Audio/Voice/Talk/speak_2_exclaim.ogg");

        [DataField("dialogueBlipSound")]
        public SoundSpecifier? DialogueBlipSound { get; set; }

        [DataField("dialogueBlipPitch")]
        public float DialogueBlipPitch { get; set; } = 1f;

        [DataField("dialogueBlipVariation")]
        public float DialogueBlipVariation { get; set; } = 0.05f;

        [DataField("dialogueBlipVolume")]
        public float DialogueBlipVolume { get; set; } = -11f;

        [DataField("dialogueCharsPerSecond")]
        public float DialogueCharsPerSecond { get; set; } = 30f;

        [DataField("dialogueCharsPerBlip")]
        public int DialogueCharsPerBlip { get; set; } = 2;

        [DataField("dialoguePunctuationPause")]
        public float DialoguePunctuationPause { get; set; } = 0.04f;
    }
}
