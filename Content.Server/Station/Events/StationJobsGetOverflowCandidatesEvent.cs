using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.Station.Events;

[ByRefEvent]
public readonly record struct StationJobsGetOverflowCandidatesEvent(
    NetUserId Player,
    EntityUid Station,
    List<ProtoId<JobPrototype>> Jobs);