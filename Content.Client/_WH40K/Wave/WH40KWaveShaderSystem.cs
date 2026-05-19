using Content.Shared.CCVar;
using Content.Shared._WH40K.Wave;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Client._WH40K.Wave;

/// <summary>
/// Applies the WH40K sprite wave post-shader to entities with <see cref="WH40KWaveShaderComponent"/>.
/// </summary>
public sealed class WH40KWaveShaderSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> Shader = "WH40KWave";

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private ShaderInstance _shader = default!;
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _shader = _protoMan.Index<ShaderPrototype>(Shader).InstanceUnique();
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
                wave.Offset = _random.NextFloat(0, 1000);

            ApplyShader(uid, enabled ? _shader : null, enabled);
        }
    }

    private void OnStartup(Entity<WH40KWaveShaderComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.Offset = _random.NextFloat(0, 1000);
        ApplyShader(ent.Owner, _enabled ? _shader : null, _enabled);
    }

    private void OnShutdown(Entity<WH40KWaveShaderComponent> ent, ref ComponentShutdown args)
    {
        ApplyShader(ent.Owner, null, false);
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

        _shader.SetParameter("Speed", ent.Comp.Speed);
        _shader.SetParameter("Dis", ent.Comp.Dis);
        _shader.SetParameter("Offset", ent.Comp.Offset);
    }
}
