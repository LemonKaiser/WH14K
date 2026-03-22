using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Alerts;
using Content.IntegrationTests.Tests.Movement;
using Content.Server._WH40K.MetaProgress;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Damage.Systems;
using Content.Server.Body.Systems;
using Content.Server.Drunk;
using Content.Server.Jittering;
using Content.Server.Stunnable;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Atmos;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Drunk;
using Content.Shared.FixedPoint;
using Content.Shared.DoAfter;
using Content.Shared.EntityEffects;
using Content.Shared.EntityEffects.Effects.Body;
using Content.Shared.Interaction.Events;
using Content.Shared.Jittering;
using Content.Shared.Medical.Healing;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Rejuvenate;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WH40K.MetaProgress;

public sealed class WH40KCharacterDevelopmentIntegrationTests : MovementTest
{
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";
    private static readonly ProtoId<AlertCategoryPrototype> StomachImpulseAlertCategory = "WH40KCharacterDevelopmentStomachImpulse";
    private static readonly ProtoId<AlertCategoryPrototype> KidneyPurgeAlertCategory = "WH40KCharacterDevelopmentKidneyPurge";
    private static readonly ProtoId<AlertCategoryPrototype> WarFurnaceAlertCategory = "WH40KCharacterDevelopmentWarFurnace";
    private static readonly ProtoId<AlertPrototype> StomachImpulseActiveAlert = "WH40KCharacterDevelopmentStomachImpulseActive";
    private static readonly ProtoId<AlertPrototype> StomachImpulseCooldownAlert = "WH40KCharacterDevelopmentStomachImpulseCooldown";
    private static readonly ProtoId<AlertPrototype> KidneyPurgeReadyAlert = "WH40KCharacterDevelopmentKidneyPurgeReady";
    private static readonly ProtoId<AlertPrototype> KidneyPurgeCooldownAlert = "WH40KCharacterDevelopmentKidneyPurgeCooldown";
    private static readonly ProtoId<AlertPrototype> WarFurnaceReadyAlert = "WH40KCharacterDevelopmentWarFurnaceReady";
    private static readonly ProtoId<AlertPrototype> WarFurnaceActiveAlert = "WH40KCharacterDevelopmentWarFurnaceActive";
    private static readonly ProtoId<AlertPrototype> WarFurnaceCooldownAlert = "WH40KCharacterDevelopmentWarFurnaceCooldown";

    private static readonly string[] BrainNodes =
    [
        "brain-root",
        "brain-u1",
        "brain-u2",
        "brain-u3",
        "brain-d1",
        "brain-d2",
        "brain-d3"
    ];

    private static readonly string[] StomachNodes =
    [
        "stomach-root",
        "stomach-u1",
        "stomach-u2",
        "stomach-u3",
        "stomach-d1",
        "stomach-d2",
        "stomach-d3"
    ];

    private static readonly string[] LungNodes =
    [
        "lungs-root",
        "lungs-u1",
        "lungs-u2",
        "lungs-u3",
        "lungs-d1",
        "lungs-d2",
        "lungs-d3"
    ];

    private static readonly string[] HeartNodes =
    [
        "heart-root",
        "heart-u1",
        "heart-u2",
        "heart-u3",
        "heart-d1",
        "heart-d2",
        "heart-d3"
    ];

    private static readonly string[] KidneyNodes =
    [
        "kidneys-root",
        "kidneys-u1",
        "kidneys-u2",
        "kidneys-u3",
        "kidneys-d1",
        "kidneys-d2",
        "kidneys-d3"
    ];

    private static readonly string[] LiverNodes =
    [
        "liver-root",
        "liver-u1",
        "liver-u2",
        "liver-u3",
        "liver-d1",
        "liver-d2",
        "liver-d3"
    ];

    private readonly EntProtoId _food = "FoodCakeVanillaSlice";
    private readonly EntProtoId _healingItem = "Brutepack1";

    protected override int Tiles => 8;

    [Test]
    public async Task DevelopmentModifiersApplyOnRespawn()
    {
        EntityUid respawned = default;

        await Server.WaitPost(() =>
        {
            UnlockNodes(ServerSession.UserId, StomachNodes);
            UnlockNodes(ServerSession.UserId, LungNodes);
            UnlockNodes(ServerSession.UserId, HeartNodes);
            UnlockNodes(ServerSession.UserId, KidneyNodes);

            var spawnCoords = SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(0f, 1f));
            respawned = SEntMan.SpawnEntity(PlayerPrototype, spawnCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, respawned);
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var bloodstreamSystem = SEntMan.System<BloodstreamSystem>();
            var alerts = Server.System<AlertsSystem>();
            var modifiers = SEntMan.GetComponent<WH40KCharacterDevelopmentModifiersComponent>(respawned);
            var baseline = SEntMan.GetComponent<WH40KCharacterDevelopmentBaselineComponent>(respawned);
            var hunger = SEntMan.GetComponent<HungerComponent>(respawned);
            var thirst = SEntMan.GetComponent<ThirstComponent>(respawned);
            var stamina = SEntMan.GetComponent<StaminaComponent>(respawned);
            var respirator = SEntMan.GetComponent<RespiratorComponent>(respawned);
            var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(respawned);
            var abilityState = SEntMan.GetComponent<WH40KCharacterDevelopmentAbilityStateComponent>(respawned);

            Assert.Multiple(() =>
            {
                Assert.That(modifiers.HungerDecayMultiplier, Is.EqualTo(0.85f).Within(0.0001f));
                Assert.That(modifiers.ThirstDecayMultiplier, Is.EqualTo(0.85f).Within(0.0001f));
                Assert.That(modifiers.HungerSatiationMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(modifiers.ThirstSatiationMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(modifiers.EatDelayMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(modifiers.StaminaSprintDrainMultiplier, Is.EqualTo(0.85f).Within(0.0001f));
                Assert.That(modifiers.StaminaWalkRecoveryMultiplier, Is.EqualTo(1.10f).Within(0.0001f));
                Assert.That(modifiers.StaminaCooldownMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(modifiers.MaxSaturationMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(modifiers.SuffocationDamageMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(modifiers.BloodRefreshMultiplier, Is.EqualTo(1.30f).Within(0.0001f));
                Assert.That(modifiers.BleedReductionMultiplier, Is.EqualTo(1.30f).Within(0.0001f));
                Assert.That(modifiers.BloodlossThresholdMultiplier, Is.EqualTo(0.65f).Within(0.0001f));
                Assert.That(modifiers.MaxBloodVolumeMultiplier, Is.EqualTo(1.05f).Within(0.0001f));
                Assert.That(modifiers.ToxinFilterMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(modifiers.StaminaIncomingDamageMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.StaminaCritThresholdMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.ForceStandStaminaMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.StaminaAfterCritRecoveryMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.StaminaCritStunTimeMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.KnockdownStandUpTimeMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.SelfHealPenaltyMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.SelfMedicalDelayMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.SelfHealingEffectMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.DrunkDurationMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.JitterDurationMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.DrowsinessDurationMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.VomitSlowdownDurationMultiplier, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(modifiers.StomachImpulseUnlocked, Is.True);
                Assert.That(modifiers.WarFurnaceUnlocked, Is.True);
                Assert.That(modifiers.KidneyPurgeUnlocked, Is.True);

                Assert.That(hunger.BaseDecayRate, Is.EqualTo(baseline.HungerBaseDecayRate * 0.85f).Within(0.0001f));
                Assert.That(thirst.BaseDecayRate, Is.EqualTo(baseline.ThirstBaseDecayRate * 0.85f).Within(0.0001f));
                Assert.That(stamina.SprintDrain, Is.EqualTo(baseline.StaminaSprintDrain * 0.85f).Within(0.0001f));
                Assert.That(stamina.WalkRecovery, Is.EqualTo(baseline.StaminaWalkRecovery * 1.10f).Within(0.0001f));
                Assert.That(stamina.Cooldown, Is.EqualTo(baseline.StaminaCooldown * 0.75f).Within(0.0001f));
                Assert.That(stamina.AfterCritDecayMultiplier, Is.EqualTo(baseline.StaminaAfterCritDecayMultiplier).Within(0.0001f));
                Assert.That(stamina.ForceStandStamina, Is.EqualTo(baseline.StaminaForceStandStamina).Within(0.0001f));
                Assert.That(stamina.StunTime.TotalSeconds, Is.EqualTo(baseline.StaminaStunTime.TotalSeconds).Within(0.0001f));
                Assert.That(stamina.CritThreshold, Is.EqualTo(stamina.BaseCritThreshold).Within(0.0001f));
                Assert.That(respirator.MaxSaturation, Is.EqualTo(baseline.RespiratorMaxSaturation * 1.25f).Within(0.0001f));
                Assert.That(bloodstreamSystem.GetBloodRefreshAmount((respawned, bloodstream)).Float(), Is.EqualTo(baseline.BloodRefreshAmount.Float() * 1.30f).Within(0.0001f));
                Assert.That(bloodstreamSystem.GetBleedReductionAmount((respawned, bloodstream)), Is.EqualTo(baseline.BleedReductionAmount * 1.30f).Within(0.0001f));
                Assert.That(bloodstream.BloodlossThreshold, Is.EqualTo(baseline.BloodlossThreshold * 0.65f).Within(0.0001f));
                Assert.That(bloodstream.MaxVolumeModifier, Is.EqualTo(baseline.MaxBloodVolumeModifier * 1.05f).Within(0.0001f));
                Assert.That(abilityState.NextKidneyPurgeReadyTime, Is.EqualTo(TimeSpan.Zero));
                Assert.That(abilityState.NextWarFurnaceReadyTime, Is.EqualTo(TimeSpan.Zero));
                Assert.That(GetShownAlertType(alerts, respawned, KidneyPurgeAlertCategory), Is.EqualTo(KidneyPurgeReadyAlert));
                Assert.That(GetShownAlertType(alerts, respawned, WarFurnaceAlertCategory), Is.EqualTo(WarFurnaceReadyAlert));
            });
        });
    }

    [Test]
    public async Task LiverModifiersImproveSelfHealingAndReduceChemicalStatusDurations()
    {
        EntityUid buffed = default;
        EntityUid control = default;
        float buffedDamageBeforeHeal = 0f;
        float controlDamageBeforeHeal = 0f;
        float buffedDamageAfterHeal = 0f;
        float controlDamageAfterHeal = 0f;
        float buffedHealingEffectMultiplier = 0f;
        float rawBuffedHealingAmount = 0f;
        float rawControlHealingAmount = 0f;
        TimeSpan buffedSelfHealDelay = TimeSpan.Zero;
        TimeSpan controlSelfHealDelay = TimeSpan.Zero;
        TimeSpan buffedDrunkTime = TimeSpan.Zero;
        TimeSpan controlDrunkTime = TimeSpan.Zero;
        TimeSpan buffedJitterTime = TimeSpan.Zero;
        TimeSpan controlJitterTime = TimeSpan.Zero;
        TimeSpan buffedDrowsinessTime = TimeSpan.Zero;
        TimeSpan controlDrowsinessTime = TimeSpan.Zero;
        TimeSpan buffedVomitTime = TimeSpan.Zero;
        TimeSpan controlVomitTime = TimeSpan.Zero;

        await Server.WaitPost(() =>
        {
            buffed = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(0f, 1f)));
            control = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(1f, 1f)));

            var modifiers = ApplyLiverModifiersForTest(buffed);
            Assert.Multiple(() =>
            {
                Assert.That(modifiers.SelfHealPenaltyMultiplier, Is.EqualTo(0.70f).Within(0.0001f));
                Assert.That(modifiers.SelfMedicalDelayMultiplier, Is.EqualTo(0.95f).Within(0.0001f));
                Assert.That(modifiers.SelfHealingEffectMultiplier, Is.EqualTo(1.20f).Within(0.0001f));
                Assert.That(modifiers.DrunkDurationMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(modifiers.JitterDurationMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(modifiers.DrowsinessDurationMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(modifiers.VomitSlowdownDurationMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
            });

            var damageable = SEntMan.System<DamageableSystem>();
            var healingSystem = SEntMan.System<HealingSystem>();
            var blunt = ProtoMan.Index(BluntDamageType);
            var damage = new DamageSpecifier(blunt, FixedPoint2.New(30f));

            SEntMan.EventBus.RaiseLocalEvent(buffed, new RejuvenateEvent());
            SEntMan.EventBus.RaiseLocalEvent(control, new RejuvenateEvent());
            Assert.That(damageable.TryChangeDamage(buffed, damage, ignoreResistances: true), Is.True);
            Assert.That(damageable.TryChangeDamage(control, damage, ignoreResistances: true), Is.True);

            var liverHealMultiplier = new WH40KModifySelfHealingEffectEvent(1f);
            SEntMan.EventBus.RaiseLocalEvent(buffed, ref liverHealMultiplier);
            buffedHealingEffectMultiplier = liverHealMultiplier.Multiplier;

            var healingPack = SEntMan.SpawnEntity(_healingItem, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(1f, 0f)));
            var healing = SEntMan.GetComponent<HealingComponent>(healingPack);
            var rawBuffedHealing = healing.Damage * (damageable.UniversalTopicalsHealModifier * buffedHealingEffectMultiplier);
            var rawControlHealing = healing.Damage * damageable.UniversalTopicalsHealModifier;

            Assert.That(
                damageable.TryChangeDamage(buffed, rawBuffedHealing, out var buffedHealed, ignoreResistances: true, interruptsDoAfters: false, origin: buffed),
                Is.True);
            Assert.That(
                damageable.TryChangeDamage(control, rawControlHealing, out var controlHealed, ignoreResistances: true, interruptsDoAfters: false, origin: control),
                Is.True);

            rawBuffedHealingAmount = -buffedHealed.GetTotal().Float();
            rawControlHealingAmount = -controlHealed.GetTotal().Float();

            SEntMan.EventBus.RaiseLocalEvent(buffed, new RejuvenateEvent());
            SEntMan.EventBus.RaiseLocalEvent(control, new RejuvenateEvent());
            Assert.That(damageable.TryChangeDamage(buffed, damage, ignoreResistances: true), Is.True);
            Assert.That(damageable.TryChangeDamage(control, damage, ignoreResistances: true), Is.True);

            buffedDamageBeforeHeal = damageable.GetTotalDamage(buffed).Float();
            controlDamageBeforeHeal = damageable.GetTotalDamage(control).Float();

            StartSelfHealing(buffed, _healingItem);
            StartSelfHealing(control, _healingItem);

            var buffedPenalty = healingSystem.GetScaledHealingPenalty(
                (buffed,
                    SEntMan.GetComponentOrNull<DamageableComponent>(buffed),
                    SEntMan.GetComponentOrNull<Content.Shared.Mobs.Components.MobThresholdsComponent>(buffed)),
                healing.SelfHealPenaltyMultiplier);
            var controlPenalty = healingSystem.GetScaledHealingPenalty(
                (control,
                    SEntMan.GetComponentOrNull<DamageableComponent>(control),
                    SEntMan.GetComponentOrNull<Content.Shared.Mobs.Components.MobThresholdsComponent>(control)),
                healing.SelfHealPenaltyMultiplier);
            var expectedBuffedDelay = healing.Delay * (1f + (buffedPenalty - 1f) * 0.70f) * 0.95f;
            var expectedControlDelay = healing.Delay * controlPenalty;
            SEntMan.DeleteEntity(healingPack);

            buffedSelfHealDelay = GetActiveDoAfterDelay(buffed);
            controlSelfHealDelay = GetActiveDoAfterDelay(control);

            Assert.Multiple(() =>
            {
                Assert.That(buffedDamageBeforeHeal, Is.EqualTo(controlDamageBeforeHeal).Within(0.001f));
                Assert.That(buffedHealingEffectMultiplier, Is.EqualTo(1.20f).Within(0.0001f));
                Assert.That(rawBuffedHealingAmount, Is.GreaterThan(rawControlHealingAmount));
                Assert.That(rawBuffedHealingAmount / rawControlHealingAmount, Is.EqualTo(1.20f).Within(0.05f));
                Assert.That(buffedSelfHealDelay.TotalSeconds, Is.EqualTo(expectedBuffedDelay.TotalSeconds).Within(0.1f));
                Assert.That(controlSelfHealDelay.TotalSeconds, Is.EqualTo(expectedControlDelay.TotalSeconds).Within(0.1f));
            });
        });

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(buffedSelfHealDelay, Is.LessThan(controlSelfHealDelay));
                Assert.That(HasActiveDoAfter(buffed), Is.True);
                Assert.That(HasActiveDoAfter(control), Is.True);
            });
        });

        await RunSeconds((float) buffedSelfHealDelay.TotalSeconds + 0.35f);

        await Server.WaitAssertion(() =>
        {
            var damageable = SEntMan.System<DamageableSystem>();
            buffedDamageAfterHeal = damageable.GetTotalDamage(buffed).Float();

            Assert.Multiple(() =>
            {
                Assert.That(HasActiveDoAfter(buffed), Is.False);
                Assert.That(HasActiveDoAfter(control), Is.True);
                Assert.That(buffedDamageAfterHeal, Is.LessThan(buffedDamageBeforeHeal));
            });
        });

        await RunSeconds(MathF.Max(0.35f, (float) (controlSelfHealDelay - buffedSelfHealDelay).TotalSeconds + 0.35f));

        await Server.WaitAssertion(() =>
        {
            var damageable = SEntMan.System<DamageableSystem>();
            controlDamageAfterHeal = damageable.GetTotalDamage(control).Float();

            Assert.Multiple(() =>
            {
                Assert.That(HasActiveDoAfter(buffed), Is.False);
                Assert.That(HasActiveDoAfter(control), Is.False);
                Assert.That(buffedDamageAfterHeal, Is.LessThan(buffedDamageBeforeHeal));
                Assert.That(controlDamageAfterHeal, Is.LessThan(controlDamageBeforeHeal));
            });
        });

        await Server.WaitPost(() =>
        {
            var drunkSystem = SEntMan.System<DrunkSystem>();
            var jitterSystem = SEntMan.System<JitteringSystem>();
            var statusEffects = SEntMan.System<StatusEffectsSystem>();
            var movementStatus = SEntMan.System<MovementModStatusSystem>();

            SEntMan.EventBus.RaiseLocalEvent(buffed, new RejuvenateEvent());
            SEntMan.EventBus.RaiseLocalEvent(control, new RejuvenateEvent());

            drunkSystem.TryApplyDrunkenness(buffed, TimeSpan.FromSeconds(20));
            drunkSystem.TryApplyDrunkenness(control, TimeSpan.FromSeconds(20));
            jitterSystem.DoJitter(buffed, TimeSpan.FromSeconds(20), true);
            jitterSystem.DoJitter(control, TimeSpan.FromSeconds(20), true);
            Assert.That(statusEffects.TryAddStatusEffectDuration(buffed, "StatusEffectDrowsiness", TimeSpan.FromSeconds(20)), Is.True);
            Assert.That(statusEffects.TryAddStatusEffectDuration(control, "StatusEffectDrowsiness", TimeSpan.FromSeconds(20)), Is.True);
            Assert.That(movementStatus.TryUpdateMovementSpeedModDuration(buffed, MovementModStatusSystem.VomitingSlowdown, TimeSpan.FromSeconds(20), 0.5f), Is.True);
            Assert.That(movementStatus.TryUpdateMovementSpeedModDuration(control, MovementModStatusSystem.VomitingSlowdown, TimeSpan.FromSeconds(20), 0.5f), Is.True);

            buffedDrunkTime = GetRemainingStatusTime(buffed, SharedDrunkSystem.Drunk);
            controlDrunkTime = GetRemainingStatusTime(control, SharedDrunkSystem.Drunk);
            buffedDrowsinessTime = GetRemainingStatusTime(buffed, "StatusEffectDrowsiness");
            controlDrowsinessTime = GetRemainingStatusTime(control, "StatusEffectDrowsiness");
            buffedVomitTime = GetRemainingStatusTime(buffed, MovementModStatusSystem.VomitingSlowdown);
            controlVomitTime = GetRemainingStatusTime(control, MovementModStatusSystem.VomitingSlowdown);
            buffedJitterTime = GetRemainingOldStatusTime(buffed, "Jitter");
            controlJitterTime = GetRemainingOldStatusTime(control, "Jitter");
        });

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(buffedDrunkTime, Is.LessThan(controlDrunkTime));
                Assert.That(buffedDrowsinessTime, Is.LessThan(controlDrowsinessTime));
                Assert.That(buffedVomitTime, Is.LessThan(controlVomitTime));
                Assert.That(buffedJitterTime, Is.LessThan(controlJitterTime));
                Assert.That(buffedDrunkTime.TotalSeconds / controlDrunkTime.TotalSeconds, Is.EqualTo(0.75f).Within(0.05f));
                Assert.That(buffedDrowsinessTime.TotalSeconds / controlDrowsinessTime.TotalSeconds, Is.EqualTo(0.75f).Within(0.05f));
                Assert.That(buffedVomitTime.TotalSeconds / controlVomitTime.TotalSeconds, Is.EqualTo(0.75f).Within(0.05f));
                Assert.That(buffedJitterTime.TotalSeconds / controlJitterTime.TotalSeconds, Is.EqualTo(0.75f).Within(0.05f));
            });
        });
    }

    private WH40KCharacterDevelopmentModifiersComponent ApplyLiverModifiersForTest(EntityUid uid)
    {
        var modifiers = SEntMan.EnsureComponent<WH40KCharacterDevelopmentModifiersComponent>(uid);
        modifiers.SelfHealPenaltyMultiplier = 0.70f;
        modifiers.SelfMedicalDelayMultiplier = 0.95f;
        modifiers.SelfHealingEffectMultiplier = 1.20f;
        modifiers.DrunkDurationMultiplier = 0.75f;
        modifiers.JitterDurationMultiplier = 0.75f;
        modifiers.DrowsinessDurationMultiplier = 0.75f;
        modifiers.VomitSlowdownDurationMultiplier = 0.75f;
        return modifiers;
    }

    [Test]
    public async Task StomachModifiersAffectDecaySatiationAndEatingTime()
    {
        EntityUid control = default;
        TimeSpan buffedTime = TimeSpan.Zero;
        TimeSpan controlTime = TimeSpan.Zero;
        float buffedHungerBefore = 0f;
        float controlHungerBefore = 0f;
        float buffedHungerAfter = 0f;
        float controlHungerAfter = 0f;
        float buffedThirstBefore = 0f;
        float controlThirstBefore = 0f;
        float buffedThirstAfter = 0f;
        float controlThirstAfter = 0f;
        float buffedGain = 0f;
        float controlGain = 0f;

        await Server.WaitPost(() =>
        {
            UnlockNodes(ServerSession.UserId, StomachNodes);

            control = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(0f, 1f)));

            var hungerSystem = SEntMan.System<HungerSystem>();
            var thirstSystem = SEntMan.System<ThirstSystem>();
            var buffedHunger = SEntMan.GetComponent<HungerComponent>(SPlayer);
            var controlHunger = SEntMan.GetComponent<HungerComponent>(control);
            var buffedThirst = SEntMan.GetComponent<ThirstComponent>(SPlayer);
            var controlThirst = SEntMan.GetComponent<ThirstComponent>(control);

            hungerSystem.SetHunger(SPlayer, buffedHunger.Thresholds[HungerThreshold.Okay], buffedHunger);
            hungerSystem.SetHunger(control, controlHunger.Thresholds[HungerThreshold.Okay], controlHunger);
            thirstSystem.SetThirst(SPlayer, buffedThirst, buffedThirst.ThirstThresholds[ThirstThreshold.Okay]);
            thirstSystem.SetThirst(control, controlThirst, controlThirst.ThirstThresholds[ThirstThreshold.Okay]);

            buffedHungerBefore = hungerSystem.GetHunger(buffedHunger);
            controlHungerBefore = hungerSystem.GetHunger(controlHunger);
            buffedThirstBefore = buffedThirst.CurrentThirst;
            controlThirstBefore = controlThirst.CurrentThirst;
        });

        await RunSeconds(60f);

        await Server.WaitAssertion(() =>
        {
            var hungerSystem = SEntMan.System<HungerSystem>();
            var buffedHunger = SEntMan.GetComponent<HungerComponent>(SPlayer);
            var controlHunger = SEntMan.GetComponent<HungerComponent>(control);
            var buffedThirst = SEntMan.GetComponent<ThirstComponent>(SPlayer);
            var controlThirst = SEntMan.GetComponent<ThirstComponent>(control);

            buffedHungerAfter = hungerSystem.GetHunger(buffedHunger);
            controlHungerAfter = hungerSystem.GetHunger(controlHunger);
            buffedThirstAfter = buffedThirst.CurrentThirst;
            controlThirstAfter = controlThirst.CurrentThirst;

            Assert.Multiple(() =>
            {
                Assert.That(buffedHungerAfter, Is.GreaterThan(controlHungerAfter));
                Assert.That(buffedThirstAfter, Is.GreaterThan(controlThirstAfter));
                Assert.That(buffedHungerAfter, Is.LessThan(buffedHungerBefore));
                Assert.That(controlHungerAfter, Is.LessThan(controlHungerBefore));
                Assert.That(buffedThirstAfter, Is.LessThan(buffedThirstBefore));
                Assert.That(controlThirstAfter, Is.LessThan(controlThirstBefore));
            });
        });

        await Server.WaitPost(() =>
        {
            var hungerSystem = SEntMan.System<HungerSystem>();
            var buffedHunger = SEntMan.GetComponent<HungerComponent>(SPlayer);
            var controlHunger = SEntMan.GetComponent<HungerComponent>(control);

            hungerSystem.SetHunger(SPlayer, buffedHunger.Thresholds[HungerThreshold.Peckish], buffedHunger);
            hungerSystem.SetHunger(control, controlHunger.Thresholds[HungerThreshold.Peckish], controlHunger);

            var beforeBuffed = hungerSystem.GetHunger(buffedHunger);
            var beforeControl = hungerSystem.GetHunger(controlHunger);
            var satiate = new SatiateHunger { Factor = 10f };
            var effect = new EntityEffectEvent<SatiateHunger>(satiate, 1f, null);

            SEntMan.EventBus.RaiseLocalEvent(SPlayer, ref effect);
            SEntMan.EventBus.RaiseLocalEvent(control, ref effect);

            buffedGain = hungerSystem.GetHunger(buffedHunger) - beforeBuffed;
            controlGain = hungerSystem.GetHunger(controlHunger) - beforeControl;

            var foodOne = SEntMan.SpawnEntity(_food, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(1f, 0f)));
            var foodTwo = SEntMan.SpawnEntity(_food, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(1f, 1f)));
            var ingestion = SEntMan.System<IngestionSystem>();

            Assert.That(ingestion.CanConsume(SPlayer, SPlayer, foodOne, out _, out var buffedDelay), Is.True);
            Assert.That(ingestion.CanConsume(control, control, foodTwo, out _, out var controlDelay), Is.True);

            buffedTime = buffedDelay ?? TimeSpan.Zero;
            controlTime = controlDelay ?? TimeSpan.Zero;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(buffedGain, Is.GreaterThan(controlGain));
                Assert.That(buffedGain / controlGain, Is.EqualTo(1.25f).Within(0.05f));
                Assert.That(buffedTime, Is.LessThan(controlTime));
                Assert.That(buffedTime.TotalSeconds / controlTime.TotalSeconds, Is.EqualTo(0.75f).Within(0.05f));
            });
        });
    }

    [Test]
    public async Task StomachImpulseAppliesTemporarySpeedBoostAndHonorsCooldown()
    {
        EntityUid control = default;
        float buffedSprintSpeed = 0f;
        float controlSprintSpeed = 0f;
        var cAlert = Client.System<AlertsSystem>();
        var sAlert = Server.System<AlertsSystem>();

        await Server.WaitPost(() =>
        {
            UnlockNodes(ServerSession.UserId, StomachNodes);
            control = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(0f, 1f)));

            var state = SEntMan.GetComponent<WH40KCharacterDevelopmentAbilityStateComponent>(SPlayer);
            Assert.That(state.NextStomachImpulseTime, Is.EqualTo(TimeSpan.Zero));

            TriggerFoodIngestion(SPlayer);
            TriggerFoodIngestion(control);
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            buffedSprintSpeed = SEntMan.GetComponent<MovementSpeedModifierComponent>(SPlayer).CurrentSprintSpeed;
            controlSprintSpeed = SEntMan.GetComponent<MovementSpeedModifierComponent>(control).CurrentSprintSpeed;

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WH40KCharacterDevelopmentSpeedBoostComponent>(SPlayer), Is.True);
                Assert.That(buffedSprintSpeed, Is.GreaterThan(controlSprintSpeed));
                Assert.That(buffedSprintSpeed / controlSprintSpeed, Is.EqualTo(1.10f).Within(0.03f));
                Assert.That(GetShownAlertType(sAlert, SPlayer, StomachImpulseAlertCategory), Is.EqualTo(StomachImpulseActiveAlert));
            });
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(GetShownAlertType(cAlert, CPlayer, StomachImpulseAlertCategory), Is.EqualTo(StomachImpulseActiveAlert));
        });

        await RunSeconds(61f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WH40KCharacterDevelopmentSpeedBoostComponent>(SPlayer), Is.False);
                Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(SPlayer).CurrentSprintSpeed,
                    Is.EqualTo(SEntMan.GetComponent<MovementSpeedModifierComponent>(control).CurrentSprintSpeed).Within(0.03f));
                Assert.That(GetShownAlertType(sAlert, SPlayer, StomachImpulseAlertCategory), Is.EqualTo(StomachImpulseCooldownAlert));
            });
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(GetShownAlertType(cAlert, CPlayer, StomachImpulseAlertCategory), Is.EqualTo(StomachImpulseCooldownAlert));
        });

        await Server.WaitPost(() =>
        {
            TriggerFoodIngestion(SPlayer);
            TriggerFoodIngestion(control);
        });

        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WH40KCharacterDevelopmentSpeedBoostComponent>(SPlayer), Is.False);
                Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(SPlayer).CurrentSprintSpeed,
                    Is.EqualTo(SEntMan.GetComponent<MovementSpeedModifierComponent>(control).CurrentSprintSpeed).Within(0.03f));
                Assert.That(GetShownAlertType(sAlert, SPlayer, StomachImpulseAlertCategory), Is.EqualTo(StomachImpulseCooldownAlert));
            });
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(GetShownAlertType(cAlert, CPlayer, StomachImpulseAlertCategory), Is.EqualTo(StomachImpulseCooldownAlert));
        });
    }

    [Test]
    public async Task WarFurnaceActionHealsOverTimeAndStartsCooldown()
    {
        float damageBefore = 0f;
        float damageAfter = 0f;
        var cAlert = Client.System<AlertsSystem>();
        var sAlert = Server.System<AlertsSystem>();

        await Server.WaitPost(() =>
        {
            UnlockNodes(ServerSession.UserId, StomachNodes);

            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new RejuvenateEvent());
            var damageable = SEntMan.System<DamageableSystem>();
            var blunt = ProtoMan.Index(BluntDamageType);
            Assert.That(
                damageable.TryChangeDamage(SPlayer, new DamageSpecifier(blunt, FixedPoint2.New(30f)), ignoreResistances: true),
                Is.True);

            damageBefore = damageable.GetTotalDamage(SPlayer).Float();
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(GetShownAlertType(sAlert, SPlayer, WarFurnaceAlertCategory), Is.EqualTo(WarFurnaceReadyAlert));
            });
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(GetShownAlertType(cAlert, CPlayer, WarFurnaceAlertCategory), Is.EqualTo(WarFurnaceReadyAlert));
        });

        await Client.WaitPost(() => Client.System<ClientAlertsSystem>().AlertClicked(WarFurnaceReadyAlert));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WH40KCharacterDevelopmentWarFurnaceActiveComponent>(SPlayer), Is.True);
                Assert.That(GetShownAlertType(sAlert, SPlayer, WarFurnaceAlertCategory), Is.EqualTo(WarFurnaceActiveAlert));
            });
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(GetShownAlertType(cAlert, CPlayer, WarFurnaceAlertCategory), Is.EqualTo(WarFurnaceActiveAlert));
        });

        await RunSeconds(5.4f);

        await Server.WaitAssertion(() =>
        {
            var damageable = SEntMan.System<DamageableSystem>();
            damageAfter = damageable.GetTotalDamage(SPlayer).Float();

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WH40KCharacterDevelopmentWarFurnaceActiveComponent>(SPlayer), Is.False);
                Assert.That(damageAfter, Is.LessThan(damageBefore));
                Assert.That(damageBefore - damageAfter, Is.EqualTo(12.5f).Within(1.0f));
                Assert.That(GetShownAlertType(sAlert, SPlayer, WarFurnaceAlertCategory), Is.EqualTo(WarFurnaceCooldownAlert));
            });
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(GetShownAlertType(cAlert, CPlayer, WarFurnaceAlertCategory), Is.EqualTo(WarFurnaceCooldownAlert));
        });
    }

    [Test]
    public async Task KidneyPurgeActionRemovesOnlyToxinsAndStartsCooldown()
    {
        float toxinBefore = 0f;
        float toxinAfter = 0f;
        float medicineBefore = 0f;
        float medicineAfter = 0f;
        var cAlert = Client.System<AlertsSystem>();
        var sAlert = Server.System<AlertsSystem>();

        await Server.WaitPost(() =>
        {
            UnlockNodes(ServerSession.UserId, KidneyNodes);

            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new RejuvenateEvent());
            AddBloodstreamReagents(SPlayer, ("Amatoxin", 8f), ("Inaprovaline", 8f));

            toxinBefore = GetBloodstreamQuantity(SPlayer, "Amatoxin");
            medicineBefore = GetBloodstreamQuantity(SPlayer, "Inaprovaline");
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(GetShownAlertType(sAlert, SPlayer, KidneyPurgeAlertCategory), Is.EqualTo(KidneyPurgeReadyAlert));
            });
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(GetShownAlertType(cAlert, CPlayer, KidneyPurgeAlertCategory), Is.EqualTo(KidneyPurgeReadyAlert));
        });

        await Client.WaitPost(() => Client.System<ClientAlertsSystem>().AlertClicked(KidneyPurgeReadyAlert));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            toxinAfter = GetBloodstreamQuantity(SPlayer, "Amatoxin");
            medicineAfter = GetBloodstreamQuantity(SPlayer, "Inaprovaline");

            Assert.Multiple(() =>
            {
                Assert.That(toxinBefore - toxinAfter, Is.EqualTo(5f).Within(0.15f));
                Assert.That(medicineAfter, Is.EqualTo(medicineBefore).Within(0.05f));
                Assert.That(GetShownAlertType(sAlert, SPlayer, KidneyPurgeAlertCategory), Is.EqualTo(KidneyPurgeCooldownAlert));
            });
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(GetShownAlertType(cAlert, CPlayer, KidneyPurgeAlertCategory), Is.EqualTo(KidneyPurgeCooldownAlert));
        });
    }

    [Test]
    public async Task LungModifiersImproveSprintDrainAndReduceSuffocationDamage()
    {
        float baselineSprintDamage = 0f;
        float buffedSprintDamage = 0f;
        EntityUid control = default;
        float buffedSuffocationDamage = 0f;
        float controlSuffocationDamage = 0f;

        await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(SPlayer, new RejuvenateEvent()));
        baselineSprintDamage = await MeasureSprintDamage(SPlayer);

        await Server.WaitPost(() =>
        {
            UnlockNodes(ServerSession.UserId, LungNodes);
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new RejuvenateEvent());
        });
        buffedSprintDamage = await MeasureSprintDamage(SPlayer);

        await Server.WaitAssertion(() =>
        {
            var stamina = SEntMan.GetComponent<StaminaComponent>(SPlayer);
            var baseline = SEntMan.GetComponent<WH40KCharacterDevelopmentBaselineComponent>(SPlayer);

            Assert.Multiple(() =>
            {
                Assert.That(buffedSprintDamage, Is.GreaterThan(0f));
                Assert.That(buffedSprintDamage, Is.LessThan(baselineSprintDamage));
                Assert.That(stamina.SprintDrain, Is.LessThan(baseline.StaminaSprintDrain));
                Assert.That(stamina.Cooldown, Is.LessThan(baseline.StaminaCooldown));
            });
        });

        await Server.WaitPost(() =>
        {
            control = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(0f, 1f)));
            SEntMan.EventBus.RaiseLocalEvent(control, new RejuvenateEvent());

            var atmos = SEntMan.System<AtmosphereSystem>();
            atmos.SetMapAtmosphere(MapData.MapUid, false, GasMixture.SpaceGas);
        });

        await RunSeconds(8f);

        await Server.WaitAssertion(() =>
        {
            var damageable = SEntMan.System<DamageableSystem>();
            buffedSuffocationDamage = damageable.GetTotalDamage(SPlayer).Float();
            controlSuffocationDamage = damageable.GetTotalDamage(control).Float();

            Assert.Multiple(() =>
            {
                Assert.That(buffedSuffocationDamage, Is.GreaterThan(0f));
                Assert.That(controlSuffocationDamage, Is.GreaterThan(0f));
                Assert.That(buffedSuffocationDamage, Is.LessThan(controlSuffocationDamage));
                Assert.That(SEntMan.GetComponent<RespiratorComponent>(SPlayer).MaxSaturation, Is.GreaterThan(SEntMan.GetComponent<WH40KCharacterDevelopmentBaselineComponent>(SPlayer).RespiratorMaxSaturation));
            });
        });
    }

    [Test]
    public async Task BrainModifiersImproveStaminaControlAndRecoveryActions()
    {
        EntityUid control = default;
        float buffedIncomingDamage = 0f;
        float controlIncomingDamage = 0f;
        float buffedForceStandCost = 0f;
        float expectedForceStandCost = 0f;
        TimeSpan buffedStandTime = TimeSpan.Zero;
        TimeSpan baselineStandTime = TimeSpan.Zero;

        await Server.WaitPost(() =>
        {
            UnlockNodes(ServerSession.UserId, BrainNodes);
            control = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(0f, 1f)));

            var modifiers = SEntMan.GetComponent<WH40KCharacterDevelopmentModifiersComponent>(SPlayer);
            var baseline = SEntMan.GetComponent<WH40KCharacterDevelopmentBaselineComponent>(SPlayer);
            var buffedStamina = SEntMan.GetComponent<StaminaComponent>(SPlayer);

            Assert.Multiple(() =>
            {
                Assert.That(modifiers.StaminaIncomingDamageMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(modifiers.StaminaCritThresholdMultiplier, Is.EqualTo(1.15f).Within(0.0001f));
                Assert.That(modifiers.ForceStandStaminaMultiplier, Is.EqualTo(0.80f).Within(0.0001f));
                Assert.That(modifiers.StaminaAfterCritRecoveryMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(modifiers.StaminaCritStunTimeMultiplier, Is.EqualTo(0.90f).Within(0.0001f));
                Assert.That(modifiers.KnockdownStandUpTimeMultiplier, Is.EqualTo(0.90f).Within(0.0001f));
                Assert.That(buffedStamina.CritThreshold, Is.EqualTo(buffedStamina.BaseCritThreshold * 1.15f).Within(0.0001f));
                Assert.That(buffedStamina.AfterCritDecayMultiplier, Is.EqualTo(baseline.StaminaAfterCritDecayMultiplier * 1.25f).Within(0.0001f));
                Assert.That(buffedStamina.ForceStandStamina, Is.EqualTo(baseline.StaminaForceStandStamina * 0.80f).Within(0.0001f));
                Assert.That(buffedStamina.StunTime.TotalSeconds, Is.EqualTo(baseline.StaminaStunTime.TotalSeconds * 0.90f).Within(0.0001f));
            });

            expectedForceStandCost = buffedStamina.ForceStandStamina;
            baselineStandTime = SEntMan.GetComponent<CrawlerComponent>(SPlayer).StandTime;
            var buffedStand = new GetStandUpTimeEvent(baselineStandTime);
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, ref buffedStand);
            buffedStandTime = buffedStand.DoAfterTime;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(buffedStandTime, Is.LessThan(baselineStandTime));
                Assert.That(buffedStandTime.TotalSeconds / baselineStandTime.TotalSeconds, Is.EqualTo(0.90f).Within(0.05f));
            });
        });

        await Server.WaitPost(() =>
        {
            var staminaSystem = SEntMan.System<StaminaSystem>();
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new RejuvenateEvent());
            SEntMan.EventBus.RaiseLocalEvent(control, new RejuvenateEvent());

            staminaSystem.TakeStaminaDamage(SPlayer, 80f, visual: false, log: false, applyCooldown: false);
            staminaSystem.TakeStaminaDamage(control, 80f, visual: false, log: false, applyCooldown: false);
        });

        await Server.WaitAssertion(() =>
        {
            var buffedStamina = SEntMan.GetComponent<StaminaComponent>(SPlayer);
            var controlStamina = SEntMan.GetComponent<StaminaComponent>(control);
            buffedIncomingDamage = buffedStamina.StaminaDamage;
            controlIncomingDamage = controlStamina.StaminaDamage;

            Assert.Multiple(() =>
            {
                Assert.That(controlStamina.Critical, Is.False);
                Assert.That(buffedStamina.Critical, Is.False);
                Assert.That(buffedIncomingDamage, Is.GreaterThan(0f));
                Assert.That(controlIncomingDamage, Is.GreaterThan(0f));
                Assert.That(buffedIncomingDamage / controlIncomingDamage, Is.EqualTo(0.75f).Within(0.02f));
            });
        });

        await Server.WaitPost(() =>
        {
            var staminaSystem = SEntMan.System<StaminaSystem>();
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new RejuvenateEvent());
            SEntMan.EventBus.RaiseLocalEvent(control, new RejuvenateEvent());

            staminaSystem.TakeStaminaDamage(SPlayer, 120f, visual: false, log: false, applyCooldown: false);
            staminaSystem.TakeStaminaDamage(control, 120f, visual: false, log: false, applyCooldown: false);
        });

        await Server.WaitAssertion(() =>
        {
            var buffedStamina = SEntMan.GetComponent<StaminaComponent>(SPlayer);
            var controlStamina = SEntMan.GetComponent<StaminaComponent>(control);

            Assert.Multiple(() =>
            {
                Assert.That(controlStamina.Critical, Is.True);
                Assert.That(buffedStamina.Critical, Is.False);
                Assert.That(controlStamina.StaminaDamage, Is.EqualTo(controlStamina.CritThreshold).Within(0.001f));
                Assert.That(buffedStamina.StaminaDamage, Is.LessThan(buffedStamina.CritThreshold));
            });
        });

        await Server.WaitPost(() =>
        {
            var stunSystem = SEntMan.System<StunSystem>();
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new RejuvenateEvent());

            Assert.That(stunSystem.TryKnockdown(SPlayer, TimeSpan.FromSeconds(0.1), autoStand: false, drop: false), Is.True);
        });

        await RunSeconds(0.3f);

        await Server.WaitPost(() =>
        {
            var stunSystem = SEntMan.System<StunSystem>();
            stunSystem.SetKnockdownTime(SPlayer, TimeSpan.Zero);
            stunSystem.ForceStandUp(SPlayer);
            buffedForceStandCost = SEntMan.GetComponent<StaminaComponent>(SPlayer).StaminaDamage;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(buffedForceStandCost, Is.GreaterThan(0f));
                Assert.That(buffedForceStandCost, Is.EqualTo(expectedForceStandCost).Within(0.05f));
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(SPlayer), Is.False);
            });
        });
    }

    [Test]
    public async Task HeartModifiersImproveBleedingBloodRefreshAndReserve()
    {
        EntityUid control = default;
        string bloodReagentId = string.Empty;
        float buffedBleedAfter = 0f;
        float controlBleedAfter = 0f;
        float buffedBloodBefore = 0f;
        float controlBloodBefore = 0f;
        float buffedBloodAfter = 0f;
        float controlBloodAfter = 0f;

        await Server.WaitPost(() =>
        {
            UnlockNodes(ServerSession.UserId, HeartNodes);
            control = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(0f, 1f)));

            var bloodstreamSystem = SEntMan.System<BloodstreamSystem>();
            var baseline = SEntMan.GetComponent<WH40KCharacterDevelopmentBaselineComponent>(SPlayer);
            var buffedBloodstream = SEntMan.GetComponent<BloodstreamComponent>(SPlayer);

            Assert.Multiple(() =>
            {
                Assert.That(bloodstreamSystem.GetBloodRefreshAmount((SPlayer, buffedBloodstream)).Float(), Is.EqualTo(baseline.BloodRefreshAmount.Float() * 1.30f).Within(0.0001f));
                Assert.That(bloodstreamSystem.GetBleedReductionAmount((SPlayer, buffedBloodstream)), Is.EqualTo(baseline.BleedReductionAmount * 1.30f).Within(0.0001f));
                Assert.That(buffedBloodstream.BloodlossThreshold, Is.EqualTo(baseline.BloodlossThreshold * 0.75f).Within(0.0001f));
                Assert.That(buffedBloodstream.MaxVolumeModifier, Is.EqualTo(baseline.MaxBloodVolumeModifier * 1.05f).Within(0.0001f));
            });

            var buffedSolution = GetBloodSolution(SPlayer);
            var controlSolution = GetBloodSolution(control);
            bloodReagentId = GetBloodReferenceReagentId(SPlayer);

            Assert.That(buffedSolution.MaxVolume.Float() / controlSolution.MaxVolume.Float(), Is.EqualTo(1.05f).Within(0.01f));

            bloodstreamSystem.TryModifyBleedAmount((SPlayer, buffedBloodstream), 6f);
            bloodstreamSystem.TryModifyBleedAmount((control, SEntMan.GetComponent<BloodstreamComponent>(control)), 6f);
        });

        await RunSeconds(4f);

        await Server.WaitAssertion(() =>
        {
            buffedBleedAfter = SEntMan.GetComponent<BloodstreamComponent>(SPlayer).BleedAmount;
            controlBleedAfter = SEntMan.GetComponent<BloodstreamComponent>(control).BleedAmount;

            Assert.Multiple(() =>
            {
                Assert.That(buffedBleedAfter, Is.LessThan(controlBleedAfter));
                Assert.That(buffedBleedAfter, Is.LessThan(6f));
                Assert.That(controlBleedAfter, Is.LessThan(6f));
            });
        });

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new RejuvenateEvent());
            SEntMan.EventBus.RaiseLocalEvent(control, new RejuvenateEvent());

            BleedOutBlood(SPlayer, 180f);
            BleedOutBlood(control, 180f);

            buffedBloodBefore = GetBloodstreamQuantity(SPlayer, bloodReagentId);
            controlBloodBefore = GetBloodstreamQuantity(control, bloodReagentId);
        });

        await RunSeconds(4f);

        await Server.WaitAssertion(() =>
        {
            buffedBloodAfter = GetBloodstreamQuantity(SPlayer, bloodReagentId);
            controlBloodAfter = GetBloodstreamQuantity(control, bloodReagentId);
            var buffedRecovered = buffedBloodAfter - buffedBloodBefore;
            var controlRecovered = controlBloodAfter - controlBloodBefore;

            Assert.Multiple(() =>
            {
                Assert.That(buffedBloodBefore, Is.EqualTo(controlBloodBefore).Within(0.01f));
                Assert.That(buffedBloodBefore, Is.LessThan(300f));
                Assert.That(controlBloodBefore, Is.LessThan(300f));
                Assert.That(buffedBloodAfter, Is.GreaterThan(controlBloodAfter));
                Assert.That(buffedRecovered, Is.GreaterThan(controlRecovered));
                Assert.That(buffedRecovered / controlRecovered, Is.EqualTo(1.30f).Within(0.15f));
            });
        });
    }

    [Test]
    public async Task KidneyModifiersImproveThirstAndFilterOnlyToxins()
    {
        EntityUid control = default;
        float buffedThirstBefore = 0f;
        float controlThirstBefore = 0f;
        float buffedThirstAfter = 0f;
        float controlThirstAfter = 0f;
        float buffedGain = 0f;
        float controlGain = 0f;
        float buffedToxin = 0f;
        float controlToxin = 0f;
        float buffedMedicine = 0f;
        float controlMedicine = 0f;

        await Server.WaitPost(() =>
        {
            UnlockNodes(ServerSession.UserId, KidneyNodes);
            control = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(0f, 1f)));

            var thirstSystem = SEntMan.System<ThirstSystem>();
            var buffedThirst = SEntMan.GetComponent<ThirstComponent>(SPlayer);
            var controlThirst = SEntMan.GetComponent<ThirstComponent>(control);

            thirstSystem.SetThirst(SPlayer, buffedThirst, buffedThirst.ThirstThresholds[ThirstThreshold.Okay]);
            thirstSystem.SetThirst(control, controlThirst, controlThirst.ThirstThresholds[ThirstThreshold.Okay]);

            buffedThirstBefore = buffedThirst.CurrentThirst;
            controlThirstBefore = controlThirst.CurrentThirst;
        });

        await RunSeconds(60f);

        await Server.WaitAssertion(() =>
        {
            buffedThirstAfter = SEntMan.GetComponent<ThirstComponent>(SPlayer).CurrentThirst;
            controlThirstAfter = SEntMan.GetComponent<ThirstComponent>(control).CurrentThirst;

            Assert.Multiple(() =>
            {
                Assert.That(buffedThirstAfter, Is.GreaterThan(controlThirstAfter));
                Assert.That(buffedThirstAfter, Is.LessThan(buffedThirstBefore));
                Assert.That(controlThirstAfter, Is.LessThan(controlThirstBefore));
            });
        });

        await Server.WaitPost(() =>
        {
            var thirstSystem = SEntMan.System<ThirstSystem>();
            var buffedThirst = SEntMan.GetComponent<ThirstComponent>(SPlayer);
            var controlThirst = SEntMan.GetComponent<ThirstComponent>(control);

            thirstSystem.SetThirst(SPlayer, buffedThirst, buffedThirst.ThirstThresholds[ThirstThreshold.Thirsty]);
            thirstSystem.SetThirst(control, controlThirst, controlThirst.ThirstThresholds[ThirstThreshold.Thirsty]);

            var beforeBuffed = buffedThirst.CurrentThirst;
            var beforeControl = controlThirst.CurrentThirst;
            var satiate = new SatiateThirst { Factor = 10f };
            var effect = new EntityEffectEvent<SatiateThirst>(satiate, 1f, null);

            SEntMan.EventBus.RaiseLocalEvent(SPlayer, ref effect);
            SEntMan.EventBus.RaiseLocalEvent(control, ref effect);

            buffedGain = SEntMan.GetComponent<ThirstComponent>(SPlayer).CurrentThirst - beforeBuffed;
            controlGain = SEntMan.GetComponent<ThirstComponent>(control).CurrentThirst - beforeControl;

            AddBloodstreamReagents(SPlayer, ("Amatoxin", 20f), ("Inaprovaline", 20f));
            AddBloodstreamReagents(control, ("Amatoxin", 20f), ("Inaprovaline", 20f));
        });

        await RunSeconds(10f);

        await Server.WaitAssertion(() =>
        {
            buffedToxin = GetBloodstreamQuantity(SPlayer, "Amatoxin");
            controlToxin = GetBloodstreamQuantity(control, "Amatoxin");
            buffedMedicine = GetBloodstreamQuantity(SPlayer, "Inaprovaline");
            controlMedicine = GetBloodstreamQuantity(control, "Inaprovaline");

            Assert.Multiple(() =>
            {
                Assert.That(buffedGain, Is.GreaterThan(controlGain));
                Assert.That(buffedGain / controlGain, Is.EqualTo(1.25f).Within(0.05f));
                Assert.That(buffedToxin, Is.LessThan(controlToxin));
                Assert.That(buffedMedicine, Is.EqualTo(controlMedicine).Within(0.20f));
            });
        });
    }

    private async Task<float> MeasureSprintDamage(EntityUid uid)
    {
        float staminaDamage = 0f;

        await Server.WaitPost(() =>
        {
            var staminaSystem = SEntMan.System<StaminaSystem>();
            var stamina = SEntMan.GetComponent<StaminaComponent>(uid);
            staminaSystem.TakeStaminaDamage(uid, stamina.SprintDrain, stamina, visual: false, log: false, applyCooldown: false);
        });

        await Server.WaitAssertion(() =>
        {
            var stamina = SEntMan.GetComponent<StaminaComponent>(uid);
            staminaDamage = stamina.StaminaDamage;

            Assert.Multiple(() =>
            {
                Assert.That(staminaDamage, Is.GreaterThan(0f));
                Assert.That(staminaDamage, Is.EqualTo(stamina.SprintDrain).Within(0.001f));
            });
        });

        return staminaDamage;
    }

    private void StartSelfHealing(EntityUid uid, EntProtoId itemProtoId)
    {
        var item = SEntMan.SpawnEntity(itemProtoId, Transform.GetMoverCoordinates(uid));
        HandSys.PickupOrDrop(uid, item, checkActionBlocker: false);

        var ev = new UseInHandEvent(uid);
        SEntMan.EventBus.RaiseLocalEvent(item, ev);
        Assert.That(ev.Handled, Is.True);
    }

    private void TriggerFoodIngestion(EntityUid uid)
    {
        var food = SEntMan.SpawnEntity(_food, Transform.GetMoverCoordinates(uid));
        var ingesting = new IngestingEvent(food, new Solution(), false);
        SEntMan.EventBus.RaiseLocalEvent(uid, ref ingesting);
        SEntMan.DeleteEntity(food);
    }

    private TimeSpan GetActiveDoAfterDelay(EntityUid uid)
    {
        var doAfter = SEntMan.GetComponent<DoAfterComponent>(uid);
        var active = doAfter.DoAfters.Values.Single(x => !x.Cancelled && !x.Completed);
        return active.Args.Delay;
    }

    private bool HasActiveDoAfter(EntityUid uid)
    {
        return SEntMan.GetComponentOrNull<DoAfterComponent>(uid)?.DoAfters.Values.Any(x => !x.Cancelled && !x.Completed) == true;
    }

    private ProtoId<AlertPrototype> GetShownAlertType(
        AlertsSystem alerts,
        EntityUid uid,
        ProtoId<AlertCategoryPrototype> category)
    {
        return alerts.TryGetAlertState(uid, AlertKey.ForCategory(category), out var state)
            ? state.Type
            : default;
    }

    private TimeSpan GetRemainingStatusTime(EntityUid uid, string effectProtoId)
    {
        var statusEffects = SEntMan.System<StatusEffectsSystem>();
        Assert.That(statusEffects.TryGetTime(uid, effectProtoId, out var time), Is.True);
        Assert.That(time.EndEffectTime, Is.Not.Null);
        return time.EndEffectTime!.Value - STiming.CurTime;
    }

    private TimeSpan GetRemainingOldStatusTime(EntityUid uid, string key)
    {
        var statusEffects = SEntMan.System<Content.Shared.StatusEffect.StatusEffectsSystem>();
        Assert.That(statusEffects.TryGetTime(uid, key, out var time), Is.True);
        Assert.That(time, Is.Not.Null);
        return time!.Value.Item2 - STiming.CurTime;
    }

    private void UnlockNodes(NetUserId userId, IEnumerable<string> nodeIds)
    {
        var meta = SEntMan.System<WH40KMetaProgressSystem>();
        _ = meta.GetSnapshot(userId);
        Assert.That(meta.TrySetLevel(userId, 40, out _, out _), Is.True);

        foreach (var nodeId in nodeIds)
        {
            Assert.That(meta.TrySetDevelopmentNodeUnlocked(userId, nodeId, true, out var error), Is.True, error);
        }
    }

    private Solution GetBloodSolution(EntityUid uid)
    {
        var solutions = SEntMan.System<SharedSolutionContainerSystem>();
        var manager = SEntMan.GetComponent<SolutionContainerManagerComponent>(uid);

        Assert.That(
            solutions.TryGetSolution((uid, manager), BloodstreamComponent.DefaultBloodSolutionName, out _, out var solution),
            Is.True);

        return solution;
    }

    private float GetBloodstreamQuantity(EntityUid uid, string reagentId)
    {
        return GetBloodSolution(uid).GetTotalPrototypeQuantity(reagentId).Float();
    }

    private void AddBloodstreamReagents(EntityUid uid, params (string ReagentId, float Quantity)[] reagents)
    {
        var bloodstream = SEntMan.System<BloodstreamSystem>();
        var solution = new Solution();

        foreach (var (reagentId, quantity) in reagents)
        {
            solution.AddReagent(reagentId, FixedPoint2.New(quantity));
        }

        Assert.That(bloodstream.TryAddToBloodstream((uid, SEntMan.GetComponent<BloodstreamComponent>(uid)), solution), Is.True);
    }

    private string GetBloodReferenceReagentId(EntityUid uid)
    {
        var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(uid);
        Assert.That(bloodstream.BloodReferenceSolution.Contents.Count, Is.GreaterThan(0));
        return bloodstream.BloodReferenceSolution.Contents[0].Reagent.Prototype;
    }

    private void BleedOutBlood(EntityUid uid, float quantity)
    {
        var bloodstream = SEntMan.System<BloodstreamSystem>();
        Assert.That(
            bloodstream.TryBleedOut((uid, SEntMan.GetComponent<BloodstreamComponent>(uid)), FixedPoint2.New(quantity)),
            Is.True);
    }
}
