using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.WaveDefence.HTN.Operators;
using Content.Shared._WH40K.WaveDefence;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.WaveDefence.Components;

[RegisterComponent, Access(typeof(WH40KWaveDefenceRuleSystem), typeof(WH40KWaveDefenceAISystem), typeof(WH40KWaveDefencePickLaneTargetOperator), typeof(WH40KWaveDefencePickObjectiveOperator), typeof(WH40KWaveDefencePickPlayerTargetOperator), typeof(WH40KWaveDefenceAiDebugOverlaySystem))]
public sealed partial class WH40KWaveDefenceAttackerComponent : Component
{
    [ViewVariables]
    public EntityUid? Objective;

    [ViewVariables]
    public string? RootTaskOverride;

    [ViewVariables]
    public WH40KWaveSquadRole Role = WH40KWaveSquadRole.Soldier;

    [ViewVariables]
    public WH40KWaveAiProfile AiProfile = WH40KWaveAiProfile.SimpleSwarm;

    [ViewVariables]
    public float VisionRadius = 12f;

    [ViewVariables]
    public float AggroVisionRadius = 16f;

    [ViewVariables]
    public string DebugState = "idle";
}
