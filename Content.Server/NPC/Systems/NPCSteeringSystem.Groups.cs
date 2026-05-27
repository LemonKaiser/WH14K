using Content.Server.NPC.Components;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem
{
    private bool TryGetCollectiveGroup(EntityUid uid, out NPCGroupComponent component)
    {
        if (_groupQuery.TryGetComponent(uid, out component!) &&
            component.CollectiveMind &&
            !string.IsNullOrWhiteSpace(component.GroupId))
        {
            return true;
        }

        component = default!;
        return false;
    }

    private bool IsInCollectiveGroup(EntityUid uid, string groupId)
    {
        return _groupQuery.TryGetComponent(uid, out var component) &&
               component.CollectiveMind &&
               component.GroupId == groupId;
    }
}
