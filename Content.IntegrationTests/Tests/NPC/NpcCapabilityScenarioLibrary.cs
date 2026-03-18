using System.Collections.Generic;
using System.Numerics;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests.NPC;

internal static class NpcCapabilityScenarioLibrary
{
    public const string AssaultPrototype = "MobWH40KWaveAssault";
    public const string AssaultNoGearPrototype = "MobWH40KWaveAssaultNoGear";
    public const string BreacherPrototype = "MobWH40KWaveBreacher";
    public const string SapperPrototype = "MobWH40KWaveSapper";
    public const string SupportPrototype = "MobWH40KWaveSupport";
    public const string LogisticsPrototype = "MobWH40KWaveLogistics";
    public const string CoordinatorPrototype = "MobWH40KWaveCoordinator";
    public const string TestHereticObjectiveAssaultPrototype = "MobWH40KWaveTestHereticObjectiveAssault";
    public const string TestHereticObjectiveBreacherPrototype = "MobWH40KWaveTestHereticObjectiveBreacher";
    public const string TestHereticObjectiveSupportPrototype = "MobWH40KWaveTestHereticObjectiveSupport";
    public const string TestHereticObjectiveLeaderPrototype = "MobWH40KWaveTestHereticObjectiveLeader";
    public const string TestHereticObjectiveCoordinatorPrototype = "MobWH40KWaveTestHereticObjectiveCoordinator";
    public const string TestHereticObjectiveSapperPrototype = "MobWH40KWaveTestHereticObjectiveSapper";
    public const string TestHereticObjectiveSoldierPrototype = "MobWH40KWaveTestHereticObjectiveSoldier";
    public const string TestHereticObjectiveStrikeSpawnPointPrototype = "SpawnWH40KHereticObjectiveStrikePoint";
    public const string BattlefieldHereticLeaderSpawnPrototype = "SpawnPointHLieutenant";
    public const string BattlefieldHereticCoordinatorSpawnPrototype = "SpawnPointHCommissar";
    public const string BattlefieldHereticSapperSpawnPrototype = "SpawnPointHStationEnginseer";
    public const string BattlefieldHereticSoldierSpawnPrototype = "SpawnPointHGuardsman";
    public const string BattlefieldHereticSoldierAlt1SpawnPrototype = "SpawnPointHSergeant";
    public const string BattlefieldHereticSoldierAlt2SpawnPrototype = "SpawnPointHSpecialistHWS";
    public const string BattlefieldHereticSoldierAlt3SpawnPrototype = "SpawnPointHSpecialistSWS";

    public static readonly IReadOnlyList<WaveRoleExpectation> WaveRoleExpectations = new[]
    {
        new WaveRoleExpectation(
            AssaultPrototype,
            expectedRootTask: "WH40KWaveAssaultRoot",
            navInteract: true,
            navPry: false,
            navSmash: false,
            navClimb: false,
            waveInfluenceEnabled: true,
            waveObjectiveEnabled: true,
            expectedFlags: PathFlags.Interact),
        new WaveRoleExpectation(
            BreacherPrototype,
            expectedRootTask: "WH40KWaveBreacherRoot",
            navInteract: true,
            navPry: true,
            navSmash: true,
            navClimb: false,
            waveInfluenceEnabled: true,
            waveObjectiveEnabled: true,
            expectedFlags: PathFlags.Interact | PathFlags.Prying | PathFlags.Smashing),
        new WaveRoleExpectation(
            SapperPrototype,
            expectedRootTask: "WH40KWaveSapperRoot",
            navInteract: true,
            navPry: false,
            navSmash: false,
            navClimb: true,
            waveInfluenceEnabled: true,
            waveObjectiveEnabled: true,
            expectedFlags: PathFlags.Interact | PathFlags.Climbing),
        new WaveRoleExpectation(
            SupportPrototype,
            expectedRootTask: "WH40KWaveSupportRoot",
            navInteract: true,
            navPry: false,
            navSmash: false,
            navClimb: false,
            waveInfluenceEnabled: true,
            waveObjectiveEnabled: true,
            expectedFlags: PathFlags.Interact),
        new WaveRoleExpectation(
            LogisticsPrototype,
            expectedRootTask: "WH40KWaveLogisticsRoot",
            navInteract: true,
            navPry: false,
            navSmash: false,
            navClimb: false,
            waveInfluenceEnabled: false,
            waveObjectiveEnabled: false,
            expectedFlags: PathFlags.Interact),
        new WaveRoleExpectation(
            CoordinatorPrototype,
            expectedRootTask: "WH40KWaveCoordinatorRoot",
            navInteract: true,
            navPry: false,
            navSmash: false,
            navClimb: false,
            waveInfluenceEnabled: true,
            waveObjectiveEnabled: true,
            expectedFlags: PathFlags.Interact),
    };

    public static EntityUid SpawnAt(
        IEntityManager entMan,
        Entity<MapGridComponent> grid,
        string prototype,
        float x,
        float y)
    {
        return entMan.SpawnEntity(prototype, new EntityCoordinates(grid.Owner, x, y));
    }

    public static List<EntityUid> SpawnSwarm(
        IEntityManager entMan,
        Entity<MapGridComponent> grid,
        string prototype,
        int count,
        Vector2 origin,
        int columns,
        float spacing)
    {
        var spawned = new List<EntityUid>(count);

        for (var i = 0; i < count; i++)
        {
            var x = origin.X + (i % columns) * spacing;
            var y = origin.Y + (i / columns) * spacing;
            spawned.Add(SpawnAt(entMan, grid, prototype, x, y));
        }

        return spawned;
    }
}

internal sealed class WaveRoleExpectation
{
    public readonly string PrototypeId;
    public readonly string ExpectedRootTask;
    public readonly bool NavInteract;
    public readonly bool NavPry;
    public readonly bool NavSmash;
    public readonly bool NavClimb;
    public readonly bool WaveInfluenceEnabled;
    public readonly bool WaveObjectiveEnabled;
    public readonly PathFlags ExpectedFlags;

    public WaveRoleExpectation(
        string prototypeId,
        string expectedRootTask,
        bool navInteract,
        bool navPry,
        bool navSmash,
        bool navClimb,
        bool waveInfluenceEnabled,
        bool waveObjectiveEnabled,
        PathFlags expectedFlags)
    {
        PrototypeId = prototypeId;
        ExpectedRootTask = expectedRootTask;
        NavInteract = navInteract;
        NavPry = navPry;
        NavSmash = navSmash;
        NavClimb = navClimb;
        WaveInfluenceEnabled = waveInfluenceEnabled;
        WaveObjectiveEnabled = waveObjectiveEnabled;
        ExpectedFlags = expectedFlags;
    }
}
