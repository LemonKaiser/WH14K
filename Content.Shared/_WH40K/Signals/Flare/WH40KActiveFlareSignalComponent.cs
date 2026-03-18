using System.Collections.Generic;
using Robust.Shared.Map;
using Robust.Shared.ViewVariables;

namespace Content.Shared._WH40K.Signals.Flare;

[RegisterComponent]
public sealed partial class WH40KActiveFlareSignalComponent : Component
{
    [ViewVariables]
    public EntityUid? User;

    [ViewVariables]
    public string TeamId = string.Empty;

    [ViewVariables]
    public int SignalId;

    [ViewVariables]
    public Queue<MapCoordinates> LastCoordinates = new();
}
