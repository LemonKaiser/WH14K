using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.AccountLoad;

[Serializable, NetSerializable]
public sealed class WH40KAccountLoadStatusEvent(
    string titleLocKey,
    string stageLocKey,
    string? detailLocKey,
    float progress,
    int completedSteps,
    int totalSteps)
    : EntityEventArgs
{
    public string TitleLocKey { get; } = titleLocKey;
    public string StageLocKey { get; } = stageLocKey;
    public string? DetailLocKey { get; } = detailLocKey;
    public float Progress { get; } = progress;
    public int CompletedSteps { get; } = completedSteps;
    public int TotalSteps { get; } = totalSteps;
}
