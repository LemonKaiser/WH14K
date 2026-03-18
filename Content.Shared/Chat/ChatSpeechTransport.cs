using Robust.Shared.Serialization;

namespace Content.Shared.Chat;

[Serializable, NetSerializable]
public enum ChatSpeechTransport : byte
{
    Direct = 0,
    Radio = 1,
}
