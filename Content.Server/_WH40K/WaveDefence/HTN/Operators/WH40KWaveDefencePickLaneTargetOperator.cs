using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Server._WH40K.WaveDefence.Components;
using Robust.Shared.Map;

namespace Content.Server._WH40K.WaveDefence.HTN.Operators;

public sealed partial class WH40KWaveDefencePickLaneTargetOperator : HTNOperator
{
    [Dependency] private  IEntityManager _entityManager = default!;
    private WH40KWaveDefenceObjectiveNavigationSystem _objectiveNavigation = default!;
    private PathfindingSystem _pathfinding = default!;

    [DataField("targetKey")]
    public string TargetKey = WH40KWaveDefenceHtnBlackboardKeys.MovementTargetCoordinates;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _objectiveNavigation = sysManager.GetEntitySystem<WH40KWaveDefenceObjectiveNavigationSystem>();
        _pathfinding = sysManager.GetEntitySystem<PathfindingSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        await Task.CompletedTask;

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entityManager.TryGetComponent<WH40KWaveDefenceAttackerComponent>(owner, out var attacker) ||
            attacker.Objective is not { } objective ||
            !_entityManager.EntityExists(objective) ||
            !_entityManager.TryGetComponent<TransformComponent>(owner, out var ownerXform))
        {
            return (false, null);
        }

        if (!_entityManager.TryGetComponent<TransformComponent>(objective, out var objectiveXform))
            return (false, null);

        if (!TryGetStableApproach(attacker, objective, ownerXform.Coordinates, out var targetCoordinates))
        {
            return (false, null);
        }

        if (!targetCoordinates.IsValid(_entityManager))
            targetCoordinates = objectiveXform.Coordinates;

        attacker.DebugState = "advancing-to-objective";
        return (true, new Dictionary<string, object>
        {
            [TargetKey] = targetCoordinates,
            [WH40KWaveDefenceHtnBlackboardKeys.PlayerCombatRole] = false,
            [WH40KWaveDefenceHtnBlackboardKeys.ObjectiveCombatRole] = false,
            [WH40KWaveDefenceHtnBlackboardKeys.MovementRole] = true
        });
    }

    private bool TryGetStableApproach(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityUid objective,
        EntityCoordinates origin,
        out EntityCoordinates targetCoordinates)
    {
        targetCoordinates = EntityCoordinates.Invalid;

        if (attacker.CachedObjectiveApproachObjective == objective &&
            attacker.CachedObjectiveApproach.IsValid(_entityManager) &&
            _pathfinding.GetPoly(attacker.CachedObjectiveApproach) != null)
        {
            targetCoordinates = attacker.CachedObjectiveApproach;
            return true;
        }

        if (!_objectiveNavigation.TryResolveObjectiveApproach(origin, objective, out targetCoordinates))
            return false;

        attacker.CachedObjectiveApproachObjective = objective;
        attacker.CachedObjectiveApproach = targetCoordinates;
        return true;
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }
}
