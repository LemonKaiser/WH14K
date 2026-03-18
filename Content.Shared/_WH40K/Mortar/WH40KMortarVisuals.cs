using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Mortar;

[Serializable, NetSerializable]
public enum WH40KMortarVisuals : byte
{
    Item,
    Deployed,
}

[Serializable, NetSerializable]
public enum WH40KMortarVisualLayers : byte
{
    State
}
