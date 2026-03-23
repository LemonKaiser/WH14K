using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.MetaProgress;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WH40KCharacterDevelopmentSpeedBoostComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt = TimeSpan.Zero;

    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 1.10f;
}
