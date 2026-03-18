using System;

namespace Content.Server._WH40K.Psyker;

[RegisterComponent]
public sealed partial class WH40KChaosKhorneJumpMarkerComponent : Component
{
    public float SpeedBuffMultiplier = 1f;
    public TimeSpan SpeedBuffDuration = TimeSpan.FromSeconds(6);
    public bool ExExplosionEnabled;
}
