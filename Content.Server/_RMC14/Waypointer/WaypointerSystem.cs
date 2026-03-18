using System.Linq;
using Content.Shared._RMC14.Waypointer;
using Content.Shared._RMC14.Waypointer.Components;
using Content.Shared._RMC14.Waypointer.Events;
using Content.Shared.Actions.Components;
using Content.Shared.Whitelist;
using JetBrains.Annotations;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._RMC14.Waypointer;

public sealed class WaypointerSystem : SharedWaypointerSystem
{
    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActiveWaypointerComponent, ComponentInit>(OnAddition);
        SubscribeLocalEvent<ActiveWaypointerComponent, ComponentRemove>(OnRemoval);

        SubscribeLocalEvent<WaypointerTrackableComponent, ComponentInit>(OnTrackableInit);

        SubscribeLocalEvent<ActiveWaypointerComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<ActiveWaypointerComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<ActiveWaypointerComponent, MapUidChangedEvent>(OnMapChanged);
    }

    protected override void OnWaypointersToggled(Entity<ActionComponent> action, ref WaypointersToggledMessage args)
    {
        base.OnWaypointersToggled(action, ref args);

        if (action.Comp.Container is not { } owner ||
            !TryComp<ActiveWaypointerComponent>(owner, out var waypointer) ||
            waypointer.WaypointerProtoIds == null)
        {
            return;
        }

        var protos = waypointer.WaypointerProtoIds.Keys.ToHashSet();
        if (args.IsActive)
            AddOverrides(owner, protos);
        else
            RemoveOverrides(owner, protos);
    }

    private void OnAddition(Entity<ActiveWaypointerComponent> ent, ref ComponentInit args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.ActionProtoId);
    }

    private void OnRemoval(Entity<ActiveWaypointerComponent> ent, ref ComponentRemove args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnTrackableInit(Entity<WaypointerTrackableComponent> trackable, ref ComponentInit args)
    {
        var waypointersToRefresh = new HashSet<ProtoId<WaypointerPrototype>>();

        foreach (var waypointer in _prototype.EnumeratePrototypes<WaypointerPrototype>())
        {
            if (!_whitelist.CheckBoth(trackable.Owner, blacklist: waypointer.Blacklist, whitelist: waypointer.Whitelist))
                continue;

            foreach (var trackedComponent in waypointer.TrackedComponents.Values)
            {
                if (!HasComp(trackable.Owner, trackedComponent.Component.GetType()))
                    continue;

                waypointersToRefresh.Add(new ProtoId<WaypointerPrototype>(waypointer.ID));
                break;
            }
        }

        if (waypointersToRefresh.Count == 0)
            return;

        var trackXform = Transform(trackable);
        var players = AllEntityQuery<ActiveWaypointerComponent, ActorComponent>();
        while (players.MoveNext(out var playerUid, out var waypointerComp, out var actorComp))
        {
            if (waypointerComp.WaypointerProtoIds == null)
                continue;

            if (Transform(playerUid).MapID != trackXform.MapID)
                continue;

            foreach (var proto in waypointerComp.WaypointerProtoIds.Keys)
            {
                if (!waypointersToRefresh.Contains(proto))
                    continue;

                _pvsOverride.AddSessionOverride(trackable, actorComp.PlayerSession);
                break;
            }
        }
    }

    private void OnPlayerAttached(Entity<ActiveWaypointerComponent> ent, ref PlayerAttachedEvent args)
    {
        if (!ent.Comp.Active || ent.Comp.WaypointerProtoIds == null)
            return;

        AddOverrides(ent, ent.Comp.WaypointerProtoIds.Keys.ToHashSet());
    }

    private void OnPlayerDetached(Entity<ActiveWaypointerComponent> ent, ref PlayerDetachedEvent args)
    {
        if (ent.Comp.WaypointerProtoIds == null)
            return;

        RemoveOverrides(ent, ent.Comp.WaypointerProtoIds.Keys.ToHashSet());
    }

    private void OnMapChanged(Entity<ActiveWaypointerComponent> ent, ref MapUidChangedEvent args)
    {
        RefreshOverrides(ent);
    }

    [PublicAPI]
    public void RefreshOverrides(Entity<ActiveWaypointerComponent> ent)
    {
        if (ent.Comp.WaypointerProtoIds == null)
            return;

        var protos = ent.Comp.WaypointerProtoIds.Keys.ToHashSet();
        RemoveOverrides(ent, protos);
        AddOverrides(ent, protos);
    }

    protected override void AddOverrides(EntityUid player, HashSet<ProtoId<WaypointerPrototype>> waypointers)
    {
        if (!_player.TryGetSessionByEntity(player, out var session))
            return;

        var playerMap = Transform(player).MapID;

        foreach (var waypointerProtoId in waypointers)
        {
            if (!_prototype.Resolve(waypointerProtoId, out var prototype))
                continue;

            var query = _entity.CompRegistryQueryEnumerator(prototype.TrackedComponents);
            while (query.MoveNext(out var target))
            {
                if (!CanBeOverridden(target, prototype))
                    continue;

                if (Transform(target).MapID == playerMap)
                    _pvsOverride.AddSessionOverride(target, session);
            }
        }
    }

    protected override void RemoveOverrides(EntityUid player, HashSet<ProtoId<WaypointerPrototype>> waypointers)
    {
        if (!_player.TryGetSessionByEntity(player, out var session))
            return;

        foreach (var waypointerProtoId in waypointers)
        {
            if (!_prototype.Resolve(waypointerProtoId, out var prototype))
                continue;

            var query = _entity.CompRegistryQueryEnumerator(prototype.TrackedComponents);
            while (query.MoveNext(out var target))
            {
                if (!CanBeOverridden(target, prototype))
                    continue;

                _pvsOverride.RemoveSessionOverride(target, session);
            }
        }
    }

    private bool CanBeOverridden(EntityUid target, WaypointerPrototype prototype)
    {
        var isGrid = HasComp<MapGridComponent>(target);
        var isExplicitTrackable = HasComp<WaypointerTrackableComponent>(target);

        if (!isGrid && !isExplicitTrackable)
            return false;

        return _whitelist.CheckBoth(target, whitelist: prototype.Whitelist, blacklist: prototype.Blacklist);
    }
}
