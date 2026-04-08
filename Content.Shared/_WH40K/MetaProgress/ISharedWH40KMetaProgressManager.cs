using Robust.Shared.Player;

namespace Content.Shared._WH40K.MetaProgress;

public interface ISharedWH40KMetaProgressManager
{
    bool TryGetMetaLevel(ICommonSession session, out int level);
    bool TryHasCompletedAchievement(ICommonSession session, string achievementId, out bool completed);
}
