using System;
using Robust.Shared.GameObjects;
using Content.Shared.FixedPoint;

namespace Content.Shared._WH40K.MetaProgress;

[ByRefEvent]
public record struct WH40KModifyHungerSatiationEvent(float Amount);

[ByRefEvent]
public record struct WH40KModifyThirstSatiationEvent(float Amount);

[ByRefEvent]
public record struct WH40KModifyEdibleDelayEvent(TimeSpan Time);

[ByRefEvent]
public record struct WH40KModifyFilteredReagentRemovalEvent(string ReagentId, string ReagentGroup, FixedPoint2 BaseAmount)
{
    public FixedPoint2 AdditionalAmount = FixedPoint2.Zero;
}

[ByRefEvent]
public record struct WH40KModifySelfHealPenaltyEvent(float PenaltyMultiplier);

[ByRefEvent]
public record struct WH40KModifySelfHealingDelayEvent(TimeSpan Time);

[ByRefEvent]
public record struct WH40KModifySelfHealingEffectEvent(float Multiplier);

[ByRefEvent]
public record struct WH40KModifyJitterDurationEvent(TimeSpan Time);
