using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Owner-only state for an Imperium psyker currently projected into a disciplined astral trance.
/// The normal SleepingComponent still provides the engine-supported sleep/black-screen rules.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WH40KPsykerAstralProjectionComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan StartedAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan RevealStartsAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan FadeEndsAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan CanExitAt;

    [ViewVariables]
    public TimeSpan NextStrainTickAt;

    [ViewVariables]
    public EntityUid? BarrierEntity;
}
