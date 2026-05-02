using Content.Shared._WH40K.GameMode;

namespace Content.Server._WH40K.Cinematic;

public sealed class WH40KApocalypsePhaseCinematicSystem : EntitySystem
{
    private const string ApocalypseCinematicId = "WH40KCinematicBattlefield40kVolcanoEruption";

    [Dependency] private readonly WH40KCinematicSystem _cinematics = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KBattlePhaseChangedEvent>(OnPhaseChanged);
    }

    private void OnPhaseChanged(WH40KBattlePhaseChangedEvent ev)
    {
        if (ev.NewPhase != WH40KBattlePhase.Apocalypse || ev.PreviousPhase == WH40KBattlePhase.Apocalypse)
            return;

        if (!_cinematics.TryValidateLoadedPrototype(ApocalypseCinematicId, out var validationMessage))
        {
            Log.Warning($"Skipped apocalypse cinematic '{ApocalypseCinematicId}': {validationMessage}");
            return;
        }

        if (!_cinematics.TryQueue(ApocalypseCinematicId, out var queueMessage))
        {
            Log.Warning($"Failed to queue apocalypse cinematic '{ApocalypseCinematicId}': {queueMessage}");
            return;
        }

        Log.Info($"Queued apocalypse cinematic '{ApocalypseCinematicId}' on battle phase change: {queueMessage}");
    }
}
