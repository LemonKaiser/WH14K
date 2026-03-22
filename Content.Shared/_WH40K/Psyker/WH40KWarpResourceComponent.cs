using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Authoritative warp charge pool used by psyker and chaos-gift actions.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KWarpResourceComponent : Component
{
    [DataField("currentCharge"), AutoNetworkedField]
    public float CurrentCharge = 100f;

    [DataField("maxCharge"), AutoNetworkedField]
    public float MaxCharge = 100f;

    [DataField("regenPerSecond"), AutoNetworkedField]
    public float RegenPerSecond = 3f;

    /// <summary>
    /// Server-side debounce for passive regen replication.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextNetworkSyncAt;
}
