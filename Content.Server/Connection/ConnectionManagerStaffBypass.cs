using System.Linq;
using Content.Server.Database;
using Content.Shared.Administration;

namespace Content.Server.Connection;

public static class ConnectionManagerStaffBypass
{
    private const AdminFlags DiscordAuthBypassFlags = AdminFlags.Moderator | AdminFlags.Admin | AdminFlags.Host;
    private const AdminFlags HostBypassFlags = AdminFlags.Host;

    public static bool HasDiscordAuthBypass(Admin? adminData)
    {
        return HasAnyFlag(adminData, DiscordAuthBypassFlags, requireUnsuspended: true);
    }

    public static bool HasHostBanBypass(Admin? adminData)
    {
        // This bypass is account-level on purpose so a HOST cannot be locked out by deadmin/suspend state.
        return HasAnyFlag(adminData, HostBypassFlags, requireUnsuspended: false);
    }

    private static bool HasAnyFlag(Admin? adminData, AdminFlags requiredFlags, bool requireUnsuspended)
    {
        if (adminData == null || requireUnsuspended && adminData.Suspended)
            return false;

        return (ResolveFlags(adminData) & requiredFlags) != 0;
    }

    private static AdminFlags ResolveFlags(Admin adminData)
    {
        var flags = AdminFlags.None;

        if (adminData.AdminRank != null)
            flags |= AdminFlagsHelper.NamesToFlags(adminData.AdminRank.Flags.Select(flag => flag.Flag));

        foreach (var dbFlag in adminData.Flags)
        {
            var flag = AdminFlagsHelper.NameToFlag(dbFlag.Flag);
            if (dbFlag.Negative)
                flags &= ~flag;
            else
                flags |= flag;
        }

        return flags;
    }
}
