#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Content.IntegrationTests.Pair;
using Content.Server.GameTicking;
using Content.Server.KillTracking;
using Content.Server._WH40K.Command;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.Influence;
using Content.Server._WH40K.MetaProgress;
using Content.Server._WH40K.Stats;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.LateJoin;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class RoundLifecycleCombatIntegrationTests
{
    private const string Imperium = "Imperium";
    private const string Heretics = "Heretics";
    private const string ImperiumReinforcementPrototype = "MobHumanWH40KImperiumReinforcement";
    private const string HereticReinforcementPrototype = "MobHumanWH40KHereticReinforcement";

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
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (userId, killerTeamId) = await EnsureSinglePlayerTeamAsync(pair);
        var enemyTeamId = ResolveEnemyTeamId(killerTeamId);
        await EnsureRuntimeMetaStateAsync(pair, userId);

        var config = server.ResolveDependency<IConfigurationManager>();
        var originalXpKill = config.GetCVar(CCVars.WH40KMetaXpKill);
        var originalKillCap = config.GetCVar(CCVars.WH40KMetaXpKillCapPerRound);
        var originalMultiplier = config.GetCVar(CCVars.WH40KMetaXpMultiplier);

        try
        {
            await server.WaitAssertion(() =>
            {
                config.SetCVar(CCVars.WH40KMetaXpKill, 10);
                config.SetCVar(CCVars.WH40KMetaXpKillCapPerRound, 2);
                config.SetCVar(CCVars.WH40KMetaXpMultiplier, 1f);
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var player = server.ResolveDependency<IPlayerManager>().Sessions.Single().AttachedEntity!.Value;
                var spawnCoords = entMan.GetComponent<TransformComponent>(player).Coordinates;

                // Keep at least one enemy alive to avoid automatic victory ending the round mid-test.
                var enemyAnchor = entMan.SpawnEntity(HereticReinforcementPrototype, spawnCoords);
                var enemyAnchorMember = entMan.EnsureComponent<WH40KTeamMemberComponent>(enemyAnchor);
                enemyAnchorMember.TeamId = enemyTeamId;
            });

            for (var i = 0; i < 3; i++)
            {
                await server.WaitAssertion(() =>
                {
                    var entMan = server.ResolveDependency<IEntityManager>();
                    var player = server.ResolveDependency<IPlayerManager>().Sessions.Single().AttachedEntity!.Value;
                    var spawnCoords = entMan.GetComponent<TransformComponent>(player).Coordinates;
                    var victim = entMan.SpawnEntity(HereticReinforcementPrototype, spawnCoords);
                    var victimMember = entMan.EnsureComponent<WH40KTeamMemberComponent>(victim);
                    victimMember.TeamId = enemyTeamId;
                    entMan.EventBus.RaiseEvent(
                        EventSource.Local,
                        new KillReportedEvent(victim, new KillPlayerSource(userId), null, false));
                });

                await pair.RunTicksSync(2);
            }

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var player = server.ResolveDependency<IPlayerManager>().Sessions.Single().AttachedEntity!.Value;
                var spawnCoords = entMan.GetComponent<TransformComponent>(player).Coordinates;
                var friendlyVictim = entMan.SpawnEntity(ImperiumReinforcementPrototype, spawnCoords);
                var friendlyMember = entMan.EnsureComponent<WH40KTeamMemberComponent>(friendlyVictim);
                friendlyMember.TeamId = killerTeamId;
                entMan.EventBus.RaiseEvent(
                    EventSource.Local,
                    new KillReportedEvent(friendlyVictim, new KillPlayerSource(userId), null, false));
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var stats = server.System<WH40KPlayerStatsSystem>();
                var meta = server.System<WH40KMetaProgressSystem>();
                var snapshot = meta.GetSnapshot(userId);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.CombatEnemyKills),
                        Is.EqualTo(3),
                        "Only enemy kills should be counted; friendly-fire kill must be ignored.");
                    Assert.That(
                        stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpKill),
                        Is.EqualTo(20),
                        "Kill XP must respect per-round cap (2 grants * 10 XP).");
                    Assert.That(
                        snapshot.LifetimeXp,
                        Is.EqualTo(20),
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
    public async Task DeathAndObjectiveAndSupportStats()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (userId, teamId) = await EnsureSinglePlayerTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, userId);

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
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(0));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.MetaXpKill), Is.EqualTo(0));
                Assert.That(snapshot.LifetimeXp, Is.EqualTo(0));
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
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.ObjectiveCaptureSuccess), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.ObjectiveDefenseSuccess), Is.EqualTo(1));
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

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(
                    stats.GetRoundCounter(userId, WH40KPlayerStatKeys.SupportHealBucket100),
                    Is.EqualTo(3),
                    "Heal bucket stat must count every full 100 HP bucket in round.");
                Assert.That(
                    stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.SupportHealBucket100),
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

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(sourceUserId, WH40KPlayerStatKeys.SupportRevives), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(sourceUserId, WH40KPlayerStatKeys.SupportStabilizations), Is.EqualTo(1));
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

            entMan.EventBus.RaiseEvent(
                EventSource.Local,
                new KillReportedEvent(victim, new KillPlayerSource(killerUserId), new KillPlayerSource(assistUserId), false));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(assistUserId, WH40KPlayerStatKeys.CombatEnemyAssists), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(assistUserId, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(0));
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
    public async Task MissionOutcomeMajorGrantsObjectiveXpOncePerOutcomeKey()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        var (userId, teamId) = await EnsureSinglePlayerTeamAsync(pair);
        await EnsureRuntimeMetaStateAsync(pair, userId);

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
                    Assert.That(snapshot.LifetimeXp, Is.EqualTo(35));
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
                    Assert.That(snapshot.LifetimeXp, Is.EqualTo(60));
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
}
