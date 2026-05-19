using System;
using System.Collections.Generic;
using Content.Server.NPC.Systems;
using Content.Shared.Actions.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.NPC.Components;

/// <summary>
/// Allows an NPC to repeatedly try using one or more actions on targets resolved from its blackboard.
/// </summary>
[RegisterComponent, Access(typeof(NPCUseActionOnTargetSystem))]
public sealed partial class NPCUseActionOnTargetComponent : Component
{
    /// <summary>
    /// Actions this NPC may use, together with the blackboard keys they read targets from.
    /// </summary>
    [DataField]
    public List<NpcActionData> Actions = new();
}

/// <summary>
/// Describes a single NPC action slot and how it is sourced.
/// </summary>
[Serializable]
[DataDefinition]
public sealed partial class NpcActionData
{
    /// <summary>
    /// Prototype used to spawn or match the action.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId<ActionComponent> ActionId;

    /// <summary>
    /// Blackboard key used to retrieve the current target entity for this action.
    /// </summary>
    [DataField]
    public string TargetKey = "Target";

    /// <summary>
    /// Runtime entity for the action, if currently available.
    /// </summary>
    [DataField]
    public EntityUid? ActionEnt;

    /// <summary>
    /// If true, do not spawn an action and instead wait for a matching action to be added externally.
    /// </summary>
    [DataField]
    public bool Ref;
}
