using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server._WH40K.WaveDefence.Components;
using Content.Shared.Examine;
using Content.Shared._WH40K.WaveDefence;
using Robust.Shared.Map;

namespace Content.Server._WH40K.WaveDefence.HTN.Operators;

public sealed partial class WH40KWaveDefencePickObjectiveOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;

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

    [DataField("ignoreMaxDistanceWhenRouteCompleted")]
    public bool IgnoreMaxDistanceWhenRouteCompleted = true;

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
            !_entityManager.TryGetComponent<TransformComponent>(objective, out var objectiveXform))
        {
            return (false, null);
        }

        if (ownerXform.MapID != objectiveXform.MapID)
            return (false, null);

        if (!TryGetAuthoritativeObjectiveTarget(attacker, objective, out var targetCoordinates))
            return (false, null);

        var ignoreMaxDistance = IgnoreMaxDistanceWhenRouteCompleted && attacker.RouteCompleted;
        if (!ignoreMaxDistance &&
            MaxDistance is { } maxDistance &&
            (!ownerXform.Coordinates.TryDistance(_entityManager, targetCoordinates, out var distance) ||
             distance > maxDistance))
        {
            return (false, null);
        }

        if (RequireLineOfSight &&
            MaxDistance is { } losRange &&
            !_examine.InRangeUnOccluded(owner, objective, losRange + 0.5f, null))
        {
            return (false, null);
        }

        var effects = new Dictionary<string, object>
        {
            [TargetKey] = objective,
            [TargetCoordinatesKey] = targetCoordinates,
            [WH40KWaveDefenceHtnBlackboardKeys.PlayerCombatRole] = false,
            [WH40KWaveDefenceHtnBlackboardKeys.ObjectiveCombatRole] = true,
            [WH40KWaveDefenceHtnBlackboardKeys.MovementRole] = false
        };

        if (!string.Equals(AttackCoordinatesKey, TargetCoordinatesKey, StringComparison.Ordinal))
            effects[AttackCoordinatesKey] = EntityCoordinates.Invalid;

        return (true, effects);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }

    private bool TryGetAuthoritativeObjectiveTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityUid objective,
        out EntityCoordinates targetCoordinates)
    {
        if (attacker.ForcedTargetKind == WH40KWaveDefenceForcedTargetKind.DirectObjective &&
            attacker.ForcedTarget.IsValid(_entityManager))
        {
            targetCoordinates = attacker.ForcedTarget;
            return true;
        }

        if (attacker.MovementTargetDirective.IsValid(_entityManager) &&
            IsObjectiveMovementDirective(attacker, objective))
        {
            targetCoordinates = attacker.MovementTargetDirective;
            return true;
        }

        if (attacker.DesiredTargetProposal.IsValid(_entityManager) &&
            IsObjectiveProposal(attacker, objective))
        {
            targetCoordinates = attacker.DesiredTargetProposal;
            return true;
        }

        if (attacker.LocomotionMode == WH40KWaveDefenceLocomotionMode.Objective &&
            attacker.LocomotionTarget.IsValid(_entityManager) &&
            IsObjectiveLocomotionTarget(attacker, objective))
        {
            targetCoordinates = attacker.LocomotionTarget;
            return true;
        }

        targetCoordinates = EntityCoordinates.Invalid;
        return false;
    }

    private bool IsObjectiveMovementDirective(WH40KWaveDefenceAttackerComponent attacker, EntityUid objective)
    {
        var label = attacker.MovementTargetDirectiveLabel;
        if (string.IsNullOrWhiteSpace(label))
            return false;

        if (label.StartsWith("objective:", StringComparison.Ordinal))
            return true;

        return label.StartsWith("forced:objective", StringComparison.Ordinal) &&
               attacker.Objective == objective;
    }

    private bool IsObjectiveProposal(WH40KWaveDefenceAttackerComponent attacker, EntityUid objective)
    {
        var label = attacker.DesiredTargetProposalLabel;
        if (string.IsNullOrWhiteSpace(label))
            return false;

        if (label.StartsWith("objective:", StringComparison.Ordinal))
            return true;

        return label.StartsWith("forced:objective", StringComparison.Ordinal) &&
               attacker.Objective == objective;
    }

    private bool IsObjectiveLocomotionTarget(WH40KWaveDefenceAttackerComponent attacker, EntityUid objective)
    {
        var label = attacker.LocomotionTargetLabel;
        if (string.IsNullOrWhiteSpace(label))
            return false;

        if (label.StartsWith("objective:", StringComparison.Ordinal))
            return true;

        return label.StartsWith("forced:objective", StringComparison.Ordinal) &&
               attacker.Objective == objective;
    }
}
