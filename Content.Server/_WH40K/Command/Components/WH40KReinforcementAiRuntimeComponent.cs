using System;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Command.Components;

[RegisterComponent]
public sealed partial class WH40KReinforcementAiRuntimeComponent : Component
{
    public EntityCoordinates HomeCoordinates = EntityCoordinates.Invalid;
    public TimeSpan NextWeaponReadyAttempt = TimeSpan.Zero;
    public TimeSpan WeaponReadyRetryInterval = TimeSpan.FromSeconds(0.5);
    public float IdleRange = 3.5f;
    public float VisionRadius = 9f;
    public float AggroVisionRadius = 12f;
    public float RangedRange = 10f;
    public float LeashRange = 7f;
    public float ReturnRange = 1.25f;
    public bool ReturningHome;
}
