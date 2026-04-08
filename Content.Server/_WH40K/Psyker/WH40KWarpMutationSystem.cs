using System;
using System.Collections.Generic;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;

namespace Content.Server._WH40K.Psyker;

[RegisterComponent]
public sealed partial class WH40KWarpMutationComponent : Component
{
    public float Severity;
    public float ThresholdMultiplier = 1f;
    public float MovementMultiplier = 1f;
    public bool BaselineCaptured;
    public SortedDictionary<FixedPoint2, MobState> BaselineThresholds = new();
}

/// <summary>
/// Applies irreversible warp-flesh degradation for the 900-999 instability band.
/// The mutation persists until the entity is deleted or the round is reset.
/// </summary>
public sealed class WH40KWarpMutationSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly MobThresholdSystem _mobThresholds = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KWarpMutationComponent, ComponentStartup>(OnMutationStartup);
        SubscribeLocalEvent<WH40KWarpMutationComponent, ComponentShutdown>(OnMutationShutdown);
        SubscribeLocalEvent<WH40KWarpMutationComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
    }

    private void OnMutationStartup(Entity<WH40KWarpMutationComponent> ent, ref ComponentStartup args)
    {
        ApplyMutation(ent.Owner, ent.Comp);
    }

    private void OnMutationShutdown(Entity<WH40KWarpMutationComponent> ent, ref ComponentShutdown args)
    {
        RestoreBaselineThresholds(ent.Owner, ent.Comp);
        _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnRefreshMovementSpeed(Entity<WH40KWarpMutationComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.MovementMultiplier >= 0.999f)
            return;

        args.ModifySpeed(ent.Comp.MovementMultiplier, ent.Comp.MovementMultiplier, MovementSpeedModifierLayer.Status);
    }

    public void ApplyMutation(EntityUid uid, WH40KWarpMutationComponent mutation)
    {
        if (mutation.Severity <= 0f)
            return;

        EnsureBaseline(uid, mutation);

        if (mutation.BaselineThresholds.Count == 0 || !TryComp<MobThresholdsComponent>(uid, out var thresholds))
        {
            _movementSpeed.RefreshMovementSpeedModifiers(uid);
            return;
        }

        var scaled = new SortedDictionary<FixedPoint2, MobState>();
        foreach (var (threshold, state) in mutation.BaselineThresholds)
        {
            var value = threshold;
            if (state != MobState.Alive && threshold > 0)
                value = threshold * mutation.ThresholdMultiplier;

            while (scaled.ContainsKey(value))
            {
                value += FixedPoint2.New(0.01f);
            }

            scaled[value] = state;
        }

        foreach (var (threshold, state) in scaled)
        {
            _mobThresholds.SetMobStateThreshold(uid, threshold, state, thresholds);
        }

        _mobThresholds.VerifyThresholds(uid, thresholds);
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }

    private void EnsureBaseline(EntityUid uid, WH40KWarpMutationComponent mutation)
    {
        if (mutation.BaselineCaptured)
            return;

        if (TryComp<MobThresholdsComponent>(uid, out var thresholds))
            mutation.BaselineThresholds = new SortedDictionary<FixedPoint2, MobState>(thresholds.Thresholds);

        mutation.BaselineCaptured = true;
    }

    private void RestoreBaselineThresholds(EntityUid uid, WH40KWarpMutationComponent mutation)
    {
        if (mutation.BaselineThresholds.Count == 0 || !TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        foreach (var (threshold, state) in mutation.BaselineThresholds)
        {
            _mobThresholds.SetMobStateThreshold(uid, threshold, state, thresholds);
        }

        _mobThresholds.VerifyThresholds(uid, thresholds);
    }
}
