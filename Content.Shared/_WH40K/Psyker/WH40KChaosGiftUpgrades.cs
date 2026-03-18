using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Psyker;

[Serializable, NetSerializable]
public enum WH40KChaosGiftUpgradePath : byte
{
    Power = 0,
    Cooldown = 1,
    Utility = 2
}

[Serializable, NetSerializable]
public enum WH40KChaosGiftUpgradeSlot : byte
{
    GiftOne = 1,
    GiftTwo = 2,
    GiftThree = 3,
    Passive = 4
}

public static class WH40KChaosGiftUpgradeMath
{
    public static float CooldownMultiplier(int tier)
    {
        return tier switch
        {
            1 => 0.85f,
            2 => 0.70f,
            3 => 0.50f,
            _ => 1f,
        };
    }
}
