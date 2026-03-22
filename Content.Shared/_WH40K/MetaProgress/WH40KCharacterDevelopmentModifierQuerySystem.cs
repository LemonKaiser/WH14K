using System;
using Content.Shared.Damage.Events;
using Content.Shared.Drunk;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;

namespace Content.Shared._WH40K.MetaProgress;

public sealed class WH40KCharacterDevelopmentModifierQuerySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, WH40KModifyHungerSatiationEvent>(OnModifyHungerSatiation);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, WH40KModifyThirstSatiationEvent>(OnModifyThirstSatiation);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, WH40KModifyEdibleDelayEvent>(OnModifyEdibleDelay);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, WH40KModifyFilteredReagentRemovalEvent>(OnModifyFilteredReagentRemoval);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, WH40KModifySelfHealPenaltyEvent>(OnModifySelfHealPenalty);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, WH40KModifySelfHealingDelayEvent>(OnModifySelfHealingDelay);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, WH40KModifySelfHealingEffectEvent>(OnModifySelfHealingEffect);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, WH40KModifyJitterDurationEvent>(OnModifyJitterDuration);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, ModifyStatusEffectDurationEvent>(OnModifyStatusEffectDuration);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, BeforeStaminaDamageEvent>(OnBeforeStaminaDamage);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, RefreshStaminaCritThresholdEvent>(OnRefreshStaminaCritThreshold);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, GetStandUpTimeEvent>(OnGetStandUpTime);
    }

    private void OnModifyHungerSatiation(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref WH40KModifyHungerSatiationEvent args)
    {
        args.Amount *= ent.Comp.HungerSatiationMultiplier;
    }

    private void OnModifyThirstSatiation(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref WH40KModifyThirstSatiationEvent args)
    {
        args.Amount *= ent.Comp.ThirstSatiationMultiplier;
    }

    private void OnModifyEdibleDelay(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref WH40KModifyEdibleDelayEvent args)
    {
        if (args.Time <= TimeSpan.Zero)
            return;

        var ticks = (long) MathF.Round(args.Time.Ticks * ent.Comp.EatDelayMultiplier);
        args.Time = TimeSpan.FromTicks(Math.Max(1L, ticks));
    }

    private void OnModifyFilteredReagentRemoval(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref WH40KModifyFilteredReagentRemovalEvent args)
    {
        if (args.BaseAmount <= 0 ||
            ent.Comp.ToxinFilterMultiplier <= 1f ||
            !string.Equals(args.ReagentGroup, "Toxins", StringComparison.Ordinal))
        {
            return;
        }

        args.AdditionalAmount += args.BaseAmount * (ent.Comp.ToxinFilterMultiplier - 1f);
    }

    private void OnModifySelfHealPenalty(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref WH40KModifySelfHealPenaltyEvent args)
    {
        if (args.PenaltyMultiplier <= 1f)
            return;

        args.PenaltyMultiplier = 1f + (args.PenaltyMultiplier - 1f) * ent.Comp.SelfHealPenaltyMultiplier;
    }

    private void OnModifySelfHealingDelay(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref WH40KModifySelfHealingDelayEvent args)
    {
        args.Time = ScaleTimeSpan(args.Time, ent.Comp.SelfMedicalDelayMultiplier);
    }

    private void OnModifySelfHealingEffect(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref WH40KModifySelfHealingEffectEvent args)
    {
        args.Multiplier *= ent.Comp.SelfHealingEffectMultiplier;
    }

    private void OnModifyJitterDuration(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref WH40KModifyJitterDurationEvent args)
    {
        args.Time = ScaleTimeSpan(args.Time, ent.Comp.JitterDurationMultiplier);
    }

    private void OnModifyStatusEffectDuration(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref ModifyStatusEffectDurationEvent args)
    {
        if (args.Duration <= TimeSpan.Zero)
            return;

        if (args.EffectProtoId == SharedDrunkSystem.Drunk)
        {
            args.Duration = ScaleTimeSpan(args.Duration, ent.Comp.DrunkDurationMultiplier);
            return;
        }

        if (args.EffectProtoId == "StatusEffectDrowsiness")
        {
            args.Duration = ScaleTimeSpan(args.Duration, ent.Comp.DrowsinessDurationMultiplier);
            return;
        }

        if (args.EffectProtoId == MovementModStatusSystem.VomitingSlowdown)
            args.Duration = ScaleTimeSpan(args.Duration, ent.Comp.VomitSlowdownDurationMultiplier);
    }

    private void OnBeforeStaminaDamage(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref BeforeStaminaDamageEvent args)
    {
        if (args.Type == StaminaDamageType.ForceStand)
            return;

        args.Value *= ent.Comp.StaminaIncomingDamageMultiplier;
    }

    private void OnRefreshStaminaCritThreshold(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref RefreshStaminaCritThresholdEvent args)
    {
        args.Modifier *= ent.Comp.StaminaCritThresholdMultiplier;
    }

    private void OnGetStandUpTime(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref GetStandUpTimeEvent args)
    {
        args.DoAfterTime = ScaleTimeSpan(args.DoAfterTime, ent.Comp.KnockdownStandUpTimeMultiplier);
    }

    private static TimeSpan ScaleTimeSpan(TimeSpan time, float multiplier)
    {
        if (time <= TimeSpan.Zero)
            return time;

        var ticks = (long) MathF.Round(time.Ticks * multiplier);
        return TimeSpan.FromTicks(Math.Max(1L, ticks));
    }
}
