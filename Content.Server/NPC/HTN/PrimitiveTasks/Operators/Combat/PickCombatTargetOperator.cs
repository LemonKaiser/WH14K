using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Systems;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat;

/// <summary>
/// Picks a target from combat memory. This avoids using raw nearby-hostile queries
/// as combat knowledge and keeps hidden targets as last-known positions instead.
/// </summary>
public sealed partial class PickCombatTargetOperator : HTNOperator
{
    private NPCPerceptionSystem _perception = default!;

    [DataField("targetKey")]
    public string TargetKey = "Target";

    [DataField("targetCoordinatesKey")]
    public string TargetCoordinatesKey = "TargetCoordinates";

    [DataField("requireVisible")]
    public bool RequireVisible = true;

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

        if (!_perception.TryGetCombatTarget(owner, RequireVisible, out var target, out var coordinates))
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { TargetKey, target },
            { TargetCoordinatesKey, coordinates },
        });
    }
}
