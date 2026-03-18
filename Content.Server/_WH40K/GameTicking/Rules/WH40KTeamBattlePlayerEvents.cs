using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Server._WH40K.GameTicking.Rules;

/// <summary>
/// Raised when a player heals an allied player in WH40K team battle context.
/// Used by external systems (meta progression, analytics) as lightweight producer input.
/// </summary>
public sealed class WH40KTeamBattleHealingDoneEvent : EntityEventArgs
{
    public NetUserId SourceUserId { get; }
    public NetUserId TargetUserId { get; }
    public string TeamId { get; }
    public int HealedAmount { get; }

    public WH40KTeamBattleHealingDoneEvent(NetUserId sourceUserId, NetUserId targetUserId, string teamId, int healedAmount)
    {
        SourceUserId = sourceUserId;
        TargetUserId = targetUserId;
        TeamId = teamId;
        HealedAmount = healedAmount;
    }
}
