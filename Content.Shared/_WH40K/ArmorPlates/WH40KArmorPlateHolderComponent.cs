using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.ArmorPlates;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class WH40KArmorPlateHolderComponent : Component
{
    public const int MaxSlots = 5;
    public const string SlotIdPrefix = "wh40k-plate-slot-";

    [DataField, AutoNetworkedField]
    public int SlotCount = 1;

    [ViewVariables]
    public Dictionary<string, ItemSlot> PlateSlots = new();

    [ViewVariables]
    public DamageModifierSet BaseModifiers = new();

    [ViewVariables]
    public bool BaseModifiersInitialized;
}
