using Content.Shared.Movement.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

/// <summary>
/// Component that modifies the movement speed of other entities that come into contact with the entity this component is added to.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SpeedModifierContactsSystem))]
public sealed partial class SpeedModifierContactsComponent : Component
{
    /// <summary>
    /// The modifier applied to the walk speed of entities that come into contact with the entity this component is added to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float WalkSpeedModifier = 1.0f;

    /// <summary>
    /// The modifier applied to the sprint speed of entities that come into contact with the entity this component is added to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SprintSpeedModifier = 1.0f;

    /// <summary>
    /// Contact aggregation policy for this speed modifier source.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ContactModifierAggregationMode AggregationMode = ContactModifierAggregationMode.Average;

    /// <summary>
    /// Weight used only when <see cref="AggregationMode"/> is <see cref="ContactModifierAggregationMode.WeightedAverage"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float AggregationWeight = 1.0f;

    /// <summary>
    /// Indicates whether this component affects the movement speed of airborne entities that come into contact with the entity this component is added to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool AffectAirborne;

    /// <summary>
    /// A whitelist of entities that should be ignored by this component's speed modifiers.
    /// </summary>
    [DataField]
    public EntityWhitelist? IgnoreWhitelist;

    /// <summary>
    /// If the slowed entity is currently intersecting any entity from this whitelist,
    /// this contact slowdown source is ignored.
    /// Useful for hazard tiles that should be bypassed by catwalks.
    /// </summary>
    [DataField]
    public EntityWhitelist? IgnoreWhenIntersectingWhitelist;

    /// <summary>
    /// If true, apply this slowdown only when the affected entity center is on the same tile
    /// as the slowdown source entity.
    /// Useful for tile hazards where edge-only collision overlap should not count as standing in the hazard.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool RequireSameTile;

    /// <summary>
    /// Optional per-contact speed ramp-in duration in seconds.
    /// When greater than zero, movement modifier is interpolated from 1.0 to target value
    /// over this time after contact starts, reducing abrupt entry speed changes.
    /// </summary>
    [DataField]
    public float RampUpDuration;
}
