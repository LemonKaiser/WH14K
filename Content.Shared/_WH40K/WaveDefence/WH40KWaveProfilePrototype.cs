using Content.Shared.EntityTable;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.WaveDefence;

[Prototype("wh40kWaveProfile")]
public sealed partial class WH40KWaveProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("waveNumber", required: true)]
    public int WaveNumber = 1;

    [DataField("announcement")]
    public string? Announcement;

    [DataField("completionPolicy")]
    public WH40KWaveCompletionPolicy CompletionPolicy = WH40KWaveCompletionPolicy.EliminateAttackers;

    [DataField("batches")]
    public List<WH40KWaveBatchEntry> Batches = new();
}

[DataDefinition]
public sealed partial class WH40KWaveBatchEntry
{
    [DataField("delaySeconds")]
    public float DelaySeconds;

    [DataField("spawnId")]
    public string? SpawnId;

    [DataField("entityTable", required: true)]
    public ProtoId<EntityTablePrototype> EntityTable = default!;

    [DataField("count")]
    public int Count = 1;

    [DataField("squadRole")]
    public WH40KWaveSquadRole SquadRole = WH40KWaveSquadRole.Soldier;

    [DataField("aiProfile")]
    public WH40KWaveAiProfile AiProfile = WH40KWaveAiProfile.SimpleSwarm;

    [DataField("npcFactionId")]
    public string? NpcFactionId;

    [DataField("rootTaskOverride")]
    public string? RootTaskOverride;

    [DataField("laneCommitSeconds")]
    public float LaneCommitSeconds = 14f;

    [DataField("stallSeconds")]
    public float StallSeconds = 8f;

    [DataField("combatStallSeconds")]
    public float CombatStallSeconds = 14f;

    [DataField("recoveryCooldownSeconds")]
    public float RecoveryCooldownSeconds = 4f;

    [DataField("visionRadius")]
    public float VisionRadius = 12f;

    [DataField("aggroVisionRadius")]
    public float AggroVisionRadius = 16f;

    [DataField("playerMemorySeconds")]
    public float PlayerMemorySeconds = 5f;
}
