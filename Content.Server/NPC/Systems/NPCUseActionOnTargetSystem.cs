using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Robust.Shared.Map;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCUseActionOnTargetSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NPCUseActionOnTargetComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NPCUseActionOnTargetComponent, AddedActionEvent>(OnAddedAction);
        SubscribeLocalEvent<NPCUseActionOnTargetComponent, RemovedActionEvent>(OnRemovedAction);
        SubscribeLocalEvent<WorldTargetActionComponent, ValidateNpcTargetEvent>(OnNpcWorldTarget);
        SubscribeLocalEvent<EntityTargetActionComponent, ValidateNpcTargetEvent>(OnNpcEntityTarget);
    }

    private void OnMapInit(Entity<NPCUseActionOnTargetComponent> ent, ref MapInitEvent args)
    {
        foreach (var action in ent.Comp.Actions)
        {
            if (action.Ref)
                continue;

            action.ActionEnt = _actions.AddAction(ent, action.ActionId);
        }
    }

    private void OnAddedAction(Entity<NPCUseActionOnTargetComponent> ent, ref AddedActionEvent args)
    {
        var protoId = MetaData(args.Action.Owner).EntityPrototype?.ID;

        foreach (var action in ent.Comp.Actions)
        {
            if (!action.Ref || protoId != action.ActionId.Id)
                continue;

            action.ActionEnt = args.Action.Owner;
            action.Ref = false;
        }
    }

    private void OnRemovedAction(Entity<NPCUseActionOnTargetComponent> ent, ref RemovedActionEvent args)
    {
        foreach (var action in ent.Comp.Actions)
        {
            if (action.ActionEnt != args.Action.Owner)
                continue;

            action.ActionEnt = null;
            action.Ref = true;
        }
    }

    private bool TryUseAction(Entity<NPCUseActionOnTargetComponent?> user, NpcActionData actionData, EntityUid target)
    {
        if (!Resolve(user, ref user.Comp, false) || actionData.ActionEnt is not { } actionEnt)
            return false;

        var ev = new ValidateNpcTargetEvent(target);
        RaiseLocalEvent(actionEnt, ref ev);

        if (ev.Invalid)
            return false;

        return _actions.TryPerformAction(user.Owner, actionEnt, ev.EntTarget, ev.EntityCoordinates, predicted: false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NPCUseActionOnTargetComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var comp, out var htn))
        {
            foreach (var action in comp.Actions)
            {
                if (action.Ref ||
                    !htn.Blackboard.TryGetValue<EntityUid>(action.TargetKey, out var target, EntityManager))
                {
                    continue;
                }

                // At most one action per NPC per tick.
                if (TryUseAction((uid, comp), action, target))
                    break;
            }
        }
    }

    private void OnNpcWorldTarget(Entity<WorldTargetActionComponent> ent, ref ValidateNpcTargetEvent ev)
    {
        ev.EntityCoordinates = Transform(ev.Target).Coordinates;
    }

    private void OnNpcEntityTarget(Entity<EntityTargetActionComponent> ent, ref ValidateNpcTargetEvent ev)
    {
        ev.EntTarget = ev.Target;
    }
}

[ByRefEvent]
public struct ValidateNpcTargetEvent(EntityUid target)
{
    public readonly EntityUid Target = target;

    public bool Invalid;
    public EntityUid? EntTarget;
    public EntityCoordinates? EntityCoordinates;
}
