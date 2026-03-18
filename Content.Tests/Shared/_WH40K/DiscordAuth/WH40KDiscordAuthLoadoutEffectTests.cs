using System;
using System.Collections.Generic;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.Shared._WH40K.DiscordAuth;
using Moq;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Tests.Shared._WH40K.DiscordAuth;

[TestFixture]
[TestOf(typeof(WH40KDiscordAuthLoadoutEffect))]
[NonParallelizable]
public sealed class WH40KDiscordAuthLoadoutEffectTests
{
    private IDependencyCollection _deps = default!;
    private FakeDiscordAuthManager _discordAuth = default!;
    private Mock<IConfigurationManager> _config = default!;
    private bool _unlockBypassed;

    [SetUp]
    public void SetUp()
    {
        _deps = IoCManager.InitThread();
        _deps.Clear();
        _discordAuth = new FakeDiscordAuthManager();
        _config = new Mock<IConfigurationManager>();
        _unlockBypassed = false;

        _config.Setup(x => x.GetCVar(CCVars.WH40KMetaUnlocksEnforced))
            .Returns(() => _unlockBypassed);

        var localization = new Mock<ILocalizationManager>();
        localization.Setup(x => x.GetString(It.IsAny<string>()))
            .Returns((string key) => key);

        _deps.RegisterInstance<IConfigurationManager>(_config.Object);
        _deps.RegisterInstance<ISharedWH40KDiscordAuthManager>(_discordAuth);
        _deps.RegisterInstance<ILocalizationManager>(localization.Object);
        _deps.BuildGraph();
        IoCManager.InitThread(_deps, replaceExisting: true);
    }

    [TearDown]
    public void TearDown()
    {
        IoCManager.Clear();
    }

    [Test]
    public void Validate_ReturnsFalse_WhenSnapshotMissing()
    {
        var effect = new WH40KDiscordAuthLoadoutEffect
        {
            RequireGuildMember = true,
        };

        var session = CreateSession();
        var result = effect.Validate(
            new HumanoidCharacterProfile(),
            new RoleLoadout("LoadoutTester"),
            session.Object,
            _deps,
            out var reason);

        Assert.That(result, Is.False);
        Assert.That(reason, Is.Not.Null);
        Assert.That(reason!.ToString(), Is.Not.Empty);
    }

    [Test]
    public void Validate_ReturnsTrue_WhenGuildMembershipSatisfied()
    {
        var session = CreateSession();
        _discordAuth.SetSnapshot(session.Object.UserId, new WH40KDiscordAuthSnapshot(
            enabled: true,
            isLinked: true,
            displayName: "GuildUser",
            username: "guild_user",
            discordUserId: "100",
            guildCheckConfigured: true,
            guildMemberKnown: true,
            isGuildMember: true,
            roleGateConfigured: false,
            roleGatePassed: false,
            cachedRoleIds: new List<string>(),
            cacheStale: false,
            refreshCooldownRemaining: default,
            blockReason: WH40KDiscordAuthGateBlockReason.None));

        var effect = new WH40KDiscordAuthLoadoutEffect
        {
            RequireGuildMember = true,
        };

        var result = effect.Validate(
            new HumanoidCharacterProfile(),
            new RoleLoadout("LoadoutTester"),
            session.Object,
            _deps,
            out var reason);

        Assert.That(result, Is.True);
        Assert.That(reason, Is.EqualTo(FormattedMessage.Empty));
    }

    [Test]
    public void Validate_ReturnsFalse_WhenRequiredRoleMissing()
    {
        var session = CreateSession();
        _discordAuth.SetSnapshot(session.Object.UserId, new WH40KDiscordAuthSnapshot(
            enabled: true,
            isLinked: true,
            displayName: "RoleUser",
            username: "role_user",
            discordUserId: "200",
            guildCheckConfigured: true,
            guildMemberKnown: true,
            isGuildMember: true,
            roleGateConfigured: true,
            roleGatePassed: false,
            cachedRoleIds: new List<string> { "role-alpha" },
            cacheStale: false,
            refreshCooldownRemaining: default,
            blockReason: WH40KDiscordAuthGateBlockReason.RoleRequired));

        var effect = new WH40KDiscordAuthLoadoutEffect
        {
            RequiredRoleIds = new List<string> { "role-beta" },
        };

        var result = effect.Validate(
            new HumanoidCharacterProfile(),
            new RoleLoadout("LoadoutTester"),
            session.Object,
            _deps,
            out var reason);

        Assert.That(result, Is.False);
        Assert.That(reason, Is.Not.Null);
        Assert.That(reason!.ToString(), Is.Not.Empty);
    }

    [Test]
    public void Validate_BypassesRequirements_WhenMetaUnlockBypassEnabled()
    {
        _unlockBypassed = true;

        var effect = new WH40KDiscordAuthLoadoutEffect
        {
            RequireGuildMember = true,
            RequiredRoleIds = new List<string> { "role-beta" },
        };

        var session = CreateSession();
        var result = effect.Validate(
            new HumanoidCharacterProfile(),
            new RoleLoadout("LoadoutTester"),
            session.Object,
            _deps,
            out var reason);

        Assert.That(result, Is.True);
        Assert.That(reason, Is.EqualTo(FormattedMessage.Empty));
    }

    [Test]
    public void Validate_ReturnsFalse_WhenDiscordCacheIsStale()
    {
        var session = CreateSession();
        _discordAuth.SetSnapshot(session.Object.UserId, new WH40KDiscordAuthSnapshot(
            enabled: true,
            isLinked: true,
            displayName: "RoleUser",
            username: "role_user",
            discordUserId: "300",
            guildCheckConfigured: true,
            guildMemberKnown: true,
            isGuildMember: true,
            roleGateConfigured: true,
            roleGatePassed: true,
            cachedRoleIds: new List<string> { "role-alpha" },
            cacheStale: true,
            refreshCooldownRemaining: default,
            blockReason: WH40KDiscordAuthGateBlockReason.CacheStale));

        var effect = new WH40KDiscordAuthLoadoutEffect
        {
            RequiredRoleIds = new List<string> { "role-alpha" },
        };

        var result = effect.Validate(
            new HumanoidCharacterProfile(),
            new RoleLoadout("LoadoutTester"),
            session.Object,
            _deps,
            out var reason);

        Assert.That(result, Is.False);
        Assert.That(reason, Is.Not.Null);
        Assert.That(reason!.ToString(), Does.Contain("loadout-group-wh40k-discord-stale-restriction"));
    }

    private static Mock<ICommonSession> CreateSession()
    {
        var session = new Mock<ICommonSession>();
        session.SetupGet(x => x.UserId).Returns(new NetUserId(Guid.NewGuid()));
        return session;
    }

    private sealed class FakeDiscordAuthManager : ISharedWH40KDiscordAuthManager
    {
        private readonly Dictionary<NetUserId, WH40KDiscordAuthSnapshot> _snapshots = new();

        public void Clear()
        {
            _snapshots.Clear();
        }

        public void SetSnapshot(NetUserId userId, WH40KDiscordAuthSnapshot snapshot)
        {
            _snapshots[userId] = snapshot;
        }

        public bool TryGetSnapshot(NetUserId userId, out WH40KDiscordAuthSnapshot snapshot)
        {
            return _snapshots.TryGetValue(userId, out snapshot!);
        }
    }
}
