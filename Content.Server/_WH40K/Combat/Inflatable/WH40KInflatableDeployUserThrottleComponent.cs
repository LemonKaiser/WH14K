using System;
using Robust.Shared.ViewVariables;

namespace Content.Server._WH40K.Combat.Inflatable;

[RegisterComponent]
public sealed partial class WH40KInflatableDeployUserThrottleComponent : Component
{
    [ViewVariables]
    public TimeSpan NextAllowedDeployAt = TimeSpan.Zero;

    [ViewVariables]
    public Queue<TimeSpan> RecentDeploys = new();
}
