using Robust.Shared.Utility;

namespace Content.Shared._WH40K.TacticalMap;

[RegisterComponent]
public sealed partial class WH40KTacticalMapComponent : Component
{
    /// <summary>
    /// Whether or not to show the current user on the tactical map.
    /// </summary>
    [DataField]
    public bool ShowLocation = true;

    /// <summary>
    /// Whether this tablet can edit and save tactical annotations.
    /// Read-only tablets still render the team feed but cannot modify it.
    /// </summary>
    [DataField]
    public bool CanAnnotate = true;

    /// <summary>
    /// If true, this tactical-map device resolves its target to the largest station grid on initialization.
    /// </summary>
    [DataField]
    public bool InitializeWithStation = true;

    /// <summary>
    /// The target grid that the tactical map will display.
    /// If null, the UI falls back to the owner's current grid.
    /// </summary>
    [DataField]
    public EntityUid? TargetGrid;

    /// <summary>
    /// Rendered texture used as the tactical-map background.
    /// </summary>
    [DataField]
    public ResPath SnapshotTexture = new("/Textures/_WH40K/Interface/TacticalMap/battlefield40k_snapshot.png");

    /// <summary>
    /// Enables chunk-based fog of war on the tactical map.
    /// </summary>
    [DataField]
    public bool FogEnabled = true;

    /// <summary>
    /// Chunk size in tiles for fog-of-war reveal and masking.
    /// </summary>
    [DataField]
    public int FogChunkSize = 8;

    /// <summary>
    /// Reveal radius in chunk units around each team player.
    /// `0` means only the chunk the player is currently standing in.
    /// </summary>
    [DataField]
    public int FogRevealRadiusChunks = 0;

    /// <summary>
    /// Enables very slow live snapshot refresh while the tactical map window is open.
    /// </summary>
    [DataField]
    public bool LiveRefreshEnabled = false;

    /// <summary>
    /// Chunk size in tiles for a single background refresh pass.
    /// </summary>
    [DataField]
    public int LiveRefreshChunkSize = 16;

    /// <summary>
    /// Delay in seconds between server-side scanner moves.
    /// </summary>
    [DataField]
    public float LiveRefreshInterval = 2.5f;

    /// <summary>
    /// PVS scale used by the scanner eye that feeds live-refresh chunks.
    /// </summary>
    [DataField]
    public float LiveRefreshPvsScale = 1f;

}
