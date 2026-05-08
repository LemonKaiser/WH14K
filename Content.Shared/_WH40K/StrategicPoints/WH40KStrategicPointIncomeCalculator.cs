using Content.Shared._WH40K.GameMode;

namespace Content.Shared._WH40K.StrategicPoints;

public static class WH40KStrategicPointIncomeCalculator
{
    public static int ApplyPhaseMultiplier(int baseAmount, WH40KBattlePhase phase, ref int remainder)
    {
        if (baseAmount <= 0)
            return 0;

        var (numerator, denominator) = GetPhaseMultiplier(phase);
        var scaled = baseAmount * numerator + remainder;
        var granted = scaled / denominator;
        remainder = scaled % denominator;
        return granted;
    }

    public static int GetEffectiveIncome(int baseAmount, WH40KBattlePhase phase)
    {
        if (baseAmount <= 0)
            return 0;

        var (numerator, denominator) = GetPhaseMultiplier(phase);
        return baseAmount * numerator / denominator;
    }

    public static (int Numerator, int Denominator) GetPhaseMultiplier(WH40KBattlePhase phase)
    {
        return phase switch
        {
            WH40KBattlePhase.Preparation => (1, 2),
            WH40KBattlePhase.Apocalypse => (3, 1),
            _ => (1, 1)
        };
    }
}
