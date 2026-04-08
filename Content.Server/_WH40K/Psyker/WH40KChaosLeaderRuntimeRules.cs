using Content.Shared._WH40K.Psyker;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Leader/follower runtime split for chaos cults.
/// Shared branch unlocks and tiers propagate cult-wide, while EX stays leader-private.
/// </summary>
internal static class WH40KChaosLeaderRuntimeRules
{
    public static bool ShouldGrantGiftSlot(
        WH40KChaosGiftProgressionComponent progression,
        int slot,
        bool isLeader)
    {
        return isLeader || slot switch
        {
            1 => progression.GiftSlotOneUnlocked,
            2 => progression.GiftSlotTwoUnlocked,
            3 => progression.GiftSlotThreeUnlocked,
            _ => false,
        };
    }

    public static bool IsGiftExUnlocked(WH40KChaosGiftProgressionComponent progression, int slot)
    {
        if (!progression.EffectiveLeader)
            return false;

        return slot switch
        {
            1 => progression.KhorneGiftOneExUnlocked,
            2 => progression.KhorneGiftTwoExUnlocked,
            3 => progression.KhorneGiftThreeExUnlocked,
            _ => false,
        };
    }

    public static bool IsPassiveExUnlocked(WH40KChaosGiftProgressionComponent progression)
    {
        return progression.EffectiveLeader && progression.KhornePassiveExUnlocked;
    }
}
