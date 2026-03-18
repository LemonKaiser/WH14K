using System;
using System.Collections.Generic;
using System.Text.Json;
using Content.Server.Database;
using Content.Shared._WH40K.DiscordAuth;

namespace Content.Server._WH40K.DiscordAuth;

public sealed record WH40KDiscordAuthPolicyConfig(
    bool Enabled,
    bool RequireLink,
    bool RequireGuildMember,
    string ClientId,
    string ClientSecret,
    string RedirectUri,
    string GuildId,
    IReadOnlySet<string> RequiredRoleIds,
    TimeSpan CacheTtl);

public sealed record WH40KDiscordAuthPolicyEvaluation(
    bool GuildConfigured,
    bool GuildMemberKnown,
    bool IsGuildMember,
    bool RoleConfigured,
    bool RoleGatePassed,
    bool CacheStale,
    WH40KDiscordAuthGateBlockReason BlockReason);

public static class WH40KDiscordAuthPolicyEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static bool IsPolicyActive(WH40KDiscordAuthPolicyConfig config)
    {
        return config.Enabled && (config.RequireLink || config.RequireGuildMember || config.RequiredRoleIds.Count > 0);
    }

    public static bool IsOAuthConfigured(string clientId, string clientSecret, string redirectUri)
    {
        return !string.IsNullOrWhiteSpace(clientId)
               && !string.IsNullOrWhiteSpace(clientSecret)
               && !string.IsNullOrWhiteSpace(redirectUri);
    }

    public static WH40KDiscordAuthPolicyEvaluation Evaluate(
        WH40KDiscordAuthPolicyConfig config,
        WH40KDiscordAuthDbData? link,
        bool loadComplete,
        DateTimeOffset now)
    {
        var guildConfigured = !string.IsNullOrWhiteSpace(config.GuildId);
        var roleConfigured = config.RequiredRoleIds.Count > 0;
        var requiresGuildContext = config.RequireGuildMember || roleConfigured;
        var policyActive = IsPolicyActive(config);
        var misconfigured = policyActive &&
            (!IsOAuthConfigured(config.ClientId, config.ClientSecret, config.RedirectUri) ||
             (requiresGuildContext && !guildConfigured));

        var guildMemberKnown = false;
        var isGuildMember = false;
        var roleGatePassed = !roleConfigured;
        var cacheStale = false;

        if (link != null)
        {
            var guildDataMatches = guildConfigured && string.Equals(link.GuildIdCached, config.GuildId, StringComparison.Ordinal);
            guildMemberKnown = guildConfigured && guildDataMatches && link.LastGuildRefreshAt != null;
            isGuildMember = guildMemberKnown && link.GuildMemberCached;
            roleGatePassed = !roleConfigured || (guildDataMatches && EvaluateRoleGate(link.RoleCacheJson, config.RequiredRoleIds));

            if (guildConfigured)
            {
                cacheStale = !guildDataMatches ||
                             link.LastGuildRefreshAt == null ||
                             now - link.LastGuildRefreshAt.Value > config.CacheTtl;
            }
        }

        var blockReason = WH40KDiscordAuthGateBlockReason.None;
        if (policyActive)
        {
            if (!loadComplete)
                blockReason = WH40KDiscordAuthGateBlockReason.Loading;
            else if (misconfigured)
                blockReason = WH40KDiscordAuthGateBlockReason.Misconfigured;
            else if (link == null)
                blockReason = WH40KDiscordAuthGateBlockReason.LinkRequired;
            else if (requiresGuildContext && cacheStale)
                blockReason = WH40KDiscordAuthGateBlockReason.CacheStale;
            else if (requiresGuildContext && !isGuildMember)
                blockReason = WH40KDiscordAuthGateBlockReason.GuildMembershipRequired;
            else if (roleConfigured && !roleGatePassed)
                blockReason = WH40KDiscordAuthGateBlockReason.RoleRequired;
        }

        return new WH40KDiscordAuthPolicyEvaluation(
            guildConfigured,
            guildMemberKnown,
            isGuildMember,
            roleConfigured,
            roleGatePassed,
            cacheStale,
            blockReason);
    }

    private static bool EvaluateRoleGate(string roleCacheJson, IReadOnlySet<string> requiredRoleIds)
    {
        if (requiredRoleIds.Count == 0)
            return true;

        try
        {
            var roles = JsonSerializer.Deserialize<List<string>>(string.IsNullOrWhiteSpace(roleCacheJson) ? "[]" : roleCacheJson, JsonOptions);
            if (roles == null)
                return false;

            foreach (var role in roles)
            {
                if (!string.IsNullOrWhiteSpace(role) && requiredRoleIds.Contains(role.Trim()))
                    return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
