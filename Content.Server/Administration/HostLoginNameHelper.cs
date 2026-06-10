using System;

namespace Content.Server.Administration;

internal static class HostLoginNameHelper
{
    private const string LocalPrefix = "localhost@";
    private const string GuestPrefix = "guest@";

    public static bool MatchesConfiguredHostUser(string actualUserName, string configuredHostUser)
    {
        if (string.IsNullOrWhiteSpace(configuredHostUser))
            return false;

        if (string.Equals(actualUserName, configuredHostUser, StringComparison.Ordinal))
            return true;

        return TryStripPrefix(actualUserName, LocalPrefix, configuredHostUser)
               || TryStripPrefix(actualUserName, GuestPrefix, configuredHostUser);
    }

    private static bool TryStripPrefix(string actualUserName, string prefix, string configuredHostUser)
    {
        return actualUserName.StartsWith(prefix, StringComparison.Ordinal)
               && string.Equals(actualUserName[prefix.Length..], configuredHostUser, StringComparison.Ordinal);
    }
}
