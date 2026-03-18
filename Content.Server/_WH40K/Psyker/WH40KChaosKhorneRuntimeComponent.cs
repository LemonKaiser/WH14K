using System;
using System.Collections.Generic;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;

namespace Content.Server._WH40K.Psyker;

[RegisterComponent]
public sealed partial class WH40KChaosKhorneRuntimeComponent : Component
{
    public bool BaselineCaptured;
    public float BaseWalkSpeed;
    public float BaseSprintSpeed;
    public SortedDictionary<FixedPoint2, MobState>? BaselineThresholds;
    public TimeSpan JumpSpeedBuffExpiresAt;
    public float JumpSpeedBuffMultiplier = 1f;
    public int DashComboRemaining;
    public byte AppliedPassiveSpeedTier;
    public byte AppliedPassiveHealthTier;
}
