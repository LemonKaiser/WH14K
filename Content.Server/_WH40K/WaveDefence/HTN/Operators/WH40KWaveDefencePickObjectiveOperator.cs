using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server._WH40K.WaveDefence.Components;
using Content.Shared._WH40K.WaveDefence;
using Robust.Shared.Map;

namespace Content.Server._WH40K.WaveDefence.HTN.Operators;

public sealed partial class WH40KWaveDefencePickObjectiveOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    private WH40KWaveDefenceObjectiveNavigationSystem _objectiveNavigation = default!;

    [DataField("targetKey")]
    public string TargetKey = WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTarget;

    [DataField("targetCoordinatesKey")]
    public string TargetCoordinatesKey = WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTargetCoordinates;

    [DataField("attackCoordinatesKey")]
    public string AttackCoordinatesKey = WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTargetCoordinates;

    [DataField("maxDistance")]
    public float? MaxDistance;

    [DataField("requireLineOfSight")]
    public bool RequireLineOfSight = true;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _objectiveNavigation = sysManager.GetEntitySystem<WH40KWaveDefenceObjectiveNavigationSystem>();
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
            !_entityManager.TryGetComponent<WH40KWaveDefenceObjectiveComponent>(objective, out var objectiveComp) ||
            objectiveComp.Destroyed ||
            !_entityManager.TryGetComponent<TransformComponent>(owner, out var ownerXform) ||
            !_entityManager.TryGetComponent<TransformComponent>(objective, out var objectiveXform) ||
            ownerXform.MapID != objectiveXform.MapID)
        {
            return (false, null);
        }

        if (!_objectiveNavigation.TryResolveObjectiveAssaultTarget(owner, ownerXform.Coordinates, objective, out var targetCoordinates))
            targetCoordinates = objectiveXform.Coordinates;

        if (MaxDistance is { } maxDistance &&
            (!ownerXform.Coordinates.TryDistance(_entityManager, targetCoordinates, out var distance) ||
             distance > maxDistance))
        {
            return (false, null);
        }

        attacker.DebugState = "attacking-objective";
        var effects = new Dictionary<string, object>
        {
            [TargetKey] = objective,
            [TargetCoordinatesKey] = targetCoordinates,
            [WH40KWaveDefenceHtnBlackboardKeys.PlayerCombatRole] = false,
            [WH40KWaveDefenceHtnBlackboardKeys.ObjectiveCombatRole] = true,
            [WH40KWaveDefenceHtnBlackboardKeys.MovementRole] = false
        };

        if (!string.Equals(AttackCoordinatesKey, TargetCoordinatesKey, StringComparison.Ordinal))
            effects[AttackCoordinatesKey] = targetCoordinates;

        return (true, effects);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }
}
