using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.WaveDefence;

public abstract class SharedWH40KWaveDefenceAiDebugOverlaySystem : EntitySystem
{
    protected const float LocalViewRange = 28f;
    protected static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(0.25f);
}

[Serializable, NetSerializable]
public sealed class WH40KWaveDefenceAiDebugOverlayDisableMessage : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class WH40KWaveDefenceAiDebugOverlayMessage : EntityEventArgs
{
    public WH40KWaveDefenceAiDebugEntry[] Entries { get; }

    public WH40KWaveDefenceAiDebugOverlayMessage(WH40KWaveDefenceAiDebugEntry[] entries)
    {
        Entries = entries;
    }
}

[Serializable, NetSerializable]
public enum WH40KWaveDefenceAiDebugTargetKind : byte
{
    None = 0,
    MoveTarget = 1,
    CombatTarget = 2,
    ObjectiveTarget = 3,
}

[Serializable, NetSerializable]
public readonly record struct WH40KWaveDefenceAiDebugEntry(
    string Label,
    MapCoordinates NpcPosition,
    float VisionRadius,
    float AggroVisionRadius,
    MapCoordinates FocusPosition,
    bool HasFocusPosition,
    WH40KWaveDefenceAiDebugTargetKind FocusKind,
    string RootTask,
    string CurrentTask,
    string SteeringStatus,
    string FocusLabel,
    string DebugState,
    bool NoPath,
    bool Engaged,
    bool IsWaveAttacker);
