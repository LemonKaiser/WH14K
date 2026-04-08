namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Chaos rune tablet used to attune a cultist to a chaos patron and boost gift progression.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KChaosSkrizhalComponent : Component
{
    [DataField("patron")]
    public WH40KChaosPatron Patron = WH40KChaosPatron.Undivided;

    [DataField("boundOwner")]
    public EntityUid? BoundOwner;

    [DataField("bindOnFirstUse")]
    public bool BindOnFirstUse = true;

    [DataField("restrictToBoundOwner")]
    public bool RestrictToBoundOwner = true;

    [DataField("attunementXpReward")]
    public float AttunementXpReward = 25f;

    [DataField("attunementXpMultiplier")]
    public float AttunementXpMultiplier = 1.2f;

    [DataField("attunementInstabilityGain")]
    public float AttunementInstabilityGain = 3f;

    /// <summary>
    /// Minimal delay between successive UI open attempts by the same user.
    /// Prevents local spam from thrashing item toggle/UI sync traffic.
    /// </summary>
    [DataField("uiInteractionCooldownSeconds")]
    public float UiInteractionCooldownSeconds = 0.75f;
}
