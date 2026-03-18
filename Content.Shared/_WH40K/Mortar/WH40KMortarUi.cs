using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Mortar;

[Serializable, NetSerializable]
public enum WH40KMortarUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class WH40KMortarBuiState : BoundUserInterfaceState
{
    public Vector2i Target { get; }
    public Vector2i Dial { get; }
    public Vector2i Position { get; }
    public Vector2i LinkedTarget { get; }
    public int MaxTarget { get; }
    public int MaxDial { get; }
    public int MinimumRange { get; }
    public int MaximumRange { get; }
    public int FireDelaySeconds { get; }
    public int CooldownRemainingSeconds { get; }
    public int LinkedDesignatorId { get; }
    public bool Deployed { get; }
    public bool Loaded { get; }
    public bool LaserTargetingMode { get; }
    public bool LinkedDesignatorAssigned { get; }
    public bool HasLinkedTarget { get; }
    public bool LinkedTargetSameGrid { get; }
    public string LoadedShellType { get; }

    public WH40KMortarBuiState(
        Vector2i target,
        Vector2i dial,
        Vector2i position,
        Vector2i linkedTarget,
        int maxTarget,
        int maxDial,
        int minimumRange,
        int maximumRange,
        int fireDelaySeconds,
        int cooldownRemainingSeconds,
        int linkedDesignatorId,
        bool deployed,
        bool loaded,
        bool laserTargetingMode,
        bool linkedDesignatorAssigned,
        bool hasLinkedTarget,
        bool linkedTargetSameGrid,
        string loadedShellType)
    {
        Target = target;
        Dial = dial;
        Position = position;
        LinkedTarget = linkedTarget;
        MaxTarget = maxTarget;
        MaxDial = maxDial;
        MinimumRange = minimumRange;
        MaximumRange = maximumRange;
        FireDelaySeconds = fireDelaySeconds;
        CooldownRemainingSeconds = cooldownRemainingSeconds;
        LinkedDesignatorId = linkedDesignatorId;
        Deployed = deployed;
        Loaded = loaded;
        LaserTargetingMode = laserTargetingMode;
        LinkedDesignatorAssigned = linkedDesignatorAssigned;
        HasLinkedTarget = hasLinkedTarget;
        LinkedTargetSameGrid = linkedTargetSameGrid;
        LoadedShellType = loadedShellType;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KMortarSetTargetMessage(Vector2i target) : BoundUserInterfaceMessage
{
    public Vector2i Target = target;
}

[Serializable, NetSerializable]
public sealed class WH40KMortarSetDialMessage(Vector2i dial) : BoundUserInterfaceMessage
{
    public Vector2i Dial = dial;
}

[Serializable, NetSerializable]
public sealed class WH40KMortarFireMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WH40KMortarToggleLaserModeMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WH40KMortarSetLinkedDesignatorMessage(int designatorId) : BoundUserInterfaceMessage
{
    public int DesignatorId = designatorId;
}
