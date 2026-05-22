using Content.Shared.CCVar;
using Content.Shared._WH40K.Wave;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Wave;

/// <summary>
/// Applies the WH40K sprite wave post-shader to entities with <see cref="WH40KWaveShaderComponent"/>.
/// </summary>
public sealed class WH40KWaveShaderSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> Shader = "WH40KWave";

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, ShaderInstance> _shaderInstances = new();
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _enabled = _cfg.GetCVar(CCVars.WH40KWaveShaderEnabled);

        SubscribeLocalEvent<WH40KWaveShaderComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WH40KWaveShaderComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KWaveShaderComponent, BeforePostShaderRenderEvent>(OnBeforePostShaderRender);

        Subs.CVar(_cfg, CCVars.WH40KWaveShaderEnabled, OnWaveShaderToggle, true);
    }

    private void OnWaveShaderToggle(bool enabled)
    {
        _enabled = enabled;

        var query = EntityQueryEnumerator<WH40KWaveShaderComponent>();
        while (query.MoveNext(out var uid, out var wave))
        {
            if (enabled)
                EnsureResolvedWaveProfile((uid, wave));

            ApplyShader(uid, enabled ? GetOrCreateShader(uid) : null, enabled);
        }
    }

    private void OnStartup(Entity<WH40KWaveShaderComponent> ent, ref ComponentStartup args)
    {
        EnsureResolvedWaveProfile(ent);
        ApplyShader(ent.Owner, _enabled ? GetOrCreateShader(ent.Owner) : null, _enabled);
    }

    private void OnShutdown(Entity<WH40KWaveShaderComponent> ent, ref ComponentShutdown args)
    {
        ApplyShader(ent.Owner, null, false);
        _shaderInstances.Remove(ent.Owner);
    }

    private void ApplyShader(Entity<SpriteComponent?> ent, ShaderInstance? instance, bool enabled)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.PostShader = enabled ? instance : null;
        ent.Comp.GetScreenTexture = false;
        ent.Comp.RaiseShaderEvent = enabled;
    }

    private void OnBeforePostShaderRender(Entity<WH40KWaveShaderComponent> ent, ref BeforePostShaderRenderEvent args)
    {
        if (!_enabled)
            return;

        EnsureResolvedWaveProfile(ent);

        if (args.Sprite.PostShader is not { } shader)
            return;

        shader.SetParameter("Speed", ent.Comp.Speed * ent.Comp.ResolvedSpeedMultiplier);
        shader.SetParameter("Dis", ent.Comp.Dis * ent.Comp.ResolvedDisMultiplier);
        shader.SetParameter("Offset", ent.Comp.ResolvedPhaseOffset);
    }

    private ShaderInstance GetOrCreateShader(EntityUid uid)
    {
        if (_shaderInstances.TryGetValue(uid, out var shader))
            return shader;

        shader = _protoMan.Index<ShaderPrototype>(Shader).InstanceUnique();
        _shaderInstances[uid] = shader;
        return shader;
    }

    private void EnsureResolvedWaveProfile(Entity<WH40KWaveShaderComponent> ent)
    {
        if (ent.Comp.ResolvedWaveProfile)
            return;

        var seed = GetDeterministicSeed(ent.Owner);
        ent.Comp.ResolvedPhaseOffset = ent.Comp.Offset + HashToRange(seed ^ 0x68bc21, 0f, MathF.Tau);
        ent.Comp.ResolvedSpeedMultiplier = HashToRange(
            seed ^ unchecked((int) 0x9e3779b9),
            1f - ent.Comp.SpeedVariance,
            1f + ent.Comp.SpeedVariance);
        ent.Comp.ResolvedDisMultiplier = HashToRange(
            seed ^ unchecked((int) 0x7f4a7c15),
            1f - ent.Comp.DisVariance,
            1f + ent.Comp.DisVariance);
        ent.Comp.ResolvedWaveProfile = true;
    }

    private int GetDeterministicSeed(EntityUid uid)
    {
        var netEntity = GetNetEntity(uid);
        if (netEntity.Valid)
            return netEntity.Id;

        var (worldPos, _) = _transform.GetWorldPositionRotation(uid);
        var xBits = BitConverter.SingleToInt32Bits(worldPos.X);
        var yBits = BitConverter.SingleToInt32Bits(worldPos.Y);
        return uid.Id ^ (xBits * 397) ^ yBits;
    }

    private static float HashToRange(int seed, float min, float max)
    {
        return min + (max - min) * Hash01(seed);
    }

    private static float Hash01(int seed)
    {
        var value = unchecked((uint) seed);
        value ^= value >> 16;
        value *= 0x7feb352d;
        value ^= value >> 15;
        value *= 0x846ca68b;
        value ^= value >> 16;
        return (value & 0x00FFFFFFu) / 16777215f;
    }
}
