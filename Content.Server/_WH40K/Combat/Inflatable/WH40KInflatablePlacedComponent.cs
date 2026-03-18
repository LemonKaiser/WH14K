using System;
using Robust.Shared.ViewVariables;

namespace Content.Server._WH40K.Combat.Inflatable;

[RegisterComponent]
public sealed partial class WH40KInflatablePlacedComponent : Component
{
    [ViewVariables]
    public EntityUid PlacedBy = EntityUid.Invalid;

    [ViewVariables]
    public TimeSpan PlacedAt = TimeSpan.Zero;
}
