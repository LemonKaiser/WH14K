using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Rangefinder;

[RegisterComponent]
public sealed partial class WH40KRangefinderComponent : Component
{
    [DataField]
    public int? Id;

    [DataField]
    public int Range = 70;

    [DataField]
    public bool CanDesignate = true;

    [DataField]
    public WH40KRangefinderMode Mode = WH40KRangefinderMode.Rangefinder;

    [DataField]
    public Vector2i? LastTarget;

    [DataField]
    public EntityUid? LastTargetGrid;

    [DataField]
    public bool RequireWield = true;

    [DataField]
    public string TargetUseDelayId = "wh40k_rangefinder_target";

    [DataField]
    public TimeSpan TargetDelay = TimeSpan.FromSeconds(0.5);

    [DataField]
    public string SwitchModeUseDelayId = "wh40k_rangefinder_mode";

    [DataField]
    public TimeSpan SwitchModeDelay = TimeSpan.FromSeconds(0.5);

    [DataField]
    public TimeSpan MarkerLifetime = TimeSpan.FromSeconds(45);

    [DataField]
    public EntProtoId MarkerPrototype = "WH40KLaserDesignatorMarker";

    [DataField]
    public SoundSpecifier? AcquireSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    [DataField]
    public SoundSpecifier? ToggleSound = new SoundPathSpecifier("/Audio/Weapons/click.ogg");
}

public enum WH40KRangefinderMode : byte
{
    Rangefinder,
    Designator,
}
