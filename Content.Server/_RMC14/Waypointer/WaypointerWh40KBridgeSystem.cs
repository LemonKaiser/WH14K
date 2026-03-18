using Content.Server._WH40K.Objectives.Components;
using Content.Shared._RMC14.Waypointer.Components;

namespace Content.Server._RMC14.Waypointer;

/// <summary>
/// Bridges WH40K objective entities into the RMC waypointer tracking layer.
/// </summary>
public sealed class WaypointerWh40KBridgeSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KObjectiveComponent, ComponentStartup>(OnObjectiveStartup);
    }

    private void OnObjectiveStartup(Entity<WH40KObjectiveComponent> ent, ref ComponentStartup args)
    {
        EnsureComp<WaypointerTrackableComponent>(ent);
    }
}
