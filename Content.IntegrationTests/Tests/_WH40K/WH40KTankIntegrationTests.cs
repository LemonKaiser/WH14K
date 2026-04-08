using System;
using System.Collections.Generic;
#pragma warning disable CS0618 // GetTotalDamage: used in test assertions; no alternative API for these checks
using System.Linq;
using System.Numerics;
using Content.Server._WH40K.Tank;
using Content.Shared._WH40K.Tank;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class WH40KTankIntegrationTests
{
    private const string TankPrototype = "WH40KLemanRussTank";
    private const string PunisherTankPrototype = "WH40KLemanRussPunisherTank";
    private const string HumanPrototype = "MobHuman";
    private const string DiagnosticsVerbText = "Open diagnostics";
    private const string BattleCannonAmmoProto = "CartridgeRocketHE";
    private const string CoaxialAmmoProto = "CartridgeHeavyBolter";
    private const string PunisherAmmoProto = "CartridgeLightRifle";
    private const string BluntDamageType = "Blunt";

    [Test]
    public async Task BoardingAssignsCrewInOrderAndEngineStartStopVerbsWork()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid tank = default;
        EntityUid driver = default;
        EntityUid gunner = default;
        EntityUid commander = default;
        EntityUid loader = default;
        EntityUid extraCrew = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();

            tank = entMan.SpawnEntity(TankPrototype, map.GridCoords);
            driver = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 0f)));
            gunner = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 1f)));
            commander = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 2f)));
            loader = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 3f)));
            extraCrew = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 4f)));

            BoardTank(server, tank, driver, gunner, commander, loader, extraCrew);

            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);
            Assert.Multiple(() =>
            {
                Assert.That(tankComp.DriverOccupant, Is.EqualTo(driver));
                Assert.That(tankComp.GunnerOccupant, Is.EqualTo(gunner));
                Assert.That(tankComp.CommanderOccupant, Is.EqualTo(commander));
                Assert.That(tankComp.LoaderOccupant, Is.EqualTo(loader));
                Assert.That(entMan.GetComponent<BuckleComponent>(extraCrew).Buckled, Is.False,
                    "A fifth crew member should not be able to enter a full tank.");
                Assert.That(entMan.TryGetComponent<RelayInputMoverComponent>(driver, out var relay), Is.True,
                    "Driver should gain an input relay when boarding the tank.");
                Assert.That(relay!.RelayEntity, Is.EqualTo(tank));
            });

            AssertTankGrantedActionsUseHudIcons(entMan, tankComp);

            var engine = entMan.GetComponent<WH40KTankEngineComponent>(tank);
            ExecuteEngineVerb(entMan, tank, driver);
            Assert.That(engine.State, Is.EqualTo(WH40KTankEngineState.Running),
                "Driver start verb did not start the tank engine.");

            ExecuteEngineVerb(entMan, tank, driver);
            Assert.That(engine.State, Is.EqualTo(WH40KTankEngineState.Off),
                "Driver stop verb did not stop the tank engine.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BoardingAndExitingRequireDelay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid tank = default;
        EntityUid driver = default;
        TimeSpan entryStartedAt = TimeSpan.Zero;
        TimeSpan exitStartedAt = TimeSpan.Zero;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

            tank = entMan.SpawnEntity(TankPrototype, map.GridCoords);
            driver = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-0.3f, 1.4f)));

            entMan.EventBus.RaiseLocalEvent(tank, new InteractHandEvent(driver, tank));
            entryStartedAt = timing.CurTime;

            Assert.That(entMan.GetComponent<BuckleComponent>(driver).Buckled, Is.False,
                "Tank boarding should not complete immediately.");
        });

        while (true)
        {
            TimeSpan now = default;
            await server.WaitAssertion(() => now = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime);
            if (now - entryStartedAt >= TimeSpan.FromSeconds(3.5))
                break;

            await pair.RunTicksSync(1);
        }

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            Assert.That(entMan.GetComponent<BuckleComponent>(driver).Buckled, Is.False,
                "Tank boarding completed too early.");
        });

        while (true)
        {
            TimeSpan now = default;
            await server.WaitAssertion(() => now = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime);
            if (now - entryStartedAt >= TimeSpan.FromSeconds(5.0))
                break;

            await pair.RunTicksSync(1);
        }

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<BuckleComponent>(driver).Buckled, Is.True,
                    "Tank boarding did not complete after the expected delay.");
                Assert.That(tankComp.DriverOccupant, Is.EqualTo(driver));
            });
        });

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();
            entMan.EventBus.RaiseLocalEvent(tank, new InteractHandEvent(driver, tank));
            exitStartedAt = timing.CurTime;
        });

        while (true)
        {
            TimeSpan now = default;
            await server.WaitAssertion(() => now = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime);
            if (now - exitStartedAt >= TimeSpan.FromSeconds(3.5))
                break;

            await pair.RunTicksSync(1);
        }

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            Assert.That(entMan.GetComponent<BuckleComponent>(driver).Buckled, Is.True,
                "Tank exit completed too early.");
        });

        while (true)
        {
            TimeSpan now = default;
            await server.WaitAssertion(() => now = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime);
            if (now - exitStartedAt >= TimeSpan.FromSeconds(5.0))
                break;

            await pair.RunTicksSync(1);
        }

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<BuckleComponent>(driver).Buckled, Is.False,
                    "Tank exit did not complete after the expected delay.");
                Assert.That(tankComp.DriverOccupant, Is.Null);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EntryVerbAllowsSoloBoardingAsGunner()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid tank = default;
        EntityUid gunner = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var actionSystem = server.System<SharedActionsSystem>();

            tank = entMan.SpawnEntity(TankPrototype, map.GridCoords);
            gunner = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 0f)));

            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);
            var gunnerEntryVerbText = GetEntryVerbText(server, WH40KTankCrewRole.Gunner);

            PrepareTankForImmediateBoarding(tankComp);

            ExecuteEntryVerb(entMan, tank, gunner, gunnerEntryVerbText);

            Assert.Multiple(() =>
            {
                Assert.That(tankComp.GunnerOccupant, Is.EqualTo(gunner),
                    "The explicit gunner entry verb should place a solo tester into the gunner station.");
                Assert.That(tankComp.DriverOccupant, Is.Null,
                    "Choosing the gunner entry verb should not also occupy the driver station.");
                Assert.That(entMan.GetComponent<BuckleComponent>(gunner).Buckled, Is.True,
                    "The gunner should be buckled into the selected tank station after using the entry verb.");
                Assert.That(CountWorldTargetActionsWithEvent<WH40KTankAimActionEvent>(entMan, actionSystem, gunner), Is.EqualTo(0),
                    "The gunner should no longer receive the legacy tank aim button after the direct mouse-aim rewrite.");
                Assert.That(CountInstantActionsWithEvent<WH40KTankFireMainGunActionEvent>(entMan, actionSystem, gunner), Is.EqualTo(0),
                    "The gunner should no longer receive the legacy main-gun fire button after the direct mouse-fire rewrite.");
                Assert.That(CountInstantActionsWithEvent<WH40KTankFireCoaxialActionEvent>(entMan, actionSystem, gunner), Is.EqualTo(1),
                    "The gunner should still retain supported tank actions like coaxial fire.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClientGunnerSeatReplicationMatchesCurrentTankControlModel()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            DummyTicker = false,
            Dirty = true,
            Fresh = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();

        EntityUid tank = default;
        EntityUid serverPlayer = default;
        NetEntity tankNet = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var transform = server.System<SharedTransformSystem>();
            var playerSession = server.ResolveDependency<Robust.Server.Player.IPlayerManager>().Sessions.Single();

            Assert.That(playerSession.AttachedEntity, Is.Not.Null,
                "The connected integration client should control an attached mob before boarding the tank.");

            serverPlayer = playerSession.AttachedEntity!.Value;
            PrepareCrewForImmediateBoarding(entMan, serverPlayer);

            tank = entMan.SpawnEntity(TankPrototype, map.GridCoords);
            tankNet = entMan.GetNetEntity(tank);
            transform.SetCoordinates(serverPlayer, map.GridCoords.Offset(new Vector2(-1f, 0f)));

            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);
            PrepareTankForImmediateBoarding(tankComp);
            ExecuteEntryVerb(entMan, tank, serverPlayer, GetEntryVerbText(server, WH40KTankCrewRole.Gunner));

            Assert.That(tankComp.GunnerOccupant, Is.EqualTo(serverPlayer),
                "The attached player should occupy the gunner seat after using the explicit gunner entry verb.");
        });

        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            Assert.That(client.AttachedEntity, Is.Not.Null,
                "The connected client should still have an attached entity after boarding the tank.");

            var entMan = client.ResolveDependency<IEntityManager>();
            var actionSystem = client.System<SharedActionsSystem>();
            var clientPlayer = client.AttachedEntity!.Value;
            var buckle = entMan.GetComponent<BuckleComponent>(clientPlayer);

            Assert.That(buckle.BuckledTo, Is.Not.Null,
                "The client player should replicate as buckled into a tank station after boarding as gunner.");

            var station = buckle.BuckledTo!.Value;
            var stationComp = entMan.GetComponent<WH40KTankStationComponent>(station);
            var clientTank = entMan.GetEntity(tankNet);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.EntityExists(clientTank), Is.True,
                    "The boarded tank should exist on the client so the tank input system can resolve it from the replicated station state.");
                Assert.That(stationComp.Role, Is.EqualTo(WH40KTankCrewRole.Gunner),
                    "The client should replicate the occupied station as the gunner role.");
                Assert.That(stationComp.Tank, Is.EqualTo(clientTank),
                    "The client should replicate the owning tank on the occupied gunner station.");
                Assert.That(CountWorldTargetActionsWithEvent<WH40KTankAimActionEvent>(entMan, actionSystem, clientPlayer), Is.EqualTo(0),
                    "The client gunner should not receive the removed tank aim action button.");
                Assert.That(CountInstantActionsWithEvent<WH40KTankFireMainGunActionEvent>(entMan, actionSystem, clientPlayer), Is.EqualTo(0),
                    "The client gunner should not receive the removed main-gun action button.");
                Assert.That(CountInstantActionsWithEvent<WH40KTankFireCoaxialActionEvent>(entMan, actionSystem, clientPlayer), Is.EqualTo(1),
                    "The client gunner should still receive supported tank actions like coaxial fire.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClientPredictiveTankAimAndFireRequestsControlTheServerMainGun()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            DummyTicker = false,
            Dirty = true,
            Fresh = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();

        EntityUid tank = default;
        EntityUid mainGun = default;
        MapCoordinates clientTarget = MapCoordinates.Nullspace;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var transform = server.System<SharedTransformSystem>();
            var playerSession = server.ResolveDependency<Robust.Server.Player.IPlayerManager>().Sessions.Single();

            Assert.That(playerSession.AttachedEntity, Is.Not.Null,
                "The connected integration client should control an attached mob before testing predictive tank input.");

            var serverPlayer = playerSession.AttachedEntity!.Value;
            PrepareCrewForImmediateBoarding(entMan, serverPlayer);

            tank = entMan.SpawnEntity(TankPrototype, map.GridCoords);
            transform.SetCoordinates(serverPlayer, map.GridCoords.Offset(new Vector2(-1f, 0f)));

            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);
            PrepareTankForImmediateBoarding(tankComp);
            ExecuteEntryVerb(entMan, tank, serverPlayer, GetEntryVerbText(server, WH40KTankCrewRole.Gunner));

            mainGun = tankComp.MainGun!.Value;

            Assert.That(server.System<SharedGunSystem>().GetAmmoCount(mainGun), Is.EqualTo(1),
                "The tank main gun should start loaded so the predictive fire request can consume the shell.");
        });

        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            Assert.That(client.AttachedEntity, Is.Not.Null,
                "The connected client should have an attached entity before sending predictive tank input.");

            var entMan = client.ResolveDependency<IEntityManager>();
            var transform = client.System<SharedTransformSystem>();
            var buckle = entMan.GetComponent<BuckleComponent>(client.AttachedEntity!.Value);

            Assert.That(buckle.BuckledTo, Is.Not.Null,
                "The client player should replicate as seated before the predictive tank input is sent.");

            var stationComp = entMan.GetComponent<WH40KTankStationComponent>(buckle.BuckledTo!.Value);
            Assert.That(stationComp.Tank, Is.Not.Null,
                "The occupied station should replicate its owning tank so the client can derive the current gunner context.");

            var clientTank = stationComp.Tank.Value;
            var tankComp = entMan.GetComponent<WH40KTankComponent>(clientTank);
            clientTarget = GetForwardAimTarget(entMan, transform, clientTank, tankComp);
        });

        await client.WaitPost(() =>
        {
            var entMan = client.ResolveDependency<IEntityManager>();
            entMan.RaisePredictiveEvent(new WH40KTankAimRequestEvent(clientTarget));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var transform = server.System<SharedTransformSystem>();
            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);
            var expectedTarget = GetForwardAimTarget(entMan, transform, tank, tankComp);

            Assert.Multiple(() =>
            {
                Assert.That(tankComp.AimTarget.MapId, Is.EqualTo(expectedTarget.MapId),
                    "The predictive aim request should update the server tank target onto the current map.");
                Assert.That((tankComp.AimTarget.Position - expectedTarget.Position).Length(), Is.LessThan(0.05f),
                    "The predictive aim request should set the same forward target the client aimed at.");
            });
        });

        await client.WaitPost(() =>
        {
            var entMan = client.ResolveDependency<IEntityManager>();
            entMan.RaisePredictiveEvent(new WH40KTankFireMainGunRequestEvent(clientTarget));
        });

        await pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var gunSystem = server.System<SharedGunSystem>();
            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);

            Assert.Multiple(() =>
            {
                Assert.That(gunSystem.GetAmmoCount(mainGun), Is.EqualTo(0),
                    "The predictive main-gun fire request should consume the loaded shell on the server.");
                Assert.That(tankComp.PendingMainGunFire, Is.False,
                    "The predictive main-gun fire request should resolve instead of leaving the weapon permanently queued.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TankMovesTenTilesThenBrakesToIdle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid tank = default;
        EntityUid driver = default;
        Vector2 start = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var transform = server.System<SharedTransformSystem>();

            tank = entMan.SpawnEntity(TankPrototype, map.GridCoords);
            driver = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 0f)));

            BoardTank(server, tank, driver);
            ExecuteEngineVerb(entMan, tank, driver);

            var mover = entMan.GetComponent<InputMoverComponent>(tank);
            mover.HeldMoveButtons = MoveButtons.Up;
            start = transform.GetMapCoordinates(tank).Position;
        });

        var reachedDistance = false;
        for (var i = 0; i < 240; i++)
        {
            await pair.RunTicksSync(1);

            await server.WaitAssertion(() =>
            {
                var transform = server.System<SharedTransformSystem>();
                var current = transform.GetMapCoordinates(tank).Position;
                if ((current - start).Length() >= 9.5f)
                    reachedDistance = true;
            });

            if (reachedDistance)
                break;
        }

        Assert.That(reachedDistance, Is.True, "Tank failed to travel at least ten tiles under engine power.");

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            entMan.GetComponent<InputMoverComponent>(tank).HeldMoveButtons = MoveButtons.None;
        });

        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var physics = entMan.GetComponent<PhysicsComponent>(tank);
            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);

            Assert.Multiple(() =>
            {
                Assert.That(physics.LinearVelocity.Length(), Is.LessThan(0.05f),
                    "Tank did not brake to a near-stop after movement input was released.");
                Assert.That(Math.Abs(physics.AngularVelocity), Is.LessThan(0.05f),
                    "Tank kept rotating after movement input was released.");
                Assert.That(tankComp.TrackVisualState, Is.EqualTo(WH40KTankVisualState.Idle));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BattleCannonFireEmptyAndReloadCycleWorks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid tank = default;
        EntityUid gunner = default;
        EntityUid loader = default;
        EntityUid mainGun = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var gunSystem = server.System<SharedGunSystem>();

            tank = entMan.SpawnEntity(TankPrototype, map.GridCoords);
            gunner = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 0f)));
            loader = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 1f)));

            // Consume driver and commander seats so gunner/loader land in their proper stations.
            var driver = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-2f, 0f)));
            var commander = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-2f, 1f)));
            BoardTank(server, tank, driver, gunner, commander, loader);

            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);
            mainGun = tankComp.MainGun!.Value;

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<BasicEntityAmmoProviderComponent>(mainGun).Proto, Is.EqualTo(BattleCannonAmmoProto));
                Assert.That(gunSystem.GetAmmoCount(mainGun), Is.EqualTo(1));
            });

            AimForward(server, tank, gunner);
            FireMainGun(entMan, tank, gunner);
        });

        await pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            var gunSystem = server.System<SharedGunSystem>();
            Assert.That(gunSystem.GetAmmoCount(mainGun), Is.EqualTo(0),
                "Main gun did not consume its single loaded shell after firing.");
        });

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            FireMainGun(entMan, tank, gunner);
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);
            var gunSystem = server.System<SharedGunSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(gunSystem.GetAmmoCount(mainGun), Is.EqualTo(0),
                    "Dry-firing the empty main gun should not create ammo or consume negative ammo.");
                Assert.That(tankComp.PendingMainGunFire, Is.False,
                    "Empty main gun fire request should clear instead of staying queued forever.");
            });
        });

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            ReloadMainGun(entMan, tank, loader);
        });

        await pair.RunTicksSync(180);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);
            var gunSystem = server.System<SharedGunSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(gunSystem.GetAmmoCount(mainGun), Is.EqualTo(1),
                    "Loader reload action did not restore battle cannon ammo to full.");
                Assert.That(tankComp.MainGunReloadCompleteAt, Is.EqualTo(TimeSpan.Zero),
                    "Main gun reload timer should be cleared once reload is complete.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MainGunShotDoesNotDamageCrewInsideTank()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid tank = default;
        EntityUid gunner = default;
        EntityUid mainGun = default;
        EntityUid[] crew = Array.Empty<EntityUid>();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var gunSystem = server.System<SharedGunSystem>();

            tank = entMan.SpawnEntity(TankPrototype, map.GridCoords);
            crew =
            [
                SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 0f))),
                SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 1f))),
                SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 2f))),
                SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 3f))),
            ];

            gunner = crew[1];
            BoardTank(server, tank, crew);

            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);
            mainGun = tankComp.MainGun!.Value;

            Assert.That(gunSystem.GetAmmoCount(mainGun), Is.EqualTo(1),
                "The main gun should start loaded so the crew-damage regression check validates a real shot.");

            AimForward(server, tank, gunner);
            FireMainGun(entMan, tank, gunner);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var damageable = server.System<DamageableSystem>();
            var gunSystem = server.System<SharedGunSystem>();

            Assert.That(gunSystem.GetAmmoCount(mainGun), Is.EqualTo(0),
                "The main gun should actually fire so the crew-damage regression check is meaningful.");

            foreach (var crewMember in crew)
            {
                Assert.That(damageable.GetTotalDamage(crewMember), Is.EqualTo(FixedPoint2.Zero),
                    "Tank crew should not take damage from their own forward main-gun shot while seated inside the hull.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoaxialAndPunisherUseExpectedAmmoAndConsumeItWhenFired()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid lemanRuss = default;
        EntityUid punisher = default;
        EntityUid coaxialGun = default;
        EntityUid punisherGun = default;
        EntityUid coaxialGunner = default;
        EntityUid punisherGunner = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var gunSystem = server.System<SharedGunSystem>();

            lemanRuss = entMan.SpawnEntity(TankPrototype, map.GridCoords.Offset(new Vector2(0f, 0f)));
            punisher = entMan.SpawnEntity(PunisherTankPrototype, map.GridCoords.Offset(new Vector2(0f, 8f)));

            var lemanDriver = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 0f)));
            coaxialGunner = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 1f)));
            BoardTank(server, lemanRuss, lemanDriver, coaxialGunner);

            var punisherDriver = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 8f)));
            punisherGunner = SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 9f)));
            BoardTank(server, punisher, punisherDriver, punisherGunner);

            var lemanComp = entMan.GetComponent<WH40KTankComponent>(lemanRuss);
            var punisherComp = entMan.GetComponent<WH40KTankComponent>(punisher);
            coaxialGun = lemanComp.CoaxialGun!.Value;
            punisherGun = punisherComp.MainGun!.Value;

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<BasicEntityAmmoProviderComponent>(coaxialGun).Proto, Is.EqualTo(CoaxialAmmoProto));
                Assert.That(entMan.GetComponent<BasicEntityAmmoProviderComponent>(punisherGun).Proto, Is.EqualTo(PunisherAmmoProto));
                Assert.That(gunSystem.GetAmmoCount(coaxialGun), Is.EqualTo(60));
                Assert.That(gunSystem.GetAmmoCount(punisherGun), Is.EqualTo(90));
            });

            AimForward(server, lemanRuss, coaxialGunner);
            AimForward(server, punisher, punisherGunner);
            FireCoaxial(entMan, lemanRuss, coaxialGunner);
            FireMainGun(entMan, punisher, punisherGunner);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var gunSystem = server.System<SharedGunSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(gunSystem.GetAmmoCount(coaxialGun), Is.EqualTo(59),
                    "Coaxial gun should consume one heavy-bolter round when fired.");
                Assert.That(gunSystem.GetAmmoCount(punisherGun), Is.EqualTo(89),
                    "Punisher gun should consume one L6-SAW-style light-rifle round when fired.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DestroyingOccupiedTankDeletesVehicleAndLeavesCrewAliveAndUnbuckled()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid tank = default;
        EntityUid[] crew = Array.Empty<EntityUid>();
        EntityUid[] children = Array.Empty<EntityUid>();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();

            tank = entMan.SpawnEntity(TankPrototype, map.GridCoords);
            crew =
            [
                SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 0f))),
                SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 1f))),
                SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 2f))),
                SpawnCrew(entMan, map.GridCoords.Offset(new Vector2(-1f, 3f))),
            ];

            BoardTank(server, tank, crew);

            var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);
            children =
            [
                tankComp.Turret!.Value,
                tankComp.MainHardpoint!.Value,
                tankComp.MainGun!.Value,
                tankComp.CoaxialHardpoint!.Value,
                tankComp.CoaxialGun!.Value,
                tankComp.DriverStation!.Value,
                tankComp.GunnerStation!.Value,
                tankComp.CommanderStation!.Value,
                tankComp.LoaderStation!.Value,
            ];

            var damageable = server.System<DamageableSystem>();
            var blunt = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>(BluntDamageType);
            Assert.That(damageable.TryChangeDamage(tank, new DamageSpecifier(blunt, 400), ignoreResistances: true), Is.True);
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();

            Assert.That(entMan.EntityExists(tank), Is.False, "Destroyed tank entity should be deleted.");

            foreach (var child in children)
            {
                Assert.That(entMan.EntityExists(child), Is.False,
                    "Tank child entities should be deleted alongside the destroyed hull.");
            }

            foreach (var crewMember in crew)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(entMan.EntityExists(crewMember), Is.True,
                        "Crew members should survive tank destruction unless explicitly killed by other damage.");
                    Assert.That(entMan.GetComponent<BuckleComponent>(crewMember).Buckled, Is.False,
                        "Crew members should not remain buckled to deleted tank stations.");
                    Assert.That(entMan.GetComponent<TransformComponent>(crewMember).MapID, Is.EqualTo(map.MapId),
                        "Crew should remain on the map after the tank is destroyed.");
                });
            }
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnCrew(IEntityManager entMan, EntityCoordinates coordinates)
    {
        var crew = entMan.SpawnEntity(HumanPrototype, coordinates);
        PrepareCrewForImmediateBoarding(entMan, crew);

        return crew;
    }

    private static void PrepareCrewForImmediateBoarding(IEntityManager entMan, EntityUid crew)
    {
        var buckle = entMan.GetComponent<BuckleComponent>(crew);

#pragma warning disable RA0002
        buckle.Delay = TimeSpan.Zero;
#pragma warning restore RA0002
    }

    private static void PrepareTankForImmediateBoarding(WH40KTankComponent tankComp)
    {
#pragma warning disable RA0002
        tankComp.EntryDelaySeconds = 0f;
        tankComp.ExitDelaySeconds = 0f;
#pragma warning restore RA0002
    }

    private static string GetEntryVerbText(IIntegrationInstance server, WH40KTankCrewRole role)
    {
        var locMan = server.Resolve<ILocalizationManager>();
        return locMan.GetString(
            "wh40k-tank-entry-verb",
            ("role", locMan.GetString(GetRoleLocKey(role))));
    }

    private static string GetRoleLocKey(WH40KTankCrewRole role)
    {
        return role switch
        {
            WH40KTankCrewRole.Driver => "wh40k-tank-role-driver",
            WH40KTankCrewRole.Gunner => "wh40k-tank-role-gunner",
            WH40KTankCrewRole.Commander => "wh40k-tank-role-commander",
            WH40KTankCrewRole.Loader => "wh40k-tank-role-loader",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };
    }

    private static void BoardTank(IIntegrationInstance server, EntityUid tank, params EntityUid[] crew)
    {
        var entMan = server.Resolve<IEntityManager>();
        var transform = server.System<SharedTransformSystem>();
        var tankCoords = transform.GetMapCoordinates(tank);

        if (entMan.TryGetComponent(tank, out WH40KTankComponent tankComp))
            PrepareTankForImmediateBoarding(tankComp);

        for (var index = 0; index < crew.Length; index++)
        {
            var crewMember = crew[index];
            var stagingCoords = new MapCoordinates(
                tankCoords.Position + new Vector2(-0.3f + index * 0.15f, 1.4f),
                tankCoords.MapId);

            transform.SetMapCoordinates(crewMember, stagingCoords);
            entMan.EventBus.RaiseLocalEvent(tank, new InteractHandEvent(crewMember, tank));
        }
    }

    private static void AssertTankGrantedActionsUseHudIcons(IEntityManager entMan, WH40KTankComponent tankComp)
    {
        AssertActionUsesHudIcon(entMan, tankComp.FireCoaxialActionEntity,
            "Tank coaxial fire actions should render their configured HUD icon without drawing the tank entity over the hotbar.");
        AssertActionUsesHudIcon(entMan, tankComp.ReloadMainGunActionEntity,
            "Tank reload actions should render their configured HUD icon without drawing the tank entity over the hotbar.");
        AssertActionUsesHudIcon(entMan, tankComp.ReloadCoaxialActionEntity,
            "Tank coaxial reload actions should render their configured HUD icon without drawing the tank entity over the hotbar.");

        AssertStationDiagnosticsActionUsesHudIcon(entMan, tankComp.DriverStation);
        AssertStationDiagnosticsActionUsesHudIcon(entMan, tankComp.GunnerStation);
        AssertStationDiagnosticsActionUsesHudIcon(entMan, tankComp.CommanderStation);
        AssertStationDiagnosticsActionUsesHudIcon(entMan, tankComp.LoaderStation);
    }

    private static void AssertStationDiagnosticsActionUsesHudIcon(IEntityManager entMan, EntityUid? stationUid)
    {
        if (stationUid is not { } station)
            return;

        var stationComp = entMan.GetComponent<WH40KTankStationComponent>(station);
        AssertActionUsesHudIcon(entMan, stationComp.DiagnosticsActionEntity,
            "Tank diagnostics actions should render their configured HUD icon without drawing the tank entity over the hotbar.");
    }

    private static void AssertActionUsesHudIcon(IEntityManager entMan, EntityUid? actionUid, string message)
    {
        if (actionUid is not { } action)
            return;

        var actionComp = entMan.GetComponent<ActionComponent>(action);
        Assert.That(actionComp.ItemIconStyle, Is.EqualTo(ItemActionIconStyle.NoItem), message);
    }

    private static void ExecuteEngineVerb(IEntityManager entMan, EntityUid tank, EntityUid user)
    {
        entMan.TryGetComponent<HandsComponent>(user, out var hands);
        var verbEvent = new GetVerbsEvent<AlternativeVerb>(
            user,
            tank,
            null,
            hands,
            canInteract: true,
            canComplexInteract: true,
            canAccess: true,
            new List<VerbCategory>());

        entMan.EventBus.RaiseLocalEvent(tank, verbEvent);
        var engineVerb = verbEvent.Verbs.Single(verb => verb.Priority != 30 && verb.Text != DiagnosticsVerbText);
        Assert.That(engineVerb.Disabled, Is.False, "Engine control verb should be executable for the current driver.");
        Assert.That(engineVerb.Act, Is.Not.Null);
        engineVerb.Act!();
    }

    private static void ExecuteEntryVerb(IEntityManager entMan, EntityUid tank, EntityUid user, string verbText)
    {
        entMan.TryGetComponent<HandsComponent>(user, out var hands);
        var verbEvent = new GetVerbsEvent<AlternativeVerb>(
            user,
            tank,
            null,
            hands,
            canInteract: true,
            canComplexInteract: true,
            canAccess: true,
            new List<VerbCategory>());

        entMan.EventBus.RaiseLocalEvent(tank, verbEvent);
        var entryVerb = verbEvent.Verbs.FirstOrDefault(verb => verb.Text == verbText);
        Assert.That(entryVerb, Is.Not.Null,
            $"Could not find tank entry verb '{verbText}'. Available verbs: {string.Join(", ", verbEvent.Verbs.Select(verb => verb.Text))}");
        Assert.That(entryVerb.Disabled, Is.False, "The requested tank entry verb should be usable when the target station is free.");
        Assert.That(entryVerb.Act, Is.Not.Null);
        entryVerb.Act!();
    }

    private static void AimForward(IIntegrationInstance server, EntityUid tank, EntityUid gunner)
    {
        var entMan = server.Resolve<IEntityManager>();
        var transform = server.System<SharedTransformSystem>();
        var tankComp = entMan.GetComponent<WH40KTankComponent>(tank);
        var target = transform.ToCoordinates(GetForwardAimTarget(entMan, transform, tank, tankComp));

        var ev = new WH40KTankAimActionEvent
        {
            Performer = gunner,
            Target = target,
        };

        entMan.EventBus.RaiseLocalEvent(tank, ev);
    }

    private static void FireMainGun(IEntityManager entMan, EntityUid tank, EntityUid gunner)
    {
        var ev = new WH40KTankFireMainGunActionEvent
        {
            Performer = gunner,
        };

        entMan.EventBus.RaiseLocalEvent(tank, ev);
    }

    private static void FireCoaxial(IEntityManager entMan, EntityUid tank, EntityUid gunner)
    {
        var ev = new WH40KTankFireCoaxialActionEvent
        {
            Performer = gunner,
        };

        entMan.EventBus.RaiseLocalEvent(tank, ev);
    }

    private static void ReloadMainGun(IEntityManager entMan, EntityUid tank, EntityUid loader)
    {
        var ev = new WH40KTankReloadMainGunActionEvent
        {
            Performer = loader,
        };

        entMan.EventBus.RaiseLocalEvent(tank, ev);
    }

    private static MapCoordinates GetForwardAimTarget(
        IEntityManager entMan,
        SharedTransformSystem transform,
        EntityUid tank,
        WH40KTankComponent tankComp)
    {
        var aimOrigin = tankComp.Turret is { } turret && entMan.EntityExists(turret)
            ? turret
            : tank;
        var mapCoords = transform.GetMapCoordinates(aimOrigin);
        var facing = transform.GetWorldRotation(aimOrigin).ToWorldVec();
        return new MapCoordinates(mapCoords.Position + facing * 20f, mapCoords.MapId);
    }

    private static int CountInstantActionsWithEvent<TEvent>(
        IEntityManager entMan,
        SharedActionsSystem actionSystem,
        EntityUid user)
    {
        var query = entMan.GetEntityQuery<InstantActionComponent>();
        return actionSystem.GetActions(user)
            .Count(action => query.CompOrNull(action)?.Event?.GetType() == typeof(TEvent));
    }

    private static int CountWorldTargetActionsWithEvent<TEvent>(
        IEntityManager entMan,
        SharedActionsSystem actionSystem,
        EntityUid user)
    {
        var query = entMan.GetEntityQuery<WorldTargetActionComponent>();
        return actionSystem.GetActions(user)
            .Count(action => query.CompOrNull(action)?.Event?.GetType() == typeof(TEvent));
    }
}
