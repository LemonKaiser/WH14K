#nullable enable
using System;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WH40K.Overlays;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class RadialGreyscaleFieldIntegrationTests : InteractionTest
{
    [Test]
    public async Task GrenadeTimerUpdatesAreCoalescedInsideField()
    {
        EntityUid zone = default;
        var grenade = await Spawn("HolyHandGrenade", PlayerCoords);

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(PlayerCoords);

            zone = SEntMan.SpawnEntity(null, coords);
            var zoneComp = SEntMan.EnsureComponent<WH40KRadialGreyscaleComponent>(zone);
            zoneComp.Radius = 6f;
            zoneComp.MovementSpeedMultiplier = 0.05f;
            zoneComp.PhysicsVelocityMultiplier = 0.05f;
            zoneComp.GrenadeFuseTimerMultiplier = 0.05f;

            var grenadeUid = ToServer(grenade);
            var trigger = SEntMan.GetComponent<TimerTriggerComponent>(grenadeUid);
            var triggerSys = SEntMan.System<TriggerSystem>();

            Assert.That(triggerSys.ActivateTimerTrigger((grenadeUid, trigger), SPlayer), Is.True);
        });

        await RunForServerTimeAsync(TimeSpan.FromMilliseconds(120));

        var grenadeClient = ToClient(grenade);
        TimeSpan firstNextTrigger = default;

        await Client.WaitAssertion(() =>
        {
            var timer = CEntMan.GetComponent<TimerTriggerComponent>(grenadeClient);
            Assert.That(timer.NextTrigger, Is.GreaterThan(TimeSpan.Zero));
            firstNextTrigger = timer.NextTrigger;
        });

        await RunForServerTimeAsync(TimeSpan.FromMilliseconds(120));

        await Client.WaitAssertion(() =>
        {
            var timer = CEntMan.GetComponent<TimerTriggerComponent>(grenadeClient);
            Assert.That(timer.NextTrigger, Is.EqualTo(firstNextTrigger),
                "Timer fuse state should not be replicated every field tick.");
        });

        await RunForServerTimeAsync(TimeSpan.FromMilliseconds(220));

        await Client.WaitAssertion(() =>
        {
            var timer = CEntMan.GetComponent<TimerTriggerComponent>(grenadeClient);
            Assert.That(timer.NextTrigger, Is.Not.EqualTo(firstNextTrigger),
                "Timer fuse state never replicated after the coalescing window elapsed.");
        });
    }

    private async Task RunForServerTimeAsync(TimeSpan duration)
    {
        var target = STiming.CurTime + duration;
        while (STiming.CurTime < target)
        {
            await RunTicks(1);
        }
    }
}
