using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.HeavyBolter;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WH40KHeavyBolterComponent : Component
{
    [DataField]
    public TimeSpan DeployDelay = TimeSpan.FromSeconds(4);

    [DataField]
    public TimeSpan FoldDelay = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan ToggleCooldown = TimeSpan.FromSeconds(2);

    [DataField, AutoNetworkedField]
    public bool Deployed;

    [DataField]
    public string FixtureId = "bolter";

    [DataField]
    public float FireArcDegrees = 120f;

    [DataField]
    public bool RequireBuckledOperator = true;

    [DataField]
    public float RotateStepDegrees = 15f;

    [DataField]
    public TimeSpan RotateCooldown = TimeSpan.FromSeconds(2);

    [DataField]
    public EntProtoId RotateLeftAction = "ActionWH40KHeavyBolterRotateLeft";

    [DataField]
    public EntProtoId RotateRightAction = "ActionWH40KHeavyBolterRotateRight";

    [DataField]
    public EntityUid? RotateLeftActionEntity;

    [DataField]
    public EntityUid? RotateRightActionEntity;

    [DataField]
    public SoundSpecifier? DeploySound = new SoundPathSpecifier("/Audio/_WH40K/Weapons/gun_mortar_unpack.ogg");

    [DataField]
    public SoundSpecifier? FoldSound = new SoundPathSpecifier("/Audio/_WH40K/Weapons/gun_mortar_unpack.ogg");

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LastToggleAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LastRotateAt;
}
