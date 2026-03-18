using System;
using Content.Shared._WH40K.GameMode;

namespace Content.Shared._WH40K.Command;

/// <summary>
/// Shared command-tree cost curve helpers.
/// Server remains authoritative; client uses the same formula for preview state.
/// </summary>
public static class WH40KCommandTreeCostCalculator
{
    public const int ReserveBasePoints = 24;
    public const int ReservePerBaseLevel = 12;
    public const int ReserveOverflowStepPoints = 15;
    public const int ReserveSurchargePerStep = 3;
    public const int ReserveSurchargeCapPreparation = 18;
    public const int ReserveSurchargeCapAssault = 15;
    public const int ReserveSurchargeCapApocalypse = 9;

    public const int CatchupDiscountPerMissingLevel = 2;
    public const int CatchupDiscountCapPreparation = 0;
    public const int CatchupDiscountCapAssault = 4;
    public const int CatchupDiscountCapApocalypse = 10;

    public static int GetReserveBudget(int baseLevel, WH40KCommandTreeCostProfilePrototype? profile = null)
    {
        var safeLevel = Math.Max(1, baseLevel);
        var reserveBase = Math.Max(0, profile?.ReserveBasePoints ?? ReserveBasePoints);
        var reservePerLevel = Math.Max(0, profile?.ReservePerBaseLevel ?? ReservePerBaseLevel);
        return Math.Max(0, reserveBase + (safeLevel - 1) * reservePerLevel);
    }

    public static int GetReserveSurcharge(
        int commandPoints,
        int baseLevel,
        WH40KBattlePhase phase,
        WH40KCommandTreeCostProfilePrototype? profile = null)
    {
        var safePoints = Math.Max(0, commandPoints);
        var budget = GetReserveBudget(baseLevel, profile);
        var overflow = Math.Max(0, safePoints - budget);
        var overflowStep = Math.Max(1, profile?.ReserveOverflowStepPoints ?? ReserveOverflowStepPoints);
        var surchargePerStep = Math.Max(0, profile?.ReserveSurchargePerStep ?? ReserveSurchargePerStep);

        if (overflow <= 0 || overflowStep <= 0 || surchargePerStep <= 0)
            return 0;

        var steps = overflow / overflowStep;
        if (steps <= 0)
            return 0;

        var raw = steps * surchargePerStep;
        var cap = phase switch
        {
            WH40KBattlePhase.Apocalypse => Math.Max(0, profile?.ReserveSurchargeCapApocalypse ?? ReserveSurchargeCapApocalypse),
            WH40KBattlePhase.Assault => Math.Max(0, profile?.ReserveSurchargeCapAssault ?? ReserveSurchargeCapAssault),
            _ => Math.Max(0, profile?.ReserveSurchargeCapPreparation ?? ReserveSurchargeCapPreparation)
        };

        return Math.Clamp(raw, 0, Math.Max(0, cap));
    }

    public static int GetCatchupDiscount(
        int baseLevel,
        WH40KBattlePhase phase,
        WH40KCommandTreeCostProfilePrototype? profile = null)
    {
        var safeLevel = Math.Max(1, baseLevel);
        var targetLevel = phase switch
        {
            WH40KBattlePhase.Apocalypse => Math.Max(1, profile?.CatchupTargetLevelApocalypse ?? 5),
            WH40KBattlePhase.Assault => Math.Max(1, profile?.CatchupTargetLevelAssault ?? 3),
            _ => Math.Max(1, profile?.CatchupTargetLevelPreparation ?? 1)
        };

        var missingLevels = Math.Max(0, targetLevel - safeLevel);
        var discountPerLevel = Math.Max(0, profile?.CatchupDiscountPerMissingLevel ?? CatchupDiscountPerMissingLevel);

        if (missingLevels <= 0 || discountPerLevel <= 0)
            return 0;

        var raw = missingLevels * discountPerLevel;
        var cap = phase switch
        {
            WH40KBattlePhase.Apocalypse => Math.Max(0, profile?.CatchupDiscountCapApocalypse ?? CatchupDiscountCapApocalypse),
            WH40KBattlePhase.Assault => Math.Max(0, profile?.CatchupDiscountCapAssault ?? CatchupDiscountCapAssault),
            _ => Math.Max(0, profile?.CatchupDiscountCapPreparation ?? CatchupDiscountCapPreparation)
        };

        return Math.Clamp(raw, 0, Math.Max(0, cap));
    }

    public static int GetEffectiveNodeCost(
        int baseCost,
        int commandPoints,
        int baseLevel,
        WH40KBattlePhase phase,
        WH40KCommandTreeCostProfilePrototype? profile = null)
    {
        var safeBaseCost = Math.Max(0, baseCost);
        if (safeBaseCost <= 0)
            return 0;

        var surcharge = GetReserveSurcharge(commandPoints, baseLevel, phase, profile);
        var discount = GetCatchupDiscount(baseLevel, phase, profile);
        return Math.Max(1, safeBaseCost + surcharge - discount);
    }
}
