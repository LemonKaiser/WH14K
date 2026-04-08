using System;
using Robust.Shared.GameObjects;
using Robust.Shared.ViewVariables;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Server-only per-user cooldown state for skrizhal UI interaction anti-spam.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KSkrizhalUiUserThrottleComponent : Component
{
    [ViewVariables]
    public TimeSpan NextAllowedUiInteractionAt;
}
