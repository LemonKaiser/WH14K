using Robust.Shared.Map;
using Content.Server.Interaction;

namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>
/// Is the specified key within the specified range of us.
/// </summary>
public sealed partial class TargetInRangePrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private InteractionSystem _interaction = default!;

    [DataField("targetKey", required: true)] public string TargetKey = default!;
    [DataField("targetCoordinatesKey")] public string? TargetCoordinatesKey;

    [DataField("rangeKey", required: true)]
    public string RangeKey = default!;

    [DataField]
    public bool Invert;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager))
            return false;

        var range = blackboard.GetValueOrDefault<float>(RangeKey, _entManager);

        if (TargetCoordinatesKey != null &&
            blackboard.TryGetValue<EntityCoordinates>(TargetCoordinatesKey, out var targetCoordinates, _entManager) &&
            targetCoordinates.IsValid(_entManager))
        {
            return _interaction.InRangeUnobstructed(owner, targetCoordinates, range) ^ Invert;
        }

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return false;

        return _interaction.InRangeUnobstructed(owner, target, range) ^ Invert;
    }
}
