using System.Collections.Generic;
using Robust.Shared.GameStates;
using Robust.Shared.Maths;

namespace Content.Shared._WH40K.TacticalMap;

/// <summary>
/// Stores mapper-authored blackout tiles for the tactical-map snapshot and runtime UI.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KTacticalMapBlackoutComponent : Component
{
    public const int ChunkSize = 8;

    /// <summary>
    /// Chunk origin and bitmask of blackout tiles inside that chunk.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<Vector2i, ulong> Data = new();
}
