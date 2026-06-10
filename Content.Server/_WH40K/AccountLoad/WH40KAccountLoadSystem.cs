using Content.Server.Database;
using Content.Shared._WH40K.AccountLoad;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.AccountLoad;

public sealed partial class WH40KAccountLoadSystem : EntitySystem
{
    [Dependency] private UserDbDataManager _userDb = default!;
    [Dependency] private IPlayerManager _players = default!;

    public override void Initialize()
    {
        base.Initialize();
        _userDb.AddOnLoadProgressChanged(OnLoadProgressChanged);
    }

    private void OnLoadProgressChanged(NetUserId userId, UserDbLoadProgress progress)
    {
        if (!_players.TryGetSessionById(userId, out var session))
            return;

        if (session.Status == SessionStatus.Disconnected)
            return;

        RaiseNetworkEvent(
            new WH40KAccountLoadStatusEvent(
                progress.TitleLocKey,
                progress.StageLocKey,
                progress.DetailLocKey,
                progress.Progress,
                progress.CompletedSteps,
                progress.TotalSteps),
            session);
    }
}
