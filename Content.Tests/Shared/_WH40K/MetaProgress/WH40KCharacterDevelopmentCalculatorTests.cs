using System.Collections.Generic;
using Content.Shared._WH40K.MetaProgress;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.MetaProgress;

[TestFixture]
public sealed class WH40KCharacterDevelopmentCalculatorTests
{
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

    [Test]
    public void CalculatesStomachAndLungBonusesFromUnlockedNodes()
    {
        var modifiers = WH40KCharacterDevelopmentCalculator.Calculate(
        [
            "stomach-root",
            "stomach-u1",
            "stomach-u2",
            "stomach-u3",
            "stomach-d1",
            "stomach-d2",
            "stomach-d3",
            "lungs-root",
            "lungs-u1",
            "lungs-u2",
            "lungs-u3",
            "lungs-d1",
            "lungs-d2",
            "lungs-d3"
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(modifiers.HungerDecayMultiplier, Is.EqualTo(0.85f).Within(0.0001f));
            Assert.That(modifiers.ThirstDecayMultiplier, Is.EqualTo(0.95f).Within(0.0001f));
            Assert.That(modifiers.HungerSatiationMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(modifiers.EatDelayMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(modifiers.StaminaSprintDrainMultiplier, Is.EqualTo(0.85f).Within(0.0001f));
            Assert.That(modifiers.StaminaWalkRecoveryMultiplier, Is.EqualTo(1.10f).Within(0.0001f));
            Assert.That(modifiers.StaminaCooldownMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(modifiers.MaxSaturationMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(modifiers.SuffocationDamageMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(modifiers.StomachImpulseUnlocked, Is.True);
            Assert.That(modifiers.WarFurnaceUnlocked, Is.True);
            Assert.That(modifiers.HasAnyEffect(), Is.True);
        });
    }

    [Test]
    public void CalculatesBrainBonusesFromUnlockedNodes()
    {
        var modifiers = WH40KCharacterDevelopmentCalculator.Calculate(BrainNodes);

        Assert.Multiple(() =>
        {
            Assert.That(modifiers.StaminaIncomingDamageMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(modifiers.StaminaCritThresholdMultiplier, Is.EqualTo(1.15f).Within(0.0001f));
            Assert.That(modifiers.ForceStandStaminaMultiplier, Is.EqualTo(0.80f).Within(0.0001f));
            Assert.That(modifiers.StaminaAfterCritRecoveryMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(modifiers.StaminaCritStunTimeMultiplier, Is.EqualTo(0.90f).Within(0.0001f));
            Assert.That(modifiers.KnockdownStandUpTimeMultiplier, Is.EqualTo(0.90f).Within(0.0001f));
            Assert.That(modifiers.HasAnyEffect(), Is.True);
        });
    }

    [Test]
    public void CalculatesHeartAndKidneyBonusesFromUnlockedNodes()
    {
        var nodes = new List<string>();
        nodes.AddRange(HeartNodes);
        nodes.AddRange(KidneyNodes);

        var modifiers = WH40KCharacterDevelopmentCalculator.Calculate(nodes);

        Assert.Multiple(() =>
        {
            Assert.That(modifiers.ThirstDecayMultiplier, Is.EqualTo(0.90f).Within(0.0001f));
            Assert.That(modifiers.ThirstSatiationMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(modifiers.BloodRefreshMultiplier, Is.EqualTo(1.30f).Within(0.0001f));
            Assert.That(modifiers.BleedReductionMultiplier, Is.EqualTo(1.30f).Within(0.0001f));
            Assert.That(modifiers.BloodlossThresholdMultiplier, Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(modifiers.MaxBloodVolumeMultiplier, Is.EqualTo(1.05f).Within(0.0001f));
            Assert.That(modifiers.ToxinFilterMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(modifiers.KidneyPurgeUnlocked, Is.True);
            Assert.That(modifiers.HasAnyEffect(), Is.True);
        });
    }

    [Test]
    public void CalculatesLiverBonusesFromUnlockedNodes()
    {
        var modifiers = WH40KCharacterDevelopmentCalculator.Calculate(LiverNodes);

        Assert.Multiple(() =>
        {
            Assert.That(modifiers.SelfHealPenaltyMultiplier, Is.EqualTo(0.70f).Within(0.0001f));
            Assert.That(modifiers.SelfMedicalDelayMultiplier, Is.EqualTo(0.95f).Within(0.0001f));
            Assert.That(modifiers.SelfHealingEffectMultiplier, Is.EqualTo(1.20f).Within(0.0001f));
            Assert.That(modifiers.DrunkDurationMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(modifiers.JitterDurationMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(modifiers.DrowsinessDurationMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(modifiers.VomitSlowdownDurationMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(modifiers.HasAnyEffect(), Is.True);
        });
    }

    [Test]
    public void ReturnsNeutralModifiersForUnknownOrEmptyNodes()
    {
        var modifiers = WH40KCharacterDevelopmentCalculator.Calculate(["unknown-node"]);

        Assert.Multiple(() =>
        {
            Assert.That(modifiers.HungerDecayMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.ThirstDecayMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.HungerSatiationMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.EatDelayMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.StaminaSprintDrainMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.StaminaWalkRecoveryMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.StaminaCooldownMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.MaxSaturationMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.SuffocationDamageMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.BloodRefreshMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.BleedReductionMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.BloodlossThresholdMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.MaxBloodVolumeMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.ToxinFilterMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.StaminaIncomingDamageMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.StaminaCritThresholdMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.ForceStandStaminaMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.StaminaAfterCritRecoveryMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.StaminaCritStunTimeMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.KnockdownStandUpTimeMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.SelfHealPenaltyMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.SelfMedicalDelayMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.SelfHealingEffectMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.DrunkDurationMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.JitterDurationMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.DrowsinessDurationMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.VomitSlowdownDurationMultiplier, Is.EqualTo(1f));
            Assert.That(modifiers.StomachImpulseUnlocked, Is.False);
            Assert.That(modifiers.WarFurnaceUnlocked, Is.False);
            Assert.That(modifiers.KidneyPurgeUnlocked, Is.False);
            Assert.That(modifiers.HasAnyEffect(), Is.False);
        });
    }
}
