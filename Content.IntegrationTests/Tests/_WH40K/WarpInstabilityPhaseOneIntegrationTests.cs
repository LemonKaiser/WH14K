#pragma warning disable CS0618 // GetTotalDamage: used in test assertions; no alternative API for these checks
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Hands.Systems;
using Content.Server._WH40K.Psyker;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Drunk;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Stunnable;
using Content.Shared.StatusEffectNew;
using Content.Shared._WH40K.Psyker;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Random;
using Robust.Server.Player;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class WarpInstabilityPhaseOneIntegrationTests : InteractionTest
{
    private const int TopTierSeed = 1;
    private const int CollapseDrunkSeed = 1;
    private const int CollapseStaminaSeed = 3;
    private const int ChatWaitMaxTicks = 60;

    protected override string PlayerPrototype => "MobHuman";
    protected override PoolSettings Settings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task GlobalWarpMaxReplicatesToClient()
    {
        await EnsureWarpRuntimeAsync();
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var instability = SEntMan.GetComponent<WH40KWarpInstabilityComponent>(SPlayer);
            Assert.That(instability.MaxInstability, Is.EqualTo(1000f));
            Assert.That(instability.DecayPerSecond, Is.EqualTo(1.2f).Within(0.0001f));
        });

        await Client.WaitAssertion(() =>
        {
            var instability = CEntMan.GetComponent<WH40KWarpInstabilityComponent>(CPlayer);
            Assert.That(instability.MaxInstability, Is.EqualTo(1000f));
            Assert.That(instability.DecayPerSecond, Is.EqualTo(1.2f).Within(0.0001f));
        });
    }

    [Test]
    public async Task CastIn350BandDealsTenWarpBurn()
    {
        await EnsureWarpRuntimeAsync();

        var damageBefore = 0f;
        var damageAfter = 0f;

        await Server.WaitPost(() =>
        {
            var damageable = SEntMan.System<DamageableSystem>();
            damageBefore = damageable.GetTotalDamage(SPlayer).Float();
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 360f, "tests.phase1.350"));
            damageAfter = damageable.GetTotalDamage(SPlayer).Float();
        });

        Assert.That(damageAfter - damageBefore, Is.EqualTo(10f).Within(0.05f));
    }

    [Test]
    public async Task CastIn400BandAppliesStunAndMildDrunk()
    {
        await EnsureWarpRuntimeAsync();
        await SetServerRandomSeedAsync(TopTierSeed);

        TimeSpan stunRemaining = TimeSpan.Zero;
        TimeSpan drunkRemaining = TimeSpan.Zero;

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 420f, "tests.phase1.400"));
            stunRemaining = GetRemainingStatusTime(SPlayer, SharedStunSystem.StunId);
            drunkRemaining = GetRemainingStatusTime(SPlayer, SharedDrunkSystem.Drunk);
        });

        Assert.Multiple(() =>
        {
            Assert.That(stunRemaining, Is.GreaterThan(TimeSpan.FromSeconds(0.6)));
            Assert.That(stunRemaining, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(1.1)));
            Assert.That(drunkRemaining, Is.GreaterThan(TimeSpan.FromSeconds(9)));
            Assert.That(drunkRemaining, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(10.5)));
        });
    }

    [Test]
    public async Task CastIn500BandCanTriggerSevereDrunkBranch()
    {
        var outcome = await Trigger500BandOutcomeAsync(CollapseDrunkSeed, "tests.phase1.500.drunk");

        Assert.That(outcome.DrunkRemaining, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(18)));
    }

    [Test]
    public async Task CastIn500BandCanTriggerStaminaCollapseBranch()
    {
        var outcome = await Trigger500BandOutcomeAsync(CollapseStaminaSeed, "tests.phase1.500.stamina");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.StaminaCritical, Is.True);
            Assert.That(outcome.StunRemaining, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(4.5)));
        });
    }

    [Test]
    public async Task CastIn550BandDropsOneToThreeHeldOrWornItems()
    {
        await EnsureWarpRuntimeAsync();
        await SetServerRandomSeedAsync(TopTierSeed);
        await EquipDropCandidatesAsync();

        var before = 0;
        var after = 0;

        await Server.WaitPost(() =>
        {
            before = CountDroppableItems(SPlayer);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 560f, "tests.phase1.550"));
            after = CountDroppableItems(SPlayer);
        });

        var dropped = before - after;
        Assert.That(dropped, Is.InRange(1, 3));
    }

    [Test]
    public async Task GlobalPulseStartsAt500AndEscalatesAt550()
    {
        await EnsureWarpRuntimeAsync();
        var controller = await GetClientChatControllerAsync();

        await Client.WaitPost(() => controller.History.Clear());

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 490f, "tests.phase1.490"));
        });
        await RunTicks(5);

        var tier500Text = await GetLocalizedServerStringAsync("wh40k-warp-instability-global-pulse-500");
        var tier550Text = await GetLocalizedServerStringAsync("wh40k-warp-instability-global-pulse-550");

        Assert.That(await GetMatchingMessageCountAsync(controller, tier500Text), Is.EqualTo(0));
        Assert.That(await GetMatchingMessageCountAsync(controller, tier550Text), Is.EqualTo(0));

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 20f, "tests.phase1.500-start"));
        });

        var firstPulse = await WaitForMessageAsync(controller, tier500Text);
        Assert.That(firstPulse.Message, Is.EqualTo(tier500Text));

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 50f, "tests.phase1.550-start"));
        });

        var secondPulse = await WaitForMessageAsync(controller, tier550Text);
        Assert.That(secondPulse.Message, Is.EqualTo(tier550Text));
    }

    private async Task<(bool StaminaCritical, TimeSpan DrunkRemaining, TimeSpan StunRemaining)> Trigger500BandOutcomeAsync(int seed, string sourceKey)
    {
        await EnsureWarpRuntimeAsync();
        await SetServerRandomSeedAsync(seed);

        var staminaCritical = false;
        var drunkRemaining = TimeSpan.Zero;
        var stunRemaining = TimeSpan.Zero;

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new WH40KWarpInstabilityContributionEvent(SPlayer, 520f, sourceKey));

            drunkRemaining = TryGetRemainingStatusTime(SPlayer, SharedDrunkSystem.Drunk);
            stunRemaining = TryGetRemainingStatusTime(SPlayer, SharedStunSystem.StunId);
            staminaCritical = SEntMan.TryGetComponent(SPlayer, out StaminaComponent? stamina) &&
                              (stamina.Critical || stamina.StaminaDamage >= stamina.CritThreshold);
        });

        return (staminaCritical, drunkRemaining, stunRemaining);
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

    private async Task EquipDropCandidatesAsync()
    {
        await Server.WaitPost(() =>
        {
            var hands = SEntMan.System<HandsSystem>();
            var inventory = SEntMan.System<InventorySystem>();
            var coords = SEntMan.GetCoordinates(PlayerCoords);

            var crowbar = SEntMan.SpawnEntity("Crowbar", coords);
            var lantern = SEntMan.SpawnEntity("FlashlightLantern", coords);
            var jumpsuit = SEntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", coords);
            var mask = SEntMan.SpawnEntity("ClothingMaskBreath", coords);
            var head = SEntMan.SpawnEntity("ClothingHeadHatBeretEngineering", coords);

            Assert.That(hands.TryPickupAnyHand(SPlayer, crowbar, checkActionBlocker: false, animateUser: false, animate: false));
            Assert.That(hands.TryPickupAnyHand(SPlayer, lantern, checkActionBlocker: false, animateUser: false, animate: false));
            Assert.That(inventory.TryEquip(SPlayer, jumpsuit, "jumpsuit"), Is.True);
            Assert.That(inventory.TryEquip(SPlayer, mask, "mask"), Is.True);
            Assert.That(inventory.TryEquip(SPlayer, head, "head"), Is.True);
        });

        await RunTicks(2);
    }

    private int CountDroppableItems(EntityUid uid)
    {
        var count = 0;

        if (SEntMan.TryGetComponent(uid, out HandsComponent? hands))
            count += SEntMan.System<HandsSystem>().EnumerateHeld((uid, hands)).Count();

        if (!SEntMan.TryGetComponent(uid, out InventoryComponent? inventory))
            return count;

        var enumerator = SEntMan.System<InventorySystem>().GetSlotEnumerator((uid, inventory),
            SlotFlags.HEAD |
            SlotFlags.EYES |
            SlotFlags.EARS |
            SlotFlags.MASK |
            SlotFlags.OUTERCLOTHING |
            SlotFlags.INNERCLOTHING |
            SlotFlags.NECK |
            SlotFlags.BACK |
            SlotFlags.BELT |
            SlotFlags.GLOVES |
            SlotFlags.LEGS |
            SlotFlags.FEET |
            SlotFlags.SUITSTORAGE);

        while (enumerator.NextItem(out _))
        {
            count++;
        }

        return count;
    }

    private TimeSpan GetRemainingStatusTime(EntityUid uid, string effectProtoId)
    {
        var statusEffects = SEntMan.System<StatusEffectsSystem>();
        Assert.That(statusEffects.TryGetTime(uid, effectProtoId, out var time), Is.True);
        Assert.That(time.EndEffectTime, Is.Not.Null);
        return time.EndEffectTime!.Value - STiming.CurTime;
    }

    private TimeSpan TryGetRemainingStatusTime(EntityUid uid, string effectProtoId)
    {
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
