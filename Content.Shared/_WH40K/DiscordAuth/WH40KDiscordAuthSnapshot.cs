using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.DiscordAuth;

[Serializable]
[NetSerializable]
public enum WH40KDiscordAuthGateBlockReason : byte
{
    None = 0,
    Loading = 1,
    LinkRequired = 2,
    GuildMembershipRequired = 3,
    RoleRequired = 4,
    Misconfigured = 5,
    CacheStale = 6,
}

[Serializable]
[NetSerializable]
public sealed class WH40KDiscordAuthSnapshot
{
    public bool Enabled { get; }
    public bool IsLinked { get; }
    public string DisplayName { get; }
    public string Username { get; }
    public string DiscordUserId { get; }
    public bool GuildCheckConfigured { get; }
    public bool GuildMemberKnown { get; }
    public bool IsGuildMember { get; }
    public bool RoleGateConfigured { get; }
    public bool RoleGatePassed { get; }
    public List<string> CachedRoleIds { get; }
    public bool CacheStale { get; }
    public TimeSpan RefreshCooldownRemaining { get; }
    public WH40KDiscordAuthGateBlockReason BlockReason { get; }

    public WH40KDiscordAuthSnapshot(
        bool enabled,
        bool isLinked,
        string displayName,
        string username,
        string discordUserId,
        bool guildCheckConfigured,
        bool guildMemberKnown,
        bool isGuildMember,
        bool roleGateConfigured,
        bool roleGatePassed,
        List<string> cachedRoleIds,
        bool cacheStale,
        TimeSpan refreshCooldownRemaining,
        WH40KDiscordAuthGateBlockReason blockReason)
    {
        Enabled = enabled;
        IsLinked = isLinked;
        DisplayName = displayName;
        Username = username;
        DiscordUserId = discordUserId;
        GuildCheckConfigured = guildCheckConfigured;
        GuildMemberKnown = guildMemberKnown;
        IsGuildMember = isGuildMember;
        RoleGateConfigured = roleGateConfigured;
        RoleGatePassed = roleGatePassed;
        CachedRoleIds = cachedRoleIds;
        CacheStale = cacheStale;
        RefreshCooldownRemaining = refreshCooldownRemaining;
        BlockReason = blockReason;
    }
}
