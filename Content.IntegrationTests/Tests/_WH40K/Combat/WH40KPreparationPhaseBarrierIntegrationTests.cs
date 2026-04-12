#nullable enable

using System;
using System.Numerics;
using Content.IntegrationTests.Tests.Movement;
using Content.Server._WH40K.Combat;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Content.Shared._WH40K.GameMode;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WH40K.Combat;

[TestOf(typeof(WH40KTdmWarningBarrierSystem))]
public sealed class WH40KPreparationPhaseBarrierIntegrationTests : MovementTest
{
    private const string StrapDummyPrototype = "WH40KBarrierStrapDummy";
    private const string InvisibleBarrierPrototype = "WH40KWallInvisibleTimedTDM";
    private const string WarningBarrierPrototype = "WH40KWallWarningTDM";

    [TestPrototypes]
    private const string TestPrototypes = @"
- type: entity
  id: WH40KBarrierStrapDummy
  components:
  - type: Strap
";

    [Test]
    public async Task WarningBarrierBlocksMovementUntilAssaultPhase()
    {
        await AssertBarrierBlocksUntilAssaultPhase(WarningBarrierPrototype);
    }

    [Test]
    public async Task InvisibleBarrierBlocksMovementUntilAssaultPhase()
    {
        await AssertBarrierBlocksUntilAssaultPhase(InvisibleBarrierPrototype);
    }

    [Test]
    public async Task WarningBarrierBlocksSlowWalkingMovementUntilAssaultPhase()
    {
        await AssertBarrierBlocksUntilAssaultPhase(WarningBarrierPrototype, blockedMoveSeconds: 1f, postAssaultMoveSeconds: 1.25f, walk: true);
    }

    [Test]
    public async Task WarningBarrierBlocksBoostedMovementUntilAssaultPhase()
    {
        await SetPlayerBaseSpeed(10f, 10f, 80f);
        await AssertBarrierBlocksUntilAssaultPhase(WarningBarrierPrototype, blockedMoveSeconds: 0.35f, postAssaultMoveSeconds: 0.35f);
    }

    [Test]
    public async Task WarningBarrierBlocksDirectTeleportUntilAssaultPhase()
    {
        await AssertDirectTeleportBlocksUntilAssaultPhase(WarningBarrierPrototype);
    }

    [Test]
    public async Task InvisibleBarrierBlocksDirectTeleportUntilAssaultPhase()
    {
        await AssertDirectTeleportBlocksUntilAssaultPhase(InvisibleBarrierPrototype);
    }

    [Test]
    public async Task WarningBarrierBlocksBuckleAttemptAcrossFrontlineUntilAssaultPhase()
    {
        await SpawnTarget(WarningBarrierPrototype);

        var serverPlayer = ToServer(Player);
        var strapCoords = ToServer(TargetCoords);
        EntityUid strap = default;
        BuckleComponent buckle = null!;
        StrapComponent strapComp = null!;

        await Server.WaitAssertion(() =>
        {
            strap = SEntMan.SpawnEntity(StrapDummyPrototype, strapCoords);
            buckle = SEntMan.GetComponent<BuckleComponent>(serverPlayer);
            strapComp = SEntMan.GetComponent<StrapComponent>(strap);

            Assert.That(buckle, Is.Not.Null, "Player is missing BuckleComponent.");
            Assert.That(strapComp, Is.Not.Null, "Strap dummy is missing StrapComponent.");

#pragma warning disable RA0002
            buckle.Delay = TimeSpan.Zero;
#pragma warning restore RA0002

            var buckleSystem = SEntMan.System<SharedBuckleSystem>();
            Assert.That(buckleSystem.TryBuckle(serverPlayer, serverPlayer, strap, buckleComp: buckle), Is.False,
                "Player should not be able to buckle across the WH40K preparation barrier.");
            Assert.That(buckle.Buckled, Is.False, "Player unexpectedly became buckled across the barrier.");
            Assert.That(strapComp.BuckledEntities, Is.Empty, "Strap unexpectedly contains a buckled player during preparation.");
        });

        await AdvanceToAssaultPhase();

        await Server.WaitAssertion(() =>
        {
            var buckleSystem = SEntMan.System<SharedBuckleSystem>();
            Assert.That(buckleSystem.TryBuckle(serverPlayer, serverPlayer, strap, buckleComp: buckle), Is.True,
                "Player should be able to buckle once the WH40K preparation barrier is removed.");
            Assert.That(buckle.BuckledTo, Is.EqualTo(strap), "Player buckled to the wrong strap after the barrier was removed.");
            Assert.That(strapComp.BuckledEntities, Does.Contain(serverPlayer), "Strap should contain the player after a successful buckle.");
        });
    }

    [Test]
    public async Task WarningBarrierBlocksBuckledStrapMovementUntilAssaultPhase()
    {
        await SpawnTarget(WarningBarrierPrototype);

        var serverPlayer = ToServer(Player);
        var startCoords = MapData.GridCoords.Offset(new Vector2(0.5f, 0.5f));
        var blockedDestination = MapData.GridCoords.Offset(new Vector2(2.5f, 0.5f));
        EntityUid strap = default;
        BuckleComponent buckle = null!;
        Vector2 blockedPlayerPosition = default;
        Vector2 blockedStrapPosition = default;

        await Server.WaitAssertion(() =>
        {
            strap = SEntMan.SpawnEntity(StrapDummyPrototype, startCoords);
            buckle = SEntMan.GetComponent<BuckleComponent>(serverPlayer);

            Assert.That(buckle, Is.Not.Null, "Player is missing BuckleComponent.");

#pragma warning disable RA0002
            buckle.Delay = TimeSpan.Zero;
#pragma warning restore RA0002

            var buckleSystem = SEntMan.System<SharedBuckleSystem>();
            Assert.That(buckleSystem.TryBuckle(serverPlayer, serverPlayer, strap, buckleComp: buckle), Is.True,
                "Player should buckle to the strap on the same side of the barrier.");
        });

        await Server.WaitPost(() =>
        {
            Transform.SetCoordinates(strap, blockedDestination);
        });

        await Server.WaitAssertion(() =>
        {
            blockedPlayerPosition = Transform.GetWorldPosition(serverPlayer);
            blockedStrapPosition = Transform.GetWorldPosition(strap);
            var barrierPosition = Transform.ToWorldPosition(ToServer(TargetCoords));

            Assert.That(buckle.BuckledTo, Is.EqualTo(strap), "Player should remain buckled after the strap is blocked by the barrier.");
            Assert.That(blockedPlayerPosition.X, Is.LessThan(barrierPosition.X),
                "Buckled player crossed the WH40K preparation barrier with the strap during preparation.");
            Assert.That(blockedStrapPosition.X, Is.LessThan(barrierPosition.X),
                "Strap crossed the WH40K preparation barrier during preparation.");
        });

        await AdvanceToAssaultPhase();

        await Server.WaitPost(() =>
        {
            Transform.SetCoordinates(strap, blockedDestination);
        });

        await Server.WaitAssertion(() =>
        {
            var postAssaultPlayerPosition = Transform.GetWorldPosition(serverPlayer);
            var postAssaultStrapPosition = Transform.GetWorldPosition(strap);
            Assert.That(postAssaultPlayerPosition.X, Is.GreaterThan(blockedPlayerPosition.X + 0.5f),
                "Buckled player did not move across the former barrier line after assault began.");
            Assert.That(postAssaultStrapPosition.X, Is.GreaterThan(blockedStrapPosition.X + 0.5f),
                "Strap did not move across the former barrier line after assault began.");
        });
    }

    [Test]
    public async Task WarningBarrierBlocksDownedDirectTeleportUntilAssaultPhase()
    {
        await AssertDirectTeleportBlocksUntilAssaultPhase(WarningBarrierPrototype, downPlayer: true);
    }

    private async Task AssertBarrierBlocksUntilAssaultPhase(
        string barrierPrototype,
        float blockedMoveSeconds = 0.5f,
        float postAssaultMoveSeconds = 1f,
        bool walk = false)
    {
        await SpawnTarget(barrierPrototype);

        AssertLocation(Target, TargetCoords);
        Assert.That(Delta(), Is.GreaterThan(0.5f), "Player did not start west of the WH40K barrier.");

        var beforeMove = Transform.GetWorldPosition(ToServer(Player));
        await SetWalkState(walk, true);
        await Move(DirectionFlag.East, blockedMoveSeconds);
        await SetWalkState(walk, false);

        var afterBlockedMove = Transform.GetWorldPosition(ToServer(Player));
        Assert.That(Delta(), Is.GreaterThan(0.5f), "Player crossed the WH40K preparation barrier during preparation.");
        Assert.That(Vector2.Distance(beforeMove, afterBlockedMove), Is.LessThan(1.5f), "Player was displaced too far when the WH40K barrier blocked movement.");
        AssertExists(Target);

        await AdvanceToAssaultPhase();

        await SetWalkState(walk, true);
        await Move(DirectionFlag.East, postAssaultMoveSeconds);
        await SetWalkState(walk, false);
        Assert.That(DeltaCoordinates(), Is.LessThan(-0.5f), "Player could not cross after the WH40K preparation barrier was removed.");
    }

    private async Task AssertDirectTeleportBlocksUntilAssaultPhase(string barrierPrototype, bool downPlayer = false)
    {
        await SpawnTarget(barrierPrototype);

        var serverPlayer = ToServer(Player);
        var blockedDestination = MapData.GridCoords.Offset(new Vector2(2.5f, 0.5f));
        var barrierPosition = Transform.ToWorldPosition(ToServer(TargetCoords));
        Vector2 blockedPlayerPosition = default;

        if (downPlayer)
        {
            await Server.WaitAssertion(() =>
            {
                var standingState = SEntMan.System<StandingStateSystem>();
                Assert.That(standingState.Down(serverPlayer), Is.True,
                    "Player should be knockable down for the WH40K barrier downed teleport test.");
            });
        }

        await Server.WaitPost(() =>
        {
            Transform.SetCoordinates(serverPlayer, blockedDestination);
        });

        await Server.WaitAssertion(() =>
        {
            blockedPlayerPosition = Transform.GetWorldPosition(serverPlayer);
            Assert.That(blockedPlayerPosition.X, Is.LessThan(barrierPosition.X),
                $"Direct coordinate movement crossed the WH40K preparation barrier for prototype {barrierPrototype}.");
            AssertExists(Target);
        });

        await AdvanceToAssaultPhase();

        await Server.WaitPost(() =>
        {
            Transform.SetCoordinates(serverPlayer, blockedDestination);
        });

        await Server.WaitAssertion(() =>
        {
            var postAssaultPlayerPosition = Transform.GetWorldPosition(serverPlayer);
            Assert.That(postAssaultPlayerPosition.X, Is.GreaterThan(blockedPlayerPosition.X + 0.5f),
                $"Direct coordinate movement did not cross the former barrier line after assault began for prototype {barrierPrototype}.");
        });
    }

    private async Task SetPlayerBaseSpeed(float baseWalkSpeed, float baseSprintSpeed, float acceleration)
    {
        await Server.WaitPost(() =>
        {
            var serverPlayer = ToServer(Player);
            var movementSpeed = SEntMan.System<MovementSpeedModifierSystem>();
            var modifier = SEntMan.GetComponent<MovementSpeedModifierComponent>(serverPlayer);
            movementSpeed.ChangeBaseSpeed(serverPlayer, baseWalkSpeed, baseSprintSpeed, acceleration, modifier);
            movementSpeed.RefreshMovementSpeedModifiers(serverPlayer, modifier);
        });

        await RunTicks(2);
    }

    private async Task SetWalkState(bool walking, bool enabled)
    {
        if (!walking)
            return;

        await SetKey(EngineKeyFunctions.Walk, enabled ? BoundKeyState.Down : BoundKeyState.Up);
    }

    private async Task AdvanceToAssaultPhase()
    {
        Assert.That(Target, Is.Not.Null, "Barrier target was not spawned.");
        var target = Target!.Value;

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(SEntMan.GetEntity(target), new WH40KBattlePhaseChangedEvent(
                EntityUid.Invalid,
                WH40KBattlePhase.Preparation,
                WH40KBattlePhase.Assault), true);
        });

        await RunTicks(10);
        AssertDeleted(Target);
    }
}
