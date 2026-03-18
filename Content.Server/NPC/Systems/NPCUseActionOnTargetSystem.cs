using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.CCVar;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.NPC.Systems;

public sealed class NpcUseActionOnTargetSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly NPCBenchmarkSystem _bench = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private float _attemptIntervalSeconds = 0.10f;
    private float _idleAttemptIntervalSeconds = 0.25f;
    private float _attemptJitterSeconds = 0.02f;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NPCUseActionOnTargetComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NPCUseActionOnTargetComponent, AddedActionEvent>(OnAddedAction);
        SubscribeLocalEvent<NPCUseActionOnTargetComponent, RemovedActionEvent>(OnRemovedAction);
        SubscribeLocalEvent<WorldTargetActionComponent, ValidateNpcTargetEvent>(OnNpcWorldTarget);
        SubscribeLocalEvent<EntityTargetActionComponent, ValidateNpcTargetEvent>(OnNpcEntityTarget);
        Subs.CVar(_cfg, CCVars.NPCActionOnTargetIntervalSeconds, value => _attemptIntervalSeconds = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, CCVars.NPCActionOnTargetIdleIntervalSeconds, value => _idleAttemptIntervalSeconds = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, CCVars.NPCActionOnTargetJitterSeconds, value => _attemptJitterSeconds = MathF.Max(0f, value), true);
    }

    private void OnMapInit(Entity<NPCUseActionOnTargetComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Actions.Count == 0 &&
            ent.Comp.LegacyActionId is { } legacyActionId)
        {
            ent.Comp.Actions.Add(new NpcActionData
            {
                ActionId = legacyActionId,
                TargetKey = ent.Comp.LegacyTargetKey,
                ActionEnt = ent.Comp.LegacyActionEnt,
            });
        }

        foreach (var action in ent.Comp.Actions)
        {
            if (!action.Ref)
                action.ActionEnt = _actions.AddAction(ent, action.ActionId) ?? null;
        }
    }

    private void OnAddedAction(Entity<NPCUseActionOnTargetComponent> entity, ref AddedActionEvent args)
    {
        var protoId = MetaData(args.Action.Owner).EntityPrototype;
        Log.Debug($"NPC: {ToPrettyString(entity)} has added an action {ToPrettyString(args.Action)}.");
        foreach (var action in entity.Comp.Actions)
        {
            // Don't try to add an action, if we already have one or if it's the wrong prototype
            if (!action.Ref || protoId?.ID != action.ActionId.Id)
                continue;

            action.ActionEnt = args.Action;
            action.Ref = false;
        }
    }

    private void OnRemovedAction(Entity<NPCUseActionOnTargetComponent> entity, ref RemovedActionEvent args)
    {
        foreach (var action in entity.Comp.Actions)
        {
            if (action.ActionEnt != args.Action.Owner)
                continue;

            action.ActionEnt = null;
            action.Ref = true;
        }
    }

    private bool TryUseAction(Entity<NPCUseActionOnTargetComponent?> user, NpcActionData action, EntityUid target)
    {
        if (!Resolve(user, ref user.Comp, false))
            return false;

        if (action.ActionEnt is not {} actionEnt)
            return false;

        var ev = new ValidateNpcTargetEvent(target);
        RaiseLocalEvent(actionEnt, ref ev);
        if (ev.Invalid)
            return false;

        return _actions.TryPerformAction(user.Owner, actionEnt, ev.EntTarget, ev.EntityCoordinates, false);
    }

    public override void Update(float frameTime)
    {
        // TODO: TryUseAction should be called by the NPC directly rather than trying to use an action every tick.
        base.Update(frameTime);
        using var benchScope = _bench.Measure("npc.action_on_target.update");

        // Tries to use the attack on the current target.
        var query = EntityQueryEnumerator<NPCUseActionOnTargetComponent, HTNComponent>();
        var npcs = 0;
        var skippedCadence = 0;
        var attempts = 0;
        var success = 0;
        var now = _timing.CurTime;

        while (query.MoveNext(out var uid, out var comp, out var htn))
        {
            npcs++;
            using var entityScope = _bench.Measure("npc.action_on_target.entity");

            if (now < comp.NextAttemptTime)
            {
                skippedCadence++;
                continue;
            }

            var hasTarget = htn.Blackboard.TryGetValue<EntityUid>("Target", out _, EntityManager);
            var interval = hasTarget ? _attemptIntervalSeconds : _idleAttemptIntervalSeconds;
            comp.NextAttemptTime = now + TimeSpan.FromSeconds(interval + _random.NextFloat(0f, _attemptJitterSeconds));

            foreach (var action in comp.Actions)
            {
                if (action.Ref || !htn.Blackboard.TryGetValue<EntityUid>(action.TargetKey, out var target, EntityManager))
                    continue;

                if (action.ActionEnt == null)
                    continue;

                attempts++;

                // Only use one action per tick
                if (TryUseAction((uid, comp), action, target))
                {
                    success++;
                    break;
                }
            }
        }

        _bench.RecordCount("npc.action_on_target.entities", npcs);
        _bench.RecordCount("npc.action_on_target.skipped_cadence", skippedCadence);
        _bench.RecordCount("npc.action_on_target.attempts", attempts);
        _bench.RecordCount("npc.action_on_target.success", success);
    }

    private void OnNpcWorldTarget(Entity<WorldTargetActionComponent> entity, ref ValidateNpcTargetEvent ev)
    {
        ev.EntityCoordinates = Transform(ev.Target).Coordinates;
    }

    private void OnNpcEntityTarget(Entity<EntityTargetActionComponent> entity, ref ValidateNpcTargetEvent ev)
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
