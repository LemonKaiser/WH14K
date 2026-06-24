using Content.Server.GameTicking;
using Content.Server.GameTicking.Presets;

namespace Content.Server._WH40K.GameTicking;

public sealed partial class WH40KPresetVoteAvailabilitySystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;

    public bool AreMiniGamesBlocked => RemainingMiniGameBlockedRounds > 0;
    public int RemainingMiniGameBlockedRounds { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.Old != GameRunLevel.InRound || ev.New != GameRunLevel.PostRound)
            return;

        if (_gameTicker.CurrentPreset is not GamePresetPrototype preset)
            return;

        if (preset.IsMiniGame)
        {
            RemainingMiniGameBlockedRounds = 3;
            return;
        }

        if (RemainingMiniGameBlockedRounds > 0)
            RemainingMiniGameBlockedRounds--;
    }
}
