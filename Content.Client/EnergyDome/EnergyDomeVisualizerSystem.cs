using Content.Shared.EnergyDome;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.EnergyDome;

public sealed class EnergyDomeVisualizerSystem : VisualizerSystem<EnergyDomeVisualsComponent>
{
    private const float FullChargeHoleScale = 10.0f;
    private const float CriticalChargeHoleScale = 7.2f;
    private const float BaseEdgeSoftness = 0.015f;
    private const float ExtraEdgeSoftness = 0.030f;
    private const float BaseAnimationSpeed = 0.55f;
    private const float ExtraAnimationSpeed = 0.65f;

    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, ShaderInstance> _shaderCache = new();
    private readonly Dictionary<EntityUid, float> _alphaCache = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EnergyDomeVisualsComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(EntityUid uid, EnergyDomeVisualsComponent component, ComponentShutdown args)
    {
        _shaderCache.Remove(uid);
        _alphaCache.Remove(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        MapCoordinates? localMap = null;
        if (_player.LocalEntity is { } localEntity && !Deleted(localEntity))
            localMap = _transform.GetMapCoordinates(localEntity);

        var query = EntityQueryEnumerator<EnergyDomeVisualsComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var visuals, out var sprite, out var xform))
        {
            ApplyInsideTransparency(uid, visuals, sprite, (uid, xform), localMap, frameTime);
        }
    }

    protected override void OnAppearanceChange(EntityUid uid, EnergyDomeVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        var chargeFraction = 1.0f;
        AppearanceSystem.TryGetData(uid, EnergyDomeVisuals.ChargeFraction, out chargeFraction, args.Component);

        ApplyShieldShader(uid, component, args.Sprite, Math.Clamp(chargeFraction, 0.0f, 1.0f));
    }

    private void ApplyShieldShader(EntityUid uid, EnergyDomeVisualsComponent component, SpriteComponent sprite, float chargeFraction)
    {
        if (!SpriteSystem.TryGetLayer((uid, sprite), component.Layer, out var layer, false))
            return;

        var startCharge = Math.Clamp(component.EffectStartCharge, 0.01f, 1.0f);
        var fullCharge = Math.Clamp(component.EffectFullCharge, 0.0f, startCharge - 0.001f);
        var severity = GetDepletionSeverity(chargeFraction, startCharge, fullCharge);
        if (severity <= 0.0001f)
        {
            // Keep full-charge dome on default sprite rendering path.
            if (layer.Shader != null)
                sprite.LayerSetShader(component.Layer, null, null);

            return;
        }

        if (!_shaderCache.TryGetValue(uid, out var shader))
        {
            if (!_prototype.TryIndex<ShaderPrototype>(component.Shader, out var prototype))
                return;

            shader = prototype.InstanceUnique();
            _shaderCache[uid] = shader;
        }

        if (layer.Shader != shader)
            sprite.LayerSetShader(component.Layer, shader, component.Shader);

        var maxCoverage = Math.Clamp(component.MaxCoverage, 0.0f, 1.0f);
        var minCoverageFactor = Math.Clamp(component.MinCoverageFactor, 0.0f, 1.0f);
        var coverageExponent = Math.Max(component.CoverageExponent, 0.05f);
        var shapedSeverity = MathF.Pow(severity, coverageExponent);
        var coverage = maxCoverage * (minCoverageFactor + (1.0f - minCoverageFactor) * shapedSeverity);
        coverage = Math.Clamp(coverage, 0.0f, maxCoverage);
        var holeScale = FullChargeHoleScale + (CriticalChargeHoleScale - FullChargeHoleScale) * severity;
        var edgeSoftness = BaseEdgeSoftness + ExtraEdgeSoftness * severity;
        var speed = BaseAnimationSpeed + ExtraAnimationSpeed * severity;

        shader.SetParameter("holeCoverage", coverage);
        shader.SetParameter("holeScale", holeScale);
        shader.SetParameter("edgeSoftness", edgeSoftness);
        shader.SetParameter("speed", speed);
        shader.SetParameter("debugView", component.DebugView);
        shader.SetParameter("darkenCoreStrength", Math.Clamp(component.DarkenCoreStrength, 0.0f, 2.0f));
        shader.SetParameter("darkenHazeStrength", Math.Clamp(component.DarkenHazeStrength, 0.0f, 2.0f));
        shader.SetParameter("alphaCoreStrength", Math.Clamp(component.AlphaCoreStrength, 0.0f, 2.0f));
        shader.SetParameter("alphaHazeStrength", Math.Clamp(component.AlphaHazeStrength, 0.0f, 2.0f));
        shader.SetParameter("minColorFactor", Math.Clamp(component.MinColorFactor, 0.0f, 1.0f));
        shader.SetParameter("minAlphaFactor", Math.Clamp(component.MinAlphaFactor, 0.0f, 1.0f));
    }

    private void ApplyInsideTransparency(
        EntityUid uid,
        EnergyDomeVisualsComponent component,
        SpriteComponent sprite,
        Entity<TransformComponent> xform,
        MapCoordinates? localMap,
        float frameTime)
    {
        if (!SpriteSystem.TryGetLayer((uid, sprite), component.Layer, out var layer, false))
            return;

        var targetAlpha = 1.0f;
        if (component.InsideTransparencyEnabled && localMap != null)
        {
            var domeMap = _transform.GetMapCoordinates(xform);
            if (domeMap.MapId == localMap.Value.MapId)
            {
                var radius = MathF.Max(component.InsideTransparencyRadius, 0.01f);
                var distanceSq = (localMap.Value.Position - domeMap.Position).LengthSquared();
                if (distanceSq <= radius * radius)
                    targetAlpha = Math.Clamp(component.InsideTransparencyAlpha, 0.05f, 1.0f);
            }
        }

        var currentAlpha = _alphaCache.TryGetValue(uid, out var cachedAlpha)
            ? cachedAlpha
            : layer.Color.A;

        var fadeSpeed = MathF.Max(component.InsideTransparencyFadeSpeed, 0.0f);
        var nextAlpha = fadeSpeed <= 0.0f
            ? targetAlpha
            : currentAlpha + (targetAlpha - currentAlpha) * Math.Clamp(frameTime * fadeSpeed, 0.0f, 1.0f);

        _alphaCache[uid] = nextAlpha;

        if (MathF.Abs(layer.Color.A - nextAlpha) > 0.001f)
            SpriteSystem.LayerSetColor((uid, sprite), component.Layer, layer.Color.WithAlpha(nextAlpha));
    }

    private static float GetDepletionSeverity(float chargeFraction, float startCharge, float fullCharge)
    {
        if (chargeFraction >= startCharge)
            return 0.0f;

        var normalized = (startCharge - chargeFraction) / (startCharge - fullCharge);
        return Math.Clamp(normalized, 0.0f, 1.0f);
    }
}
