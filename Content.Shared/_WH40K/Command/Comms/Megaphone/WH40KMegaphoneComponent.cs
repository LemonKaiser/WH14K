using System;
using Content.Shared.Speech;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Command.Comms.Megaphone;

[RegisterComponent]
public sealed partial class WH40KMegaphoneComponent : Component
{
    [DataField]
    public TimeSpan BroadcastDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public string BroadcastUseDelayId = "wh40k-megaphone-broadcast";

    [DataField]
    public TimeSpan UserCooldown = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan RateLimitWindow = TimeSpan.FromSeconds(20);

    [DataField]
    public int MaxBroadcastsPerWindow = 5;

    [DataField]
    public TimeSpan ReplayWindow = TimeSpan.FromSeconds(90);

    [DataField]
    public int ReplayEntryLimit = 5;

    [DataField]
    public float ReplayRadius = 16f;

    [DataField]
    public int InputMaxLength = 150;

    [DataField]
    public ProtoId<SpeechSoundsPrototype> SpeechSounds = "WH40KMegaphone";

    [DataField]
    public ProtoId<SpeechVerbPrototype> SpeechVerb = "WH40KMegaphone";

    [DataField]
    public Dictionary<string, ProtoId<SpeechVerbPrototype>> SuffixSpeechVerbs = new()
    {
        { "chat-speech-verb-suffix-exclamation-strong", "WH40KMegaphone" },
        { "chat-speech-verb-suffix-exclamation", "WH40KMegaphone" },
        { "chat-speech-verb-suffix-question", "WH40KMegaphone" },
        { "chat-speech-verb-suffix-stutter", "WH40KMegaphone" },
        { "chat-speech-verb-suffix-mumble", "WH40KMegaphone" },
    };
}
