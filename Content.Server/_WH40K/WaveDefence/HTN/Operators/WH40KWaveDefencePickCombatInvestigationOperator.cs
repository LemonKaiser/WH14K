using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Content.Server._WH40K.WaveDefence.Components;

namespace Content.Server._WH40K.WaveDefence.HTN.Operators;

public sealed partial class WH40KWaveDefencePickCombatInvestigationOperator : HTNOperator
{
    [Dependency] private  IEntityManager _entityManager = default!;
    private NPCPerceptionSystem _perception = default!;

    [DataField("targetKey")]
    public string TargetKey = WH40KWaveDefenceHtnBlackboardKeys.CombatInvestigationTarget;

    [DataField("targetCoordinatesKey")]
    public string TargetCoordinatesKey = WH40KWaveDefenceHtnBlackboardKeys.CombatLastKnownCoordinates;

    [DataField("searchTimeKey")]
    public string SearchTimeKey = WH40KWaveDefenceHtnBlackboardKeys.CombatSearchTime;

    [DataField("searchTime")]
    public float SearchTime = 1.25f;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _perception = sysManager.GetEntitySystem<NPCPerceptionSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        await Task.CompletedTask;

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_perception.TryGetInvestigationPoint(owner, out var target, out var coordinates))
            return (false, null);

        if (_entityManager.TryGetComponent<WH40KWaveDefenceAttackerComponent>(owner, out var attacker))
            attacker.DebugState = $"investigate-last-known:{_entityManager.ToPrettyString(target)}";

        return (true, new Dictionary<string, object>
        {
            [TargetKey] = target,
            [TargetCoordinatesKey] = coordinates,
            [SearchTimeKey] = SearchTime,
            [WH40KWaveDefenceHtnBlackboardKeys.PlayerCombatRole] = false,
            [WH40KWaveDefenceHtnBlackboardKeys.ObjectiveCombatRole] = false,
            [WH40KWaveDefenceHtnBlackboardKeys.MovementRole] = true
        });
    }
}
