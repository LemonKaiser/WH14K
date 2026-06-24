using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.GunGame;

[RegisterComponent]
public sealed partial class WH40KGunGamePickupSpawnerComponent : Component
{
    [DataField("pickupType")]
    public WH40KGunGamePickupType Type;

    [DataField]
    public TimeSpan RespawnDelay = TimeSpan.FromSeconds(30);

    [DataField(required: true)]
    public EntProtoId ItemPrototype = default!;

    [DataField]
    public float PickupRadius = 0.8f;

    [ViewVariables]
    public TimeSpan LastTaken;

    [ViewVariables]
    public bool HasSpawnedItem;

    [ViewVariables]
    public EntityUid? CurrentItem;
}

public enum WH40KGunGamePickupType : byte
{
    Ammo,
    Medkit,
}
