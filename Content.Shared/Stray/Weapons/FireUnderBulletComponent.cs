using Robust.Shared.GameStates;
using Content.Shared.Atmos;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Stray.Weapons.FireUnderBullet;


[RegisterComponent]
[NetworkedComponent]
[Access(typeof(SharedFireUnderBulletSystem))]

public sealed partial class FireUnderBulletComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField("ruptureSound")]
    public SoundSpecifier RuptureSound = new SoundPathSpecifier("/Audio/_WH40K/Weapons/flamethrower.ogg");

    [ViewVariables(VVAccess.ReadWrite), DataField("pickedUp")]
    public bool pickedUp = true;

    [ViewVariables(VVAccess.ReadWrite), DataField("releaseSpeed")]
    public float releaseSpeed = 1;

    [ViewVariables(VVAccess.ReadWrite), DataField("releaseTemp")]
    public float releaseTemp = 279;

    [ViewVariables(VVAccess.ReadWrite), DataField("releaseGas")]
    public GasMixture releaseGas = new();

    [ViewVariables(VVAccess.ReadWrite), DataField("hitRelease")]
    public bool HitRelease = false;

    [ViewVariables(VVAccess.ReadWrite), DataField("hotspotExpose")]
    public bool HotspotExpose = false;

    [ViewVariables(VVAccess.ReadWrite), DataField("hotspotTemperature")]
    public float HotspotTemperature = 700f;

    [ViewVariables(VVAccess.ReadWrite), DataField("hotspotVolume")]
    public float HotspotVolume = 50f;

    [ViewVariables(VVAccess.ReadWrite), DataField("hotspotExposeRadius")]
    public int HotspotExposeRadius = 0;

    [ViewVariables(VVAccess.ReadWrite), DataField("hotspotSeedGas")]
    public Gas HotspotSeedGas = Gas.Plasma;

    [ViewVariables(VVAccess.ReadWrite), DataField("hotspotSeedMoles")]
    public float HotspotSeedMoles = 0f;

    [ViewVariables(VVAccess.ReadWrite), DataField("hotspotCleanupDelay")]
    public float HotspotCleanupDelay = 0f;

    [ViewVariables(VVAccess.ReadWrite), DataField("hotspotCleanupRadius")]
    public int HotspotCleanupRadius = 0;

    [ViewVariables(VVAccess.ReadWrite), DataField("hotspotCleanupTemperature")]
    public float HotspotCleanupTemperature = Atmospherics.T20C;

    [ViewVariables(VVAccess.ReadWrite), DataField("hotspotCleanupRemoveGases")]
    public bool HotspotCleanupRemoveGases = true;

    public TimeSpan minusTime = TimeSpan.Zero;
    public TimeSpan removeTime = TimeSpan.Zero;
    public TimeSpan startTime = TimeSpan.Zero;
}
