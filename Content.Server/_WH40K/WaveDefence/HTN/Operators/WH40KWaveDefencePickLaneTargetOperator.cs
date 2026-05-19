using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server._WH40K.WaveDefence.Components;
using Robust.Shared.Map;

namespace Content.Server._WH40K.WaveDefence.HTN.Operators;

public sealed partial class WH40KWaveDefencePickLaneTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;

    [DataField("targetKey")]
    public string TargetKey = WH40KWaveDefenceHtnBlackboardKeys.MovementTargetCoordinates;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        await Task.CompletedTask;

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entityManager.TryGetComponent<WH40KWaveDefenceAttackerComponent>(owner, out var attacker) ||
            !TryGetAuthoritativeMovementTarget(attacker, out var targetCoordinates))
        {
            return (false, null);
        }

        return (true, new Dictionary<string, object>
        {
            { TargetKey, targetCoordinates },
            { WH40KWaveDefenceHtnBlackboardKeys.PlayerCombatRole, false },
            { WH40KWaveDefenceHtnBlackboardKeys.ObjectiveCombatRole, false },
            { WH40KWaveDefenceHtnBlackboardKeys.MovementRole, true }
        });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }

    private bool TryGetAuthoritativeMovementTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        out EntityCoordinates targetCoordinates)
    {
        if (attacker.MovementTargetDirective.IsValid(_entityManager))
        {
            targetCoordinates = attacker.MovementTargetDirective;
            return true;
        }

        targetCoordinates = EntityCoordinates.Invalid;
        return false;
    }
}
