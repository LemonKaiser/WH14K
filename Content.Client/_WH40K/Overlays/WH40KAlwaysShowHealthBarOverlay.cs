using System;
using System.Numerics;
using Content.Client.UserInterface.Systems;
using Content.Shared._WH40K.Overlays;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using static Robust.Shared.Maths.Color;

namespace Content.Client._WH40K.Overlays;

/// <summary>
/// Always-on health bar overlay for entities marked with WH40KAlwaysShowHealthBarComponent.
/// </summary>
public sealed class WH40KAlwaysShowHealthBarOverlay : Overlay
{
    private readonly IEntityManager _entManager;
    private readonly SharedTransformSystem _transform;
    private readonly MobStateSystem _mobStateSystem;
    private readonly MobThresholdSystem _mobThresholdSystem;
    private readonly SpriteSystem _spriteSystem;
    private readonly ProgressColorSystem _progressColor;
    private readonly DamageableSystem _damageable;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WH40KAlwaysShowHealthBarOverlay(IEntityManager entManager)
    {
        _entManager = entManager;
        _transform = _entManager.System<SharedTransformSystem>();
        _mobStateSystem = _entManager.System<MobStateSystem>();
        _mobThresholdSystem = _entManager.System<MobThresholdSystem>();
        _spriteSystem = _entManager.System<SpriteSystem>();
        _progressColor = _entManager.System<ProgressColorSystem>();
        _damageable = _entManager.System<DamageableSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var rotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        var xformQuery = _entManager.GetEntityQuery<TransformComponent>();

        const float scale = 1f;
        var scaleMatrix = Matrix3Helpers.CreateScale(new Vector2(scale, scale));
        var rotationMatrix = Matrix3Helpers.CreateRotation(-rotation);

        var query = _entManager.AllEntityQueryEnumerator<WH40KAlwaysShowHealthBarComponent, DamageableComponent, SpriteComponent>();
        while (query.MoveNext(out var uid,
            out var marker,
            out var damageableComponent,
            out var spriteComponent))
        {
            if (!xformQuery.TryGetComponent(uid, out var xform) ||
                xform.MapID != args.MapId)
                continue;

            var bounds = _spriteSystem.GetLocalBounds((uid, spriteComponent));
            var worldPos = _transform.GetWorldPosition(xform, xformQuery);

            if (!bounds.Translated(worldPos).Intersects(args.WorldAABB))
                continue;

            if (CalcProgress(uid, marker, damageableComponent) is not { } deathProgress)
                continue;

            var worldPosition = _transform.GetWorldPosition(xform);
            var worldMatrix = Matrix3Helpers.CreateTranslation(worldPosition);

            var scaledWorld = Matrix3x2.Multiply(scaleMatrix, worldMatrix);
            var matty = Matrix3x2.Multiply(rotationMatrix, scaledWorld);

            handle.SetTransform(matty);

            var heightPx = bounds.Height * EyeManager.PixelsPerMeter;
            var widthPx = bounds.Width * EyeManager.PixelsPerMeter;

            var barWidth = marker.BarWidthPixels > 0f ? marker.BarWidthPixels : widthPx;
            var barHeight = marker.BarHeightPixels > 0f ? marker.BarHeightPixels : 3f;

            var yOffset = heightPx / 2 + heightPx * marker.YOffsetSpritePercent + marker.YOffsetPixels;

            var position = new Vector2(-barWidth / EyeManager.PixelsPerMeter / 2, yOffset / EyeManager.PixelsPerMeter);
            var color = GetProgressColor(deathProgress.ratio, deathProgress.inCrit);

            var xProgress = barWidth * deathProgress.ratio;

            var boxBackground = new Box2(new Vector2(0f, 0f) / EyeManager.PixelsPerMeter, new Vector2(barWidth, barHeight) / EyeManager.PixelsPerMeter);
            boxBackground = boxBackground.Translated(position);
            handle.DrawRect(boxBackground, Black.WithAlpha(192));

            var boxMain = new Box2(new Vector2(0f, 0f) / EyeManager.PixelsPerMeter, new Vector2(xProgress, barHeight) / EyeManager.PixelsPerMeter);
            boxMain = boxMain.Translated(position);
            handle.DrawRect(boxMain, color);

            var darkenStart = MathF.Max(0f, barHeight - 1f);
            var pixelDarken = new Box2(new Vector2(0f, darkenStart) / EyeManager.PixelsPerMeter, new Vector2(xProgress, barHeight) / EyeManager.PixelsPerMeter);
            pixelDarken = pixelDarken.Translated(position);
            handle.DrawRect(pixelDarken, Black.WithAlpha(128));
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    private (float ratio, bool inCrit)? CalcProgress(EntityUid uid, WH40KAlwaysShowHealthBarComponent marker, DamageableComponent dmg)
    {
#pragma warning disable CS0618 // GetTotalDamage: no alternative API for health bar calculation
        var totalDamage = _damageable.GetTotalDamage((uid, dmg));
#pragma warning restore CS0618

        if (marker.UseMobThresholds &&
            _entManager.TryGetComponent(uid, out MobStateComponent? mobState) &&
            _entManager.TryGetComponent(uid, out MobThresholdsComponent? thresholds))
        {
            if (_mobStateSystem.IsAlive(uid, mobState))
            {
                if (dmg.HealthBarThreshold != null && totalDamage < dmg.HealthBarThreshold)
                    return null;

                if (!_mobThresholdSystem.TryGetThresholdForState(uid, MobState.Critical, out var threshold, thresholds) &&
                    !_mobThresholdSystem.TryGetThresholdForState(uid, MobState.Dead, out threshold, thresholds))
                    return (1, false);

                var ratio = 1 - ((FixedPoint2)(totalDamage / threshold)).Float();
                return (ClampRatio(ratio), false);
            }

            if (_mobStateSystem.IsCritical(uid, mobState))
            {
                if (!_mobThresholdSystem.TryGetThresholdForState(uid, MobState.Critical, out var critThreshold, thresholds) ||
                    !_mobThresholdSystem.TryGetThresholdForState(uid, MobState.Dead, out var deadThreshold, thresholds))
                {
                    return (1, true);
                }

                var ratio = 1 - ((totalDamage - critThreshold) / (deadThreshold - critThreshold)).Value.Float();
                return (ClampRatio(ratio), true);
            }

            return (0, true);
        }

        if (marker.MaxHealth == null || marker.MaxHealth <= FixedPoint2.Zero)
            return null;

        if (dmg.HealthBarThreshold != null && totalDamage < dmg.HealthBarThreshold)
            return null;

        var max = marker.MaxHealth.Value;
        var ratioFallback = 1 - ((FixedPoint2)(totalDamage / max)).Float();
        return (ClampRatio(ratioFallback), false);
    }

    private Color GetProgressColor(float progress, bool crit)
    {
        if (crit)
            progress = 0;

        return _progressColor.GetProgressColor(progress);
    }

    private static float ClampRatio(float ratio)
    {
        if (ratio < 0f)
            return 0f;
        if (ratio > 1f)
            return 1f;
        return ratio;
    }
}
