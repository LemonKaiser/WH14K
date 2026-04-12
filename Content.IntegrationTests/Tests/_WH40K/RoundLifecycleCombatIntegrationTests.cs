#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Pair;
using Content.Server.GameTicking;
using Content.Server.Ghost.Roles.Components;
using Content.Server.KillTracking;
using Content.Server.Mind;
using Content.Server.NPC.HTN;
using Content.Server._WH40K.Command;
using Content.Server._WH40K.Command.Components;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.Influence;
using Content.Server._WH40K.MetaProgress;
using Content.Server._WH40K.Stats;
using Content.Server.Zombies;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind.Components;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.LateJoin;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class RoundLifecycleCombatIntegrationTests
{
    private const string Imperium = "Imperium";
    private const string Heretics = "Heretics";
    private const string ImperiumReinforcementPrototype = "MobHumanWH40KImperiumReinforcement";
    private const string HereticReinforcementPrototype = "MobHumanWH40KHereticReinforcement";
    private const string MonkeyPrototype = "MobMonkey";
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";

    [Test]
    public async Task TeamBattleUsesThreeHourRoundLimitAndOneHourAssaultProfile()
    {
        await using var pair = await StartWh40KRoundAsync(requireAttachedEntities: false);
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var ticker = server.System<GameTicker>();

            var query = entMan.EntityQueryEnumerator<WH40KTeamBattleRuleComponent, GameRuleComponent>();
            WH40KTeamBattleRuleComponent? activeRule = null;

            while (query.MoveNext(out var uid, out var rule, out var gameRule))
            {
                if (!ticker.IsGameRuleActive(uid, gameRule))
                    continue;

                activeRule = rule;
                break;
            }

            Assert.That(activeRule, Is.Not.Null, "Expected active WH40K team-battle rule.");

            Assert.Multiple(() =>
            {
                Assert.That(activeRule!.RoundTimeLimitSeconds, Is.EqualTo(10800f));
                Assert.That(activeRule.AssaultDurationSeconds, Is.EqualTo(3600f));
                Assert.That(activeRule.PreparationDurationSeconds, Is.EqualTo(600f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundEndWithoutOutcomeRecordsCompletionButNoWinRewards()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (userId, _) = await EnsureSinglePlayerTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, userId);
        await pair.WaitCommand("endround");

        for (var i = 0; i < 180; i++)
            await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PostRound));
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(
                    stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundCompletedFaction),
                    Is.EqualTo(1),
                    "Round completion must be recorded exactly once.");
                Assert.That(
                    stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundWins),
                    Is.EqualTo(0),
                    "Round win must not be granted when outcome is unresolved.");
                Assert.That(
                    stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpRoundWin),
                    Is.EqualTo(0),
                    "Round-win XP must not be granted when outcome is unresolved.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundEndDuplicateUserEntriesCountParticipationOnce()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (userId, _) = await EnsureSinglePlayerTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, userId);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var duplicateInfo = new RoundEndMessageEvent.RoundEndPlayerInfo
            {
                PlayerOOCName = "integration-user",
                PlayerICName = "integration-character",
                PlayerGuid = userId,
                Role = "test-role",
                JobPrototypes = Array.Empty<string>(),
                AntagPrototypes = Array.Empty<string>(),
                Connected = true,
                Antag = false,
                Observer = false
            };

            var ev = new RoundEndMessageEvent(
                "WH40KTeamBattle",
                string.Empty,
                TimeSpan.FromMinutes(5),
                4242,
                1,
                new[] { duplicateInfo, duplicateInfo },
                null);
            entMan.EventBus.RaiseEvent(EventSource.Local, ev);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundParticipationActive), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaSessionRoundsPlayed), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundCompletedFaction), Is.EqualTo(1));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundEndWithWinnerAwardsWinAndRoundWinXp()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (userId, _) = await EnsureSinglePlayerTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, userId);

        var winnerTeamId = string.Empty;
        await server.WaitAssertion(() =>
        {
            var teamBattle = server.System<WH40KTeamBattleRuleSystem>();
            Assert.That(teamBattle.TryGetTeamIdForUser(userId, out winnerTeamId), Is.True);

            var loserTeamId = teamBattle.GetTeamIds().First(x => !string.Equals(x, winnerTeamId, StringComparison.Ordinal));
            teamBattle.HandleObjectiveDestroyed(loserTeamId);
        });

        for (var i = 0; i < 180; i++)
            await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PostRound));
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundCompletedFaction), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundWins), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpRoundWin), Is.EqualTo(100));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundEndGhostedReinforcementStillAwardsWinProgress()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (userId, teamId) = await EnsureSinglePlayerTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, userId);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var minds = entMan.System<MindSystem>();
            var ticker = server.System<GameTicker>();

            var activeRule = GetActiveRule(entMan, ticker);
            var rememberedField = typeof(WH40KTeamBattleRuleComponent)
                .GetField(nameof(WH40KTeamBattleRuleComponent.PlayerLastKnownTeam), BindingFlags.Public | BindingFlags.Instance)!;
            var rememberedTeams = (Dictionary<NetUserId, string>) rememberedField.GetValue(activeRule)!;
            rememberedTeams.Remove(userId);

            var currentEntity = GetAttachedEntity(playerMan, userId);
            Assert.That(minds.TryGetMind(currentEntity, out var mindId, out var mind), Is.True);

            var prototype = string.Equals(teamId, Imperium, StringComparison.Ordinal)
                ? ImperiumReinforcementPrototype
                : HereticReinforcementPrototype;

            var reinforcement = entMan.SpawnEntity(
                prototype,
                entMan.GetComponent<TransformComponent>(currentEntity).Coordinates.Offset(new Vector2(1f, 0f)));

            entMan.EnsureComponent<WH40KTeamMemberComponent>(reinforcement).TeamId = teamId;
            entMan.EnsureComponent<WH40KTeamBattleFactionIconComponent>(reinforcement).TeamId = teamId;
            entMan.EnsureComponent<GhostTakeoverAvailableComponent>(reinforcement);
            entMan.EnsureComponent<GhostRoleComponent>(reinforcement);
            entMan.EnsureComponent<WH40KReinforcementGhostRoleOneShotComponent>(reinforcement);

            minds.TransferTo(mindId, reinforcement, createGhost: false, mind: mind);
        });

        await pair.RunTicksSync(10);
        await pair.WaitCommand("ghost");
        await pair.RunTicksSync(10);

        var rememberedTeamAfterGhost = string.Empty;
        await server.WaitAssertion(() =>
        {
            var teamBattle = server.System<WH40KTeamBattleRuleSystem>();
            Assert.That(teamBattle.TryGetRememberedTeam(userId, out rememberedTeamAfterGhost), Is.True);
            Assert.That(rememberedTeamAfterGhost, Is.EqualTo(teamId));
        });

        await server.WaitAssertion(() =>
        {
            var teamBattle = server.System<WH40KTeamBattleRuleSystem>();
            var loserTeamId = teamBattle.GetTeamIds().First(x => !string.Equals(x, teamId, StringComparison.Ordinal));
            teamBattle.HandleObjectiveDestroyed(loserTeamId);
        });

        for (var i = 0; i < 180; i++)
            await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PostRound));
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();
            var snapshot = meta.GetSnapshot(userId);
            var achievement = snapshot.Achievements.Single(entry => entry.Id == "wh40k-ach-victory-parade");

            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundCompletedFaction), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundWins), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpRoundWin), Is.EqualTo(100));
                Assert.That(achievement.Progress, Is.EqualTo(1));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundEndWithDrawSkipsWinRewardsButKeepsCompletion()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (userId, _) = await EnsureSinglePlayerTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, userId);

        await server.WaitAssertion(() =>
        {
            var teamBattle = server.System<WH40KTeamBattleRuleSystem>();
            teamBattle.HandleObjectiveDestroyed("UnknownTeam");
        });

        for (var i = 0; i < 180; i++)
            await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PostRound));
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundCompletedFaction), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundWins), Is.EqualTo(0));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpRoundWin), Is.EqualTo(0));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CombatKillStatsRespectXpCapAndIgnoreFriendlyFire()
    {
        await using var pair = await StartWh40KRoundAsync(dummySessions: 1);
        var server = pair.Server;

        var (userId, friendlyVictimUserId, killerTeamId) = await EnsureTwoDistinctPlayersAsync(pair);
        var enemyTeamId = ResolveEnemyTeamId(killerTeamId);
        await EnsureRuntimeMetaStateAsync(pair, userId);
        await EnsureRuntimeMetaStateAsync(pair, friendlyVictimUserId);
        var initialLifetimeXp = await GetLifetimeXpAsync(pair, userId);

        var config = server.ResolveDependency<IConfigurationManager>();
        var originalXpKill = config.GetCVar(CCVars.WH40KMetaXpKill);
        var originalKillCap = config.GetCVar(CCVars.WH40KMetaXpKillCapPerRound);
        var originalMultiplier = config.GetCVar(CCVars.WH40KMetaXpMultiplier);

        try
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpKill, 25);
                config.SetCVar(CCVars.WH40KMetaXpKillCapPerRound, 100);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, 1f);
            });

            await ForceUserTeamAsync(pair, friendlyVictimUserId, killerTeamId);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                for (var i = 0; i < 6; i++)
                {
                    entMan.EventBus.RaiseEvent(
                        EventSource.Local,
                        new WH40KValidatedKillRewardEvent(
                            EntityUid.Invalid,
                            userId,
                            null,
                            killerTeamId,
                            enemyTeamId,
                            $"it-round-kill-cap-{i}"));
                    entMan.EventBus.RaiseEvent(
                        EventSource.Local,
                        new WH40KConfirmedEliminationEvent(
                            EntityUid.Invalid,
                            new KillPlayerSource(userId),
                            Array.Empty<KillSource>(),
                            null,
                            killerTeamId,
                            enemyTeamId,
                            false));
                }
            });
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var playerMan = server.ResolveDependency<IPlayerManager>();
                var friendlyVictim = GetAttachedEntity(playerMan, friendlyVictimUserId);
                entMan.EnsureComponent<WH40KTeamMemberComponent>(friendlyVictim).TeamId = killerTeamId;
                var ev = new AttributedKilledEvent(friendlyVictim, new KillPlayerSource(userId), Array.Empty<KillSource>(), false);
                entMan.EventBus.RaiseLocalEvent(friendlyVictim, ref ev, true);
            });

            await pair.RunTicksSync(5);

            await FinalizePendingEliminationsAsync(pair);

            await server.WaitAssertion(() =>
            {
                var stats = server.System<WH40KPlayerStatsSystem>();
                var meta = server.System<WH40KMetaProgressSystem>();
                var snapshot = meta.GetSnapshot(userId);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.CombatEnemyEliminations),
                        Is.EqualTo(6),
                        "Only enemy eliminations should be counted; friendly-fire kill must be ignored.");
                    Assert.That(
                        stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpKill),
                        Is.EqualTo(100),
                        "Kill XP must respect the per-round XP cap, not a raw kill-count cap.");
                    Assert.That(
                        snapshot.LifetimeXp,
                        Is.EqualTo(initialLifetimeXp + 100),
                        "Meta lifetime XP must match granted kill XP under cap.");
                });
            });
        }
        finally
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpKill, originalXpKill);
                config.SetCVar(CCVars.WH40KMetaXpKillCapPerRound, originalKillCap);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, originalMultiplier);
            });
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CombatKillXpCapsByAmountAndAchievementXpStaysOutsideRepeatableBudget()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (userId, killerTeamId) = await EnsureSinglePlayerTeamAsync(pair);
        var enemyTeamId = ResolveEnemyTeamId(killerTeamId);
        await EnsureRuntimeMetaStateAsync(pair, userId);
        var initialLifetimeXp = await GetLifetimeXpAsync(pair, userId);

        var config = server.ResolveDependency<IConfigurationManager>();
        var originalXpKill = config.GetCVar(CCVars.WH40KMetaXpKill);
        var originalKillCap = config.GetCVar(CCVars.WH40KMetaXpKillCapPerRound);
        var originalObjectiveMajor = config.GetCVar(CCVars.WH40KMetaXpObjectiveMajor);
        var originalObjectiveCap = config.GetCVar(CCVars.WH40KMetaXpObjectiveCapPerRound);
        var originalRoundWin = config.GetCVar(CCVars.WH40KMetaXpRoundWin);
        var originalRepeatableCap = config.GetCVar(CCVars.WH40KMetaXpRepeatableCapPerRound);
        var originalMultiplier = config.GetCVar(CCVars.WH40KMetaXpMultiplier);

        try
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpKill, 15);
                config.SetCVar(CCVars.WH40KMetaXpKillCapPerRound, 100);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveMajor, 100);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveCapPerRound, 200);
                config.SetCVar(CCVars.WH40KMetaXpRoundWin, 100);
                config.SetCVar(CCVars.WH40KMetaXpRepeatableCapPerRound, 400);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, 1f);
            });

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                for (var i = 0; i < 8; i++)
                {
                    entMan.EventBus.RaiseEvent(
                        EventSource.Local,
                        new WH40KValidatedKillRewardEvent(
                            EntityUid.Invalid,
                            userId,
                            null,
                            killerTeamId,
                            enemyTeamId,
                            $"it-repeatable-kill-{i}"));
                    entMan.EventBus.RaiseEvent(
                        EventSource.Local,
                        new WH40KConfirmedEliminationEvent(
                            EntityUid.Invalid,
                            new KillPlayerSource(userId),
                            Array.Empty<KillSource>(),
                            null,
                            killerTeamId,
                            enemyTeamId,
                            false));
                }
            });
            await pair.RunTicksSync(10);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var teamBattle = server.System<WH40KTeamBattleRuleSystem>();
                var teamId = string.Empty;
                Assert.That(teamBattle.TryGetTeamIdForUser(userId, out teamId), Is.True);

                var now = DateTimeOffset.UtcNow.Ticks;
                entMan.EventBus.RaiseEvent(
                    EventSource.Local,
                    new WH40KMissionOutcomeAppliedEvent(
                        teamId,
                        "it-repeatable-cap-major-1",
                        WH40KMissionObjectiveType.ZoneControl,
                        WH40KCommandDynamicMissionScope.Faction,
                        WH40KMissionOutcomeTier.Major,
                        1,
                        now));
                entMan.EventBus.RaiseEvent(
                    EventSource.Local,
                    new WH40KMissionOutcomeAppliedEvent(
                        teamId,
                        "it-repeatable-cap-major-2",
                        WH40KMissionObjectiveType.ZoneControl,
                        WH40KCommandDynamicMissionScope.Faction,
                        WH40KMissionOutcomeTier.Major,
                        1,
                        now + 1));
                entMan.EventBus.RaiseEvent(
                    EventSource.Local,
                    new WH40KMissionOutcomeAppliedEvent(
                        teamId,
                        "it-repeatable-cap-major-3",
                        WH40KMissionObjectiveType.ZoneControl,
                        WH40KCommandDynamicMissionScope.Faction,
                        WH40KMissionOutcomeTier.Major,
                        1,
                        now + 2));

                teamBattle.HandleObjectiveDestroyed(ResolveEnemyTeamId(teamId));
            });

            await pair.RunTicksSync(40);
            await FinalizePendingEliminationsAsync(pair);
            await pair.RunTicksSync(40);

            await server.WaitAssertion(() =>
            {
                var stats = server.System<WH40KPlayerStatsSystem>();
                var meta = server.System<WH40KMetaProgressSystem>();
                var snapshot = meta.GetSnapshot(userId);

                Assert.Multiple(() =>
                {
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(8));
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpKill), Is.EqualTo(100));
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MissionOutcomes), Is.EqualTo(3));
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpObjective), Is.EqualTo(200));
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundWins), Is.EqualTo(1));
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpRoundWin), Is.EqualTo(100));
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaAchievementCompleted), Is.GreaterThanOrEqualTo(1));
                    Assert.That(stats.GetLifetimeCounter(userId, "meta.xp.achievement"), Is.EqualTo(150));
                    Assert.That(snapshot.LifetimeXp, Is.EqualTo(initialLifetimeXp + 550));
                });
            });
        }
        finally
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpKill, originalXpKill);
                config.SetCVar(CCVars.WH40KMetaXpKillCapPerRound, originalKillCap);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveMajor, originalObjectiveMajor);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveCapPerRound, originalObjectiveCap);
                config.SetCVar(CCVars.WH40KMetaXpRoundWin, originalRoundWin);
                config.SetCVar(CCVars.WH40KMetaXpRepeatableCapPerRound, originalRepeatableCap);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, originalMultiplier);
            });
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CombatReviveLoopRevokesNetXpAndOnlyCountsFinalElimination()
    {
        await using var pair = await StartWh40KRoundAsync(dummySessions: 2);
        var server = pair.Server;

        var (healerUserId, victimUserId, sourceTeamId) = await EnsureTwoPlayersSameTeamAsync(pair);
        var killerUserId = await EnsureThirdDistinctUserAsync(pair, healerUserId, victimUserId);
        var enemyTeamId = ResolveEnemyTeamId(sourceTeamId);
        await ForceUserTeamAsync(pair, killerUserId, enemyTeamId);
        await EnsureRuntimeMetaStateAsync(pair, healerUserId);
        await EnsureRuntimeMetaStateAsync(pair, victimUserId);
        await EnsureRuntimeMetaStateAsync(pair, killerUserId);
        var initialLifetimeXp = await GetLifetimeXpAsync(pair, killerUserId);

        var config = server.ResolveDependency<IConfigurationManager>();
        var originalXpKill = config.GetCVar(CCVars.WH40KMetaXpKill);
        var originalKillCap = config.GetCVar(CCVars.WH40KMetaXpKillCapPerRound);
        var originalMultiplier = config.GetCVar(CCVars.WH40KMetaXpMultiplier);

        try
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpKill, 25);
                config.SetCVar(CCVars.WH40KMetaXpKillCapPerRound, 100);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, 1f);
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var playerMan = server.ResolveDependency<IPlayerManager>();
                var victimEntity = GetAttachedEntity(playerMan, victimUserId);

                var ev = new AttributedKilledEvent(victimEntity, new KillPlayerSource(killerUserId), Array.Empty<KillSource>(), false);
                entMan.EventBus.RaiseLocalEvent(victimEntity, ref ev, true);
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var stats = server.System<WH40KPlayerStatsSystem>();
                Assert.Multiple(() =>
                {
                    Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.MetaXpKill), Is.EqualTo(25));
                    Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(0));
                });
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var playerMan = server.ResolveDependency<IPlayerManager>();
                var healerEntity = GetAttachedEntity(playerMan, healerUserId);
                var victimEntity = GetAttachedEntity(playerMan, victimUserId);
                var victimMobState = entMan.GetComponent<MobStateComponent>(victimEntity);

                entMan.EventBus.RaiseEvent(
                    EventSource.Local,
                    new MobStateChangedEvent(victimEntity, victimMobState, MobState.Dead, MobState.Critical, healerEntity));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var stats = server.System<WH40KPlayerStatsSystem>();
                var meta = server.System<WH40KMetaProgressSystem>();
                var snapshot = meta.GetSnapshot(killerUserId);

                Assert.Multiple(() =>
                {
                    Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.MetaXpKill), Is.EqualTo(25));
                    Assert.That(stats.GetLifetimeCounter(killerUserId, "meta.xp.kill.revoked"), Is.EqualTo(-25));
                    Assert.That(snapshot.LifetimeXp, Is.EqualTo(initialLifetimeXp));
                });
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var playerMan = server.ResolveDependency<IPlayerManager>();
                var victimEntity = GetAttachedEntity(playerMan, victimUserId);

                var ev = new AttributedKilledEvent(victimEntity, new KillPlayerSource(killerUserId), Array.Empty<KillSource>(), false);
                entMan.EventBus.RaiseLocalEvent(victimEntity, ref ev, true);
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var stats = server.System<WH40KPlayerStatsSystem>();
                Assert.Multiple(() =>
                {
                    Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.MetaXpKill), Is.EqualTo(25));
                    Assert.That(stats.GetLifetimeCounter(killerUserId, "meta.xp.kill.revoked"), Is.EqualTo(-25));
                    Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(0));
                });
            });

            await FinalizePendingEliminationsAsync(pair);

            await server.WaitAssertion(() =>
            {
                var stats = server.System<WH40KPlayerStatsSystem>();
                var meta = server.System<WH40KMetaProgressSystem>();
                var snapshot = meta.GetSnapshot(killerUserId);

                Assert.Multiple(() =>
                {
                    Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(1));
                    Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.MetaXpKill), Is.EqualTo(25));
                    Assert.That(stats.GetLifetimeCounter(killerUserId, "meta.xp.kill.revoked"), Is.EqualTo(-25));
                    Assert.That(snapshot.LifetimeXp, Is.EqualTo(initialLifetimeXp));
                });
            });
        }
        finally
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpKill, originalXpKill);
                config.SetCVar(CCVars.WH40KMetaXpKillCapPerRound, originalKillCap);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, originalMultiplier);
            });
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeathAndObjectiveAndSupportStats()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (userId, teamId) = await EnsureSinglePlayerTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, userId);
        var initialLifetimeXp = await GetLifetimeXpAsync(pair, userId);

        // ── Part 1: Suicide death counts but grants no kill or XP rewards ───────
        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single().AttachedEntity!.Value;
            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new KillReportedEvent(player, new KillPlayerSource(userId), null, true));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();
            var snapshot = meta.GetSnapshot(userId);

            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.CombatDeaths), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(0));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpKill), Is.EqualTo(0));
                Assert.That(snapshot.LifetimeXp, Is.EqualTo(initialLifetimeXp));
            });
        });

        // ── Part 2: Influence capture and defense events record objective stats ─
        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var userEntity = GetAttachedEntity(playerMan, userId);

            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new WH40KInfluencePointCapturedEvent(teamId, userEntity));
            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new WH40KInfluencePointRewardTickEvent(teamId, userEntity, 3));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.Multiple(() =>
            {
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.ObjectiveCaptureSuccessValidated), Is.EqualTo(1));
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.ObjectiveDefenseSuccessValidated), Is.EqualTo(1));
            });
        });

        // ── Part 3: Support healing buckets respect threshold, ignore invalid ────
        var targetUserId = new NetUserId(Guid.NewGuid());

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();

            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new WH40KTeamBattleHealingDoneEvent(userId, targetUserId, teamId, 60));
            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new WH40KTeamBattleHealingDoneEvent(userId, targetUserId, teamId, 30));
            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new WH40KTeamBattleHealingDoneEvent(userId, targetUserId, teamId, 20));
            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new WH40KTeamBattleHealingDoneEvent(userId, targetUserId, teamId, 190));

            // Invalid events must be ignored.
            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new WH40KTeamBattleHealingDoneEvent(userId, userId, teamId, 100));
            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new WH40KTeamBattleHealingDoneEvent(userId, targetUserId, teamId, 0));
            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new WH40KTeamBattleHealingDoneEvent(userId, targetUserId, " ", 100));
        });

        await pair.RunTicksSync(10);
        await FinalizePendingEliminationsAsync(pair);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(
                    stats.GetRoundCounter(userId, WH40KPlayerStatKeys.SupportHealBucket100Validated),
                    Is.EqualTo(3),
                    "Heal bucket stat must count every full 100 HP bucket in round.");
                Assert.That(
                    stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.SupportHealBucket100Validated),
                    Is.EqualTo(3),
                    "Lifetime heal bucket stat must match applied round buckets.");
            });
        });

        await pair.CleanReturnAsync();
    }


    [Test]
    public async Task SupportReviveStabilizeAndEnemyAssist()
    {
        await using var pair = await StartWh40KRoundAsync(dummySessions: 2);
        var server = pair.Server;

        // ── Part 1: Support revive and stabilize require valid allied source/target
        var (sourceUserId, targetUserId, sourceTeamId) = await EnsureTwoPlayersSameTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, sourceUserId);
        await EnsureRuntimeMetaStateAsync(pair, targetUserId);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();

            var sourceEntity = GetAttachedEntity(playerMan, sourceUserId);
            var targetEntity = GetAttachedEntity(playerMan, targetUserId);
            var targetMobState = entMan.EnsureComponent<MobStateComponent>(targetEntity);

            // Valid allied revive.
            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new MobStateChangedEvent(targetEntity, targetMobState, MobState.Dead, MobState.Critical, sourceEntity));
            // Valid allied stabilize.
            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new MobStateChangedEvent(targetEntity, targetMobState, MobState.Critical, MobState.Alive, sourceEntity));

            // Invalid: source == target.
            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new MobStateChangedEvent(targetEntity, targetMobState, MobState.Dead, MobState.Critical, targetEntity));

            // Invalid: team mismatch (same actors, mismatched target team).
            var targetMember = entMan.EnsureComponent<WH40KTeamMemberComponent>(targetEntity);
            targetMember.TeamId = ResolveEnemyTeamId(sourceTeamId);
            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new MobStateChangedEvent(targetEntity, targetMobState, MobState.Dead, MobState.Critical, sourceEntity));
        });

        await pair.RunTicksSync(10);
        await FinalizePendingEliminationsAsync(pair);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(sourceUserId, WH40KPlayerStatKeys.SupportRevivesValidated), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(sourceUserId, WH40KPlayerStatKeys.SupportStabilizationsValidated), Is.EqualTo(1));
            });
        });

        // Restore target team so EnsureTwoPlayersSameTeamAsync can find two same-team players.
        await server.WaitAssertion(() =>
        {
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var targetEntity = GetAttachedEntity(playerMan, targetUserId);
            var targetMember = entMan.EnsureComponent<WH40KTeamMemberComponent>(targetEntity);
            targetMember.TeamId = sourceTeamId;
        });

        // ── Part 2: Enemy assist is recorded for valid teammate only ─────────────
        var (killerUserId, assistUserId, killerTeamId) = await EnsureTwoPlayersSameTeamAsync(pair);
        var enemyTeamId = ResolveEnemyTeamId(killerTeamId);
        await EnsureRuntimeMetaStateAsync(pair, killerUserId);
        await EnsureRuntimeMetaStateAsync(pair, assistUserId);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var killerEntity = GetAttachedEntity(playerMan, killerUserId);
            var spawnCoords = entMan.GetComponent<TransformComponent>(killerEntity).Coordinates;

            var victim = entMan.SpawnEntity(HereticReinforcementPrototype, spawnCoords);
            var victimMember = entMan.EnsureComponent<WH40KTeamMemberComponent>(victim);
            victimMember.TeamId = enemyTeamId;

            var ev = new AttributedKilledEvent(
                victim,
                new KillPlayerSource(killerUserId),
                new KillSource[] { new KillPlayerSource(assistUserId) },
                false);
            entMan.EventBus.RaiseLocalEvent(victim, ref ev, true);
        });

        await pair.RunTicksSync(10);
        await FinalizePendingEliminationsAsync(pair);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(assistUserId, WH40KPlayerStatKeys.CombatEnemyAssistsValidated), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(assistUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(0));
                Assert.That(stats.GetLifetimeCounter(assistUserId, WH40KPlayerStatKeys.MetaXpKill), Is.EqualTo(0));
            });
        });

        await pair.CleanReturnAsync();
    }


    [Test]
    public async Task KillReportedOnPlayerVictimRecordsVictimDeathStat()
    {
        await using var pair = await StartWh40KRoundAsync(dummySessions: 1);
        var server = pair.Server;

        var (killerUserId, victimUserId, killerTeamId) = await EnsureTwoDistinctPlayersAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, killerUserId);
        await EnsureRuntimeMetaStateAsync(pair, victimUserId);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var victimEntity = GetAttachedEntity(playerMan, victimUserId);

            var victimMember = entMan.EnsureComponent<WH40KTeamMemberComponent>(victimEntity);
            victimMember.TeamId = ResolveEnemyTeamId(killerTeamId);

            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new KillReportedEvent(victimEntity, new KillPlayerSource(killerUserId), null, false));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.That(stats.GetLifetimeCounter(victimUserId, WH40KPlayerStatKeys.CombatDeaths), Is.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CombatAttributionCountsOnDeadOnlyAndCreditsEnvironmentFinishToHeldItemAttacker()
    {
        await using var pair = await StartWh40KRoundAsync(dummySessions: 1);
        var server = pair.Server;

        var (killerUserId, victimUserId, killerTeamId) = await EnsureTwoDistinctPlayersAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, killerUserId);
        await EnsureRuntimeMetaStateAsync(pair, victimUserId);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var protoMan = server.ResolveDependency<IPrototypeManager>();
            var hands = entMan.System<SharedHandsSystem>();
            var damageable = entMan.System<DamageableSystem>();
            var capture = server.System<WH40KKillAttributionTestSystem>();

            capture.Reset();

            var killerEntity = GetAttachedEntity(playerMan, killerUserId);
            var victimEntity = GetAttachedEntity(playerMan, victimUserId);
            var killerCoords = entMan.GetComponent<TransformComponent>(killerEntity).Coordinates;

            entMan.EnsureComponent<WH40KTeamMemberComponent>(victimEntity).TeamId = ResolveEnemyTeamId(killerTeamId);

            var heldItem = entMan.SpawnEntity("Crowbar", killerCoords);
            Assert.That(
                hands.TryPickupAnyHand(killerEntity, heldItem, checkActionBlocker: false, animateUser: false, animate: false),
                Is.True);

            var blunt = protoMan.Index(BluntDamageType);
            var (criticalThreshold, deadThreshold) = GetCriticalAndDeadThresholds(entMan.GetComponent<MobThresholdsComponent>(victimEntity));

            Assert.That(
                damageable.TryChangeDamage(
                    victimEntity,
                    new DamageSpecifier(blunt, criticalThreshold),
                    ignoreResistances: true,
                    origin: heldItem),
                Is.True);

            Assert.That(deadThreshold > criticalThreshold, Is.True);
        });

        await pair.RunTicksSync(10);
        await FinalizePendingEliminationsAsync(pair);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var mobState = entMan.System<MobStateSystem>();
            var stats = server.System<WH40KPlayerStatsSystem>();
            var capture = server.System<WH40KKillAttributionTestSystem>();
            var victimEntity = GetAttachedEntity(playerMan, victimUserId);

            Assert.Multiple(() =>
            {
                Assert.That(mobState.IsCritical(victimEntity), Is.True, "Victim should be downed but not dead after the first hit.");
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(0));
                Assert.That(stats.GetLifetimeCounter(victimUserId, WH40KPlayerStatKeys.CombatDeaths), Is.EqualTo(0));
                Assert.That(capture.DownedCount, Is.EqualTo(1));
                Assert.That(capture.KilledCount, Is.EqualTo(0));
                Assert.That(capture.CompatibilityKillCount, Is.EqualTo(0), "WH40K kill compatibility event must not fire on Critical.");
                Assert.That(capture.LastDowned?.Primary, Is.EqualTo(new KillPlayerSource(killerUserId)));
            });
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var protoMan = server.ResolveDependency<IPrototypeManager>();
            var damageable = entMan.System<DamageableSystem>();
            var victimEntity = GetAttachedEntity(playerMan, victimUserId);
            var blunt = protoMan.Index(BluntDamageType);
            var (_, deadThreshold) = GetCriticalAndDeadThresholds(entMan.GetComponent<MobThresholdsComponent>(victimEntity));
            var currentDamage = damageable.GetTotalDamage(victimEntity);
            var finishingDamage = deadThreshold - currentDamage + FixedPoint2.New(5);

            Assert.That(
                damageable.TryChangeDamage(
                    victimEntity,
                    new DamageSpecifier(blunt, finishingDamage),
                    ignoreResistances: true,
                    origin: null),
                Is.True);
        });

        await pair.RunTicksSync(10);
        await FinalizePendingEliminationsAsync(pair);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var mobState = entMan.System<MobStateSystem>();
            var stats = server.System<WH40KPlayerStatsSystem>();
            var capture = server.System<WH40KKillAttributionTestSystem>();
            var victimEntity = GetAttachedEntity(playerMan, victimUserId);

            Assert.Multiple(() =>
            {
                Assert.That(mobState.IsDead(victimEntity), Is.True);
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(victimUserId, WH40KPlayerStatKeys.CombatDeaths), Is.EqualTo(1));
                Assert.That(capture.DownedCount, Is.EqualTo(1));
                Assert.That(capture.KilledCount, Is.EqualTo(1));
                Assert.That(capture.CompatibilityKillCount, Is.EqualTo(1));
                Assert.That(capture.LastKilled?.Primary, Is.EqualTo(new KillPlayerSource(killerUserId)));
                Assert.That(capture.LastCompatibilityKill?.Primary, Is.EqualTo(new KillPlayerSource(killerUserId)));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CombatAttributionPrioritizesPlayerOverNpcDamage()
    {
        await using var pair = await StartWh40KRoundAsync(dummySessions: 1);
        var server = pair.Server;

        var (killerUserId, victimUserId, killerTeamId) = await EnsureTwoDistinctPlayersAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, killerUserId);
        await EnsureRuntimeMetaStateAsync(pair, victimUserId);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var protoMan = server.ResolveDependency<IPrototypeManager>();
            var damageable = entMan.System<DamageableSystem>();
            var capture = server.System<WH40KKillAttributionTestSystem>();

            capture.Reset();

            var killerEntity = GetAttachedEntity(playerMan, killerUserId);
            var victimEntity = GetAttachedEntity(playerMan, victimUserId);
            var npcCoords = entMan.GetComponent<TransformComponent>(killerEntity).Coordinates.Offset(new Vector2(1f, 1f));

            entMan.EnsureComponent<WH40KTeamMemberComponent>(victimEntity).TeamId = ResolveEnemyTeamId(killerTeamId);

            var npcEntity = entMan.SpawnEntity(MonkeyPrototype, npcCoords);
            entMan.EnsureComponent<HTNComponent>(npcEntity);

            var blunt = protoMan.Index(BluntDamageType);
            var (_, deadThreshold) = GetCriticalAndDeadThresholds(entMan.GetComponent<MobThresholdsComponent>(victimEntity));

            Assert.That(
                damageable.TryChangeDamage(
                    victimEntity,
                    new DamageSpecifier(blunt, FixedPoint2.New(5)),
                    ignoreResistances: true,
                    origin: killerEntity),
                Is.True);

            var currentDamage = damageable.GetTotalDamage(victimEntity);
            var lethalNpcDamage = deadThreshold - currentDamage + FixedPoint2.New(20);

            Assert.That(
                damageable.TryChangeDamage(
                    victimEntity,
                    new DamageSpecifier(blunt, lethalNpcDamage),
                    ignoreResistances: true,
                    origin: npcEntity),
                Is.True);
        });

        await pair.RunTicksSync(10);
        await FinalizePendingEliminationsAsync(pair);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var capture = server.System<WH40KKillAttributionTestSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(victimUserId, WH40KPlayerStatKeys.CombatDeaths), Is.EqualTo(1));
                Assert.That(capture.KilledCount, Is.EqualTo(1));
                Assert.That(capture.CompatibilityKillCount, Is.EqualTo(1));
                Assert.That(capture.LastKilled?.Primary, Is.EqualTo(new KillPlayerSource(killerUserId)));
                Assert.That(capture.LastCompatibilityKill?.Primary, Is.EqualTo(new KillPlayerSource(killerUserId)));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CombatAttributionRecordsMultipleAssistsFromRealDamage()
    {
        await using var pair = await StartWh40KRoundAsync(dummySessions: 3);
        var server = pair.Server;

        var (killerUserId, assistUserIdOne, assistUserIdTwo, victimUserId, killerTeamId) = await EnsureThreeAttackersAndVictimAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, killerUserId);
        await EnsureRuntimeMetaStateAsync(pair, assistUserIdOne);
        await EnsureRuntimeMetaStateAsync(pair, assistUserIdTwo);
        await EnsureRuntimeMetaStateAsync(pair, victimUserId);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var protoMan = server.ResolveDependency<IPrototypeManager>();
            var damageable = entMan.System<DamageableSystem>();
            var capture = server.System<WH40KKillAttributionTestSystem>();

            capture.Reset();

            var assistOneEntity = GetAttachedEntity(playerMan, assistUserIdOne);
            var assistTwoEntity = GetAttachedEntity(playerMan, assistUserIdTwo);
            var killerEntity = GetAttachedEntity(playerMan, killerUserId);
            var victimEntity = GetAttachedEntity(playerMan, victimUserId);

            entMan.EnsureComponent<WH40KTeamMemberComponent>(victimEntity).TeamId = ResolveEnemyTeamId(killerTeamId);

            var blunt = protoMan.Index(BluntDamageType);
            var (_, deadThreshold) = GetCriticalAndDeadThresholds(entMan.GetComponent<MobThresholdsComponent>(victimEntity));

            Assert.That(damageable.TryChangeDamage(victimEntity, new DamageSpecifier(blunt, FixedPoint2.New(30)), ignoreResistances: true, origin: assistOneEntity), Is.True);
            Assert.That(damageable.TryChangeDamage(victimEntity, new DamageSpecifier(blunt, FixedPoint2.New(20)), ignoreResistances: true, origin: assistTwoEntity), Is.True);

            var currentDamage = damageable.GetTotalDamage(victimEntity);
            var lethalFinisher = deadThreshold - currentDamage + FixedPoint2.New(5);
            Assert.That(damageable.TryChangeDamage(victimEntity, new DamageSpecifier(blunt, lethalFinisher), ignoreResistances: true, origin: killerEntity), Is.True);
        });

        await pair.RunTicksSync(10);
        await FinalizePendingEliminationsAsync(pair);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var capture = server.System<WH40KKillAttributionTestSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(assistUserIdOne, WH40KPlayerStatKeys.CombatEnemyAssistsValidated), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(assistUserIdTwo, WH40KPlayerStatKeys.CombatEnemyAssistsValidated), Is.EqualTo(1));
                Assert.That(capture.KilledCount, Is.EqualTo(1));
                Assert.That(capture.LastKilled?.Primary, Is.EqualTo(new KillPlayerSource(killerUserId)));
                Assert.That(capture.LastKilled?.Assists.Length, Is.EqualTo(2));
                Assert.That(capture.LastKilled?.Assists, Does.Contain(new KillPlayerSource(assistUserIdOne)));
                Assert.That(capture.LastKilled?.Assists, Does.Contain(new KillPlayerSource(assistUserIdTwo)));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CombatAttributionFullHealRemovesOldAssistCredit()
    {
        await using var pair = await StartWh40KRoundAsync(dummySessions: 2);
        var server = pair.Server;

        var (killerUserId, assistUserId, victimUserId, killerTeamId) = await EnsureTwoAttackersAndVictimAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, killerUserId);
        await EnsureRuntimeMetaStateAsync(pair, assistUserId);
        await EnsureRuntimeMetaStateAsync(pair, victimUserId);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var protoMan = server.ResolveDependency<IPrototypeManager>();
            var damageable = entMan.System<DamageableSystem>();
            var capture = server.System<WH40KKillAttributionTestSystem>();

            capture.Reset();

            var assistEntity = GetAttachedEntity(playerMan, assistUserId);
            var killerEntity = GetAttachedEntity(playerMan, killerUserId);
            var victimEntity = GetAttachedEntity(playerMan, victimUserId);
            var victimDamageable = entMan.GetComponent<DamageableComponent>(victimEntity);

            entMan.EnsureComponent<WH40KTeamMemberComponent>(victimEntity).TeamId = ResolveEnemyTeamId(killerTeamId);

            var blunt = protoMan.Index(BluntDamageType);
            Assert.That(damageable.TryChangeDamage(victimEntity, new DamageSpecifier(blunt, FixedPoint2.New(40)), ignoreResistances: true, origin: assistEntity), Is.True);

            var healedAmount = damageable.GetTotalDamage(victimEntity);
            damageable.HealEvenly((victimEntity, victimDamageable), -healedAmount, origin: null, ignoreGlobalModifiers: true);

            var (_, deadThreshold) = GetCriticalAndDeadThresholds(entMan.GetComponent<MobThresholdsComponent>(victimEntity));
            var lethalFinisher = deadThreshold + FixedPoint2.New(5);
            Assert.That(damageable.TryChangeDamage(victimEntity, new DamageSpecifier(blunt, lethalFinisher), ignoreResistances: true, origin: killerEntity), Is.True);
        });

        await pair.RunTicksSync(10);
        await FinalizePendingEliminationsAsync(pair);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var capture = server.System<WH40KKillAttributionTestSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(assistUserId, WH40KPlayerStatKeys.CombatEnemyAssistsValidated), Is.EqualTo(0), "A fully healed-out contribution must not linger as an assist.");
                Assert.That(capture.KilledCount, Is.EqualTo(1));
                Assert.That(capture.LastKilled?.Primary, Is.EqualTo(new KillPlayerSource(killerUserId)));
                Assert.That(capture.LastKilled?.Assists.Length, Is.EqualTo(0));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnclaimedReinforcementKillGrantsNoXpOrRoundStats()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (killerUserId, killerTeamId) = await EnsureSinglePlayerTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, killerUserId);
        var initialLifetimeXp = await GetLifetimeXpAsync(pair, killerUserId);

        var enemyTeamId = ResolveEnemyTeamId(killerTeamId);
        EntityUid reinforcement = default;
        var initialFrontPoints = 0;
        var initialCommandPoints = 0;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var ticker = server.System<GameTicker>();

            var player = server.ResolveDependency<IPlayerManager>().Sessions.First().AttachedEntity!.Value;
            var killerCoords = entMan.GetComponent<TransformComponent>(player).Coordinates.Offset(new Vector2(1f, 0f));
            reinforcement = entMan.SpawnEntity(HereticReinforcementPrototype, killerCoords);

            var teamMember = entMan.EnsureComponent<WH40KTeamMemberComponent>(reinforcement);
            teamMember.TeamId = enemyTeamId;

            var rewardState = entMan.EnsureComponent<WH40KReinforcementRewardStateComponent>(reinforcement);
            rewardState.WasClaimedByPlayer = false;
            rewardState.ClaimedUserId = null;

            var rule = GetActiveRule(entMan, ticker);
            initialFrontPoints = rule.TeamFrontPoints.GetValueOrDefault(killerTeamId);
            initialCommandPoints = rule.TeamCommandPoints.GetValueOrDefault(killerTeamId);
        });

        await RaiseAttributedKillAsync(pair, reinforcement, killerUserId);
        await FinalizePendingEliminationsAsync(pair);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var ticker = server.System<GameTicker>();
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();
            var rule = GetActiveRule(entMan, ticker);
            var killerTeamIndex = GetTeamIndex(rule, killerTeamId);
            var enemyTeamIndex = GetTeamIndex(rule, enemyTeamId);

            Assert.Multiple(() =>
            {
                Assert.That(meta.GetSnapshot(killerUserId).LifetimeXp, Is.EqualTo(initialLifetimeXp));
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.MetaXpKill), Is.EqualTo(0));
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(0));
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(0));
                Assert.That(rule.TeamFrontPoints.GetValueOrDefault(killerTeamId), Is.EqualTo(initialFrontPoints));
                Assert.That(rule.TeamCommandPoints.GetValueOrDefault(killerTeamId), Is.EqualTo(initialCommandPoints));
                Assert.That(rule.TeamKills[killerTeamIndex], Is.EqualTo(0));
                Assert.That(rule.TeamDeaths[enemyTeamIndex], Is.EqualTo(0));
                Assert.That(rule.PlayerKills.GetValueOrDefault(killerUserId), Is.EqualTo(0));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClaimedReinforcementKillCountsOnlyThreeTimesPerUserPerRound()
    {
        await using var pair = await StartWh40KRoundAsync(dummySessions: 1);
        var server = pair.Server;

        var (killerUserId, claimedUserId, killerTeamId) = await EnsureTwoDistinctPlayersAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, killerUserId);
        await EnsureRuntimeMetaStateAsync(pair, claimedUserId);
        var initialLifetimeXp = await GetLifetimeXpAsync(pair, killerUserId);

        var config = server.ResolveDependency<IConfigurationManager>();
        var originalKill = config.GetCVar(CCVars.WH40KMetaXpKill);
        var originalKillCap = config.GetCVar(CCVars.WH40KMetaXpKillCapPerRound);
        var originalRepeatableCap = config.GetCVar(CCVars.WH40KMetaXpRepeatableCapPerRound);
        var originalMultiplier = config.GetCVar(CCVars.WH40KMetaXpMultiplier);

        var enemyTeamId = ResolveEnemyTeamId(killerTeamId);
        var initialFrontPoints = 0;
        var initialCommandPoints = 0;
        var frontPointsPerKill = 0;

        try
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpKill, 25);
                config.SetCVar(CCVars.WH40KMetaXpKillCapPerRound, 400);
                config.SetCVar(CCVars.WH40KMetaXpRepeatableCapPerRound, 400);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, 1f);

                var entMan = server.ResolveDependency<IEntityManager>();
                var ticker = server.System<GameTicker>();

                var rule = GetActiveRule(entMan, ticker);
                initialFrontPoints = rule.TeamFrontPoints.GetValueOrDefault(killerTeamId);
                initialCommandPoints = rule.TeamCommandPoints.GetValueOrDefault(killerTeamId);
                frontPointsPerKill = Math.Max(1, rule.FrontPointsPerKill);
            });

            for (var i = 0; i < 4; i++)
            {
                var reinforcement = await SpawnReinforcementBodyAsync(
                    pair,
                    HereticReinforcementPrototype,
                    enemyTeamId,
                    claimedUserId,
                    i + 1);

                await RaiseAttributedKillAsync(pair, reinforcement, killerUserId);
                await FinalizePendingEliminationsAsync(pair);
            }

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var ticker = server.System<GameTicker>();
                var stats = server.System<WH40KPlayerStatsSystem>();
                var meta = server.System<WH40KMetaProgressSystem>();
                var rule = GetActiveRule(entMan, ticker);
                var killerTeamIndex = GetTeamIndex(rule, killerTeamId);
                var enemyTeamIndex = GetTeamIndex(rule, enemyTeamId);

                Assert.Multiple(() =>
                {
                    Assert.That(meta.GetSnapshot(killerUserId).LifetimeXp, Is.EqualTo(initialLifetimeXp + 75));
                    Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.MetaXpKill), Is.EqualTo(75));
                    Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(3));
                    Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(0), "Legacy raw kill telemetry must stay suppressed for reinforcement bodies.");
                    Assert.That(rule.TeamFrontPoints.GetValueOrDefault(killerTeamId), Is.EqualTo(initialFrontPoints + frontPointsPerKill * 3));
                    Assert.That(rule.TeamCommandPoints.GetValueOrDefault(killerTeamId), Is.EqualTo(initialCommandPoints + frontPointsPerKill * 3));
                    Assert.That(rule.TeamKills[killerTeamIndex], Is.EqualTo(3));
                    Assert.That(rule.TeamDeaths[enemyTeamIndex], Is.EqualTo(3));
                    Assert.That(rule.PlayerKills.GetValueOrDefault(killerUserId), Is.EqualTo(3));
                });
            });
        }
        finally
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpKill, originalKill);
                config.SetCVar(CCVars.WH40KMetaXpKillCapPerRound, originalKillCap);
                config.SetCVar(CCVars.WH40KMetaXpRepeatableCapPerRound, originalRepeatableCap);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, originalMultiplier);
            });
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NpcKillGrantsNoXpOrRoundStats()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (killerUserId, killerTeamId) = await EnsureSinglePlayerTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, killerUserId);
        var initialLifetimeXp = await GetLifetimeXpAsync(pair, killerUserId);

        var enemyTeamId = ResolveEnemyTeamId(killerTeamId);
        EntityUid npc = default;
        var initialFrontPoints = 0;
        var initialCommandPoints = 0;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var ticker = server.System<GameTicker>();
            var player = server.ResolveDependency<IPlayerManager>().Sessions.First().AttachedEntity!.Value;
            var coords = entMan.GetComponent<TransformComponent>(player).Coordinates.Offset(new Vector2(2f, 0f));

            npc = entMan.SpawnEntity(MonkeyPrototype, coords);
            entMan.EnsureComponent<WH40KTeamMemberComponent>(npc).TeamId = enemyTeamId;

            var rule = GetActiveRule(entMan, ticker);
            initialFrontPoints = rule.TeamFrontPoints.GetValueOrDefault(killerTeamId);
            initialCommandPoints = rule.TeamCommandPoints.GetValueOrDefault(killerTeamId);
        });

        await RaiseAttributedKillAsync(pair, npc, killerUserId);
        await FinalizePendingEliminationsAsync(pair);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var ticker = server.System<GameTicker>();
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();
            var rule = GetActiveRule(entMan, ticker);
            var killerTeamIndex = GetTeamIndex(rule, killerTeamId);
            var enemyTeamIndex = GetTeamIndex(rule, enemyTeamId);

            Assert.Multiple(() =>
            {
                Assert.That(meta.GetSnapshot(killerUserId).LifetimeXp, Is.EqualTo(initialLifetimeXp));
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.MetaXpKill), Is.EqualTo(0));
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(0));
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(0));
                Assert.That(rule.TeamFrontPoints.GetValueOrDefault(killerTeamId), Is.EqualTo(initialFrontPoints));
                Assert.That(rule.TeamCommandPoints.GetValueOrDefault(killerTeamId), Is.EqualTo(initialCommandPoints));
                Assert.That(rule.TeamKills[killerTeamIndex], Is.EqualTo(0));
                Assert.That(rule.TeamDeaths[enemyTeamIndex], Is.EqualTo(0));
                Assert.That(rule.PlayerKills.GetValueOrDefault(killerUserId), Is.EqualTo(0));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MindlessZombieKillGrantsNoXpOrRoundStats()
    {
        await using var pair = await StartWh40KRoundAsync(dummySessions: 1);
        var server = pair.Server;

        var (killerUserId, victimUserId, killerTeamId) = await EnsureTwoDistinctPlayersAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, killerUserId);
        await EnsureRuntimeMetaStateAsync(pair, victimUserId);
        var initialLifetimeXp = await GetLifetimeXpAsync(pair, killerUserId);

        var enemyTeamId = ResolveEnemyTeamId(killerTeamId);
        EntityUid zombieVictim = default;
        var initialFrontPoints = 0;
        var initialCommandPoints = 0;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var ticker = server.System<GameTicker>();
            var minds = entMan.System<MindSystem>();
            var zombies = entMan.System<ZombieSystem>();

            zombieVictim = GetAttachedEntity(playerMan, victimUserId);
            entMan.EnsureComponent<WH40KTeamMemberComponent>(zombieVictim).TeamId = enemyTeamId;

            Assert.That(minds.TryGetMind(zombieVictim, out var mindId, out var mind), Is.True);
            minds.TransferTo(mindId, null, createGhost: false, mind: mind);
            zombies.ZombifyEntity(zombieVictim);

            var rule = GetActiveRule(entMan, ticker);
            initialFrontPoints = rule.TeamFrontPoints.GetValueOrDefault(killerTeamId);
            initialCommandPoints = rule.TeamCommandPoints.GetValueOrDefault(killerTeamId);
        });

        await pair.RunTicksSync(5);
        await RaiseAttributedKillAsync(pair, zombieVictim, killerUserId);
        await FinalizePendingEliminationsAsync(pair);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var ticker = server.System<GameTicker>();
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();
            var rule = GetActiveRule(entMan, ticker);
            var killerTeamIndex = GetTeamIndex(rule, killerTeamId);
            var enemyTeamIndex = GetTeamIndex(rule, enemyTeamId);

            Assert.Multiple(() =>
            {
                Assert.That(meta.GetSnapshot(killerUserId).LifetimeXp, Is.EqualTo(initialLifetimeXp));
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.MetaXpKill), Is.EqualTo(0));
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyEliminations), Is.EqualTo(0));
                Assert.That(stats.GetLifetimeCounter(killerUserId, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(0));
                Assert.That(rule.TeamFrontPoints.GetValueOrDefault(killerTeamId), Is.EqualTo(initialFrontPoints));
                Assert.That(rule.TeamCommandPoints.GetValueOrDefault(killerTeamId), Is.EqualTo(initialCommandPoints));
                Assert.That(rule.TeamKills[killerTeamIndex], Is.EqualTo(0));
                Assert.That(rule.TeamDeaths[enemyTeamIndex], Is.EqualTo(0));
                Assert.That(rule.PlayerKills.GetValueOrDefault(killerUserId), Is.EqualTo(0));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MissionOutcomeMajorGrantsObjectiveXpOncePerOutcomeKey()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (userId, teamId) = await EnsureSinglePlayerTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, userId);
        var initialLifetimeXp = await GetLifetimeXpAsync(pair, userId);

        var config = server.ResolveDependency<IConfigurationManager>();
        var originalMajor = config.GetCVar(CCVars.WH40KMetaXpObjectiveMajor);
        var originalCap = config.GetCVar(CCVars.WH40KMetaXpObjectiveCapPerRound);
        var originalMultiplier = config.GetCVar(CCVars.WH40KMetaXpMultiplier);

        try
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpObjectiveMajor, 35);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveCapPerRound, 120);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, 1f);
            });

            var missionStartedAt = DateTimeOffset.UtcNow.Ticks;
            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var ev = new WH40KMissionOutcomeAppliedEvent(
                    teamId,
                    "it-test-mission-major",
                    WH40KMissionObjectiveType.ZoneControl,
                    WH40KCommandDynamicMissionScope.Faction,
                    WH40KMissionOutcomeTier.Major,
                    awardedDevelopmentPoints: 18,
                    missionStartedAt);

                entMan.EventBus.RaiseEvent(EventSource.Local, ev);
                entMan.EventBus.RaiseEvent(EventSource.Local, ev);
            });

            await pair.RunTicksSync(10);

            await server.WaitAssertion(() =>
            {
                var stats = server.System<WH40KPlayerStatsSystem>();
                var meta = server.System<WH40KMetaProgressSystem>();
                var snapshot = meta.GetSnapshot(userId);

                Assert.Multiple(() =>
                {
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MissionOutcomes), Is.EqualTo(1));
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpObjective), Is.EqualTo(35));
                    Assert.That(snapshot.LifetimeXp, Is.EqualTo(initialLifetimeXp + 35));
                });
            });
        }
        finally
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpObjectiveMajor, originalMajor);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveCapPerRound, originalCap);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, originalMultiplier);
            });
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MissionOutcomesTrackLogisticsTierXpAndRespectObjectiveXpCap()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (userId, teamId) = await EnsureSinglePlayerTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, userId);
        var initialLifetimeXp = await GetLifetimeXpAsync(pair, userId);

        var config = server.ResolveDependency<IConfigurationManager>();
        var originalMajor = config.GetCVar(CCVars.WH40KMetaXpObjectiveMajor);
        var originalMinor = config.GetCVar(CCVars.WH40KMetaXpObjectiveMinor);
        var originalTimeout = config.GetCVar(CCVars.WH40KMetaXpObjectiveTimeout);
        var originalFailure = config.GetCVar(CCVars.WH40KMetaXpObjectiveFailure);
        var originalCap = config.GetCVar(CCVars.WH40KMetaXpObjectiveCapPerRound);
        var originalMultiplier = config.GetCVar(CCVars.WH40KMetaXpMultiplier);

        try
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpObjectiveMajor, 35);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveMinor, 20);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveTimeout, 10);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveFailure, 0);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveCapPerRound, 60);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, 1f);
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var now = DateTimeOffset.UtcNow.Ticks;

                entMan.EventBus.RaiseEvent(
                    EventSource.Local,
                    new WH40KMissionOutcomeAppliedEvent(
                        teamId,
                        "it-cargo-major-1",
                        WH40KMissionObjectiveType.CargoDelivery,
                        WH40KCommandDynamicMissionScope.Faction,
                        WH40KMissionOutcomeTier.Major,
                        11,
                        now));

                entMan.EventBus.RaiseEvent(
                    EventSource.Local,
                    new WH40KMissionOutcomeAppliedEvent(
                        teamId,
                        "it-cargo-minor",
                        WH40KMissionObjectiveType.CargoDelivery,
                        WH40KCommandDynamicMissionScope.Faction,
                        WH40KMissionOutcomeTier.Minor,
                        7,
                        now + 1));

                entMan.EventBus.RaiseEvent(
                    EventSource.Local,
                    new WH40KMissionOutcomeAppliedEvent(
                        teamId,
                        "it-zone-timeout",
                        WH40KMissionObjectiveType.ZoneControl,
                        WH40KCommandDynamicMissionScope.Faction,
                        WH40KMissionOutcomeTier.Timeout,
                        0,
                        now + 2));

                entMan.EventBus.RaiseEvent(
                    EventSource.Local,
                    new WH40KMissionOutcomeAppliedEvent(
                        teamId,
                        "it-zone-failure",
                        WH40KMissionObjectiveType.ZoneControl,
                        WH40KCommandDynamicMissionScope.Faction,
                        WH40KMissionOutcomeTier.Failure,
                        0,
                        now + 3));

                entMan.EventBus.RaiseEvent(
                    EventSource.Local,
                    new WH40KMissionOutcomeAppliedEvent(
                        teamId,
                        "it-cargo-major-2",
                        WH40KMissionObjectiveType.CargoDelivery,
                        WH40KCommandDynamicMissionScope.Faction,
                        WH40KMissionOutcomeTier.Major,
                        13,
                        now + 4));
            });

            await pair.RunTicksSync(10);

            await server.WaitAssertion(() =>
            {
                var stats = server.System<WH40KPlayerStatsSystem>();
                var meta = server.System<WH40KMetaProgressSystem>();
                var snapshot = meta.GetSnapshot(userId);

                Assert.Multiple(() =>
                {
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MissionOutcomes), Is.EqualTo(5));
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.LogisticsDeliverySuccess), Is.EqualTo(3));
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.LogisticsDeliveryValue), Is.EqualTo(31));
                    Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpObjective), Is.EqualTo(60));
                });
            });
        }
        finally
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpObjectiveMajor, originalMajor);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveMinor, originalMinor);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveTimeout, originalTimeout);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveFailure, originalFailure);
                config.SetCVar(CCVars.WH40KMetaXpObjectiveCapPerRound, originalCap);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, originalMultiplier);
            });
        }

        await pair.CleanReturnAsync();
    }

    private static async Task<TestPair> StartWh40KRoundAsync(
        int dummySessions = 0,
        bool freshPair = true,
        bool requireAttachedEntities = true)
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Dirty = true,
            InLobby = true,
            DummyTicker = false,
            Fresh = freshPair
        });

        await pair.WaitCommand("forcemap Battlefield40k");
        await pair.WaitCommand("setgamepreset WH40KTeamBattle 9999");

        if (dummySessions > 0)
        {
            await pair.Server.AddDummySessions(dummySessions);
            await pair.RunTicksSync(10);
        }

        await pair.WaitCommand("startround");
        await pair.RunTicksSync(60);

        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();
            var sessions = playerMan.Sessions.ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
                Assert.That(sessions.Length, Is.GreaterThanOrEqualTo(1));
            });
        });

        if (!requireAttachedEntities)
            return pair;

        // WH40K requires faction selection before late-join.
        // Main client session selects via the normal network path.
        await pair.Client.WaitPost(() =>
        {
            var factionSys = pair.Client.System<Content.Client._WH40K.LateJoin.WH40KFactionSystem>();
            factionSys.SelectFaction("Imperium", WH40KFactionSelectionPurpose.LateJoin);
        });
        await pair.RunTicksSync(10);

        // Dummy sessions have no client; inject selections via reflection.
        if (dummySessions > 0)
        {
            await pair.Server.WaitPost(() =>
            {
                var serverFactionSys = pair.Server.System<Content.Server._WH40K.LateJoin.WH40KFactionSystem>();
                var sysType = serverFactionSys.GetType();
                var dictField = sysType.GetField("_lateJoinSelections",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;
                var dict = (IDictionary)dictField.GetValue(serverFactionSys)!;
                var pendingType = sysType.GetNestedType("PendingLateJoinSelection",
                    BindingFlags.NonPublic)!;
                var timing = pair.Server.ResolveDependency<IGameTiming>();
                var pending = Activator.CreateInstance(pendingType,
                    "Imperium", timing.CurTime + TimeSpan.FromMinutes(2))!;

                var playerMan = pair.Server.ResolveDependency<IPlayerManager>();
                foreach (var session in playerMan.Sessions)
                {
                    if (!dict.Contains(session.UserId))
                        dict[session.UserId] = pending;
                }
            });
            await pair.RunTicksSync(5);
        }

        await pair.Server.WaitPost(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();
            foreach (var session in playerMan.Sessions)
                ticker.MakeJoinGame(session, EntityUid.Invalid, "Guardsman");
        });
        await pair.RunTicksSync(20);

        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();
            var sessions = playerMan.Sessions.ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
                Assert.That(sessions.Length, Is.GreaterThanOrEqualTo(1));
                Assert.That(sessions.All(x => x.AttachedEntity != null), Is.True);
            });
        });

        return pair;
    }

    private static async Task<(NetUserId UserId, string TeamId)> EnsureSinglePlayerTeamAsync(TestPair pair)
    {
        NetUserId userId = default;
        var resolvedTeamId = string.Empty;
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var session = playerMan.Sessions.Single();
            userId = session.UserId;
            var teamBattle = server.System<WH40KTeamBattleRuleSystem>();
            Assert.That(teamBattle.TryGetTeamIdForUser(userId, out resolvedTeamId), Is.True);
        });

        return (userId, resolvedTeamId);
    }

    private static async Task<(NetUserId UserId, string TeamId)> EnsureEnemyPlayerAsync(TestPair pair, string teamIdToAvoid)
    {
        NetUserId userId = default;
        var resolvedTeamId = string.Empty;
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var teamBattle = server.System<WH40KTeamBattleRuleSystem>();

            foreach (var session in playerMan.Sessions)
            {
                if (!teamBattle.TryGetTeamIdForUser(session.UserId, out var teamId) ||
                    string.IsNullOrWhiteSpace(teamId) ||
                    string.Equals(teamId, teamIdToAvoid, StringComparison.Ordinal))
                {
                    continue;
                }

                userId = session.UserId;
                resolvedTeamId = teamId;
                return;
            }

            Assert.Fail("Expected at least one enemy player on a different WH40K team.");
        });

        return (userId, resolvedTeamId);
    }

    private static async Task<NetUserId> EnsureThirdDistinctUserAsync(TestPair pair, params NetUserId[] excludedUsers)
    {
        NetUserId userId = default;
        var excluded = excludedUsers.ToHashSet();

        await pair.Server.WaitAssertion(() =>
        {
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();
            foreach (var session in playerMan.Sessions)
            {
                if (excluded.Contains(session.UserId))
                    continue;

                userId = session.UserId;
                return;
            }

            Assert.Fail("Expected an additional distinct player session for the test.");
        });

        return userId;
    }

    private static async Task<(NetUserId FirstUserId, NetUserId SecondUserId, string TeamId)> EnsureTwoPlayersSameTeamAsync(TestPair pair)
    {
        NetUserId first = default;
        NetUserId second = default;
        var teamId = string.Empty;
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var teamBattle = server.System<WH40KTeamBattleRuleSystem>();
            var teamBuckets = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<NetUserId>>(StringComparer.Ordinal);

            foreach (var session in playerMan.Sessions)
            {
                if (!teamBattle.TryGetTeamIdForUser(session.UserId, out var resolvedTeamId) ||
                    string.IsNullOrWhiteSpace(resolvedTeamId))
                {
                    continue;
                }

                if (!teamBuckets.TryGetValue(resolvedTeamId, out var users))
                {
                    users = new System.Collections.Generic.List<NetUserId>();
                    teamBuckets[resolvedTeamId] = users;
                }

                users.Add(session.UserId);
            }

            System.Collections.Generic.List<NetUserId>? selectedUsers = null;
            foreach (var (resolvedTeamId, users) in teamBuckets)
            {
                if (users.Count < 2)
                    continue;

                selectedUsers = users;
                teamId = resolvedTeamId;
                break;
            }

            Assert.That(selectedUsers, Is.Not.Null);
            first = selectedUsers![0];
            second = selectedUsers[1];
        });

        return (first, second, teamId);
    }

    private static async Task<(NetUserId KillerUserId, NetUserId AssistUserIdOne, NetUserId AssistUserIdTwo, NetUserId VictimUserId, string TeamId)> EnsureThreeAttackersAndVictimAsync(TestPair pair)
    {
        NetUserId killer = default;
        NetUserId assistOne = default;
        NetUserId assistTwo = default;
        NetUserId victim = default;
        var teamId = string.Empty;
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var teamBattle = server.System<WH40KTeamBattleRuleSystem>();
            var grouped = playerMan.Sessions
                .Select(session => (session.UserId, HasTeam: teamBattle.TryGetTeamIdForUser(session.UserId, out var resolvedTeamId), TeamId: resolvedTeamId))
                .Where(entry => entry.HasTeam && !string.IsNullOrWhiteSpace(entry.TeamId))
                .GroupBy(entry => entry.TeamId, StringComparer.Ordinal)
                .Select(group => group.Select(entry => entry.UserId).ToArray())
                .FirstOrDefault(group => group.Length >= 4);

            Assert.That(grouped, Is.Not.Null);
            killer = grouped![0];
            assistOne = grouped[1];
            assistTwo = grouped[2];
            victim = grouped[3];
            Assert.That(teamBattle.TryGetTeamIdForUser(killer, out teamId), Is.True);
        });

        return (killer, assistOne, assistTwo, victim, teamId);
    }

    private static async Task<(NetUserId KillerUserId, NetUserId AssistUserId, NetUserId VictimUserId, string TeamId)> EnsureTwoAttackersAndVictimAsync(TestPair pair)
    {
        NetUserId killer = default;
        NetUserId assist = default;
        NetUserId victim = default;
        var teamId = string.Empty;
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var teamBattle = server.System<WH40KTeamBattleRuleSystem>();
            var grouped = playerMan.Sessions
                .Select(session => (session.UserId, HasTeam: teamBattle.TryGetTeamIdForUser(session.UserId, out var resolvedTeamId), TeamId: resolvedTeamId))
                .Where(entry => entry.HasTeam && !string.IsNullOrWhiteSpace(entry.TeamId))
                .GroupBy(entry => entry.TeamId, StringComparer.Ordinal)
                .Select(group => group.Select(entry => entry.UserId).ToArray())
                .FirstOrDefault(group => group.Length >= 3);

            Assert.That(grouped, Is.Not.Null);
            killer = grouped![0];
            assist = grouped[1];
            victim = grouped[2];
            Assert.That(teamBattle.TryGetTeamIdForUser(killer, out teamId), Is.True);
        });

        return (killer, assist, victim, teamId);
    }

    private static async Task<(NetUserId KillerUserId, NetUserId VictimUserId, string KillerTeamId)> EnsureTwoDistinctPlayersAsync(TestPair pair)
    {
        NetUserId killerUserId = default;
        NetUserId victimUserId = default;
        var killerTeamId = string.Empty;
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var teamBattle = server.System<WH40KTeamBattleRuleSystem>();
            var resolvedUsers = playerMan.Sessions
                .Select(session => (session.UserId, HasTeam: teamBattle.TryGetTeamIdForUser(session.UserId, out var teamId), TeamId: teamId))
                .Where(entry => entry.HasTeam && !string.IsNullOrWhiteSpace(entry.TeamId))
                .Take(2)
                .ToArray();

            Assert.That(resolvedUsers.Length, Is.EqualTo(2));
            killerUserId = resolvedUsers[0].UserId;
            victimUserId = resolvedUsers[1].UserId;
            killerTeamId = resolvedUsers[0].TeamId;
        });

        return (killerUserId, victimUserId, killerTeamId);
    }

    private static EntityUid GetAttachedEntity(IPlayerManager playerMan, NetUserId userId)
    {
        var session = playerMan.Sessions.Single(session => session.UserId == userId);
        return session.AttachedEntity!.Value;
    }

    private static (FixedPoint2 CriticalThreshold, FixedPoint2 DeadThreshold) GetCriticalAndDeadThresholds(MobThresholdsComponent thresholds)
    {
        var critical = thresholds.Thresholds
            .Where(entry => entry.Value == MobState.Critical)
            .Select(entry => entry.Key)
            .DefaultIfEmpty(FixedPoint2.Zero)
            .Max();
        var dead = thresholds.Thresholds
            .Where(entry => entry.Value == MobState.Dead)
            .Select(entry => entry.Key)
            .DefaultIfEmpty(FixedPoint2.Zero)
            .Max();

        Assert.That(critical > FixedPoint2.Zero, Is.True, "Expected a critical threshold for the target mob.");
        Assert.That(dead > critical, Is.True, "Expected a dead threshold above the critical threshold for the target mob.");
        return (critical, dead);
    }

    private static string ResolveEnemyTeamId(string killerTeamId)
    {
        if (string.Equals(killerTeamId, Imperium, StringComparison.Ordinal))
            return Heretics;

        if (string.Equals(killerTeamId, Heretics, StringComparison.Ordinal))
            return Imperium;

        return string.Equals(killerTeamId, "Chaos", StringComparison.Ordinal)
            ? Imperium
            : Heretics;
    }

    private static async Task EnsureRuntimeMetaStateAsync(TestPair pair, NetUserId userId)
    {
        await pair.Server.WaitAssertion(() =>
        {
            var meta = pair.Server.System<WH40KMetaProgressSystem>();
            _ = meta.GetSnapshot(userId);
        });
    }

    private static async Task ForceUserTeamAsync(TestPair pair, NetUserId userId, string teamId)
    {
        await pair.Server.WaitAssertion(() =>
        {
            var server = pair.Server;
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var ruleQuery = entMan.EntityQueryEnumerator<WH40KTeamBattleRuleComponent, GameRuleComponent>();
            var rememberedField = typeof(WH40KTeamBattleRuleComponent)
                .GetField(nameof(WH40KTeamBattleRuleComponent.PlayerLastKnownTeam), BindingFlags.Public | BindingFlags.Instance)!;

            while (ruleQuery.MoveNext(out _, out var rule, out _))
            {
                var rememberedTeams = (System.Collections.Generic.Dictionary<NetUserId, string>) rememberedField.GetValue(rule)!;
                rememberedTeams[userId] = teamId;
                break;
            }

            var entity = GetAttachedEntity(playerMan, userId);
            entMan.EnsureComponent<WH40KTeamMemberComponent>(entity).TeamId = teamId;
        });
    }

    private static async Task FinalizePendingEliminationsAsync(TestPair pair)
    {
        await pair.Server.WaitAssertion(() =>
        {
            var validation = pair.Server.System<WH40KRoundRewardValidationSystem>();
            validation.FinalizePendingEliminations();
        });

        await pair.RunTicksSync(5);
    }

    private static async Task<int> GetLifetimeXpAsync(TestPair pair, NetUserId userId)
    {
        var lifetimeXp = 0;

        await pair.Server.WaitAssertion(() =>
        {
            var meta = pair.Server.System<WH40KMetaProgressSystem>();
            lifetimeXp = meta.GetSnapshot(userId).LifetimeXp;
        });

        return lifetimeXp;
    }

    private static async Task<EntityUid> SpawnReinforcementBodyAsync(
        TestPair pair,
        string prototypeId,
        string teamId,
        NetUserId? claimedUserId,
        int xOffset)
    {
        EntityUid spawned = default;

        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var player = pair.Server.ResolveDependency<IPlayerManager>().Sessions.First().AttachedEntity!.Value;
            var coords = entMan.GetComponent<TransformComponent>(player).Coordinates.Offset(new Vector2(xOffset, 1f));

            spawned = entMan.SpawnEntity(prototypeId, coords);
            entMan.EnsureComponent<WH40KTeamMemberComponent>(spawned).TeamId = teamId;

            var rewardState = entMan.EnsureComponent<WH40KReinforcementRewardStateComponent>(spawned);
            rewardState.WasClaimedByPlayer = claimedUserId.HasValue;
            rewardState.ClaimedUserId = claimedUserId;
        });

        await pair.RunTicksSync(2);
        return spawned;
    }

    private static async Task KillEntityAsync(TestPair pair, EntityUid attacker, EntityUid victim)
    {
        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();
            var damageable = entMan.System<DamageableSystem>();
            var blunt = protoMan.Index(BluntDamageType);
            var (_, deadThreshold) = GetCriticalAndDeadThresholds(entMan.GetComponent<MobThresholdsComponent>(victim));
            var currentDamage = damageable.GetTotalDamage(victim);
            var lethalDamage = deadThreshold - currentDamage + FixedPoint2.New(5);

            Assert.That(
                damageable.TryChangeDamage(
                    victim,
                    new DamageSpecifier(blunt, lethalDamage),
                    ignoreResistances: true,
                    origin: attacker),
                Is.True);
        });

        await pair.RunTicksSync(10);
    }

    private static async Task RaiseAttributedKillAsync(TestPair pair, EntityUid victim, NetUserId killerUserId)
    {
        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var ev = new AttributedKilledEvent(victim, new KillPlayerSource(killerUserId), Array.Empty<KillSource>(), false);
            entMan.EventBus.RaiseLocalEvent(victim, ref ev, true);
        });

        await pair.RunTicksSync(2);
    }

    private static WH40KTeamBattleRuleComponent GetActiveRule(IEntityManager entMan, GameTicker ticker)
    {
        var query = entMan.EntityQueryEnumerator<WH40KTeamBattleRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var rule, out var gameRule))
        {
            if (ticker.IsGameRuleActive(uid, gameRule))
                return rule;
        }

        Assert.Fail("Expected an active WH40K team-battle rule.");
        throw new InvalidOperationException("Expected an active WH40K team-battle rule.");
    }

    private static int GetTeamIndex(WH40KTeamBattleRuleComponent rule, string teamId)
    {
        for (var i = 0; i < rule.Teams.Count; i++)
        {
            if (string.Equals(rule.Teams[i].Id, teamId, StringComparison.Ordinal))
                return i;
        }

        Assert.Fail($"Expected team '{teamId}' to exist in the active WH40K rule.");
        return -1;
    }
}
