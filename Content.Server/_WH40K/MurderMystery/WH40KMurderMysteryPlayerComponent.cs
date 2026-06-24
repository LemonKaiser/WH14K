using Content.Shared._WH40K.MurderMystery;

namespace Content.Server._WH40K.MurderMystery;

[RegisterComponent]
public sealed partial class WH40KMurderMysteryPlayerComponent : Component
{
    [DataField]
    public WH40KMurderMysteryRole Role = WH40KMurderMysteryRole.Unassigned;

    [ViewVariables]
    public EntityUid? SmokeActionEntity;

    [ViewVariables]
    public EntityUid? FlashActionEntity;

    [ViewVariables]
    public int SmokeUsesRemaining = 3;

    [ViewVariables]
    public int FlashUsesRemaining = 3;

    [ViewVariables]
    public bool PreviousHandsCanBeStripped = true;

    [ViewVariables]
    public bool RemovedStrippable;

    [ViewVariables]
    public bool Eliminated;

    [ViewVariables]
    public HashSet<EntityUid> ProtectedItems = new();
}
