using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Command;

/// <summary>
/// Temporary team-wide gameplay modifiers applied by command-node runtime random events.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WH40KTeamEventEffectComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [DataField, AutoNetworkedField]
    public string TeamId = string.Empty;

    [DataField, AutoNetworkedField]
    public string EventId = string.Empty;

    [DataField, AutoNetworkedField]
    public float OutgoingDamageMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float IncomingDamageMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float MedicalDelayMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float ConstructionDelayMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public bool IgnorePullSlowdown;
}
