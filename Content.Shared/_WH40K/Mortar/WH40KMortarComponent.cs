using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.Mortar;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WH40KMortarComponent : Component
{
    [DataField, AutoNetworkedField]
    public string ContainerId = "wh40k_mortar_container";

    [DataField, AutoNetworkedField]
    public TimeSpan DeployDelay = TimeSpan.FromSeconds(4);

    [DataField, AutoNetworkedField]
    public TimeSpan FoldDelay = TimeSpan.FromSeconds(3);

    [DataField, AutoNetworkedField]
    public bool Deployed;

    [DataField, AutoNetworkedField]
    public Vector2i Target;

    [DataField, AutoNetworkedField]
    public Vector2i Dial;

    [DataField("fireDelay"), AutoNetworkedField]
    public TimeSpan FireDelay = TimeSpan.FromSeconds(10);

    [DataField, AutoNetworkedField]
    public int MaxTarget = 1000;

    [DataField, AutoNetworkedField]
    public int MaxDial = 10;

    [DataField("minimumRange"), AutoNetworkedField]
    public int MinimumRange = 15;

    [DataField("maximumRange"), AutoNetworkedField]
    public int MaximumRange = 65;

    [DataField, AutoNetworkedField]
    public bool LaserTargetingMode;

    [DataField, AutoNetworkedField]
    public int? LinkedDesignatorId;

    [DataField, AutoNetworkedField]
    public string FixtureId = "mortar";

    [DataField, AutoNetworkedField]
    public string AnimationLayer = "mortar";

    [DataField, AutoNetworkedField]
    public string AnimationState = "mortar_m402_fire";

    [DataField, AutoNetworkedField]
    public string DeployedState = "mortar_m402";

    [DataField, AutoNetworkedField]
    public TimeSpan AnimationTime = TimeSpan.FromSeconds(0.3);

    [DataField, AutoNetworkedField]
    public SoundSpecifier? DeploySound = new SoundPathSpecifier("/Audio/_WH40K/Weapons/gun_mortar_unpack.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? ReloadSound = new SoundPathSpecifier("/Audio/_WH40K/Weapons/gun_mortar_reload.ogg", AudioParams.Default.WithVariation(0.4f));

    [DataField, AutoNetworkedField]
    public SoundSpecifier? FireSound = new SoundPathSpecifier("/Audio/_WH40K/Weapons/gun_mortar_fire.ogg", AudioParams.Default.AddVolume(4f));

    [DataField, AutoNetworkedField]
    public SoundSpecifier? TravelSound = new SoundPathSpecifier("/Audio/_WH40K/Weapons/gun_mortar_travel.ogg");

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan LastFiredAt;

    [DataField, AutoNetworkedField]
    public int[] FireRandomOffset = new[] { -1, 0, 0, 1 };

    [DataField, AutoNetworkedField]
    public bool UseRandomScatter = true;
}
