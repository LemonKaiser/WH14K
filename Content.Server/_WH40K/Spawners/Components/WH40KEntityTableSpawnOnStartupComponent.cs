using Content.Server._WH40K.Spawners;
using Content.Shared.EntityTable.EntitySelectors;

namespace Content.Server._WH40K.Spawners.Components;

[RegisterComponent, Access(typeof(WH40KEntityTableSpawnOnStartupSystem))]
public sealed partial class WH40KEntityTableSpawnOnStartupComponent : Component
{
    /// <summary>
    /// Table that determines what gets spawned when this marker starts up.
    /// </summary>
    [DataField(required: true)]
    public EntityTableSelector Table = default!;

    /// <summary>
    /// Scatter radius for spawned entities around this marker.
    /// </summary>
    [DataField]
    public float Offset = 1.1f;

    /// <summary>
    /// Deletes the marker after the startup spawn is processed.
    /// </summary>
    [DataField]
    public bool DeleteSpawnerAfterSpawn = true;
}
