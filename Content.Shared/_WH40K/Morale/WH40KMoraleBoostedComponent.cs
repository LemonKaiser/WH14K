using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Morale;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WH40KMoraleBoostedComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt = TimeSpan.Zero;

    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 1.1f;

    [DataField, AutoNetworkedField]
    public float OutgoingDamageMultiplier = 1.1f;

    /// <summary>
    /// Incoming damage multiplier. 0.9 means 10% damage reduction.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float IncomingDamageMultiplier = 0.9f;
}
