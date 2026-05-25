using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server._WH40K.WaveDefence.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;

namespace Content.Server._WH40K.WaveDefence.HTN.Operators;

public sealed partial class WH40KWaveDefencePickPlayerTargetOperator : HTNOperator
{
    private const string VisionRadiusKey = "VisionRadius";
    private const string AggroVisionRadiusKey = "AggroVisionRadius";

    [Dependency] private readonly IEntityManager _entityManager = default!;
    private MobStateSystem _mobState = default!;
    private NpcFactionSystem _npcFaction = default!;

    [DataField("targetKey")]
    public string TargetKey = WH40KWaveDefenceHtnBlackboardKeys.PlayerTarget;

    [DataField("targetCoordinatesKey")]
    public string TargetCoordinatesKey = WH40KWaveDefenceHtnBlackboardKeys.PlayerTargetCoordinates;

    [DataField("attackCoordinatesKey")]
    public string AttackCoordinatesKey = WH40KWaveDefenceHtnBlackboardKeys.PlayerTargetCoordinates;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _mobState = sysManager.GetEntitySystem<MobStateSystem>();
        _npcFaction = sysManager.GetEntitySystem<NpcFactionSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        await Task.CompletedTask;

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entityManager.TryGetComponent<WH40KWaveDefenceAttackerComponent>(owner, out var attacker) ||
            !_entityManager.TryGetComponent<TransformComponent>(owner, out var ownerXform))
        {
            return (false, null);
        }

        var searchRadius = Math.Max(
            blackboard.GetValueOrDefault<float>(AggroVisionRadiusKey, _entityManager),
            blackboard.GetValueOrDefault<float>(VisionRadiusKey, _entityManager));
        searchRadius = Math.Max(searchRadius, Math.Max(attacker.VisionRadius, attacker.AggroVisionRadius));

        if (!TryPickTarget(owner, ownerXform.MapID, ownerXform.Coordinates, searchRadius, out var target, out var targetCoordinates))
            return (false, null);

        attacker.DebugState = $"combat-target:{_entityManager.ToPrettyString(target)}";
        var effects = new Dictionary<string, object>
        {
            [TargetKey] = target,
            [TargetCoordinatesKey] = targetCoordinates,
            [WH40KWaveDefenceHtnBlackboardKeys.PlayerCombatRole] = true,
            [WH40KWaveDefenceHtnBlackboardKeys.ObjectiveCombatRole] = false,
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

    private bool TryPickTarget(
        EntityUid owner,
        MapId ownerMap,
        EntityCoordinates ownerCoordinates,
        float searchRadius,
        out EntityUid target,
        out EntityCoordinates targetCoordinates)
    {
        target = EntityUid.Invalid;
        targetCoordinates = EntityCoordinates.Invalid;

        var hostiles = _npcFaction.GetNearbyHostiles((
            owner,
            _entityManager.TryGetComponent(owner, out NpcFactionMemberComponent? faction) ? faction : null,
            _entityManager.TryGetComponent(owner, out FactionExceptionComponent? exception) ? exception : null), searchRadius);

        var bestAnyDistance = float.MaxValue;
        var bestAnyTarget = EntityUid.Invalid;
        var bestAnyCoordinates = EntityCoordinates.Invalid;

        foreach (var candidate in hostiles)
        {
            if (!_mobState.IsAlive(candidate) ||
                !_entityManager.TryGetComponent<TransformComponent>(candidate, out var candidateXform) ||
                candidateXform.MapID != ownerMap)
            {
                continue;
            }

            if (!ownerCoordinates.TryDistance(_entityManager, candidateXform.Coordinates, out var distance))
                continue;

            if (distance < bestAnyDistance)
            {
                bestAnyDistance = distance;
                bestAnyTarget = candidate;
                bestAnyCoordinates = candidateXform.Coordinates;
            }

        }

        if (bestAnyTarget != EntityUid.Invalid)
        {
            target = bestAnyTarget;
            targetCoordinates = bestAnyCoordinates;
            return true;
        }

        return false;
    }
}
