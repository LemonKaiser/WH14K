#nullable enable
using System;
using Content.IntegrationTests;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WH40K.GameMode;
using Content.Shared._WH40K.Influence;
using Content.Shared._WH40K.Psyker;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class PsykerInfluenceNetworkIntegrationTests : InteractionTest
{
    protected override PoolSettings Settings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task WarpResourcesDoNotReplicateEveryPassiveTick()
    {
        await Server.WaitPost(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var timing = Server.ResolveDependency<IGameTiming>();
            var actor = ToServer(Player);

            var warp = entMan.EnsureComponent<WH40KWarpResourceComponent>(actor);
            warp.CurrentCharge = 0f;
            warp.MaxCharge = 100f;
            warp.RegenPerSecond = 3f;
            warp.NextNetworkSyncAt = timing.CurTime + TimeSpan.FromSeconds(0.6);
            entMan.Dirty(actor, warp);

            var instability = entMan.EnsureComponent<WH40KWarpInstabilityComponent>(actor);
            instability.NextNetworkSyncAt = timing.CurTime + TimeSpan.FromSeconds(0.6);
            entMan.Dirty(actor, instability);

            entMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(actor, 8f, "tests.network"));
            instability.NextNetworkSyncAt = timing.CurTime + TimeSpan.FromSeconds(0.6);
            entMan.Dirty(actor, instability);
        });

        await RunForServerTimeAsync(TimeSpan.FromMilliseconds(150));

        var initialCharge = 0f;
        var initialInstability = 0f;

        await Client.WaitAssertion(() =>
        {
            var entMan = Client.ResolveDependency<IEntityManager>();
            var actor = ToClient(Player);
            var warp = entMan.GetComponent<WH40KWarpResourceComponent>(actor);
            var instability = entMan.GetComponent<WH40KWarpInstabilityComponent>(actor);

            initialCharge = warp.CurrentCharge;
            initialInstability = instability.CurrentInstability;
        });

        await RunForServerTimeAsync(TimeSpan.FromMilliseconds(200));

        await Client.WaitAssertion(() =>
        {
            var entMan = Client.ResolveDependency<IEntityManager>();
            var actor = ToClient(Player);
            var warp = entMan.GetComponent<WH40KWarpResourceComponent>(actor);
            var instability = entMan.GetComponent<WH40KWarpInstabilityComponent>(actor);

            Assert.Multiple(() =>
            {
                Assert.That(warp.CurrentCharge, Is.EqualTo(initialCharge).Within(0.0001f),
                    "Passive warp regen replicated again before the debounce window elapsed.");
                Assert.That(instability.CurrentInstability, Is.EqualTo(initialInstability).Within(0.0001f),
                    "Passive warp instability replicated again before the debounce window elapsed.");
            });
        });

        await RunForServerTimeAsync(TimeSpan.FromMilliseconds(450));

        await Client.WaitAssertion(() =>
        {
            var entMan = Client.ResolveDependency<IEntityManager>();
            var actor = ToClient(Player);
            var warp = entMan.GetComponent<WH40KWarpResourceComponent>(actor);
            var instability = entMan.GetComponent<WH40KWarpInstabilityComponent>(actor);

            Assert.Multiple(() =>
            {
                Assert.That(warp.CurrentCharge, Is.GreaterThan(initialCharge + 0.05f),
                    "Warp regen never reached the client after the debounce window.");
                Assert.That(instability.CurrentInstability, Is.LessThan(initialInstability - 0.05f),
                    "Warp instability never reached the client after the debounce window.");
            });
        });
    }

    [Test]
    public async Task InfluenceProgressCoalescesIntoFewerClientUpdates()
    {
        var point = await Spawn("MachineChipProduser", PlayerCoords);

        await Server.WaitPost(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var pointUid = ToServer(point);

            var pointComp = entMan.GetComponent<WH40KInfluencePointComponent>(pointUid);
            pointComp.CaptureEnabledFromPhase = WH40KBattlePhase.Preparation;
            pointComp.OwnerTeamId = null;
            pointComp.CapturingTeamId = "Imperium";
            pointComp.CaptureProgressSeconds = 0f;
            pointComp.LastSyncedCaptureProgressSeconds = 0f;
            pointComp.CaptureProgressSyncStep = 0.25f;
            entMan.Dirty(pointUid, pointComp);
        });

        await RunForServerTimeAsync(TimeSpan.FromMilliseconds(100));

        await Server.WaitPost(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var pointUid = ToServer(point);
            var pointComp = entMan.GetComponent<WH40KInfluencePointComponent>(pointUid);
            pointComp.CaptureProgressSeconds = 0.15f;
        });

        await RunForServerTimeAsync(TimeSpan.FromMilliseconds(200));

        await Client.WaitAssertion(() =>
        {
            var entMan = Client.ResolveDependency<IEntityManager>();
            var pointUid = ToClient(point);
            var pointComp = entMan.GetComponent<WH40KInfluencePointComponent>(pointUid);

            Assert.That(pointComp.CaptureProgressSeconds, Is.EqualTo(0f).Within(0.0001f),
                "Influence capture progress replicated too early.");
        });

        await Server.WaitPost(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var pointUid = ToServer(point);
            var pointComp = entMan.GetComponent<WH40KInfluencePointComponent>(pointUid);
            pointComp.CaptureProgressSeconds = 0.35f;
        });

        await RunForServerTimeAsync(TimeSpan.FromMilliseconds(200));

        await Client.WaitAssertion(() =>
        {
            var entMan = Client.ResolveDependency<IEntityManager>();
            var pointUid = ToClient(point);
            var pointComp = entMan.GetComponent<WH40KInfluencePointComponent>(pointUid);

            Assert.That(pointComp.CaptureProgressSeconds, Is.GreaterThan(0.25f),
                "Influence capture progress did not reach the client after the debounce window.");
        });
    }

    private async Task RunForServerTimeAsync(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        var targetTime = TimeSpan.Zero;
        await Server.WaitPost(() =>
        {
            var timing = Server.ResolveDependency<IGameTiming>();
            targetTime = timing.CurTime + duration;
        });

        for (var i = 0; i < 240; i++)
        {
            await RunTicks(1);

            var reachedTarget = false;
            await Server.WaitPost(() =>
            {
                var timing = Server.ResolveDependency<IGameTiming>();
                reachedTarget = timing.CurTime >= targetTime;
            });

            if (reachedTarget)
                return;
        }

        Assert.Fail($"Server time failed to advance by {duration} within the allotted tick budget.");
    }
}
