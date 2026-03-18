using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.GameMode;

public sealed class WH40KBattlePhaseChangedEvent : EntityEventArgs
{
    public EntityUid RuleUid { get; }
    public WH40KBattlePhase PreviousPhase { get; }
    public WH40KBattlePhase NewPhase { get; }

    public WH40KBattlePhaseChangedEvent(EntityUid ruleUid, WH40KBattlePhase previousPhase, WH40KBattlePhase newPhase)
    {
        RuleUid = ruleUid;
        PreviousPhase = previousPhase;
        NewPhase = newPhase;
    }
}
