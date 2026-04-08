#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WH40K.Psyker;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Mind;
using Content.Shared._WH40K.Psyker;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Random;
using Robust.Server.Player;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class WarpInstabilityPhaseThreeIntegrationTests : InteractionTest
{
    private const int TopTierSeed = 1;
    private const int MutationPersistenceTicks = 15;
    private const int CatastrophePinnedTicks = 30;
    private const int ChatWaitMaxTicks = 60;

    protected override string PlayerPrototype => "MobHuman";
    protected override PoolSettings Settings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task CastIn900BandAppliesIrreversibleMutationInExpectedRange()
    {
        await EnsureWarpRuntimeAsync();
        await SetServerRandomSeedAsync(TopTierSeed);

        var hasMutation = false;
        var stillHasMutation = false;
        var severity = 0f;
        var persistedSeverity = 0f;
        var movementMultiplier = 1f;
        var thresholdMultiplier = 1f;

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 930f, "tests.phase3.900"));
        });
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            hasMutation = SEntMan.TryGetComponent(SPlayer, out WH40KWarpMutationComponent? mutation) && mutation != null;
            if (!hasMutation || mutation == null)
                return;

            severity = mutation.Severity;
            movementMultiplier = mutation.MovementMultiplier;
            thresholdMultiplier = mutation.ThresholdMultiplier;
        });

        await RunTicks(MutationPersistenceTicks);

        await Server.WaitPost(() =>
        {
            stillHasMutation = SEntMan.TryGetComponent(SPlayer, out WH40KWarpMutationComponent? mutation) && mutation != null;
            if (stillHasMutation && mutation != null)
                persistedSeverity = mutation.Severity;
        });

        Assert.Multiple(() =>
        {
            Assert.That(hasMutation, Is.True);
            Assert.That(severity, Is.InRange(0.25f, 0.75f));
            Assert.That(movementMultiplier, Is.LessThan(1f));
            Assert.That(thresholdMultiplier, Is.LessThan(1f));
            Assert.That(stillHasMutation, Is.True);
            Assert.That(persistedSeverity, Is.EqualTo(severity).Within(0.0001f));
        });
    }

    [Test]
    public async Task Reaching1000DisablesDecayAndKeepsMirrorPinned()
    {
        await EnsureWarpRuntimeAsync();

        EntityUid observer = default;
        var immediateInstability = 0f;
        var laterInstability = 0f;
        var decayPerSecond = -1f;

        await Server.WaitPost(() =>
        {
            observer = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(4f, 0f)));
            SEntMan.EnsureComponent<WH40KWarpInstabilityComponent>(observer);
        });
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 1000f, "tests.phase3.1000-decay"));
        });
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            var instability = SEntMan.GetComponent<WH40KWarpInstabilityComponent>(observer);
            immediateInstability = instability.CurrentInstability;
            decayPerSecond = instability.DecayPerSecond;
        });

        await RunTicks(CatastrophePinnedTicks);

        await Server.WaitPost(() =>
        {
            var instability = SEntMan.GetComponent<WH40KWarpInstabilityComponent>(observer);
            laterInstability = instability.CurrentInstability;
        });

        Assert.Multiple(() =>
        {
            Assert.That(immediateInstability, Is.EqualTo(1000f).Within(0.001f));
            Assert.That(decayPerSecond, Is.EqualTo(0f).Within(0.001f));
            Assert.That(laterInstability, Is.EqualTo(1000f).Within(0.001f));
        });
    }

    [Test]
    public async Task Reaching1000AshesWarpUsersSpawnsCowAndSkips900Pulse()
    {
        await EnsureWarpRuntimeAsync();

        var controller = await GetClientChatControllerAsync();
        await Client.WaitPost(() => controller.History.Clear());

        var catastropheText = await GetLocalizedServerStringAsync("wh40k-warp-instability-global-catastrophe");
        var tier900Text = await GetLocalizedServerStringAsync("wh40k-warp-instability-global-pulse-900");

        EntityUid secondWarpUser = default;
        var ashBefore = 0;
        var ashAfter = 0;
        var cowBefore = 0;
        var cowAfter = 0;
        var playerDeleted = false;
        var secondDeleted = false;

        await Server.WaitPost(() =>
        {
            secondWarpUser = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(2f, 0f)));
            SEntMan.EnsureComponent<WH40KWarpResourceComponent>(secondWarpUser);
            SEntMan.EnsureComponent<WH40KWarpInstabilityComponent>(secondWarpUser);

            ashBefore = CountEntitiesByPrototype("Ash");
            cowBefore = CountEntitiesByPrototype("MobCow");

            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 1000f, "tests.phase3.1000-final"));
        });

        var catastrophe = await WaitForMessageAsync(controller, catastropheText);
        Assert.That(catastrophe.Message, Is.EqualTo(catastropheText));

        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            ashAfter = CountEntitiesByPrototype("Ash");
            cowAfter = CountEntitiesByPrototype("MobCow");
            playerDeleted = SEntMan.Deleted(SPlayer);
            secondDeleted = SEntMan.Deleted(secondWarpUser);
        });

        Assert.Multiple(() =>
        {
            Assert.That(playerDeleted, Is.True);
            Assert.That(secondDeleted, Is.True);
            Assert.That(ashAfter, Is.GreaterThanOrEqualTo(ashBefore + 2));
            Assert.That(cowAfter, Is.EqualTo(cowBefore + 1));
        });

        Assert.That(await GetMatchingMessageCountAsync(controller, tier900Text), Is.EqualTo(0));
    }

    private async Task EnsureWarpRuntimeAsync()
    {
        await Server.WaitPost(() =>
        {
            var warpSystem = SEntMan.System<WH40KGlobalWarpInstabilitySystem>();
            var playerMan = Server.ResolveDependency<IPlayerManager>();
            var mindSystem = SEntMan.System<SharedMindSystem>();
            var session = playerMan.Sessions.Single();
            var mind = mindSystem.GetOrCreateMind(session.UserId);

            warpSystem.AdminResetState();

            if (mind.Comp.OwnedEntity != SPlayer)
                mindSystem.TransferTo(mind.Owner, SPlayer);

            Server.CfgMan.SetCVar(CCVars.WH40KWarpHighestTierChance, 1f);
            SEntMan.EnsureComponent<WH40KWarpResourceComponent>(SPlayer);
            SEntMan.EnsureComponent<WH40KWarpInstabilityComponent>(SPlayer);
        });

        await RunTicks(5);
    }

    private async Task SetServerRandomSeedAsync(int seed)
    {
        await Server.WaitPost(() => Server.ResolveDependency<IRobustRandom>().SetSeed(seed));
    }

    private int CountEntitiesByPrototype(string prototypeId)
    {
        var count = 0;
        var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var meta))
        {
            if (string.Equals(meta.EntityPrototype?.ID, prototypeId, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private async Task<ChatUIController> GetClientChatControllerAsync()
    {
        ChatUIController? controller = null;

        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            controller = ui.GetUIController<ChatUIController>();
        });

        return controller!;
    }

    private async Task<string> GetLocalizedServerStringAsync(string key)
    {
        var text = string.Empty;
        await Server.WaitPost(() => text = Server.ResolveDependency<ILocalizationManager>().GetString(key));
        return text;
    }

    private async Task<ChatMessage> WaitForMessageAsync(ChatUIController controller, string text, int maxTicks = ChatWaitMaxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            ChatMessage? match = null;
            await Client.WaitPost(() =>
            {
                match = controller.History
                    .Select(entry => entry.Msg)
                    .LastOrDefault(entry => entry.Channel == ChatChannel.Radio && string.Equals(entry.Message, text, StringComparison.Ordinal));
            });

            if (match != null)
                return match;

            await RunTicks(1);
        }

        Assert.Fail($"Timed out waiting for expected chat message: {text}");
        return null!;
    }

    private async Task<int> GetMatchingMessageCountAsync(ChatUIController controller, string text)
    {
        var count = 0;
        await Client.WaitPost(() =>
        {
            count = controller.History
                .Select(entry => entry.Msg)
                .Count(entry => entry.Channel == ChatChannel.Radio && string.Equals(entry.Message, text, StringComparison.Ordinal));
        });
        return count;
    }
}
