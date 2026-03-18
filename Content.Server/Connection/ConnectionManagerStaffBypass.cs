using System.Linq;
using Content.Server.Database;
using Content.Shared.Administration;

namespace Content.Server.Connection;

public static class ConnectionManagerStaffBypass
{
    private const AdminFlags DiscordAuthBypassFlags = AdminFlags.Moderator | AdminFlags.Admin | AdminFlags.Host;

    public static bool HasDiscordAuthBypass(Admin? adminData)
    {
        if (adminData == null || adminData.Suspended)
            return false;

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

        // This bypass is account-level on purpose so staff can still recover access even if they are currently deadminned.
        return (flags & DiscordAuthBypassFlags) != 0;
    }
}
