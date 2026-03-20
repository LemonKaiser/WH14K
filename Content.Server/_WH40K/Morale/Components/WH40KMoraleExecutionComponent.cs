using Content.Shared.Alert;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Morale.Components;

[RegisterComponent]
public sealed partial class WH40KMoraleExecutionComponent : Component
{
    [DataField]
    public float CooldownSeconds = 1200f;

    [DataField]
    public float ExecutionRange = 2f;

    [DataField]
    public float AuraRadius = 10f;

    [DataField]
    public float BuffDurationSeconds = 300f;

    [DataField]
    public float SpeedMultiplier = 1.1f;

    [DataField]
    public float OutgoingDamageMultiplier = 1.1f;

    /// <summary>
    /// Incoming damage multiplier. 0.9 means 10% damage reduction.
    /// </summary>
    [DataField]
    public float IncomingDamageMultiplier = 0.9f;

    [DataField]
    public ProtoId<AlertPrototype> CooldownAlert = "WH40KMoraleExecution";

    [DataField]
    public EntProtoId ActionPrototype = "ActionWH40KMoraleExecution";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public float BlockedKillPopupCooldownSeconds = 1.5f;

    [DataField]
    public TimeSpan NextUseTime = TimeSpan.Zero;

    [DataField]
    public TimeSpan NextBlockedKillPopupTime = TimeSpan.Zero;

    [DataField]
    public bool CooldownShown;
}
