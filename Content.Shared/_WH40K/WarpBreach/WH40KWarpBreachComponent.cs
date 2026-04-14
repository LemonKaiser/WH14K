using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.WarpBreach;

/// <summary>
/// Marks an entity as a warp breach visual — a tear in reality showing another dimension inside.
/// The client overlay reads these fields to drive the shader.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class WH40KWarpBreachComponent : Component
{
    /// <summary>
    /// Distortion intensity around the breach edge.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float Intensity = 20f;

    /// <summary>
    /// Visual radius of the breach opening in world-units (tiles).
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float Radius = 3f;

    /// <summary>
    /// How quickly edge distortion falls off (higher = sharper edge).
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float Falloff = 2f;

    /// <summary>
    /// How long (seconds) it takes to go from initial slash to fully open portal.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float OpenDuration = 3f;

    /// <summary>
    /// Server time when the breach was created. Set on MapInit.
    /// Used by the client to compute animation progress without resetting on PVS re-entry.
    /// </summary>
    [AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan CreatedAt;
}
