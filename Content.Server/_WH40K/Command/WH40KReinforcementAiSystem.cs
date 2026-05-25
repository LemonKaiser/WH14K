using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server._WH40K.Command.Components;
using Content.Shared.NPC;
using Content.Shared._WH40K.Command;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Command;

public sealed class WH40KReinforcementAiSystem : EntitySystem
{
    private static readonly ProtoId<HTNCompoundPrototype> ReinforcementRootTask = "SimpleHumanoidHostileCompound";

    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(NPCSteeringSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WH40KReinforcementAiRuntimeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var runtime, out var xform))
        {
            UpdateLeash((uid, runtime, xform));
        }
    }

    public void Enable(EntityUid uid, EntityCoordinates homeCoordinates)
    {
        var runtime = EnsureComp<WH40KReinforcementAiRuntimeComponent>(uid);
        runtime.HomeCoordinates = homeCoordinates;
        runtime.ReturningHome = false;

        EnsureComp<WH40KReinforcementAiStatusIconComponent>(uid);

        var htn = EnsureComp<HTNComponent>(uid);
        htn.RootTask = new HTNCompoundTask { Task = ReinforcementRootTask };
        htn.Blackboard.SetValue(NPCBlackboard.Owner, uid);

        _npc.SetBlackboard(uid, "IdleRange", runtime.IdleRange, htn);
        _npc.SetBlackboard(uid, "VisionRadius", runtime.VisionRadius, htn);
        _npc.SetBlackboard(uid, "AggroVisionRadius", runtime.AggroVisionRadius, htn);
        _npc.SetBlackboard(uid, "RangedRange", runtime.RangedRange, htn);
        _npc.SetBlackboard(uid, "MinimumIdleTime", 1.5f, htn);
        _npc.SetBlackboard(uid, "MaximumIdleTime", 4.5f, htn);

        if (TryComp<NPCSteeringComponent>(uid, out var steering))
            _steering.Unregister(uid, steering);

        _npc.WakeNPC(uid, htn);
        _htn.Replan(htn);
    }

    public void Disable(EntityUid uid)
    {
        if (TryComp<NPCSteeringComponent>(uid, out var steering))
            _steering.Unregister(uid, steering);

        if (TryComp<HTNComponent>(uid, out var htn))
        {
            _htn.SetHTNEnabled((uid, htn), false);
            RemComp<HTNComponent>(uid);
        }

        RemComp<ActiveNPCComponent>(uid);
        RemComp<WH40KReinforcementAiRuntimeComponent>(uid);
        RemComp<WH40KReinforcementAiStatusIconComponent>(uid);
    }

    private void UpdateLeash(Entity<WH40KReinforcementAiRuntimeComponent, TransformComponent> ent)
    {
        if (ent.Comp1.HomeCoordinates == EntityCoordinates.Invalid || ent.Comp2.MapID == MapId.Nullspace)
            return;

        var currentMap = _transform.ToMapCoordinates(ent.Comp2.Coordinates, logError: false);
        var homeMap = _transform.ToMapCoordinates(ent.Comp1.HomeCoordinates, logError: false);
        if (currentMap.MapId == MapId.Nullspace || homeMap.MapId == MapId.Nullspace)
            return;

        var shouldReturn = currentMap.MapId != homeMap.MapId;
        var distanceSquared = shouldReturn
            ? float.MaxValue
            : (currentMap.Position - homeMap.Position).LengthSquared();

        if (ent.Comp1.ReturningHome)
        {
            if (!shouldReturn && distanceSquared <= ent.Comp1.ReturnRange * ent.Comp1.ReturnRange)
            {
                FinishReturnHome(ent);
                return;
            }

            RefreshReturnHome(ent);
            return;
        }

        if (distanceSquared > ent.Comp1.LeashRange * ent.Comp1.LeashRange)
            StartReturnHome(ent);
    }

    private void StartReturnHome(Entity<WH40KReinforcementAiRuntimeComponent> ent)
    {
        if (ent.Comp.ReturningHome)
            return;

        ent.Comp.ReturningHome = true;

        if (TryComp<HTNComponent>(ent.Owner, out var htn))
            _htn.SetHTNEnabled((ent.Owner, htn), false);

        EnsureComp<ActiveNPCComponent>(ent.Owner);
        var steering = _steering.Register(ent.Owner, ent.Comp.HomeCoordinates, CompOrNull<NPCSteeringComponent>(ent.Owner));
        steering.Range = ent.Comp.ReturnRange;
        steering.ArriveOnLineOfSight = false;
        steering.DirectMove = false;
    }

    private void RefreshReturnHome(Entity<WH40KReinforcementAiRuntimeComponent> ent)
    {
        EnsureComp<ActiveNPCComponent>(ent.Owner);
        var steering = TryComp<NPCSteeringComponent>(ent.Owner, out var existing)
            ? existing
            : _steering.Register(ent.Owner, ent.Comp.HomeCoordinates);

        if (!steering.Coordinates.Equals(ent.Comp.HomeCoordinates) || steering.Status == SteeringStatus.NoPath)
            steering = _steering.Register(ent.Owner, ent.Comp.HomeCoordinates, steering);

        steering.Range = ent.Comp.ReturnRange;
        steering.ArriveOnLineOfSight = false;
        steering.DirectMove = false;
    }

    private void FinishReturnHome(Entity<WH40KReinforcementAiRuntimeComponent> ent)
    {
        ent.Comp.ReturningHome = false;

        if (TryComp<NPCSteeringComponent>(ent.Owner, out var steering))
            _steering.Unregister(ent.Owner, steering);

        if (!TryComp<HTNComponent>(ent.Owner, out var htn))
            return;

        _npc.WakeNPC(ent.Owner, htn);
        _htn.SetHTNEnabled((ent.Owner, htn), true, 0.2f);
    }
}
