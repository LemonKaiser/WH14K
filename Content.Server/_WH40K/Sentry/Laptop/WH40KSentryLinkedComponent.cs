using System;
using System.Collections.Generic;
using Robust.Shared.GameStates;

namespace Content.Server._WH40K.Sentry.Laptop;

[RegisterComponent]
public sealed partial class WH40KSentryLinkedComponent : Component
{
    [ViewVariables]
    public EntityUid? LinkedLaptop;

    [ViewVariables]
    public HashSet<string> BaselineFactions = new(StringComparer.OrdinalIgnoreCase);
}
