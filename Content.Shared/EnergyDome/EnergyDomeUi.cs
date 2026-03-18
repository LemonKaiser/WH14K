using Robust.Shared.Serialization;

namespace Content.Shared.EnergyDome;

[Serializable, NetSerializable]
public enum EnergyDomeUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum EnergyDomeAutoResponseProfile : byte
{
    Balanced,
    HoldLine,
    Sustain
}

[Serializable, NetSerializable]
public sealed class EnergyDomeUiLinkedNode
{
    public float RelativeX { get; }
    public float RelativeY { get; }
    public bool Active { get; }
    public bool IsSelf { get; }
    public float ChargeFraction { get; }

    public EnergyDomeUiLinkedNode(float relativeX, float relativeY, bool active, bool isSelf, float chargeFraction)
    {
        RelativeX = relativeX;
        RelativeY = relativeY;
        Active = active;
        IsSelf = isSelf;
        ChargeFraction = chargeFraction;
    }
}

[Serializable, NetSerializable]
public sealed class EnergyDomeBuiState : BoundUserInterfaceState
{
    public bool GlobalEnabled { get; }
    public bool Enabled { get; }
    public bool WaitingRecharge { get; }
    public bool HasPowerCell { get; }
    public bool UseModeProfiles { get; }
    public bool UseSizeColorProfiles { get; }
    public bool UseAutoResponseProfiles { get; }
    public bool ColorSelectionLocked { get; }
    public bool Contested { get; }
    public int LinkedPeerCount { get; }
    public int CooldownRemainingSeconds { get; }
    public int FriendlyInside { get; }
    public int HostileInside { get; }

    public EnergyDomeOperationMode Mode { get; }
    public EnergyDomeSizePreset Size { get; }
    public EnergyDomeColorPreset Color { get; }
    public EnergyDomeWallSide WallSide { get; }
    public EnergyDomeAutoResponseProfile AutoResponseProfile { get; }

    public float ChargeFraction { get; }
    public float OverloadFraction { get; }
    public float PassiveDrawPerSecond { get; }
    public float PredictedUptimeSeconds { get; }
    public float HeatThreatFraction { get; }
    public float PiercingThreatFraction { get; }
    public float OtherThreatFraction { get; }

    public float[] IncomingCompass { get; }
    public float[] SectorIntegrity { get; }
    public EnergyDomeUiLinkedNode[] LinkedNodes { get; }
    public string[] RecommendationLocKeys { get; }

    public EnergyDomeBuiState(
        bool globalEnabled,
        bool enabled,
        bool waitingRecharge,
        bool hasPowerCell,
        bool useModeProfiles,
        bool useSizeColorProfiles,
        bool useAutoResponseProfiles,
        bool colorSelectionLocked,
        bool contested,
        int linkedPeerCount,
        int cooldownRemainingSeconds,
        int friendlyInside,
        int hostileInside,
        EnergyDomeOperationMode mode,
        EnergyDomeSizePreset size,
        EnergyDomeColorPreset color,
        EnergyDomeWallSide wallSide,
        EnergyDomeAutoResponseProfile autoResponseProfile,
        float chargeFraction,
        float overloadFraction,
        float passiveDrawPerSecond,
        float predictedUptimeSeconds,
        float heatThreatFraction,
        float piercingThreatFraction,
        float otherThreatFraction,
        float[] incomingCompass,
        float[] sectorIntegrity,
        EnergyDomeUiLinkedNode[] linkedNodes,
        string[] recommendationLocKeys)
    {
        GlobalEnabled = globalEnabled;
        Enabled = enabled;
        WaitingRecharge = waitingRecharge;
        HasPowerCell = hasPowerCell;
        UseModeProfiles = useModeProfiles;
        UseSizeColorProfiles = useSizeColorProfiles;
        UseAutoResponseProfiles = useAutoResponseProfiles;
        ColorSelectionLocked = colorSelectionLocked;
        Contested = contested;
        LinkedPeerCount = linkedPeerCount;
        CooldownRemainingSeconds = cooldownRemainingSeconds;
        FriendlyInside = friendlyInside;
        HostileInside = hostileInside;
        Mode = mode;
        Size = size;
        Color = color;
        WallSide = wallSide;
        AutoResponseProfile = autoResponseProfile;
        ChargeFraction = chargeFraction;
        OverloadFraction = overloadFraction;
        PassiveDrawPerSecond = passiveDrawPerSecond;
        PredictedUptimeSeconds = predictedUptimeSeconds;
        HeatThreatFraction = heatThreatFraction;
        PiercingThreatFraction = piercingThreatFraction;
        OtherThreatFraction = otherThreatFraction;
        IncomingCompass = incomingCompass;
        SectorIntegrity = sectorIntegrity;
        LinkedNodes = linkedNodes;
        RecommendationLocKeys = recommendationLocKeys;
    }
}

[Serializable, NetSerializable]
public sealed class EnergyDomeUiToggleMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class EnergyDomeUiSetModeMessage(EnergyDomeOperationMode mode) : BoundUserInterfaceMessage
{
    public EnergyDomeOperationMode Mode = mode;
}

[Serializable, NetSerializable]
public sealed class EnergyDomeUiSetSizeMessage(EnergyDomeSizePreset size) : BoundUserInterfaceMessage
{
    public EnergyDomeSizePreset Size = size;
}

[Serializable, NetSerializable]
public sealed class EnergyDomeUiSetColorMessage(EnergyDomeColorPreset color) : BoundUserInterfaceMessage
{
    public EnergyDomeColorPreset Color = color;
}

[Serializable, NetSerializable]
public sealed class EnergyDomeUiSetWallSideMessage(EnergyDomeWallSide side) : BoundUserInterfaceMessage
{
    public EnergyDomeWallSide Side = side;
}

[Serializable, NetSerializable]
public sealed class EnergyDomeUiSetAutoResponseProfileMessage(EnergyDomeAutoResponseProfile profile) : BoundUserInterfaceMessage
{
    public EnergyDomeAutoResponseProfile Profile = profile;
}
