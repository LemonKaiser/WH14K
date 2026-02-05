using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Overlays;

/// <summary>
/// Marks an entity to always display its health bar without HUD items.
/// Can use MobThresholds when available, or a fixed max health value.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KAlwaysShowHealthBarComponent : Component
{
    /// <summary>
    /// Optional max health for non-mob entities. If null, MobThresholds will be used when available.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public FixedPoint2? MaxHealth;

    /// <summary>
    /// If true, prefer MobThresholds/MobState when present.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public bool UseMobThresholds = true;

    /// <summary>
    /// Width of the health bar in pixels. Set to 0 or less to use sprite width.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float BarWidthPixels = 24f;

    /// <summary>
    /// Height of the health bar in pixels.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float BarHeightPixels = 3f;

    /// <summary>
    /// Vertical offset above the sprite, as a percentage of sprite height.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float YOffsetSpritePercent = 0.2f;

    /// <summary>
    /// Additional vertical offset in pixels.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float YOffsetPixels = 0f;
}
