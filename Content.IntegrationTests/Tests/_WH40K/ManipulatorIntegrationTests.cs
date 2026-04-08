#nullable enable
using System;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.GameTicking;
using Content.Shared._WH40K.Manipulator;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
[NonParallelizable]
public sealed class ManipulatorIntegrationTests
{
    [Test]
    public async Task ConveyorManipulatorServerConfigFieldsStayOffTheWire()
    {
        await using var pair = await StartWh40KRoundAsync();

        EntityUid manipulator = EntityUid.Invalid;
        NetEntity netManipulator = NetEntity.Invalid;

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var player = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single().AttachedEntity!.Value;
            var actorCoords = entMan.GetComponent<TransformComponent>(player).Coordinates;

            manipulator = entMan.SpawnEntity("WH40KConveyorManipulator", actorCoords);
            netManipulator = entMan.GetNetEntity(manipulator);
        });

        await pair.RunTicksSync(20);

        await pair.Client.WaitAssertion(() =>
        {
            var entMan = pair.Client.ResolveDependency<IEntityManager>();
            Assert.That(entMan.TryGetEntity(netManipulator, out var clientManipulator), Is.True,
                "Manipulator entity did not replicate to the client.");
            Assert.That(clientManipulator, Is.Not.Null);
            Assert.That(entMan.TryGetComponent<WH40KConveyorManipulatorComponent>(clientManipulator!.Value, out var component), Is.True);
            Assert.That(component, Is.Not.Null);
            var clientComponent = component!;

            Assert.Multiple(() =>
            {
                Assert.That(clientComponent.TransferCooldown, Is.EqualTo(0.2f).Within(0.0001f));
                Assert.That(clientComponent.TransferDuration, Is.EqualTo(0.45f).Within(0.0001f));
                Assert.That(clientComponent.ArcHeight, Is.EqualTo(0.3f).Within(0.0001f));
                Assert.That(clientComponent.RequirePowered, Is.True);
            });
        });

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var component = entMan.GetComponent<WH40KConveyorManipulatorComponent>(manipulator);

            component.TransferCooldown = 1.75f;
            component.TransferDuration = 2.5f;
            component.ArcHeight = 0.9f;
            component.RequirePowered = false;
            entMan.Dirty(manipulator, component);
        });

        await pair.RunTicksSync(20);

        await pair.Client.WaitAssertion(() =>
        {
            var entMan = pair.Client.ResolveDependency<IEntityManager>();
            Assert.That(entMan.TryGetEntity(netManipulator, out var clientManipulator), Is.True,
                "Manipulator entity disappeared from the client before the verification tick.");
            Assert.That(clientManipulator, Is.Not.Null);
            Assert.That(entMan.TryGetComponent<WH40KConveyorManipulatorComponent>(clientManipulator!.Value, out var component), Is.True);
            Assert.That(component, Is.Not.Null);
            var clientComponent = component!;

            Assert.Multiple(() =>
            {
                Assert.That(clientComponent.TransferCooldown, Is.EqualTo(0.2f).Within(0.0001f),
                    "TransferCooldown leaked to the client even though it should be server-only.");
                Assert.That(clientComponent.TransferDuration, Is.EqualTo(0.45f).Within(0.0001f),
                    "TransferDuration leaked to the client even though it should be server-only.");
                Assert.That(clientComponent.ArcHeight, Is.EqualTo(0.3f).Within(0.0001f),
                    "ArcHeight leaked to the client even though it should be server-only.");
                Assert.That(clientComponent.RequirePowered, Is.True,
                    "RequirePowered leaked to the client even though it should be server-only.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConveyorManipulatorActiveItemStaysOffTheWire()
    {
        await using var pair = await StartWh40KRoundAsync();

        EntityUid manipulator = EntityUid.Invalid;
        NetEntity netManipulator = NetEntity.Invalid;

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var player = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single().AttachedEntity!.Value;
            var actorCoords = entMan.GetComponent<TransformComponent>(player).Coordinates;

            manipulator = entMan.SpawnEntity("WH40KConveyorManipulator", actorCoords);
            netManipulator = entMan.GetNetEntity(manipulator);
        });

        await pair.RunTicksSync(20);

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var component = entMan.GetComponent<WH40KConveyorManipulatorComponent>(manipulator);

            component.NextTransferAt = TimeSpan.FromMinutes(1);
            component.Busy = false;
            component.ActiveItem = manipulator;
            component.Status = WH40KManipulatorStatus.NoPower;
            entMan.Dirty(manipulator, component);
        });

        await pair.RunTicksSync(20);

        await pair.Client.WaitAssertion(() =>
        {
            var entMan = pair.Client.ResolveDependency<IEntityManager>();
            Assert.That(entMan.TryGetEntity(netManipulator, out var clientManipulator), Is.True,
                "Manipulator entity did not replicate to the client.");
            Assert.That(clientManipulator, Is.Not.Null);
            Assert.That(entMan.TryGetComponent<WH40KConveyorManipulatorComponent>(clientManipulator!.Value, out var component), Is.True);
            Assert.That(component, Is.Not.Null);
            var clientComponent = component!;

            Assert.Multiple(() =>
            {
                Assert.That(clientComponent.Busy, Is.False,
                    "Busy should remain networked for manipulator visuals/state.");
                Assert.That(clientComponent.Status, Is.EqualTo(WH40KManipulatorStatus.NoPower),
                    "Status should remain networked for manipulator visuals/state.");
                Assert.That(clientComponent.ActiveItem, Is.Null,
                    "ActiveItem leaked to the client even though it should remain server-only.");
            });
        });

        await pair.CleanReturnAsync();
    }

    private static async Task<TestPair> StartWh40KRoundAsync()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Dirty = true,
            InLobby = true,
            DummyTicker = false,
            Fresh = true
        });

        await pair.WaitCommand("forcemap Battlefield40k");
        await pair.WaitCommand("setgamepreset WH40KTeamBattle 9999");
        await pair.WaitCommand("startround");
        await pair.RunTicksSync(60);

        // WH40K requires faction selection before late-join.
        await pair.Client.WaitPost(() =>
        {
            var factionSys = pair.Client.System<Content.Client._WH40K.LateJoin.WH40KFactionSystem>();
            factionSys.SelectFaction("Imperium", Content.Shared._WH40K.LateJoin.WH40KFactionSelectionPurpose.LateJoin);
        });
        await pair.RunTicksSync(10);

        await pair.Server.WaitPost(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();
            ticker.MakeJoinGame(playerMan.Sessions.Single(), EntityUid.Invalid, "Guardsman");
        });
        await pair.RunTicksSync(20);

        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();

            Assert.Multiple(() =>
            {
                Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
                Assert.That(playerMan.Sessions.Single().AttachedEntity, Is.Not.Null);
            });
        });

        return pair;
    }
}
