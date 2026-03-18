using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Action unlock entry bound to a minimum progression level.
/// </summary>
[DataDefinition, Serializable]
public sealed partial class WH40KLevelLockedAction
{
    [DataField("actionPrototype", required: true)]
    public string ActionPrototype = string.Empty;

    [DataField("requiredLevel")]
    public int RequiredLevel = 1;
}
