using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Whitelist;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Vehicle.Components;

/// <summary>
/// Vehicles are objects that have the behavior of moving when a player "operates" them.
/// The details of when the vehicle can operate and who the operator is are not defined here.
/// This simply contains the baseline behavior of the vehicle itself.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehicleSystem))]
public sealed partial class VehicleComponent : Component
{
    /// <summary>
    /// The driver of this vehicle.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Operator;

    /// <summary>
    /// Simple whitelist for determining who can operate this vehicle.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityWhitelist? OperatorWhitelist;

    /// <summary>
    /// If true, damage to the vehicle will be transferred to the operator.
    /// This damage is modified by <see cref="TransferDamageModifier"/>
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool TransferDamage = true;

    /// <summary>
    /// Scalar applied after <see cref="TransferDamageModifier"/> when passing damage to the operator.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TransferDamageMultiplier = 1f;

    /// <summary>
    /// A damage modifier set that adjusts the damage passed from the vehicle to the operator.
    /// </summary>
    [DataField, AutoNetworkedField]
    public DamageModifierSet? TransferDamageModifier;

    /// <summary>
    /// Whether the operator requires hands to operate this vehicle.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool RequiresHands = true;

    /// <summary>
    /// Optional horn sound for the vehicle.
    /// </summary>
    [DataField]
    public SoundSpecifier? HornSound;

    /// <summary>
    /// Action prototype granted to the operator for honking.
    /// </summary>
    [DataField]
    public string? HornAction = "ActionVehicleHorn";

    /// <summary>
    /// Cached horn action entity stored on the vehicle.
    /// </summary>
    [DataField]
    public EntityUid? HornActionEntity;
}

[Serializable, NetSerializable]
public enum VehicleVisuals : byte
{
    HasOperator,
    CanRun,
}

[ByRefEvent, UsedImplicitly]
public readonly record struct OnVehicleEnteredEvent(Entity<VehicleComponent> Vehicle, EntityUid Operator);

[ByRefEvent, UsedImplicitly]
public readonly record struct OnVehicleExitedEvent(Entity<VehicleComponent> Vehicle, EntityUid Operator);

[ByRefEvent, UsedImplicitly]
public readonly record struct VehicleOperatorSetEvent(EntityUid? NewOperator, EntityUid? OldOperator);

[ByRefEvent, UsedImplicitly]
public readonly record struct VehicleCanRunEvent(Entity<VehicleComponent> Vehicle, bool CanRun = true);

public sealed partial class HonkActionEvent : InstantActionEvent;
