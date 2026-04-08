#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Actions;
using Content.Server._WH40K.Psyker;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared._WH40K.Psyker;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class PsykerChaosLeaderFollowerRuntimeIntegrationTests
{
    private const string HumanPrototype = "MobHuman";

    [Test]
    public async Task FirstLeaderEligibleKeepsLeadershipWhenSecondJoinsCult()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid firstLeader = default;
        EntityUid secondLeader = default;

        try
        {
            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();

                firstLeader = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(-1f, 0f)), leaderEligible: true);
                secondLeader = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(1f, 0f)), leaderEligible: true);

                AttuneMember(entMan, cult, firstLeader, WH40KChaosPatron.Khorne);
                AttuneMember(entMan, cult, secondLeader, WH40KChaosPatron.Khorne);
            });

            await pair.RunTicksSync(1);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();
                var firstProgression = entMan.GetComponent<WH40KChaosGiftProgressionComponent>(firstLeader);
                var secondProgression = entMan.GetComponent<WH40KChaosGiftProgressionComponent>(secondLeader);

                Assert.Multiple(() =>
                {
                    AssertLeaderState(entMan, cult, firstLeader, WH40KChaosPatron.Khorne, expectedLeader: true);
                    AssertLeaderState(entMan, cult, secondLeader, WH40KChaosPatron.Khorne, expectedLeader: false);
                    Assert.That(firstProgression.PatronLeadershipOrder, Is.LessThan(secondProgression.PatronLeadershipOrder));
                    Assert.That(cult.ResolveActiveLeader(WH40KChaosPatron.Khorne), Is.EqualTo(firstLeader));
                });
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task LeadershipReassignsWhenActiveLeaderLeavesCult()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid departingLeader = default;
        EntityUid successor = default;

        try
        {
            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();

                departingLeader = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(-1f, 0f)), leaderEligible: true);
                successor = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(1f, 0f)), leaderEligible: true);

                AttuneMember(entMan, cult, departingLeader, WH40KChaosPatron.Khorne);
                AttuneMember(entMan, cult, successor, WH40KChaosPatron.Khorne);
            });

            await pair.RunTicksSync(1);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                entMan.DeleteEntity(departingLeader);
            });

            await pair.RunTicksSync(2);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();

                Assert.Multiple(() =>
                {
                    Assert.That(entMan.EntityExists(departingLeader), Is.False);
                    AssertLeaderState(entMan, cult, successor, WH40KChaosPatron.Khorne, expectedLeader: true);
                    Assert.That(cult.ResolveActiveLeader(WH40KChaosPatron.Khorne), Is.EqualTo(successor));
                });
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task DeadLeaderLeavesCultAwaitingSuccessorUntilReplacementAttunes()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid fallenLeader = default;
        EntityUid successor = default;

        try
        {
            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();

                fallenLeader = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(-1f, 0f)), leaderEligible: true);
                AttuneMember(entMan, cult, fallenLeader, WH40KChaosPatron.Khorne);
            });

            await pair.RunTicksSync(1);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mobStateSystem = entMan.System<MobStateSystem>();
                var mobState = entMan.GetComponent<MobStateComponent>(fallenLeader);

                mobStateSystem.ChangeMobState(fallenLeader, MobState.Dead, mobState);
            });

            await pair.RunTicksSync(2);

            await server.WaitAssertion(() =>
            {
                var cult = server.System<WH40KChaosCultSystem>();
                var leaderState = cult.ResolveLeaderState(WH40KChaosPatron.Khorne);

                Assert.Multiple(() =>
                {
                    Assert.That(leaderState.ActiveLeader, Is.Null);
                    Assert.That(leaderState.AwaitingLeaderSuccessor, Is.True);
                });
            });

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();

                successor = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(1f, 0f)), leaderEligible: true);
                AttuneMember(entMan, cult, successor, WH40KChaosPatron.Khorne);
            });

            await pair.RunTicksSync(2);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();
                var leaderState = cult.ResolveLeaderState(WH40KChaosPatron.Khorne);

                Assert.Multiple(() =>
                {
                    AssertLeaderState(entMan, cult, successor, WH40KChaosPatron.Khorne, expectedLeader: true);
                    Assert.That(leaderState.ActiveLeader, Is.EqualTo(successor));
                    Assert.That(leaderState.AwaitingLeaderSuccessor, Is.False);
                });
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task SwitchingPatronPromotesOldCultSuccessorWithoutDisplacingNewCultLeader()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid switchingLeader = default;
        EntityUid khorneSuccessor = default;
        EntityUid nurgleLeader = default;

        try
        {
            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();

                switchingLeader = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(-2f, 0f)), leaderEligible: true);
                khorneSuccessor = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(0f, 0f)), leaderEligible: true);
                nurgleLeader = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(2f, 0f)), leaderEligible: true);

                AttuneMember(entMan, cult, switchingLeader, WH40KChaosPatron.Khorne);
                AttuneMember(entMan, cult, khorneSuccessor, WH40KChaosPatron.Khorne);
                AttuneMember(entMan, cult, nurgleLeader, WH40KChaosPatron.Nurgle);
            });

            await pair.RunTicksSync(1);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();
                AttuneMember(entMan, cult, switchingLeader, WH40KChaosPatron.Nurgle);
            });

            await pair.RunTicksSync(1);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();

                Assert.Multiple(() =>
                {
                    AssertLeaderState(entMan, cult, khorneSuccessor, WH40KChaosPatron.Khorne, expectedLeader: true);
                    AssertLeaderState(entMan, cult, nurgleLeader, WH40KChaosPatron.Nurgle, expectedLeader: true);
                    AssertLeaderState(entMan, cult, switchingLeader, WH40KChaosPatron.Nurgle, expectedLeader: false);
                    Assert.That(cult.ResolveActiveLeader(WH40KChaosPatron.Khorne), Is.EqualTo(khorneSuccessor));
                    Assert.That(cult.ResolveActiveLeader(WH40KChaosPatron.Nurgle), Is.EqualTo(nurgleLeader));
                });
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task PreCreatedCultStateDoesNotRestoreLegacyPassiveXpDefaults()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid leader = default;

        try
        {
            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();

                _ = cult.ResolveLeaderState(WH40KChaosPatron.Khorne);

                leader = SpawnChaosCultist(entMan, map.GridCoords, leaderEligible: true);
                AttuneMember(entMan, cult, leader, WH40KChaosPatron.Khorne);
            });

            await pair.RunTicksSync(1);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var progression = entMan.GetComponent<WH40KChaosGiftProgressionComponent>(leader);

                Assert.Multiple(() =>
                {
                    Assert.That(progression.PassiveXpBasePerTick, Is.EqualTo(1f).Within(0.0001f));
                    Assert.That(progression.PassiveXpPerLevelBonus, Is.EqualTo(0.025f).Within(0.0001f));
                    Assert.That(progression.PassiveXpInterval, Is.EqualTo(TimeSpan.FromMinutes(1)));
                });
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task LeaderGetsFullBranchWhileFollowerReceivesOnlySharedUnlockedGiftActions()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid leader = default;
        EntityUid follower = default;

        var khorneBranchActions = new[]
        {
            "ActionWH40KChaosKhorneRepulse",
            "ActionWH40KChaosKhorneExecutionStep",
            "ActionWH40KChaosKhorneBloodstorm",
        };

        try
        {
            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();

                leader = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(-1f, 0f)), leaderEligible: true);
                follower = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(1f, 0f)), leaderEligible: false);

                AttuneMember(entMan, cult, leader, WH40KChaosPatron.Khorne);
                AttuneMember(entMan, cult, follower, WH40KChaosPatron.Khorne);
            });

            await pair.RunTicksSync(2);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var leaderActions = GetGrantedActionPrototypeIds(entMan, leader);
                var followerActions = GetGrantedActionPrototypeIds(entMan, follower);

                Assert.Multiple(() =>
                {
                    Assert.That(leaderActions, Is.SupersetOf(khorneBranchActions));
                    Assert.That(leaderActions, Does.Contain("ActionWH40KChaosLeaderSacrifice"));

                    Assert.That(followerActions, Does.Not.Contain("ActionWH40KChaosKhorneRepulse"));
                    Assert.That(followerActions, Does.Not.Contain("ActionWH40KChaosKhorneExecutionStep"));
                    Assert.That(followerActions, Does.Not.Contain("ActionWH40KChaosKhorneBloodstorm"));
                    Assert.That(followerActions, Does.Not.Contain("ActionWH40KChaosLeaderSacrifice"));
                });
            });

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();
                var progression = entMan.GetComponent<WH40KChaosGiftProgressionComponent>(leader);

                progression.PrimaryGiftSlot = 1;
                progression.GiftSlotOneUnlocked = true;
                cult.CaptureSharedProgression(leader, progression);
                entMan.Dirty(leader, progression);
            });

            await pair.RunTicksSync(2);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var followerActions = GetGrantedActionPrototypeIds(entMan, follower);

                Assert.Multiple(() =>
                {
                    Assert.That(followerActions, Does.Contain("ActionWH40KChaosKhorneRepulse"));
                    Assert.That(followerActions, Does.Not.Contain("ActionWH40KChaosKhorneExecutionStep"));
                    Assert.That(followerActions, Does.Not.Contain("ActionWH40KChaosKhorneBloodstorm"));
                    Assert.That(followerActions, Does.Not.Contain("ActionWH40KChaosLeaderSacrifice"));
                });
            });

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();
                var progression = entMan.GetComponent<WH40KChaosGiftProgressionComponent>(leader);

                progression.GiftSlotTwoUnlocked = true;
                progression.GiftSlotThreeUnlocked = true;
                cult.CaptureSharedProgression(leader, progression);
                entMan.Dirty(leader, progression);
            });

            await pair.RunTicksSync(2);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var followerActions = GetGrantedActionPrototypeIds(entMan, follower);

                Assert.Multiple(() =>
                {
                    Assert.That(followerActions, Is.SupersetOf(khorneBranchActions));
                    Assert.That(followerActions, Does.Not.Contain("ActionWH40KChaosLeaderSacrifice"));
                });
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task GiftOneExBladeManifestUsesEffectiveLeaderInsteadOfRawExFlag()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid leader = default;
        EntityUid follower = default;

        try
        {
            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var cult = server.System<WH40KChaosCultSystem>();

                leader = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(-1f, 0f)), leaderEligible: true);
                follower = SpawnChaosCultist(entMan, map.GridCoords.Offset(new Vector2(1f, 0f)), leaderEligible: false);

                AttuneMember(entMan, cult, leader, WH40KChaosPatron.Khorne);
                AttuneMember(entMan, cult, follower, WH40KChaosPatron.Khorne);

                var leaderProgression = entMan.GetComponent<WH40KChaosGiftProgressionComponent>(leader);
                leaderProgression.PrimaryGiftSlot = 1;
                leaderProgression.GiftSlotOneUnlocked = true;
                cult.CaptureSharedProgression(leader, leaderProgression);
                entMan.Dirty(leader, leaderProgression);
            });

            await pair.RunTicksSync(2);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();

                var leaderProgression = entMan.GetComponent<WH40KChaosGiftProgressionComponent>(leader);
                leaderProgression.KhorneGiftOneExUnlocked = true;
                entMan.Dirty(leader, leaderProgression);

                var followerProgression = entMan.GetComponent<WH40KChaosGiftProgressionComponent>(follower);
                followerProgression.KhorneGiftOneExUnlocked = true;
                entMan.Dirty(follower, followerProgression);
            });

            await pair.RunTicksSync(1);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var leaderProgression = entMan.GetComponent<WH40KChaosGiftProgressionComponent>(leader);
                var followerProgression = entMan.GetComponent<WH40KChaosGiftProgressionComponent>(follower);

                Assert.Multiple(() =>
                {
                    Assert.That(leaderProgression.EffectiveLeader, Is.True);
                    Assert.That(followerProgression.EffectiveLeader, Is.False);
                    Assert.That(leaderProgression.KhorneGiftOneExUnlocked, Is.True);
                    Assert.That(followerProgression.KhorneGiftOneExUnlocked, Is.True);
                });
            });

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var actions = server.System<SharedActionsSystem>();

                var leaderBladeAction = GetGrantedActionByPrototype(entMan, leader, "ActionWH40KChaosKhorneRepulse");
                var followerBladeAction = GetGrantedActionByPrototype(entMan, follower, "ActionWH40KChaosKhorneRepulse");

                Assert.Multiple(() =>
                {
                    Assert.That(leaderBladeAction, Is.Not.Null);
                    Assert.That(followerBladeAction, Is.Not.Null);
                    Assert.That(actions.TryPerformAction(leader, leaderBladeAction!.Value, null, null, predicted: false), Is.True);
                    Assert.That(actions.TryPerformAction(follower, followerBladeAction!.Value, null, null, predicted: false), Is.True);
                });
            });

            await pair.RunTicksSync(1);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var leaderRuntime = entMan.GetComponent<WH40KChaosKhorneChosenRuntimeComponent>(leader);
                var followerRuntime = entMan.GetComponent<WH40KChaosKhorneChosenRuntimeComponent>(follower);

                Assert.Multiple(() =>
                {
                    Assert.That(leaderRuntime.BladeUid, Is.Not.Null);
                    Assert.That(followerRuntime.BladeUid, Is.Not.Null);
                    Assert.That(GetPrototypeId(entMan, leaderRuntime.BladeUid!.Value), Is.EqualTo("WH40KChaosKhorneStealerBladeEx"));
                    Assert.That(GetPrototypeId(entMan, followerRuntime.BladeUid!.Value), Is.EqualTo("WH40KChaosKhorneStealerBlade"));
                });
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    private static EntityUid SpawnChaosCultist(IEntityManager entMan, EntityCoordinates coordinates, bool leaderEligible)
    {
        var uid = entMan.SpawnEntity(HumanPrototype, coordinates);
        entMan.EnsureComponent<WH40KChaosGiftRoleComponent>(uid);
        entMan.EnsureComponent<WH40KChaosGiftProgressionComponent>(uid);

        if (leaderEligible)
            entMan.EnsureComponent<WH40KChaosLeaderRoleComponent>(uid);

        return uid;
    }

    private static void AttuneMember(
        IEntityManager entMan,
        WH40KChaosCultSystem cult,
        EntityUid uid,
        WH40KChaosPatron patron)
    {
        var progression = entMan.EnsureComponent<WH40KChaosGiftProgressionComponent>(uid);
        var previousPatron = progression.AttunedPatron;

        progression.AllowPatronSwitch = true;
        progression.AttunedPatron = patron;
        progression.PatronSelectionLocked = true;
        progression.StarterSkrizhalIssued = true;

        if (previousPatron != patron && entMan.HasComponent<WH40KChaosLeaderRoleComponent>(uid))
            cult.RegisterLeadershipCandidate(uid, progression);

        cult.AttachMemberToCult(uid, progression, previousPatron);
        entMan.Dirty(uid, progression);
    }

    private static void AssertLeaderState(
        IEntityManager entMan,
        WH40KChaosCultSystem cult,
        EntityUid uid,
        WH40KChaosPatron expectedPatron,
        bool expectedLeader)
    {
        var progression = entMan.GetComponent<WH40KChaosGiftProgressionComponent>(uid);

        Assert.Multiple(() =>
        {
            Assert.That(progression.AttunedPatron, Is.EqualTo(expectedPatron));
            Assert.That(progression.EffectiveLeader, Is.EqualTo(expectedLeader));
            Assert.That(cult.IsEffectiveLeader(uid, progression), Is.EqualTo(expectedLeader));
        });
    }

    private static HashSet<string> GetGrantedActionPrototypeIds(IEntityManager entMan, EntityUid uid)
    {
        var loadout = entMan.GetComponent<WH40KChaosGiftStarterActionLoadoutComponent>(uid);
        var prototypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var actionUid in loadout.GrantedActions)
        {
            if (!entMan.EntityExists(actionUid))
                continue;

            var prototype = entMan.GetComponent<MetaDataComponent>(actionUid).EntityPrototype?.ID;
            if (!string.IsNullOrWhiteSpace(prototype))
                prototypes.Add(prototype);
        }

        return prototypes;
    }

    private static EntityUid? GetGrantedActionByPrototype(IEntityManager entMan, EntityUid uid, string prototypeId)
    {
        var loadout = entMan.GetComponent<WH40KChaosGiftStarterActionLoadoutComponent>(uid);

        foreach (var actionUid in loadout.GrantedActions)
        {
            if (!entMan.EntityExists(actionUid))
                continue;

            if (string.Equals(GetPrototypeId(entMan, actionUid), prototypeId, StringComparison.Ordinal))
                return actionUid;
        }

        return null;
    }

    private static string? GetPrototypeId(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
    }
}
