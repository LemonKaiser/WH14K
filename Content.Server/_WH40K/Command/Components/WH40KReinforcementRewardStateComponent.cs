using Robust.Shared.Network;

namespace Content.Server._WH40K.Command.Components;

[RegisterComponent]
public sealed partial class WH40KReinforcementRewardStateComponent : Component
{
    public bool WasClaimedByPlayer;
    public NetUserId? ClaimedUserId;
}
