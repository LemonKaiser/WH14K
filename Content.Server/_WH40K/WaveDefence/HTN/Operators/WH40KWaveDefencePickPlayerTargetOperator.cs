using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Destructible;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Content.Server._WH40K.WaveDefence.Components;
using Content.Shared._WH40K.Combat;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Turrets;
using Robust.Shared.Map;

namespace Content.Server._WH40K.WaveDefence.HTN.Operators;

public sealed partial class WH40KWaveDefencePickPlayerTargetOperator : HTNOperator
{
    private const string VisionRadiusKey = "VisionRadius";
    private const string AggroVisionRadiusKey = "AggroVisionRadius";

    [Dependency] private readonly IEntityManager _entityManager = default!;
    private MobStateSystem _mobState = default!;
    private NpcFactionSystem _npcFaction = default!;
    private WH40KWaveDefenceObjectiveNavigationSystem _objectiveNavigation = default!;
    private NPCPerceptionSystem _perception = default!;

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
        _objectiveNavigation = sysManager.GetEntitySystem<WH40KWaveDefenceObjectiveNavigationSystem>();
        _perception = sysManager.GetEntitySystem<NPCPerceptionSystem>();
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

        if (_entityManager.HasComponent<NPCCombatMemoryComponent>(owner))
        {
            if (!_perception.TryGetCombatTarget(owner, requireVisible: true, out var combatTarget, out var combatCoordinates))
                return (false, null);

            attacker.DebugState = $"combat-visible:{_entityManager.ToPrettyString(combatTarget)}";
            return (true, BuildEffects(
                combatTarget,
                ResolveAttackCoordinates(owner, ownerXform.Coordinates, combatTarget, combatCoordinates)));
        }

        var searchRadius = Math.Max(
            blackboard.GetValueOrDefault<float>(AggroVisionRadiusKey, _entityManager),
            blackboard.GetValueOrDefault<float>(VisionRadiusKey, _entityManager));
        searchRadius = Math.Max(searchRadius, Math.Max(attacker.VisionRadius, attacker.AggroVisionRadius));

        if (!TryPickTarget(owner, ownerXform.MapID, ownerXform.Coordinates, searchRadius, out var target, out var targetCoordinates))
            return (false, null);

        attacker.DebugState = $"combat-target:{_entityManager.ToPrettyString(target)}";
        return (true, BuildEffects(
            target,
            ResolveAttackCoordinates(owner, ownerXform.Coordinates, target, targetCoordinates)));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }

    private Dictionary<string, object> BuildEffects(EntityUid target, EntityCoordinates targetCoordinates)
    {
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

        return effects;
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
            if (!IsUsableCombatTarget(candidate) ||
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

    private EntityCoordinates ResolveAttackCoordinates(
        EntityUid owner,
        EntityCoordinates ownerCoordinates,
        EntityUid target,
        EntityCoordinates fallback)
    {
        if (IsStaticCombatThreat(target) &&
            _entityManager.TryGetComponent<TransformComponent>(target, out var targetXform) &&
            _objectiveNavigation.TryResolvePointApproach(ownerCoordinates, targetXform.Coordinates, out var approach))
        {
            return approach;
        }

        if (_objectiveNavigation.TryResolveSwarmSlotTarget(owner, ownerCoordinates, fallback, out var slot))
            return slot;

        return fallback;
    }

    private bool IsUsableCombatTarget(EntityUid target)
    {
        if (_mobState.IsAlive(target))
            return true;

        if (!IsStaticCombatThreat(target) ||
            !_entityManager.HasComponent<DamageableComponent>(target))
        {
            return false;
        }

        return !_entityManager.TryGetComponent<DestructibleComponent>(target, out var destructible) ||
               !destructible.IsBroken;
    }

    private bool IsStaticCombatThreat(EntityUid target)
    {
        return _entityManager.HasComponent<WH40KTurretProfileComponent>(target) ||
               _entityManager.HasComponent<DeployableTurretComponent>(target);
    }
}
