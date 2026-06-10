using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    /// Enables the one-shot bridge that migrates legacy guest-assigned account data to authenticated SS14 accounts
    /// by exact username match from <c>assigned_user_id</c>.
    /// </summary>
    public static readonly CVarDef<bool> WH40KAuthMigrationEnabled =
        CVarDef.Create("wh40k.auth_migration.enabled", true, CVar.ARCHIVE | CVar.SERVERONLY);
}
