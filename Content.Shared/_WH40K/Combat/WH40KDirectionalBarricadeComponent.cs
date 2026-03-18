namespace Content.Shared._WH40K.Combat;

/// <summary>
/// Directional barricade behavior for hitscan weapons.
/// Shots traveling in the pass direction can go through if close enough,
/// while the blocked side has a small random pass chance.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KDirectionalBarricadeComponent : Component
{
    /// <summary>
    /// Maximum distance (in tiles/meters) from the barricade to allow pass-through
    /// when shooting from the pass side.
    /// </summary>
    [DataField("passSideMaxDistance")]
    public float PassSideMaxDistance = 2f;

    /// <summary>
    /// Chance (0..1) for shots from the blocked side to pass through.
    /// </summary>
    [DataField("blockedSidePassChance")]
    public float BlockedSidePassChance = 0.05f;

    /// <summary>
    /// If shooter is on blocked side but very close to the barricade, allow deterministic pass-through.
    /// Set to 0 to disable.
    /// </summary>
    [DataField("blockedSidePointBlankPassDistance")]
    public float BlockedSidePointBlankPassDistance = 1f;

    /// <summary>
    /// Flip the pass side by 180 degrees for this barricade.
    /// </summary>
    [DataField("flipPassSide")]
    public bool FlipPassSide;

}
