namespace Content.Server.NPC.Pathfinding;

/// <summary>
/// High-level path cost strategy selected by AI control layers.
/// </summary>
public enum PathCostProfile : byte
{
    /// <summary>
    /// Baseline balanced profile.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Prioritizes progress pressure over safety.
    /// </summary>
    Assault = 1,

    /// <summary>
    /// Aggressive breach behavior for frontline obstacle destruction.
    /// </summary>
    Breach = 2,

    /// <summary>
    /// Safer routing that avoids hazards and expensive obstacle interactions.
    /// </summary>
    Safe = 3,
}
