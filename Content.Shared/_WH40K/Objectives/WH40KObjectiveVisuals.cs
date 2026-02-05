using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Objectives;

/// <summary>
/// Appearance keys and states for WH40K objectives.
/// </summary>
[Serializable, NetSerializable]
public enum WH40KObjectiveVisuals : byte
{
    State
}

[Serializable, NetSerializable]
public enum WH40KObjectiveVisualState : byte
{
    Intact,
    Destroying,
    Destroyed
}

[Serializable, NetSerializable]
public enum WH40KObjectiveVisualLayers : byte
{
    Base,
    Destroying,
    Destroyed
}
