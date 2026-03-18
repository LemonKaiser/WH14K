using System;
using Robust.Shared.ViewVariables;

namespace Content.Server._WH40K.Command.Comms.Megaphone;

[RegisterComponent]
public sealed partial class WH40KMegaphoneUserThrottleComponent : Component
{
    [ViewVariables]
    public TimeSpan NextAllowedBroadcastAt = TimeSpan.Zero;

    [ViewVariables]
    public Queue<TimeSpan> RecentBroadcasts = new();
}
