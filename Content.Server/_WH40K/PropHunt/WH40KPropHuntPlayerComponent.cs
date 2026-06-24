using Content.Shared._WH40K.PropHunt;

namespace Content.Server._WH40K.PropHunt;

[RegisterComponent]
public sealed partial class WH40KPropHuntPlayerComponent : Component
{
    [DataField]
    public WH40KPropHuntRole Role;

    [ViewVariables]
    public EntityUid? MorphActionEntity;

    [ViewVariables]
    public EntityUid? HonkActionEntity;

    [ViewVariables]
    public EntityUid? InvisibilityActionEntity;

    [ViewVariables]
    public EntityUid? SmokeActionEntity;

    [ViewVariables]
    public EntityUid? SeekerPulseActionEntity;

    [ViewVariables]
    public EntityUid? Projector;

    [ViewVariables]
    public EntityUid? Disguise;

    [ViewVariables]
    public EntityUid? PrimaryWeapon;

    [ViewVariables]
    public bool GrantedGodmode;

    [ViewVariables]
    public bool PreviousHandsCanBeStripped = true;

    [ViewVariables]
    public bool RemovedCombatModeAction;

    [ViewVariables]
    public bool RemovedScreamAction;

    [ViewVariables]
    public bool RemovedStrippable;

    [ViewVariables]
    public bool HasMorphed;

    [ViewVariables]
    public bool InvisibilityUsed;

    [ViewVariables]
    public bool SmokeUsed;

    [ViewVariables]
    public TimeSpan InvisibleUntil = TimeSpan.Zero;
}
