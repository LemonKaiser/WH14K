using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Command.Pinpointer;

[Serializable, NetSerializable]
public enum WH40KMissionPinpointerPreset : byte
{
    Relay = 0,
    Cargo = 1,
    Banner = 2
}

[RegisterComponent]
public sealed partial class WH40KMissionPinpointerComponent : Component
{
    [DataField]
    public WH40KMissionPinpointerPreset Preset = WH40KMissionPinpointerPreset.Relay;

    [DataField]
    public bool RequireTeam = true;

    [DataField]
    public List<string> AllowedTeamIds = new();

    [DataField]
    public bool TrackGlobalMissionFallback = true;

    [DataField]
    public TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    [DataField]
    public string NoMissionTargetName = "No active mission target";

    [DataField]
    public string NoTeamTargetName = "Team identity is not resolved";

    [DataField]
    public string UnauthorizedTargetName = "Restricted to another faction";

    [ViewVariables]
    public TimeSpan NextRefreshAt = TimeSpan.Zero;
}
