using System;
using System.Collections.Generic;
using Robust.Shared.ViewVariables;

namespace Content.Server._WH40K.Signals.Flare;

[RegisterComponent]
public sealed partial class WH40KFlareSignalUserThrottleComponent : Component
{
    [ViewVariables]
    public TimeSpan NextAllowedSignalAt = TimeSpan.Zero;

    [ViewVariables]
    public Queue<TimeSpan> RecentSignals = new();
}
