using System;
using System.Collections.Generic;

namespace Content.Shared._WH40K.Fulton;

[RegisterComponent]
public sealed partial class WH40KFultonUserThrottleComponent : Component
{
    [ViewVariables]
    public TimeSpan NextAllowedUseAt = TimeSpan.Zero;

    [ViewVariables]
    public Queue<TimeSpan> RecentUses = new();
}
