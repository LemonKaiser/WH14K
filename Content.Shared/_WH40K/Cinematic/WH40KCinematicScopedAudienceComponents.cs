using System.Collections.Generic;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Cinematic;

[Serializable, NetSerializable]
public enum WH40KCinematicTriggerAudienceMode : byte
{
    TriggerUser,
    Radius,
    AllPlayersOnMap,
    AllRoundPlayers
}

[RegisterComponent]
public sealed partial class WH40KCinematicTriggerComponent : Component
{
    [DataField("cinematic")]
    public ProtoId<WH40KCinematicPrototype>? CinematicId;

    [DataField("signal")]
    public string? Signal;

    [DataField("signalTargetCinematic")]
    public ProtoId<WH40KCinematicPrototype>? SignalTargetCinematicId;

    [DataField("signalScopeCurrentMapOnly")]
    public bool SignalScopeCurrentMapOnly = true;

    [DataField("audienceMode")]
    public WH40KCinematicTriggerAudienceMode AudienceMode = WH40KCinematicTriggerAudienceMode.TriggerUser;

    [DataField("ghostAudiencePolicy")]
    public WH40KCinematicGhostAudiencePolicy GhostAudiencePolicy = WH40KCinematicGhostAudiencePolicy.MirrorAudience;

    [DataField("priority")]
    public int Priority;

    [DataField("radius")]
    public float Radius = 8f;

    [DataField("teamId")]
    public string? TeamId;

    [DataField("npcFactionId")]
    public ProtoId<NpcFactionPrototype>? NpcFactionId;

    [DataField("jobId")]
    public ProtoId<JobPrototype>? JobId;

    [DataField("aliveOnly")]
    public bool AliveOnly;

    [DataField("nonGhostOnly")]
    public bool NonGhostOnly = true;

    [DataField("oncePerRound")]
    public bool OncePerRound;

    [DataField("oncePerUser")]
    public bool OncePerUser;

    public bool TriggeredThisRound;
    public HashSet<NetUserId> TriggeredUsers { get; } = new();
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KCinematicProtectedComponent : Component
{
    [DataField, AutoNetworkedField]
    public int RunSerial;

    public bool HandsProtectionApplied;
    public bool PreviousHandsCanBeStripped = true;
    public bool GrantedGodmode;
}
