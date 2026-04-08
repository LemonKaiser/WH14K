using System;
using System.Numerics;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Tank;

public enum WH40KTankCrewRole : byte
{
    Driver = 0,
    Gunner = 1,
    Commander = 2,
    Loader = 3,
}

[Serializable, NetSerializable]
public enum WH40KTankEngineState : byte
{
    Off = 0,
    Running = 1,
}

[Serializable, NetSerializable]
public enum WH40KTankVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum WH40KTankVisualLayers : byte
{
    Tracks,
}

[Serializable, NetSerializable]
public enum WH40KTankVisualState : byte
{
    Idle = 0,
    Moving = 1,
}

public enum WH40KTankAudioState : byte
{
    Off = 0,
    Starting = 1,
    Idle = 2,
    Accelerating = 3,
    Moving = 4,
    Decelerating = 5,
    Stopping = 6,
}

[DataDefinition]
public sealed partial class WH40KTankDirectionalOffsetSet
{
    [DataField]
    public Vector2 South = Vector2.Zero;

    [DataField]
    public Vector2 North = Vector2.Zero;

    [DataField]
    public Vector2 East = Vector2.Zero;

    [DataField]
    public Vector2 West = Vector2.Zero;

    public Vector2 Resolve(Direction direction, Vector2 fallback)
    {
        return direction switch
        {
            Direction.South => South,
            Direction.North => North,
            Direction.East => East,
            Direction.West => West,
            _ => fallback,
        };
    }
}

[RegisterComponent]
public sealed partial class WH40KTankComponent : Component
{
    [DataField(required: true)]
    public EntProtoId TurretPrototype = default!;

    [DataField]
    public Vector2 TurretOffset = Vector2.Zero;

    [DataField]
    public WH40KTankDirectionalOffsetSet? TurretDirectionalOffsets;

    [DataField(required: true)]
    public EntProtoId MainHardpointPrototype = default!;

    [DataField]
    public Vector2 MainHardpointOffset = Vector2.Zero;

    [DataField]
    public WH40KTankDirectionalOffsetSet? MainHardpointDirectionalOffsets;

    [DataField]
    public EntProtoId? CoaxialHardpointPrototype;

    [DataField]
    public Vector2 CoaxialHardpointOffset = Vector2.Zero;

    [DataField(required: true)]
    public EntProtoId DriverStationPrototype = default!;

    [DataField]
    public Vector2 DriverStationOffset = Vector2.Zero;

    [DataField(required: true)]
    public EntProtoId GunnerStationPrototype = default!;

    [DataField]
    public Vector2 GunnerStationOffset = Vector2.Zero;

    [DataField(required: true)]
    public EntProtoId CommanderStationPrototype = default!;

    [DataField]
    public Vector2 CommanderStationOffset = Vector2.Zero;

    [DataField(required: true)]
    public EntProtoId LoaderStationPrototype = default!;

    [DataField]
    public Vector2 LoaderStationOffset = Vector2.Zero;

    [DataField(required: true)]
    public EntProtoId MainGunPrototype = default!;

    [DataField]
    public Vector2 MainGunOffset = Vector2.Zero;

    [DataField]
    public EntProtoId? CoaxialGunPrototype;

    [DataField]
    public Vector2 CoaxialGunOffset = Vector2.Zero;

    [DataField]
    public float TurretTraverseSpeed = 90f;

    [DataField]
    public float TurretAlignmentTolerance = 4f;

    [DataField]
    public EntProtoId? FireCoaxialAction = "ActionWH40KTankFireCoaxial";

    [DataField]
    public EntProtoId DiagnosticsAction = "ActionWH40KTankDiagnostics";

    [DataField]
    public EntProtoId ReloadMainGunAction = "ActionWH40KTankReloadMainGun";

    [DataField]
    public EntProtoId? ReloadCoaxialAction = "ActionWH40KTankReloadCoaxial";

    [DataField]
    public float MainGunReloadSeconds = 4.5f;

    [DataField]
    public float CoaxialReloadSeconds = 5.25f;

    [DataField]
    public float EntryDelaySeconds = 4f;

    [DataField]
    public float ExitDelaySeconds = 4f;

    [DataField]
    public string MainWeaponLocKey = "wh40k-tank-ui-module-main-gun";

    [DataField]
    public string CoaxialWeaponLocKey = "wh40k-tank-ui-module-coaxial";

    [DataField]
    public string MainAmmoLocKey = "wh40k-tank-ui-ammo-rocket-he";

    [DataField]
    public string CoaxialAmmoLocKey = "wh40k-tank-ui-ammo-heavy-bolter";

    [DataField]
    public SoundSpecifier EngineStartSound = new SoundPathSpecifier("/Audio/_WH40K/Vehicles/LemanRuss/tank_start.wav")
    {
        Params = AudioParams.Default.WithVolume(-2f).WithMaxDistance(16f),
    };

    [DataField]
    public SoundSpecifier EngineStopSound = new SoundPathSpecifier("/Audio/_WH40K/Vehicles/LemanRuss/tank_stop.wav")
    {
        Params = AudioParams.Default.WithVolume(-2f).WithMaxDistance(16f),
    };

    [DataField]
    public SoundSpecifier IdleSound = new SoundPathSpecifier("/Audio/_WH40K/Vehicles/LemanRuss/tank_idle.wav")
    {
        Params = AudioParams.Default.WithVolume(-4f).WithMaxDistance(16f),
    };

    [DataField]
    public SoundSpecifier MovementStartSound = new SoundPathSpecifier("/Audio/_WH40K/Vehicles/LemanRuss/tank_accel.wav")
    {
        Params = AudioParams.Default.WithVolume(-2f).WithMaxDistance(16f),
    };

    [DataField]
    public SoundSpecifier MovementLoopSound = new SoundPathSpecifier("/Audio/_WH40K/Vehicles/LemanRuss/tank_move.wav")
    {
        Params = AudioParams.Default.WithVolume(-3f).WithMaxDistance(16f),
    };

    [DataField]
    public SoundSpecifier MovementStopSound = new SoundPathSpecifier("/Audio/_WH40K/Vehicles/LemanRuss/tank_deccel.wav")
    {
        Params = AudioParams.Default.WithVolume(-2f).WithMaxDistance(16f),
    };

    [DataField]
    public SoundSpecifier ReloadSound = new SoundPathSpecifier("/Audio/_WH40K/Vehicles/LemanRuss/tank_reload.wav")
    {
        Params = AudioParams.Default.WithVolume(-1.5f).WithMaxDistance(16f),
    };

    [DataField]
    public SoundSpecifier DestroyedSound = new SoundPathSpecifier("/Audio/_WH40K/Vehicles/LemanRuss/tank_destruction.wav")
    {
        Params = AudioParams.Default.WithVolume(2f).WithMaxDistance(24f),
    };

    [DataField]
    public float HullMaxIntegrity = 350f;

    [DataField]
    public float EngineMaxIntegrity = 65f;

    [DataField]
    public float TracksMaxIntegrity = 90f;

    [DataField]
    public float TurretMaxIntegrity = 75f;

    [DataField]
    public float MainGunMaxIntegrity = 70f;

    [DataField]
    public float CoaxialMaxIntegrity = 50f;

    public EntityUid? Turret;
    public EntityUid? MainHardpoint;
    public EntityUid? CoaxialHardpoint;
    public EntityUid? MainGun;
    public EntityUid? CoaxialGun;
    public EntityUid? DriverStation;
    public EntityUid? GunnerStation;
    public EntityUid? CommanderStation;
    public EntityUid? LoaderStation;
    public EntityUid? DriverOccupant;
    public EntityUid? GunnerOccupant;
    public EntityUid? CommanderOccupant;
    public EntityUid? LoaderOccupant;
    public float EngineDamage;
    public float TracksDamage;
    public float TurretDamage;
    public float MainGunDamage;
    public float CoaxialDamage;
    public EntityUid? FireCoaxialActionEntity;
    public EntityUid? ReloadMainGunActionEntity;
    public EntityUid? ReloadCoaxialActionEntity;
    public MapCoordinates AimTarget = MapCoordinates.Nullspace;
    public bool PendingMainGunFire;
    public bool PendingCoaxialFire;
    public WH40KTankVisualState TrackVisualState = WH40KTankVisualState.Idle;
    public WH40KTankAudioState AudioState = WH40KTankAudioState.Off;
    public EntityUid? AudioLoopStream;
    public EntityUid? AudioTransitionStream;
    public TimeSpan AudioTransitionAt = TimeSpan.Zero;
    public TimeSpan MainGunReloadCompleteAt = TimeSpan.Zero;
    public TimeSpan CoaxialReloadCompleteAt = TimeSpan.Zero;
    public TimeSpan NextUiRefreshAt = TimeSpan.Zero;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KTankStationComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public WH40KTankCrewRole Role = WH40KTankCrewRole.Driver;

    [DataField]
    public Vector2 ExitOffset = Vector2.Zero;

    [AutoNetworkedField]
    public EntityUid? Tank;
    public EntityUid? DiagnosticsActionEntity;
    public EntityUid? PendingEntrant;
    public EntityUid? PendingExitOccupant;
}

[RegisterComponent]
public sealed partial class WH40KTankEngineComponent : Component
{
    public WH40KTankEngineState State = WH40KTankEngineState.Off;
}

[RegisterComponent]
public sealed partial class WH40KTankFuelComponent : Component
{
    [DataField]
    public string Solution = "tank";

    [DataField]
    public FixedPoint2 StartupConsumption = FixedPoint2.New(0.5f);

    [DataField]
    public FixedPoint2 IdleConsumption = FixedPoint2.New(0.03f);

    [DataField]
    public FixedPoint2 MovementConsumption = FixedPoint2.New(0.10f);
}

[RegisterComponent]
public sealed partial class WH40KTankDriveComponent : Component
{
    [DataField]
    public float ForwardSpeed = 3.25f;

    [DataField]
    public float ReverseSpeed = 1.75f;

    [DataField]
    public float TurnSpeed = 1.35f;

    [DataField]
    public float PivotTurnSpeed = 0.95f;

    [DataField]
    public float Acceleration = 4.5f;

    [DataField]
    public float BrakeDeceleration = 6f;

    [DataField]
    public float LateralDamping = 10f;

    [DataField]
    public float AngularAcceleration = 4.5f;

    [DataField]
    public float AngularDeceleration = 6f;
}
