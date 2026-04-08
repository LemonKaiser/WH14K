using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Networked read-model of the global warp instability pool.
/// The authoritative value lives in server runtime and is mirrored here for UI.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KWarpInstabilityComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [DataField("currentInstability"), AutoNetworkedField]
    public float CurrentInstability;

    [DataField("maxInstability"), AutoNetworkedField]
    public float MaxInstability = 1000f;

    [DataField("decayPerSecond"), AutoNetworkedField]
    public float DecayPerSecond = 1.2f;

    /// <summary>
    /// Server-side debounce for passive decay replication.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextNetworkSyncAt;
}
