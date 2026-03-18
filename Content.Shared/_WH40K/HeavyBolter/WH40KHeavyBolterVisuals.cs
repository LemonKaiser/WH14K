using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.HeavyBolter;

[Serializable, NetSerializable]
public enum WH40KHeavyBolterVisuals : byte
{
    State
}

[Serializable, NetSerializable]
public enum WH40KHeavyBolterVisualLayers : byte
{
    Folded,
    Deployed,
    Magazine
}

[Serializable, NetSerializable]
public enum WH40KHeavyBolterVisualState : byte
{
    Folded,
    Deployed
}
