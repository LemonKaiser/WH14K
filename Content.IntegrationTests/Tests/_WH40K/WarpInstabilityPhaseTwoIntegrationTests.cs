#nullable enable
using System;
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Mind;
using Content.Server.NPC.HTN;
using Content.Server._WH40K.Psyker;
using Content.Shared.CCVar;
using Content.Shared.Body.Components;
using Content.Shared.Chat;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Stunnable;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.StatusEffectNew;
using Content.Shared._WH40K.Psyker;
using Robust.Client.UserInterface;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class WarpInstabilityPhaseTwoIntegrationTests : InteractionTest
{
    private const string NanoTrasenFaction = "NanoTrasen";
    private const int TopTierSeed = 1;
    private const int FleshRiftHellspawnSeed = 1;
    private const int FleshRiftDeathSeed = 2;
    private const int FleshRiftParalyzeSeed = 3;
    private const int ChatWaitMaxTicks = 60;

    protected override string PlayerPrototype => "MobHuman";
    protected override PoolSettings Settings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task CastIn600BandAppliesHeavyButSubmaxBleeding()
    {
        await EnsureWarpRuntimeAsync();
        await SetServerRandomSeedAsync(TopTierSeed);

        var bleedAmount = 0f;
        var maxBleed = 0f;

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 620f, "tests.phase2.600"));

            var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(SPlayer);
            bleedAmount = bloodstream.BleedAmount;
            maxBleed = bloodstream.MaxBleedAmount;
        });

        Assert.Multiple(() =>
        {
            Assert.That(bleedAmount, Is.GreaterThanOrEqualTo(5f));
            Assert.That(bleedAmount, Is.LessThan(maxBleed));
        });
    }

    [Test]
    public async Task CastIn650BandSpawnsWarpDoppelganger()
    {
        await EnsureWarpRuntimeAsync();
        await SetServerRandomSeedAsync(TopTierSeed);

        var before = 0;
        var after = 0;

        await Server.WaitPost(() =>
        {
            before = CountEntitiesByPrototype("MobWH40KWarpDoppelganger");
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 660f, "tests.phase2.650"));
            after = CountEntitiesByPrototype("MobWH40KWarpDoppelganger");
        });

        Assert.That(after, Is.EqualTo(before + 2));
    }

    [Test]
    public async Task CastIn700BandCanPolymorphIntoHellspawn()
    {
        var outcome = await Trigger700BandOutcomeAsync(FleshRiftHellspawnSeed, "tests.phase2.700.hellspawn");

        Assert.That(outcome.AttachedPrototype, Is.EqualTo("MobHellspawn"));
    }

    [Test]
    public async Task CastIn700BandCanKillCaster()
    {
        var outcome = await Trigger700BandOutcomeAsync(FleshRiftDeathSeed, "tests.phase2.700.death");

        Assert.That(outcome.PlayerDead, Is.True);
    }

    [Test]
    public async Task CastIn700BandCanParalyzeCaster()
    {
        var outcome = await Trigger700BandOutcomeAsync(FleshRiftParalyzeSeed, "tests.phase2.700.paralyze");

        Assert.That(outcome.StunRemaining, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(9)));
    }

    private async Task<(string? AttachedPrototype, bool PlayerDead, TimeSpan StunRemaining)> Trigger700BandOutcomeAsync(int seed, string sourceKey)
    {
        await EnsureWarpRuntimeAsync();
        await SetServerRandomSeedAsync(seed);

        EntityUid? attached = null;
        string? attachedPrototype = null;
        var playerDead = false;
        var stunRemaining = TimeSpan.Zero;

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 720f, sourceKey));
        });
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            var playerMan = Server.ResolveDependency<IPlayerManager>();
            attached = playerMan.Sessions.Single().AttachedEntity;
            if (attached != null && SEntMan.TryGetComponent(attached.Value, out MetaDataComponent? attachedMeta))
                attachedPrototype = attachedMeta.EntityPrototype?.ID;

            playerDead = SEntMan.TryGetComponent(SPlayer, out MobStateComponent? mobState) && mobState.CurrentState == MobState.Dead;
            stunRemaining = TryGetRemainingStatusTime(SPlayer, SharedStunSystem.StunId);
        });

        return (attachedPrototype, playerDead, stunRemaining);
    }

    [Test]
    public async Task CastIn800BandStartsPossessionAndRemovesPlayerControl()
    {
        await EnsureWarpRuntimeAsync();
        await SetServerRandomSeedAsync(TopTierSeed);

        EntityUid? attached = null;
        var hasHostileFaction = false;
        var hasNpc = false;
        var bodyStillHasMind = true;

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 820f, "tests.phase2.800"));
        });
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            var mindSystem = SEntMan.System<MindSystem>();
            var playerMan = Server.ResolveDependency<IPlayerManager>();
            var factionSystem = SEntMan.System<NpcFactionSystem>();
            attached = playerMan.Sessions.Single().AttachedEntity;
            hasHostileFaction = SEntMan.TryGetComponent(SPlayer, out NpcFactionMemberComponent? faction) &&
                               factionSystem.IsFactionHostile(NanoTrasenFaction, (SPlayer, faction));
            hasNpc = SEntMan.HasComponent<HTNComponent>(SPlayer);
            bodyStillHasMind = mindSystem.TryGetMind(SPlayer, out _, out _);
        });

        Assert.Multiple(() =>
        {
            Assert.That(hasHostileFaction, Is.True);
            Assert.That(hasNpc, Is.True);
            Assert.That(attached != SPlayer || !bodyStillHasMind, Is.True);
        });
    }

    [Test]
    public async Task GlobalPulseStartsAt600AndEscalatesAt700()
    {
        await EnsureWarpRuntimeAsync();
        var controller = await GetClientChatControllerAsync();

        await Client.WaitPost(() => controller.History.Clear());

        var tier600Text = await GetLocalizedServerStringAsync("wh40k-warp-instability-global-pulse-600");
        var tier700Text = await GetLocalizedServerStringAsync("wh40k-warp-instability-global-pulse-700");

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 590f, "tests.phase2.590"));
        });
        await RunTicks(5);

        Assert.That(await GetMatchingMessageCountAsync(controller, tier600Text), Is.EqualTo(0));
        Assert.That(await GetMatchingMessageCountAsync(controller, tier700Text), Is.EqualTo(0));

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 20f, "tests.phase2.600-start"));
        });

        var firstPulse = await WaitForMessageAsync(controller, tier600Text);
        Assert.That(firstPulse.Message, Is.EqualTo(tier600Text));

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 100f, "tests.phase2.700-start"));
        });

        var secondPulse = await WaitForMessageAsync(controller, tier700Text);
        Assert.That(secondPulse.Message, Is.EqualTo(tier700Text));
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

    private TimeSpan TryGetRemainingStatusTime(EntityUid uid, string effectProtoId)
    {
        if (!SEntMan.HasComponent<StatusEffectContainerComponent>(uid))
            return TimeSpan.Zero;

        var statusEffects = SEntMan.System<StatusEffectsSystem>();
        if (!statusEffects.TryGetTime(uid, effectProtoId, out var time) || time.EndEffectTime == null)
            return TimeSpan.Zero;

        return time.EndEffectTime.Value - STiming.CurTime;
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
