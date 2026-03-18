using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.DiscordAuth;

[Serializable]
[NetSerializable]
public sealed class WH40KDiscordAuthRequestStateEvent : EntityEventArgs
{
}

[Serializable]
[NetSerializable]
public sealed class WH40KDiscordAuthStateEvent : EntityEventArgs
{
    public WH40KDiscordAuthSnapshot Snapshot { get; }

    public WH40KDiscordAuthStateEvent(WH40KDiscordAuthSnapshot snapshot)
    {
        Snapshot = snapshot;
    }
}

[Serializable]
[NetSerializable]
public sealed class WH40KDiscordAuthStartLinkEvent : EntityEventArgs
{
}

[Serializable]
[NetSerializable]
public sealed class WH40KDiscordAuthRefreshProfileEvent : EntityEventArgs
{
}

[Serializable]
[NetSerializable]
public sealed class WH40KDiscordAuthUnlinkEvent : EntityEventArgs
{
}

[Serializable]
[NetSerializable]
public sealed class WH40KDiscordAuthOpenUrlEvent : EntityEventArgs
{
    public string Url { get; }

    public WH40KDiscordAuthOpenUrlEvent(string url)
    {
        Url = url;
    }
}
