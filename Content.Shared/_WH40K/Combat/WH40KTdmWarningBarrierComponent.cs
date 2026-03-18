namespace Content.Shared._WH40K.Combat;

[RegisterComponent]
public sealed partial class WH40KTdmWarningBarrierComponent : Component
{
    [DataField]
    public float PushbackDistance = 0.8f;

    [DataField]
    public float PopupCooldownSeconds = 1.5f;

    [DataField]
    public string PopupLocPrefix = "wh40k-tdm-warning-barrier-popup";

    [DataField]
    public string GenericPopupLocKey = "wh40k-tdm-warning-barrier-popup-generic";
}
