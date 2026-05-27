using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.WaveDefence;

[RegisterComponent]
public sealed partial class WH40KWaveSpawnPointComponent : Component
{
    [DataField("spawnType", required: true)]
    public WH40KWaveSpawnPointType SpawnType;

    [DataField("teamId")]
    public string TeamId = string.Empty;

    [DataField("spawnId")]
    public string SpawnId = string.Empty;

    [DataField("priority")]
    public int Priority = 1;
}

[RegisterComponent]
public sealed partial class WH40KWaveImperiumBaseComponent : Component
{
    [DataField("teamId")]
    public string TeamId = "Imperium";
}
