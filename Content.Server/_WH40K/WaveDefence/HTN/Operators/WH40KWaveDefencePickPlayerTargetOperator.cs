using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server._WH40K.WaveDefence.Components;
using Robust.Shared.Map;
using Content.Shared.Mobs.Systems;

namespace Content.Server._WH40K.WaveDefence.HTN.Operators;

/// <summary>
/// Thin HTN bridge that consumes the authoritative player contact state maintained by WaveDefence AI.
/// Perception itself is handled by the main-thread brain plus the perception scheduler pipeline.
/// </summary>
public sealed partial class WH40KWaveDefencePickPlayerTargetOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    [DataField("targetKey")]
    public string TargetKey = WH40KWaveDefenceHtnBlackboardKeys.PlayerTarget;

    [DataField("targetCoordinatesKey")]
    public string TargetCoordinatesKey = WH40KWaveDefenceHtnBlackboardKeys.PlayerTargetCoordinates;

    [DataField("attackCoordinatesKey")]
    public string AttackCoordinatesKey = WH40KWaveDefenceHtnBlackboardKeys.PlayerTargetCoordinates;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        await Task.CompletedTask;

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entityManager.TryGetComponent<WH40KWaveDefenceAttackerComponent>(owner, out var attacker))
            return (false, null);

        if (TryGetCombatFocus(attacker, out var combatTarget, out var combatCoordinates))
        {
            var effects = new Dictionary<string, object>
            {
                [TargetKey] = combatTarget,
                [TargetCoordinatesKey] = combatCoordinates,
                [WH40KWaveDefenceHtnBlackboardKeys.PlayerCombatRole] = true,
                [WH40KWaveDefenceHtnBlackboardKeys.ObjectiveCombatRole] = false,
                [WH40KWaveDefenceHtnBlackboardKeys.MovementRole] = false
            };

            if (!string.Equals(AttackCoordinatesKey, TargetCoordinatesKey, StringComparison.Ordinal))
                effects[AttackCoordinatesKey] = EntityCoordinates.Invalid;

            return (true, effects);
        }

        return (false, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }

    private bool TryGetCombatFocus(
        WH40KWaveDefenceAttackerComponent attacker,
        out EntityUid target,
        out EntityCoordinates coordinates)
    {
        target = EntityUid.Invalid;
        coordinates = EntityCoordinates.Invalid;

        if (attacker.PlayerContactMode != WH40KWaveDefencePlayerContactMode.VisibleCombat ||
            !attacker.CombatFocusTarget.IsValid() ||
            !attacker.CombatFocusCoordinates.IsValid(_entityManager) ||
            !_entityManager.EntityExists(attacker.CombatFocusTarget) ||
            !_mobState.IsAlive(attacker.CombatFocusTarget))
        {
            return false;
        }

        target = attacker.CombatFocusTarget;
        coordinates = attacker.CombatFocusCoordinates;
        return true;
    }
}
