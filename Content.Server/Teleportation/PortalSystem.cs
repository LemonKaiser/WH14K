using Content.Shared.Administration.Logs;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Robust.Shared.Map;

namespace Content.Server.Teleportation;

public sealed partial class PortalSystem : SharedPortalSystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private PathfindingSystem _pathfinding = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PortalComponent, EntityLinkedEvent>(OnLinked);
        SubscribeLocalEvent<PortalComponent, EntityUnlinkedEvent>(OnUnlinked);
    }

    private void OnLinked(Entity<PortalComponent> ent, ref EntityLinkedEvent args)
    {
        if (!ent.Comp.NavPortal)
            return;

        // Only create one navigation edge per linked pair.
        if (ent.Owner.Id > args.Other.Id)
            return;

        var xformA = Transform(ent);
        var xformB = Transform(args.Other);

        if (_pathfinding.TryCreatePortal(xformA.Coordinates, xformB.Coordinates, out var handle))
            ent.Comp.NavPortalHandles[args.Other] = handle;
    }

    private void OnUnlinked(Entity<PortalComponent> ent, ref EntityUnlinkedEvent args)
    {
        if (!ent.Comp.NavPortalHandles.TryGetValue(args.Other, out var handle))
            return;

        _pathfinding.RemovePortal(handle);
        ent.Comp.NavPortalHandles.Remove(args.Other);
    }

    // TODO Move to shared
    protected override void LogTeleport(EntityUid portal, EntityUid subject, EntityCoordinates source,
        EntityCoordinates target)
    {
        if (HasComp<MindContainerComponent>(subject) && !HasComp<GhostComponent>(subject))
            _adminLogger.Add(LogType.Teleport, LogImpact.Low, $"{ToPrettyString(subject):player} teleported via {ToPrettyString(portal)} from {source} to {target}");
    }
}
