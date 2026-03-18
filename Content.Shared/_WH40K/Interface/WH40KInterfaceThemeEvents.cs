using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Interface;

[Serializable, NetSerializable]
public sealed class WH40KTeamThemeAssignedEvent : EntityEventArgs
{
    public string? TeamId { get; }

    public WH40KTeamThemeAssignedEvent(string? teamId)
    {
        TeamId = teamId;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KTeamColorDefinition
{
    public string TeamId { get; }
    public string ColorHex { get; }

    public WH40KTeamColorDefinition(string teamId, string colorHex)
    {
        TeamId = teamId;
        ColorHex = colorHex;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KTeamColorsAssignedEvent : EntityEventArgs
{
    public List<WH40KTeamColorDefinition> TeamColors { get; }

    public WH40KTeamColorsAssignedEvent(List<WH40KTeamColorDefinition> teamColors)
    {
        TeamColors = teamColors;
    }
}
