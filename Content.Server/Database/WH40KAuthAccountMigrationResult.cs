using Robust.Shared.Network;

namespace Content.Server.Database;

public enum WH40KAuthAccountMigrationOutcome : byte
{
    None,
    CleanedAssignment,
    Migrated
}

public sealed record WH40KAuthAccountMigrationResult(
    WH40KAuthAccountMigrationOutcome Outcome,
    NetUserId? LegacyUserId = null)
{
    public bool HasAssignment => LegacyUserId != null;
    public bool Migrated => Outcome == WH40KAuthAccountMigrationOutcome.Migrated;
}
