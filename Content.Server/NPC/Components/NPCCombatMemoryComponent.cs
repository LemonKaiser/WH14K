using Robust.Shared.Map;

namespace Content.Server.NPC.Components;

/// <summary>
/// Stores combat contacts known by an NPC. Contacts may be personally visible or
/// reported by nearby group members, and decay into last-known-position searches.
/// </summary>
[RegisterComponent]
public sealed partial class NPCCombatMemoryComponent : Component
{
    [ViewVariables]
    public readonly Dictionary<EntityUid, NPCCombatContact> Contacts = new();

    [ViewVariables]
    public EntityUid AssignedTarget = EntityUid.Invalid;

    [ViewVariables]
    public EntityUid AssignedBy = EntityUid.Invalid;
}

public sealed class NPCCombatContact
{
    [ViewVariables]
    public EntityUid Target;

    [ViewVariables]
    public EntityCoordinates LastKnownCoordinates;

    [ViewVariables]
    public TimeSpan LastSeen;

    [ViewVariables]
    public TimeSpan LastUpdated;

    [ViewVariables]
    public TimeSpan VisibleUntil;

    [ViewVariables]
    public float InitialConfidence;

    [ViewVariables]
    public bool PersonallySeen;

    [ViewVariables]
    public bool Reported;

    [ViewVariables]
    public EntityUid ReportedBy = EntityUid.Invalid;

    [ViewVariables]
    public NPCCombatContactState State = NPCCombatContactState.Reported;
}

public enum NPCCombatContactState : byte
{
    Visible,
    Reported,
    Investigating,
    Searching,
    Lost
}
