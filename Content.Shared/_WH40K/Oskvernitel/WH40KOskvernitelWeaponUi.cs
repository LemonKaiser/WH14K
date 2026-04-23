using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Oskvernitel;

[Serializable, NetSerializable]
public enum WH40KOskvernitelWeaponUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum WH40KOskvernitelWeaponUiEntryId : byte
{
    Minigun,
    Autogun,
}

[Serializable, NetSerializable]
public sealed class WH40KOskvernitelWeaponEntryState(
    WH40KOskvernitelWeaponUiEntryId id,
    string prototypeId,
    string nameLocKey,
    int currentAmmo,
    int maxAmmo,
    bool selected)
{
    public WH40KOskvernitelWeaponUiEntryId Id { get; } = id;
    public string PrototypeId { get; } = prototypeId;
    public string NameLocKey { get; } = nameLocKey;
    public int CurrentAmmo { get; } = currentAmmo;
    public int MaxAmmo { get; } = maxAmmo;
    public bool Selected { get; } = selected;
}

[Serializable, NetSerializable]
public sealed class WH40KOskvernitelWeaponBuiState(
    WH40KOskvernitelWeaponEntryState[] entries) : BoundUserInterfaceState
{
    public WH40KOskvernitelWeaponEntryState[] Entries { get; } = entries;
}

[Serializable, NetSerializable]
public sealed class WH40KOskvernitelWeaponSelectMessage(
    WH40KOskvernitelWeaponUiEntryId entry) : BoundUserInterfaceMessage
{
    public WH40KOskvernitelWeaponUiEntryId Entry { get; } = entry;
}
