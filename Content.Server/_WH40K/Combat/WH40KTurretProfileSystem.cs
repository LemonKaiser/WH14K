using System;
using Content.Server.NPC.HTN;
using Content.Shared._WH40K.Combat;

namespace Content.Server._WH40K.Combat;

/// <summary>
/// Applies WH40K turret profile range tuning into HTN blackboard keys.
/// </summary>
public sealed class WH40KTurretProfileSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KTurretProfileComponent, ComponentStartup>(OnProfileChanged);
        SubscribeLocalEvent<WH40KTurretProfileComponent, MapInitEvent>(OnProfileChanged);
    }

    private void OnProfileChanged(Entity<WH40KTurretProfileComponent> ent, ref ComponentStartup args)
    {
        ApplyProfile(ent);
    }

    private void OnProfileChanged(Entity<WH40KTurretProfileComponent> ent, ref MapInitEvent args)
    {
        ApplyProfile(ent);
    }

    private void ApplyProfile(Entity<WH40KTurretProfileComponent> ent)
    {
        if (!TryComp<HTNComponent>(ent, out var htn))
            return;

        var detectionRange = MathF.Max(ent.Comp.DetectionRange, 0.1f);
        var aggroRange = MathF.Max(ent.Comp.AggroDetectionRange ?? detectionRange, 0.1f);
        var fireRange = MathF.Max(ent.Comp.FireRange ?? detectionRange, 0.1f);

        htn.Blackboard.SetValue("VisionRadius", detectionRange);
        htn.Blackboard.SetValue("AggroVisionRadius", aggroRange);
        htn.Blackboard.SetValue("RangedRange", fireRange);
    }
}
