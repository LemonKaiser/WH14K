using System.Collections.Generic;
using Content.Shared._WH40K.DiscordAuth;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.DiscordAuth;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class WH40KDiscordAuthRequirementEvaluatorTests
{
    [Test]
    public void NormalizeRoleIds_TrimsAndDeduplicates()
    {
        var normalized = WH40KDiscordAuthRequirementEvaluator.NormalizeRoleIds(new[]
        {
            " 123 ",
            "123",
            "",
            "456",
        });

        Assert.That(normalized, Is.EqualTo(new List<string> { "123", "456" }));
    }

    [Test]
    public void MeetsRequirements_AllowsEmptyRequirementWithoutSnapshot()
    {
        var result = WH40KDiscordAuthRequirementEvaluator.MeetsRequirements(
            snapshot: null,
            requireGuildMember: false,
            requiredRoleIds: new List<string>());

        Assert.That(result, Is.True);
    }

    [Test]
    public void MeetsRequirements_RejectsGuildRequirementWithoutMembership()
    {
        var snapshot = new WH40KDiscordAuthSnapshot(
            enabled: true,
            isLinked: true,
            displayName: "User",
            username: "user",
            discordUserId: "1",
            guildCheckConfigured: true,
            guildMemberKnown: true,
            isGuildMember: false,
            roleGateConfigured: false,
            roleGatePassed: false,
            cachedRoleIds: new List<string>(),
            cacheStale: false,
            refreshCooldownRemaining: default,
            blockReason: WH40KDiscordAuthGateBlockReason.None);

        var result = WH40KDiscordAuthRequirementEvaluator.MeetsRequirements(
            snapshot,
            requireGuildMember: true,
            requiredRoleIds: new List<string>());

        Assert.That(result, Is.False);
    }

    [Test]
    public void MeetsRequirements_AcceptsAnyMatchingRole()
    {
        var snapshot = new WH40KDiscordAuthSnapshot(
            enabled: true,
            isLinked: true,
            displayName: "User",
            username: "user",
            discordUserId: "1",
            guildCheckConfigured: true,
            guildMemberKnown: true,
            isGuildMember: true,
            roleGateConfigured: true,
            roleGatePassed: true,
            cachedRoleIds: new List<string> { "role-a", "role-b" },
            cacheStale: false,
            refreshCooldownRemaining: default,
            blockReason: WH40KDiscordAuthGateBlockReason.None);

        var result = WH40KDiscordAuthRequirementEvaluator.MeetsRequirements(
            snapshot,
            requireGuildMember: false,
            requiredRoleIds: new List<string> { "role-x", "role-b" });

        Assert.That(result, Is.True);
    }

    [Test]
    public void MeetsRequirements_RejectsStaleCacheEvenWhenMembershipWasPreviouslyCached()
    {
        var snapshot = new WH40KDiscordAuthSnapshot(
            enabled: true,
            isLinked: true,
            displayName: "User",
            username: "user",
            discordUserId: "1",
            guildCheckConfigured: true,
            guildMemberKnown: true,
            isGuildMember: true,
            roleGateConfigured: true,
            roleGatePassed: true,
            cachedRoleIds: new List<string> { "role-a" },
            cacheStale: true,
            refreshCooldownRemaining: default,
            blockReason: WH40KDiscordAuthGateBlockReason.CacheStale);

        var result = WH40KDiscordAuthRequirementEvaluator.MeetsRequirements(
            snapshot,
            requireGuildMember: true,
            requiredRoleIds: new List<string> { "role-a" });

        Assert.That(result, Is.False);
    }
}
