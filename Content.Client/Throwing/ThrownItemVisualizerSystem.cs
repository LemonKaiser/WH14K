using Content.Shared.Throwing;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client.Throwing;

/// <summary>
///     Handles animating thrown items.
/// </summary>
public sealed class ThrownItemVisualizerSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float ThrowScalePeak = 1.4f;
    private const float ScaleEpsilon = 0.0001f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ThrownItemComponent, AfterAutoHandleStateEvent>(OnAutoHandleState);
        SubscribeLocalEvent<ThrownItemComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnAutoHandleState(EntityUid uid, ThrownItemComponent component, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite) || !component.Animate)
            return;

        component.OriginalScale ??= sprite.Scale;
        ApplyThrowScale((uid, component, sprite));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ThrownItemComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var thrown, out var sprite))
        {
            if (!thrown.Animate)
                continue;

            thrown.OriginalScale ??= sprite.Scale;
            ApplyThrowScale((uid, thrown, sprite));
        }
    }

    private void OnShutdown(EntityUid uid, ThrownItemComponent component, ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite) && component.OriginalScale != null)
            _sprite.SetScale((uid, sprite), component.OriginalScale.Value);
    }

    private void ApplyThrowScale(Entity<ThrownItemComponent, SpriteComponent> ent)
    {
        if (ent.Comp1.OriginalScale is not { } originalScale)
            return;

        var targetScale = originalScale * GetThrowScaleMultiplier(ent.Comp1);
        if ((ent.Comp2.Scale - targetScale).LengthSquared() <= ScaleEpsilon)
            return;

        _sprite.SetScale((ent.Owner, ent.Comp2), targetScale);
    }

    private float GetThrowScaleMultiplier(ThrownItemComponent component)
    {
        if (component.ThrownTime == null || component.LandTime == null)
            return 1.0f;

        var start = component.ThrownTime.Value;
        var end = component.LandTime.Value;
        if (end <= start)
            return 1.0f;

        var progress = (float) ((_timing.CurTime - start).TotalSeconds / (end - start).TotalSeconds);
        progress = Math.Clamp(progress, 0.0f, 1.0f);

        if (progress <= 0.25f)
        {
            var t = progress / 0.25f;
            return Lerp(1.0f, ThrowScalePeak, t);
        }

        if (progress <= 0.75f)
        {
            var t = (progress - 0.25f) / 0.5f;
            return Lerp(ThrowScalePeak, 1.0f, t);
        }

        return 1.0f;
    }

    private static float Lerp(float from, float to, float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        return from + (to - from) * t;
    }
}
