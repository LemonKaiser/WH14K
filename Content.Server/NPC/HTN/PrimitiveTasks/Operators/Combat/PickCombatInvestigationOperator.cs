using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Systems;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat;

/// <summary>
/// Picks the last known position of a remembered enemy so the NPC can investigate
/// without following the hidden entity directly.
/// </summary>
public sealed partial class PickCombatInvestigationOperator : HTNOperator
{
    private NPCPerceptionSystem _perception = default!;

    [DataField("targetKey")]
    public string TargetKey = "CombatInvestigationTarget";

    [DataField("targetCoordinatesKey")]
    public string TargetCoordinatesKey = "CombatLastKnownCoordinates";

    [DataField("searchTimeKey")]
    public string SearchTimeKey = "CombatSearchTime";

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
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_perception.TryGetInvestigationPoint(owner, out var target, out var coordinates))
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { TargetKey, target },
            { TargetCoordinatesKey, coordinates },
            { SearchTimeKey, SearchTime },
        });
    }
}
