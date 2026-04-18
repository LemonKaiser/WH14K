using System.Collections.Generic;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.Psyker;

[RegisterComponent]
public sealed partial class WH40KPsykerDisciplineRuntimeComponent : Component
{
    public float MovementMultiplier = 1f;
    public float ThresholdMultiplier = 1f;
    public float DamageTakenMultiplier = 1f;
    public bool BaselineCaptured;
    public SortedDictionary<FixedPoint2, MobState> BaselineThresholds = new();
}

/// <summary>
/// Applies node-driven passive bonuses that need runtime hooks:
/// movement, effective health thresholds and incoming damage scaling.
/// </summary>
public sealed class WH40KPsykerDisciplineRuntimeSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly MobThresholdSystem _mobThresholds = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerDisciplineRuntimeComponent, ComponentStartup>(OnRuntimeStartup);
        SubscribeLocalEvent<WH40KPsykerDisciplineRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
        SubscribeLocalEvent<WH40KPsykerDisciplineRuntimeComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<WH40KPsykerDisciplineRuntimeComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnRuntimeStartup(Entity<WH40KPsykerDisciplineRuntimeComponent> ent, ref ComponentStartup args)
    {
        EnsureBaseline(ent.Owner, ent.Comp);
        ApplyThresholdScaling(ent.Owner, ent.Comp);
    }

    private void OnRuntimeShutdown(Entity<WH40KPsykerDisciplineRuntimeComponent> ent, ref ComponentShutdown args)
    {
        RestoreBaselineThresholds(ent.Owner, ent.Comp);
        _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnRefreshMovementSpeed(Entity<WH40KPsykerDisciplineRuntimeComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.MovementMultiplier is > 0.999f and < 1.001f)
            return;

        args.ModifySpeed(ent.Comp.MovementMultiplier, ent.Comp.MovementMultiplier, MovementSpeedModifierLayer.Status);
    }

    private void OnDamageModify(Entity<WH40KPsykerDisciplineRuntimeComponent> ent, ref DamageModifyEvent args)
    {
        if (ent.Comp.DamageTakenMultiplier is > 0.999f and < 1.001f)
            return;

        var positive = DamageSpecifier.GetPositive(args.Damage);
        if (positive.Empty)
            return;

        var negative = DamageSpecifier.GetNegative(args.Damage);
        args.Damage = positive * ent.Comp.DamageTakenMultiplier + negative;
    }

    public void ApplyRuntimeState(
        EntityUid uid,
        float movementMultiplier,
        float thresholdMultiplier,
        float damageTakenMultiplier)
    {
        var runtime = EnsureComp<WH40KPsykerDisciplineRuntimeComponent>(uid);
        EnsureBaseline(uid, runtime);

        movementMultiplier = MathF.Max(0.1f, movementMultiplier);
        thresholdMultiplier = MathF.Max(0.1f, thresholdMultiplier);
        damageTakenMultiplier = MathF.Max(0.1f, damageTakenMultiplier);

        var refreshMovement = !MathHelper.CloseToPercent(runtime.MovementMultiplier, movementMultiplier);
        var refreshThresholds = !MathHelper.CloseToPercent(runtime.ThresholdMultiplier, thresholdMultiplier);
        runtime.MovementMultiplier = movementMultiplier;
        runtime.ThresholdMultiplier = thresholdMultiplier;
        runtime.DamageTakenMultiplier = damageTakenMultiplier;

        if (refreshThresholds)
            ApplyThresholdScaling(uid, runtime);

        if (refreshMovement)
            _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }

    public void ResetRuntimeState(EntityUid uid)
    {
        if (!TryComp<WH40KPsykerDisciplineRuntimeComponent>(uid, out _))
            return;

        RemComp<WH40KPsykerDisciplineRuntimeComponent>(uid);
    }

    private void EnsureBaseline(EntityUid uid, WH40KPsykerDisciplineRuntimeComponent runtime)
    {
        if (runtime.BaselineCaptured)
            return;

        if (TryComp<MobThresholdsComponent>(uid, out var thresholds))
            runtime.BaselineThresholds = new SortedDictionary<FixedPoint2, MobState>(thresholds.Thresholds);

        runtime.BaselineCaptured = true;
    }

    private void ApplyThresholdScaling(EntityUid uid, WH40KPsykerDisciplineRuntimeComponent runtime)
    {
        if (runtime.BaselineThresholds.Count == 0 || !TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        var scaled = new SortedDictionary<FixedPoint2, MobState>();
        foreach (var (threshold, state) in runtime.BaselineThresholds)
        {
            var value = threshold;
            if (state != MobState.Alive && threshold > 0)
                value = threshold * runtime.ThresholdMultiplier;

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
    }

    private void RestoreBaselineThresholds(EntityUid uid, WH40KPsykerDisciplineRuntimeComponent runtime)
    {
        if (runtime.BaselineThresholds.Count == 0 || !TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        foreach (var (threshold, state) in runtime.BaselineThresholds)
        {
            _mobThresholds.SetMobStateThreshold(uid, threshold, state, thresholds);
        }

        _mobThresholds.VerifyThresholds(uid, thresholds);
    }
}
