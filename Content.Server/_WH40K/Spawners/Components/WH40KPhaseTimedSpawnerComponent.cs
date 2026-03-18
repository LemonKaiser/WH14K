using Content.Shared._WH40K.GameMode;
using Content.Server._WH40K.Spawners;

namespace Content.Server._WH40K.Spawners.Components;

[RegisterComponent, Access(typeof(WH40KPhaseTimedSpawnerSystem))]
public sealed partial class WH40KPhaseTimedSpawnerComponent : Component
{
    /// <summary>
    /// Spawner will stay disabled until this phase.
    /// </summary>
    [DataField("enabledFromPhase")]
    public WH40KBattlePhase EnabledFromPhase = WH40KBattlePhase.Assault;

    /// <summary>
    /// If true, a fresh interval starts when the spawner becomes enabled.
    /// </summary>
    [DataField("resetTimerOnEnable")]
    public bool ResetTimerOnEnable = true;

    [ViewVariables]
    public bool Enabled;

    [ViewVariables]
    public float SavedChance = -1f;
}
