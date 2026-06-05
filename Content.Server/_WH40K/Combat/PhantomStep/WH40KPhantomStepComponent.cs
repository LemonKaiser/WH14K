using Robust.Shared.Prototypes;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Combat.PhantomStep;

[RegisterComponent]
public sealed partial class WH40KPhantomStepComponent : Component
{
    [DataField]
    public bool Enabled = true;

    [DataField]
    public bool DodgeRanged = true;

    [DataField]
    public bool DodgeMelee;

    [DataField]
    public int MaxCharges = 1;

    [ViewVariables]
    public int Charges = 1;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(20);

    [ViewVariables]
    public TimeSpan NextRecharge = TimeSpan.Zero;

    [DataField]
    public int MinDistance = 1;

    [DataField]
    public int MaxDistance = 3;

    [DataField]
    public TimeSpan Invulnerability = TimeSpan.FromSeconds(0.25);

    [ViewVariables]
    public TimeSpan InvulnerableUntil = TimeSpan.Zero;

    [DataField]
    public TimeSpan DashDuration = TimeSpan.FromSeconds(0.12);

    [DataField]
    public int TrailCopies = 5;

    [DataField]
    public TimeSpan TrailLifetime = TimeSpan.FromSeconds(0.32);

    [DataField]
    public EntProtoId AfterimagePrototype = "WH40KPhantomStepAfterimage";

    [DataField]
    public EntProtoId ToggleAction = "ActionWH40KTogglePhantomStep";

    [ViewVariables]
    public EntityUid? ToggleActionEntity;

    [ViewVariables]
    public bool Dashing;

    [ViewVariables]
    public TimeSpan DashStartedAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan DashEndsAt = TimeSpan.Zero;

    [ViewVariables]
    public MapCoordinates DashStart = MapCoordinates.Nullspace;

    [ViewVariables]
    public MapCoordinates DashEnd = MapCoordinates.Nullspace;

    [ViewVariables]
    public EntityCoordinates DashEndCoordinates = EntityCoordinates.Invalid;
}
