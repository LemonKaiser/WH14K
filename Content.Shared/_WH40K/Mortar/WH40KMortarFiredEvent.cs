using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Mortar;

[Serializable, NetSerializable]
public sealed class WH40KMortarFiredEvent(NetEntity mortar) : EntityEventArgs
{
    public readonly NetEntity Mortar = mortar;
}
