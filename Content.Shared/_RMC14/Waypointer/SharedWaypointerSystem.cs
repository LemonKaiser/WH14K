using System.Linq;
using Content.Shared._RMC14.Waypointer.Components;
using Content.Shared._RMC14.Waypointer.Events;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Clothing;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Waypointer;

public abstract partial class SharedWaypointerSystem : EntitySystem
{
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] protected  SharedActionsSystem _actions = default!;
    [Dependency] private  SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<InnateWaypointerComponent, MapInitEvent>(OnMapInit);

        SubscribeLocalEvent<ActiveWaypointerComponent, ActionManageWaypointersEvent>(OnActionPressed);
        SubscribeLocalEvent<ActionComponent, WaypointersToggledMessage>(OnWaypointersToggled);
        SubscribeLocalEvent<ActionComponent, WaypointerStatusChangedMessage>(OnWaypointerStatusChanged);

        SubscribeLocalEvent<ClothingShowWaypointerComponent, ClothingGotEquippedEvent>(OnEquip);
        SubscribeLocalEvent<ClothingShowWaypointerComponent, ClothingGotUnequippedEvent>(OnUnequip);

        SubscribeLocalEvent<InnateWaypointerComponent, WaypointerChangedEvent>(OnWaypointerChanged);
        SubscribeLocalEvent<ClothingShowWaypointerComponent, InventoryRelayedEvent<WaypointerChangedEvent>>(OnWaypointerChanged);
    }

    private void OnMapInit(Entity<InnateWaypointerComponent> ent, ref MapInitEvent args)
    {
        SetWaypointerComponent(ent);
    }

    private void OnActionPressed(Entity<ActiveWaypointerComponent> ent, ref ActionManageWaypointersEvent args)
    {
        if (args.Handled)
            return;

        _ui.OpenUi(args.Action.Owner, WaypointerUiKey.Key, ent.Owner);
        args.Handled = true;
    }

    protected virtual void OnWaypointersToggled(Entity<ActionComponent> action, ref WaypointersToggledMessage args)
    {
        if (!TryComp<ActiveWaypointerComponent>(action.Comp.Container, out var waypointer) ||
            waypointer.WaypointerProtoIds == null)
        {
            return;
        }

        waypointer.Active = args.IsActive;
        _actions.SetToggled(action.AsNullable(), args.IsActive);
        Dirty(action.Comp.Container.Value, waypointer);
    }

    private void OnWaypointerStatusChanged(Entity<ActionComponent> action, ref WaypointerStatusChangedMessage args)
    {
        if (!TryComp<ActiveWaypointerComponent>(action.Comp.Container, out var waypointer) ||
            waypointer.WaypointerProtoIds == null)
        {
            return;
        }

        if (!waypointer.WaypointerProtoIds.ContainsKey(args.ToggledWaypointerProtoId))
            return;

        waypointer.WaypointerProtoIds[args.ToggledWaypointerProtoId] =
            !waypointer.WaypointerProtoIds[args.ToggledWaypointerProtoId];
        Dirty(action.Comp.Container.Value, waypointer);
    }

    private void OnEquip(Entity<ClothingShowWaypointerComponent> ent, ref ClothingGotEquippedEvent args)
    {
        SetWaypointerComponent(args.Wearer);
    }

    private void OnUnequip(Entity<ClothingShowWaypointerComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        SetWaypointerComponent(args.Wearer);
    }

    private void OnWaypointerChanged(Entity<InnateWaypointerComponent> ent, ref WaypointerChangedEvent args)
    {
        args.Waypointers.UnionWith(ent.Comp.WaypointerProtoIds);
    }

    private void OnWaypointerChanged(Entity<ClothingShowWaypointerComponent> ent, ref InventoryRelayedEvent<WaypointerChangedEvent> args)
    {
        args.Args.Waypointers.UnionWith(ent.Comp.WaypointerProtoIds);
    }

    private void SetWaypointerComponent(EntityUid player)
    {
        if (_timing.ApplyingState)
            return;

        var comp = EnsureComp<ActiveWaypointerComponent>(player);

        HashSet<ProtoId<WaypointerPrototype>>? previous = null;
        HashSet<ProtoId<WaypointerPrototype>>? toRemove = null;
        if (comp.WaypointerProtoIds != null)
        {
            previous = comp.WaypointerProtoIds.Keys.ToHashSet();
            toRemove = comp.WaypointerProtoIds.Keys.ToHashSet();
        }

        var ev = new WaypointerChangedEvent();
        RaiseLocalEvent(player, ref ev);

        if (toRemove != null)
        {
            toRemove.ExceptWith(ev.Waypointers);
            RemoveOverrides(player, toRemove);
        }

        if (ev.Waypointers.Count == 0)
        {
            RemComp<ActiveWaypointerComponent>(player);
            return;
        }

        var newDict = ev.Waypointers.ToDictionary(key => key, _ => true);
        if (comp.WaypointerProtoIds != null)
        {
            foreach (var pair in comp.WaypointerProtoIds.Where(pair => newDict.ContainsKey(pair.Key)))
            {
                newDict[pair.Key] = pair.Value;
            }
        }

        comp.WaypointerProtoIds = newDict;

        if (previous != null)
            ev.Waypointers.ExceptWith(previous);

        AddOverrides(player, ev.Waypointers);
        Dirty(player, comp);
    }

    protected virtual void AddOverrides(EntityUid player, HashSet<ProtoId<WaypointerPrototype>> waypointers)
    {
    }

    protected virtual void RemoveOverrides(EntityUid player, HashSet<ProtoId<WaypointerPrototype>> waypointers)
    {
    }
}

[Serializable, NetSerializable]
public enum WaypointerUiKey : byte
{
    Key,
}
