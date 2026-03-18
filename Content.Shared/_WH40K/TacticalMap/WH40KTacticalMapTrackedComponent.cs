using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.TacticalMap;

/// <summary>
/// Explicitly marks an entity as eligible for allied tactical-map overlays.
/// Team filtering still happens server-side.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KTacticalMapTrackedComponent : Component
{
}
