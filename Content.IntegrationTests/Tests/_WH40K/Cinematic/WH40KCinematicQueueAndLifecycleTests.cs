using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WH40K.Cinematic;
using Content.Shared._WH40K.Cinematic;
using Robust.Shared.Console;
using ClientCinematicSystem = Content.Client._WH40K.Cinematic.WH40KCinematicSystem;

namespace Content.IntegrationTests.Tests._WH40K.Cinematic;

[TestFixture]
[NonParallelizable]
public sealed class WH40KCinematicQueueAndLifecycleTests : WH40KCinematicGameTest
{
    private const string NonRepeatableA = "WH40KCinematicPhase1NonRepeatableA";
    private const string NonRepeatableB = "WH40KCinematicPhase1NonRepeatableB";
    private const string Repeatable = "WH40KCinematicPhase1Repeatable";
    private const string LongRunning = "WH40KCinematicPhase1LongRunning";
    private const string InvalidTerminal = "WH40KCinematicPhase1InvalidTerminal";

    [TestPrototypes]
    private static readonly string TestPrototypes = $@"
- type: wh40kCinematic
  id: {NonRepeatableA}
  steps:
  - id: hold
    type: Marker
    waitMode: Duration
    duration: 1.00
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {NonRepeatableB}
  steps:
  - id: hold
    type: Marker
    waitMode: Duration
    duration: 1.00
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {Repeatable}
  allowRepeat: true
  steps:
  - id: hold
    type: Marker
    waitMode: Duration
    duration: 0.35
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {LongRunning}
  steps:
  - id: hold
    type: Marker
    waitMode: Duration
    duration: 5.0
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {InvalidTerminal}
  steps:
  - id: invalid-end
    type: EndCinematic
    waitMode: Duration
    duration: 0.10
";

    [Test]
    public async Task QueueStartsSecondCinematicAfterFirstCompletes()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var clientSys = Client.System<ClientCinematicSystem>();

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(NonRepeatableA), out _), Is.True);
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(NonRepeatableB), out _), Is.True);

            var snapshot = serverSys.GetSnapshot();
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.IsActive, Is.True);
                Assert.That(snapshot.ActiveCinematicId, Is.EqualTo(NonRepeatableA));
                Assert.That(snapshot.QueueLength, Is.EqualTo(1));
            });
        });

        await RunTicksStep(20);

        await ServerStep(() =>
        {
            var snapshot = serverSys.GetSnapshot();
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.IsActive, Is.True);
                Assert.That(snapshot.ActiveCinematicId, Is.EqualTo(NonRepeatableA));
                Assert.That(snapshot.QueueLength, Is.EqualTo(1));
            });
        });

        await WaitForPairConditionStep(
            () => serverSys.GetSnapshot().ActiveCinematicId == NonRepeatableB,
            maxTicks: 40,
            label: "wait for queue transition to NonRepeatableB");

        await ServerStep(() =>
        {
            var snapshot = serverSys.GetSnapshot();
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.IsActive, Is.True);
                Assert.That(snapshot.ActiveCinematicId, Is.EqualTo(NonRepeatableB));
                Assert.That(snapshot.QueueLength, Is.EqualTo(0));
            });
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive &&
                  clientSys.ActiveState == null &&
                  clientSys.LastStoppedEvent != null,
            maxTicks: 60,
            label: "wait for phase1 queue completion");

        await ServerStep(() =>
        {
            var snapshot = serverSys.GetSnapshot();
            Assert.That(snapshot.IsActive, Is.False);
            Assert.That(snapshot.CompletedNonRepeatableCount, Is.EqualTo(2));
        });

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Null);
            Assert.That(clientSys.LastStoppedEvent, Is.Not.Null);
            Assert.That(clientSys.LastStoppedEvent!.CinematicId, Is.EqualTo(NonRepeatableB));
            Assert.That(clientSys.LastStoppedEvent.Completed, Is.True);
        });
    }

    [Test]
    public async Task NonRepeatableCinematicCannotRestartAfterCompletion()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(NonRepeatableA), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive,
            maxTicks: 60,
            label: "wait for NonRepeatableA completion");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(NonRepeatableA), out var message), Is.False);
            Assert.That(message, Does.Contain("non-repeatable"));
        });
    }

    [Test]
    public async Task RepeatableCinematicCanRestartAfterCompletion()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(Repeatable), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive,
            maxTicks: 50,
            label: "wait for Repeatable completion");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(Repeatable), out _), Is.True);
            Assert.That(serverSys.GetSnapshot().ActiveCinematicId, Is.EqualTo(Repeatable));
        });
    }

    [Test]
    public async Task ManualStopCleansUpAndStartsQueuedCinematic()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var clientSys = Client.System<ClientCinematicSystem>();

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(LongRunning), out _), Is.True);
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(NonRepeatableB), out _), Is.True);
            Assert.That(serverSys.GetSnapshot().QueueLength, Is.EqualTo(1));
        });

        await RunTicksStep(5);

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryStopActive("Integration test stop.", markCompleted: false), Is.True);
            var snapshot = serverSys.GetSnapshot();
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.IsActive, Is.True);
                Assert.That(snapshot.ActiveCinematicId, Is.EqualTo(NonRepeatableB));
                Assert.That(snapshot.QueueLength, Is.EqualTo(0));
                Assert.That(snapshot.CompletedNonRepeatableCount, Is.EqualTo(0));
            });
        });

        await WaitForPairConditionStep(
            () => clientSys.ActiveState?.CinematicId == NonRepeatableB,
            maxTicks: 20,
            label: "wait for queued cinematic after manual stop");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.CinematicId, Is.EqualTo(NonRepeatableB));
        });
    }

    [Test]
    public async Task AdminCommandCanStartAndReportStatus()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var console = Server.ResolveDependency<IConsoleHost>();

        await ServerStep(() =>
        {
            console.ExecuteCommand($"wh40kcinematic start {Repeatable}");
            var snapshot = serverSys.GetSnapshot();
            Assert.That(snapshot.IsActive, Is.True);
            Assert.That(snapshot.ActiveCinematicId, Is.EqualTo(Repeatable));

            console.ExecuteCommand("wh40kcinematic status");
            console.ExecuteCommand("wh40kcinematic stop");
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
        });
    }

    [Test]
    public async Task ValidatorRejectsEndCinematicWithoutTerminalWaitMode()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();

        await ServerStep(() =>
        {
            var errors = serverSys.ValidatePrototype(SProtoMan.Index<WH40KCinematicPrototype>(InvalidTerminal));
            Assert.That(errors, Is.Not.Empty);
            Assert.That(errors.Any(error => error.Contains("endCinematic step must use Terminal waitMode.")), Is.True);
        });
    }
}
