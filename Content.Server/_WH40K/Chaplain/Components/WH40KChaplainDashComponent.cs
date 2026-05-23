using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Chaplain.Components;

[RegisterComponent]
public sealed partial class WH40KChaplainDashComponent : Component
{
    [DataField]
    public float CooldownSeconds = 300f;

    [DataField]
    public float DashRange = 16f;

    [DataField]
    public float ThrowSpeed = 20f;

    [DataField]
    public float Damage = 16f;

    [DataField]
    public float KnockdownSeconds = 2f;

    [DataField]
    public float StunSeconds = 0.8f;

    [DataField]
    public float HitPadding = 0.15f;

    [DataField]
    public EntProtoId ActionPrototype = "ActionWH40KChaplainDash";

    [DataField]
    public EntityUid? ActionEntity;
}
