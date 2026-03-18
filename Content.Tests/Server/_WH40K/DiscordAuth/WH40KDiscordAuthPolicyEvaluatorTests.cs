#nullable enable
using System;
using System.Collections.Generic;
using Content.Server.Database;
using Content.Server._WH40K.DiscordAuth;
using Content.Shared._WH40K.DiscordAuth;
using NUnit.Framework;

namespace Content.Tests.Server._WH40K.DiscordAuth;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class WH40KDiscordAuthPolicyEvaluatorTests
{
    [Test]
    public void Evaluate_ReturnsMisconfigured_WhenGuildPolicyEnabledWithoutGuildId()
    {
        var evaluation = WH40KDiscordAuthPolicyEvaluator.Evaluate(
            CreateConfig(requireLink: true, requireGuildMember: true, guildId: string.Empty),
            CreateLink(guildIdCached: "guild-1", isGuildMember: true, roles: ["role-1"]),
            loadComplete: true,
            now: DateTimeOffset.UtcNow);

        Assert.That(evaluation.BlockReason, Is.EqualTo(WH40KDiscordAuthGateBlockReason.Misconfigured));
    }

    [Test]
    public void Evaluate_ReturnsGuildMembershipRequired_WhenRoleGateNeedsGuildContext()
    {
        var evaluation = WH40KDiscordAuthPolicyEvaluator.Evaluate(
            CreateConfig(requiredRoleIds: new HashSet<string> { "role-1" }),
            CreateLink(guildIdCached: "guild-1", isGuildMember: false, roles: ["role-1"]),
            loadComplete: true,
            now: DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(evaluation.IsGuildMember, Is.False);
            Assert.That(evaluation.BlockReason, Is.EqualTo(WH40KDiscordAuthGateBlockReason.GuildMembershipRequired));
        });
    }

    [Test]
    public void Evaluate_ReturnsRoleRequired_WhenMemberLacksConfiguredRole()
    {
        var evaluation = WH40KDiscordAuthPolicyEvaluator.Evaluate(
            CreateConfig(requiredRoleIds: new HashSet<string> { "role-1" }),
            CreateLink(guildIdCached: "guild-1", isGuildMember: true, roles: ["role-2"]),
            loadComplete: true,
            now: DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(evaluation.IsGuildMember, Is.True);
            Assert.That(evaluation.RoleGatePassed, Is.False);
            Assert.That(evaluation.BlockReason, Is.EqualTo(WH40KDiscordAuthGateBlockReason.RoleRequired));
        });
    }

    [Test]
    public void Evaluate_ReturnsLoading_BeforeDbLoadCompletes()
    {
        var evaluation = WH40KDiscordAuthPolicyEvaluator.Evaluate(
            CreateConfig(requireLink: true),
            link: null,
            loadComplete: false,
            now: DateTimeOffset.UtcNow);

        Assert.That(evaluation.BlockReason, Is.EqualTo(WH40KDiscordAuthGateBlockReason.Loading));
    }

    [Test]
    public void Evaluate_ReturnsCacheStale_WhenGuildContextIsExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var evaluation = WH40KDiscordAuthPolicyEvaluator.Evaluate(
            CreateConfig(requireGuildMember: true),
            CreateLink(guildIdCached: "guild-1", isGuildMember: true, roles: ["role-1"], lastGuildRefreshAt: now.AddHours(-13)),
            loadComplete: true,
            now: now);

        Assert.Multiple(() =>
        {
            Assert.That(evaluation.CacheStale, Is.True);
            Assert.That(evaluation.BlockReason, Is.EqualTo(WH40KDiscordAuthGateBlockReason.CacheStale));
        });
    }

    private static WH40KDiscordAuthPolicyConfig CreateConfig(
        bool requireLink = false,
        bool requireGuildMember = false,
        string guildId = "guild-1",
        IReadOnlySet<string>? requiredRoleIds = null)
    {
        return new WH40KDiscordAuthPolicyConfig(
            Enabled: true,
            RequireLink: requireLink,
            RequireGuildMember: requireGuildMember,
            ClientId: "client-id",
            ClientSecret: "client-secret",
            RedirectUri: "https://example.com/wh40k/discord-auth/callback",
            GuildId: guildId,
            RequiredRoleIds: requiredRoleIds ?? new HashSet<string>(),
            CacheTtl: TimeSpan.FromHours(2));
    }

    private static WH40KDiscordAuthDbData CreateLink(
        string guildIdCached,
        bool isGuildMember,
        IReadOnlyList<string> roles,
        DateTimeOffset? lastGuildRefreshAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new WH40KDiscordAuthDbData(
            DiscordUserId: "discord-user-1",
            Username: "demiurge",
            GlobalName: "Arch Demiurge",
            AvatarHash: "avatar",
            AccessToken: "access",
            RefreshToken: "refresh",
            TokenType: "Bearer",
            Scope: "identify guilds.members.read",
            LinkedAt: now,
            TokenExpiresAt: now.AddHours(1),
            LastRefreshAt: now,
            GuildIdCached: guildIdCached,
            LastGuildRefreshAt: lastGuildRefreshAt ?? now,
            GuildMemberCached: isGuildMember,
            GuildNickname: "Demiurge",
            RoleCacheJson: System.Text.Json.JsonSerializer.Serialize(roles));
    }
}
