using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.Movement.Systems;

public sealed class FloorOcclusionSystem : SharedFloorOcclusionSystem
{
    private static readonly ProtoId<ShaderPrototype> HorizontalCut = "HorizontalCut";

    [Dependency] private readonly IPrototypeManager _proto = default!;

    private EntityQuery<SpriteComponent> _spriteQuery;
    private ShaderInstance _horizontalCutShader = default!;

    public override void Initialize()
    {
        base.Initialize();

        _spriteQuery = GetEntityQuery<SpriteComponent>();
        _horizontalCutShader = _proto.Index(HorizontalCut).Instance();

        SubscribeLocalEvent<FloorOcclusionComponent, ComponentStartup>(OnOcclusionStartup);
        SubscribeLocalEvent<FloorOcclusionComponent, ComponentShutdown>(OnOcclusionShutdown);
        SubscribeLocalEvent<FloorOcclusionComponent, AfterAutoHandleStateEvent>(OnOcclusionAuto);
        SubscribeLocalEvent<FloorOcclusionComponent, MoveEvent>(OnOcclusionMove);
    }

    private void OnOcclusionAuto(Entity<FloorOcclusionComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        SetShader(ent.Owner, ShouldApplyOcclusion(ent));
    }

    private void OnOcclusionStartup(Entity<FloorOcclusionComponent> ent, ref ComponentStartup args)
    {
        SetShader(ent.Owner, ShouldApplyOcclusion(ent));
    }

    private void OnOcclusionShutdown(Entity<FloorOcclusionComponent> ent, ref ComponentShutdown args)
    {
        SetShader(ent.Owner, false);
    }

    protected override void SetEnabled(Entity<FloorOcclusionComponent> entity)
    {
        SetShader(entity.Owner, ShouldApplyOcclusion(entity));
    }

    private void OnOcclusionMove(Entity<FloorOcclusionComponent> ent, ref MoveEvent args)
    {
        SetShader(ent.Owner, ShouldApplyOcclusion(ent));
    }

    private void SetShader(Entity<SpriteComponent?> sprite, bool enabled)
    {
        if (!_spriteQuery.Resolve(sprite.Owner, ref sprite.Comp, false))
            return;

        if (enabled)
        {
            if (sprite.Comp.PostShader is null || sprite.Comp.PostShader == _horizontalCutShader)
                sprite.Comp.PostShader = _horizontalCutShader;
        }
        else
        {
            if (sprite.Comp.PostShader == _horizontalCutShader)
                sprite.Comp.PostShader = null;
        }
    }
}
