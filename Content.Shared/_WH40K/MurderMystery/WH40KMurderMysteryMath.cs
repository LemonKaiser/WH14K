namespace Content.Shared._WH40K.MurderMystery;

public readonly record struct WH40KMurderMysteryRoleSplit(int Murders, int Sheriffs, int Civilians);

public static class WH40KMurderMysteryMath
{
    public static int GetMurdersForPlayers(int totalPlayers)
    {
        if (totalPlayers <= 0)
            return 0;

        return Math.Max(1, (int) Math.Ceiling(totalPlayers / 10f));
    }

    public static int GetSheriffsForPlayers(int totalPlayers)
    {
        if (totalPlayers <= 0)
            return 0;

        var murders = GetMurdersForPlayers(totalPlayers);
        return Math.Min(GetMurdersForPlayers(totalPlayers), Math.Max(0, totalPlayers - murders));
    }

    public static WH40KMurderMysteryRoleSplit GetRoleSplit(int totalPlayers)
    {
        if (totalPlayers <= 0)
            return new WH40KMurderMysteryRoleSplit(0, 0, 0);

        var murders = GetMurdersForPlayers(totalPlayers);
        var sheriffs = GetSheriffsForPlayers(totalPlayers);
        var civilians = Math.Max(0, totalPlayers - murders - sheriffs);
        return new WH40KMurderMysteryRoleSplit(murders, sheriffs, civilians);
    }
}
