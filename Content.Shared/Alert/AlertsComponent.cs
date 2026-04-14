using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Alert;

/// <summary>
///     Handles the icons on the right side of the screen.
///     Should only be used for player-controlled entities.
/// </summary>
// Component is not AutoNetworked due to supporting clientside-only alerts.
// Component state is handled manually to avoid the server overwriting the client list.
[RegisterComponent, NetworkedComponent]
public sealed partial class AlertsComponent : Component
{
    [ViewVariables]
    public Dictionary<AlertKey, AlertState> Alerts = new();

    public override bool SendOnlyToOwner => true;
}

/// <summary>
/// When present on a controlled entity, indicates that its HUD should display alerts of another source entity,
/// and clicks may optionally be proxied back to that source.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AlertsDisplayRelayComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [DataField, AutoNetworkedField]
    public EntityUid? Source;

    [DataField, AutoNetworkedField]
    public bool InteractAsSource = false;
}

[Serializable, NetSerializable]
public sealed class AlertComponentState : ComponentState
{
    public Dictionary<AlertKey, AlertState> Alerts { get; }
    public AlertComponentState(Dictionary<AlertKey, AlertState> alerts)
    {
        Alerts = alerts;
    }
}
