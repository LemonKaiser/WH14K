namespace Content.Server.NPC.Components;

/// <summary>
/// Optional steering coordination tag for NPCs that should behave as a named group.
/// NPCs without collectiveMind enabled keep the old individual behavior.
/// </summary>
[RegisterComponent]
public sealed partial class NPCGroupComponent : Component
{
    [DataField("groupId"), ViewVariables(VVAccess.ReadWrite)]
    public string GroupId = string.Empty;

    [DataField("collectiveMind"), ViewVariables(VVAccess.ReadWrite)]
    public bool CollectiveMind;

    [DataField("coordinateObstacles"), ViewVariables(VVAccess.ReadWrite)]
    public bool CoordinateObstacles = true;

    [DataField("waitForGroupObstacle"), ViewVariables(VVAccess.ReadWrite)]
    public bool WaitForGroupObstacle = true;

    /// <summary>
    /// Local radius for temporary working sub-groups. NPCs outside this radius may handle their own obstacle.
    /// </summary>
    [DataField("workGroupRadius"), ViewVariables(VVAccess.ReadWrite)]
    public float WorkGroupRadius = 3.0f;

    [DataField("separationRadius"), ViewVariables(VVAccess.ReadWrite)]
    public float SeparationRadius = 0.9f;

    [DataField("separationWeight"), ViewVariables(VVAccess.ReadWrite)]
    public float SeparationWeight = 0.65f;
}
