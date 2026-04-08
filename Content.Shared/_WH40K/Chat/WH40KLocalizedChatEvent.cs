using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Chat;

[Serializable, NetSerializable]
public sealed class WH40KLocalizedChatEvent : EntityEventArgs
{
    public string LocKey { get; init; } = string.Empty;

    public Dictionary<string, string>? LocArgs { get; init; }

    public bool ResolveArgValues { get; init; }

    public Color? ColorOverride { get; init; }
}
