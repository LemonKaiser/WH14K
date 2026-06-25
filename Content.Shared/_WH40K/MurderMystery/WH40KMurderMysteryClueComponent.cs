namespace Content.Shared._WH40K.MurderMystery;

/// <summary>
/// A clue entity spawned on the play grid during a Murder Mystery round.
/// Only civilians (Unassigned role awaiting promotion, or Civilian) may pick
/// it up. Collecting <see cref="WH40KMurderMysteryRuleComponent.CluesToRevolver"/>
/// clues converts the collector into the sheriff and grants the sheriff revolver.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KMurderMysteryClueComponent : Component
{
}
