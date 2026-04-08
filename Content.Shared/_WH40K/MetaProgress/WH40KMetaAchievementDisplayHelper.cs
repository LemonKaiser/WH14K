namespace Content.Shared._WH40K.MetaProgress;

public static class WH40KMetaAchievementDisplayHelper
{
    public const string HiddenPlaceholder = "???";

    public static bool ShouldMaskSecretDetails(WH40KMetaAchievementSnapshotEntry entry)
    {
        return entry.Hidden && !entry.Completed;
    }
}
