using System;
using System.Collections.Generic;
using Robust.Shared.ViewVariables;

namespace Content.Server._WH40K.Command.Whistle;

[RegisterComponent]
public sealed partial class WH40KTacticalWhistleUserThrottleComponent : Component
{
    [ViewVariables]
    public TimeSpan NextAllowedSignalAt = TimeSpan.Zero;

    [ViewVariables]
    public Queue<TimeSpan> RecentSignals = new();
}
