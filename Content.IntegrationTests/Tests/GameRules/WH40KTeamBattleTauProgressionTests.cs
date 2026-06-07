using System;
using Content.IntegrationTests.Fixtures;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.MetaProgress;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.GameRules;

[TestFixture]
public sealed class WH40KTeamBattleTauProgressionTests : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
        DummyTicker = false,
        Connected = true,
        Map = PoolManager.TestStation
    };

    [Test]
    public async Task ValidatedTauKillRewardUsesCanonicalTeamStateWithoutJobRole()
    {
        var server = Pair.Server;
        EntityUid ruleUid = default;
        EntityUid killerMobUid = default;
        EntityUid victimUid = default;

        await server.WaitAssertion(() =>
        {
            var ruleId = "WH40KTeamBattle";
            var ticker = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<GameTicker>();
            var teamBattle = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<WH40KTeamBattleRuleSystem>();
            var minds = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<MindSystem>();

            Assert.That(ServerSession, Is.Not.Null);
            Assert.That(ticker.StartGameRule(ruleId, out ruleUid), Is.True);

            killerMobUid = server.EntMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
            victimUid = server.EntMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);

            minds.WipeMind(ServerSession!);
            var killerMind = minds.CreateMind(ServerSession!.UserId, "Tau Reward Test");
            minds.TransferTo(killerMind, killerMobUid);

            Assert.That(teamBattle.TrySetEntityTeam(killerMobUid, "Tau"), Is.True);
            Assert.That(ServerSession.AttachedEntity, Is.EqualTo(killerMobUid));
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var teamBattle = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<WH40KTeamBattleRuleSystem>();
            var rule = entMan.GetComponent<Content.Server._WH40K.GameTicking.Rules.Components.WH40KTeamBattleRuleComponent>(ruleUid);
            var killerUserId = ServerSession!.UserId;

            Assert.That(teamBattle.TryGetTeamProgress("Tau", out _, out var baselineXp, out _), Is.True);

            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new WH40KValidatedKillRewardEvent(victimUid, killerUserId, null, "Tau", "Imperium", "tau-reward"));

            Assert.That(teamBattle.TryGetTeamProgress("Tau", out _, out var afterGrant, out _), Is.True);
            Assert.That(afterGrant, Is.EqualTo(baselineXp + Math.Max(1, rule.FrontPointsPerKill)));

            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new WH40KValidatedKillRewardRevokedEvent(victimUid, killerUserId, null, "Tau", "Imperium", "tau-reward"));

            Assert.That(teamBattle.TryGetTeamProgress("Tau", out _, out var afterRevoke, out _), Is.True);
            Assert.That(afterRevoke, Is.EqualTo(baselineXp));
        });

        await server.WaitAssertion(() =>
        {
            var ticker = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<GameTicker>();
            ticker.ClearGameRules();
            Assert.That(ticker.GetAddedGameRules(), Is.Empty);
        });
    }
}
