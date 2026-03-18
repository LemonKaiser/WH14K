using Content.Shared.EnergyDome;
using Content.Shared.DeviceLinking;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server.EnergyDome;

/// <summary>
/// Enables an entity to project an energy dome powered by an attached battery source.
/// </summary>
[RegisterComponent, Access(typeof(EnergyDomeSystem))]
public sealed partial class EnergyDomeGeneratorComponent : Component
{
    [DataField]
    public bool Enabled;

    [DataField("globalEnabled"), ViewVariables(VVAccess.ReadWrite)]
    public bool GlobalEnabled;

    /// <summary>
    /// Whether this generator should start globally enabled when spawned.
    /// Runtime state still uses <see cref="GlobalEnabled"/>.
    /// </summary>
    [DataField("enabledOnSpawn")]
    public bool EnabledOnSpawn;

    /// <summary>
    /// How much battery energy is drained per one point of dome damage.
    /// </summary>
    [DataField]
    public float DamageEnergyDraw = 10f;

    /// <summary>
    /// Whether this can be toggled via direct interaction.
    /// </summary>
    [DataField]
    public bool CanInteractUse = true;

    /// <summary>
    /// Whether this can be toggled via device-link signals.
    /// </summary>
    [DataField]
    public bool CanDeviceNetworkUse;

    /// <summary>
    /// Activation priority when multiple generators attempt to protect the same parent entity.
    /// Higher value wins; ties are resolved deterministically by entity uid.
    /// </summary>
    [DataField]
    public int Priority;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public EntProtoId DomePrototype = "EnergyDomeSmallRed";

    [DataField("mode"), ViewVariables(VVAccess.ReadWrite)]
    public EnergyDomeOperationMode Mode = EnergyDomeOperationMode.Bubble;

    [DataField("useModeProfiles")]
    public bool UseModeProfiles;

    [DataField("useSizeColorProfiles")]
    public bool UseSizeColorProfiles;

    [DataField("useAutoResponseProfiles")]
    public bool UseAutoResponseProfiles = true;

    [DataField("size"), ViewVariables(VVAccess.ReadWrite)]
    public EnergyDomeSizePreset Size = EnergyDomeSizePreset.Small;

    [DataField("color"), ViewVariables(VVAccess.ReadWrite)]
    public EnergyDomeColorPreset Color = EnergyDomeColorPreset.Red;

    [DataField("bubbleDomePrototype")]
    public EntProtoId BubbleDomePrototype = "EnergyDomeSmallRed";

    [DataField("wallDomePrototype")]
    public EntProtoId WallDomePrototype = "EnergyDomeSmallRed";

    /// <summary>
    /// Additional one-time battery cost paid when enabling the shield.
    /// </summary>
    [DataField("activationEnergyCost")]
    public float ActivationEnergyCost;

    /// <summary>
    /// Additional battery draw per second while active.
    /// </summary>
    [DataField("passiveEnergyDraw")]
    public float PassiveEnergyDraw;

    /// <summary>
    /// Global multiplier for impact-energy draw channel.
    /// </summary>
    [DataField("impactEnergyDrawMultiplier")]
    public float ImpactEnergyDrawMultiplier = 1f;

    [DataField("bubbleCostMultiplier")]
    public float BubbleCostMultiplier = 1f;

    [DataField("wallCostMultiplier")]
    public float WallCostMultiplier = 0.65f;

    /// <summary>
    /// Local forward offset (in tiles) applied to spawned dome in wall mode.
    /// </summary>
    [DataField("wallForwardOffset")]
    public float WallForwardOffset;

    [DataField("wallSide"), ViewVariables(VVAccess.ReadWrite)]
    public EnergyDomeWallSide WallSide = EnergyDomeWallSide.Front;

    [DataField("heatImpactMultiplier")]
    public float HeatImpactMultiplier = 1f;

    [DataField("piercingImpactMultiplier")]
    public float PiercingImpactMultiplier = 1f;

    [DataField("otherImpactMultiplier")]
    public float OtherImpactMultiplier = 1f;

    [DataField("burstWindow")]
    public TimeSpan BurstWindow = TimeSpan.FromSeconds(0.60f);

    [DataField("burstStepMultiplier")]
    public float BurstStepMultiplier = 0.15f;

    [DataField("burstMaxMultiplier")]
    public float BurstMaxMultiplier = 1.90f;

    [DataField("stressEnabled")]
    public bool StressEnabled = true;

    [DataField("stressPerDamage")]
    public float StressPerDamage = 0.0030f;

    [DataField("stressDecayPerSecond")]
    public float StressDecayPerSecond = 0.080f;

    [DataField("stressDecayInactiveMultiplier")]
    public float StressDecayInactiveMultiplier = 1.30f;

    [DataField("stressImpactEnergyMultiplier")]
    public float StressImpactEnergyMultiplier = 0.75f;

    [DataField("stressPassiveEnergyMultiplier")]
    public float StressPassiveEnergyMultiplier = 0.50f;

    [DataField("stressBreakThreshold")]
    public float StressBreakThreshold = 1.00f;

    [DataField("stressWarningThreshold")]
    public float StressWarningThreshold = 0.60f;

    /// <summary>
    /// Optional fixed team id for shield IFF logic (e.g. Imperium / Heretics / Neutral).
    /// </summary>
    [DataField("teamId")]
    public string TeamId = string.Empty;

    [DataField("contestedCheckInterval")]
    public TimeSpan ContestedCheckInterval = TimeSpan.FromSeconds(0.25f);

    [DataField("contestedRequireDistinctTeams")]
    public bool ContestedRequireDistinctTeams = true;

    [DataField("interiorRadiusMultiplier")]
    public float InteriorRadiusMultiplier = 1.0f;

    [DataField("autoResponseProfile"), ViewVariables(VVAccess.ReadWrite)]
    public EnergyDomeAutoResponseProfile AutoResponseProfile = EnergyDomeAutoResponseProfile.Balanced;

    [DataField("uiUpdateInterval")]
    public TimeSpan UiUpdateInterval = TimeSpan.FromSeconds(0.25f);

    [DataField("uiTelemetryDecayPerSecond")]
    public float UiTelemetryDecayPerSecond = 1.2f;

    [DataField("uiSectorRecoveryPerSecond")]
    public float UiSectorRecoveryPerSecond = 0.22f;

    [DataField("uiIncomingBinImpulseScale")]
    public float UiIncomingBinImpulseScale = 0.07f;

    [DataField("uiThreatWindowSeconds")]
    public float UiThreatWindowSeconds = 6.0f;

    [DataField("linkEnabled")]
    public bool LinkEnabled;

    [DataField("linkRange")]
    public float LinkRange = 5.0f;

    [DataField("linkMaxPeers")]
    public int LinkMaxPeers = 2;

    [DataField("linkReserveCharge")]
    public float LinkReserveCharge = 40.0f;

    [DataField("linkTransferEfficiency")]
    public float LinkTransferEfficiency = 0.90f;

    [DataField("linkRequireActiveDonor")]
    public bool LinkRequireActiveDonor = true;

    [ViewVariables]
    public EntityUid? SpawnedDome;

    [ViewVariables]
    public EntityUid? DomeParentEntity;

    [DataField]
    public EntProtoId ToggleAction = "ActionToggleDome";

    [ViewVariables]
    public EntityUid? ToggleActionEntity;

    [DataField]
    public SoundSpecifier AccessDeniedSound = new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg");

    [DataField]
    public SoundSpecifier TurnOnSound = new SoundPathSpecifier("/Audio/Machines/energyshield_up.ogg");

    [DataField]
    public SoundSpecifier EnergyOutSound = new SoundPathSpecifier("/Audio/Machines/energyshield_down.ogg");

    [DataField]
    public SoundSpecifier TurnOffSound = new SoundPathSpecifier("/Audio/Machines/button.ogg");

    [DataField]
    public SoundSpecifier ParrySound = new SoundPathSpecifier("/Audio/Machines/energyshield_parry.ogg");

    [DataField]
    public TimeSpan ParrySoundCooldown = TimeSpan.FromSeconds(0.2);

    [ViewVariables]
    public TimeSpan NextParrySoundAt;

    [ViewVariables]
    public TimeSpan NextVisualUpdateAt;

    [ViewVariables]
    public float LastVisualChargeFraction = float.NaN;

    [ViewVariables]
    public bool WaitingForRechargeReadyEvent;

    [ViewVariables]
    public float Stress;

    [ViewVariables]
    public TimeSpan LastImpactAt;

    [ViewVariables]
    public int BurstHitStreak;

    [ViewVariables]
    public bool Contested;

    [ViewVariables]
    public TimeSpan NextContestedCheckAt;

    [ViewVariables]
    public int LinkedPeerCount;

    [ViewVariables]
    public TimeSpan NextUiUpdateAt;

    [ViewVariables]
    public float UiThreatHeat;

    [ViewVariables]
    public float UiThreatPiercing;

    [ViewVariables]
    public float UiThreatOther;

    [ViewVariables]
    public float[] UiIncomingCompass = new float[8];

    [ViewVariables]
    public float[] UiSectorIntegrity = new float[] { 1f, 1f, 1f, 1f };

    [ViewVariables]
    public TimeSpan UiUptimeSampleAt;

    [ViewVariables]
    public float UiUptimeSampleCharge;

    [ViewVariables]
    public float UiObservedDrawPerSecond;

    [ViewVariables]
    public TimeSpan LastFriendlyPresenceAt;

    [ViewVariables]
    public bool PowerProfileSizeReduced;

    [ViewVariables]
    public bool AutoProfileFriendlyNearby;

    [ViewVariables]
    public TimeSpan NextAutoEnableAttemptAt;

    [DataField]
    public ProtoId<SinkPortPrototype> TogglePort = "Toggle";

    [DataField]
    public ProtoId<SinkPortPrototype> OnPort = "On";

    [DataField]
    public ProtoId<SinkPortPrototype> OffPort = "Off";
}
