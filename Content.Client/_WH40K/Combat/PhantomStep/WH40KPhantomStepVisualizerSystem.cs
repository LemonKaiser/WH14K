using System;
using System.Linq;
using System.Numerics;
using Content.Shared._WH40K.Combat.PhantomStep;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Animations;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using static Robust.Client.Animations.AnimationTrackProperty;

namespace Content.Client._WH40K.Combat.PhantomStep;

public sealed partial class WH40KPhantomStepVisualizerSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> Shader = "WH40KPhantomAfterimage";
    private static readonly Color TrailTint = Color.FromHex("#B5F4FF");

    [Dependency] private AnimationPlayerSystem _animations = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KPhantomStepTrailEvent>(OnTrail);
    }

    private void OnTrail(WH40KPhantomStepTrailEvent ev)
    {
        var source = GetEntity(ev.Entity);
        var start = GetCoordinates(ev.Start);
        var end = GetCoordinates(ev.End);

        if (!Exists(source) || !start.IsValid(EntityManager) || !end.IsValid(EntityManager))
            return;

        if (!TryComp(source, out SpriteComponent? sourceSprite))
            return;

        var startMap = _transform.ToMapCoordinates(start);
        var endMap = _transform.ToMapCoordinates(end);
        if (startMap.MapId == MapId.Nullspace || startMap.MapId != endMap.MapId)
            return;

        var delta = endMap.Position - startMap.Position;
        if (delta.LengthSquared() <= 0.0001f)
            return;

        var direction = Vector2.Normalize(delta);
        var normal = new Vector2(-direction.Y, direction.X);
        var copies = Math.Clamp(ev.Copies, 1, 8);
        var lifetime = MathF.Max(0.08f, ev.TrailLifetime);
        var duration = MathF.Max(0.04f, ev.Duration);
        var shaderPrototype = _prototypeManager.Index<ShaderPrototype>(Shader);

        for (var i = 0; i < copies; i++)
        {
            var normalized = copies == 1 ? 1f : i / (float) (copies - 1);
            var trailIndex = i;
            var spawnDelay = duration * normalized * 0.82f;

            void SpawnClone()
            {
                SpawnTrailClone(source, direction, normal, shaderPrototype, trailIndex, normalized, lifetime, duration);
            }

            if (spawnDelay <= 0f)
                SpawnClone();
            else
                Timer.Spawn(TimeSpan.FromSeconds(spawnDelay), SpawnClone);
        }
    }

    private void SpawnTrailClone(
        EntityUid source,
        Vector2 direction,
        Vector2 normal,
        ShaderPrototype shaderPrototype,
        int cloneIndex,
        float normalized,
        float lifetime,
        float duration)
    {
        if (!Exists(source) || !TryComp(source, out SpriteComponent? sourceSprite))
            return;

        var clone = Spawn("clientsideclone", Transform(source).Coordinates);
        var cloneSprite = EnsureComp<SpriteComponent>(clone);

        _sprite.CopySprite((source, sourceSprite), (clone, cloneSprite));
        _sprite.SetVisible((clone, cloneSprite), true);
        StripLighting(clone, cloneSprite);

        var baseAlpha = Math.Clamp(0.16f + normalized * 0.48f, 0.12f, 0.72f);
        var baseColor = TrailTint.WithAlpha(baseAlpha);
        _sprite.SetColor((clone, cloneSprite), baseColor);

        cloneSprite.PostShader = CreateShaderInstance(shaderPrototype, normalized, duration);
        cloneSprite.GetScreenTexture = false;
        cloneSprite.RaiseShaderEvent = false;

        var despawn = EnsureComp<TimedDespawnComponent>(clone);
        despawn.Lifetime = lifetime + 0.05f;

        var lateralSign = cloneIndex % 2 == 0 ? -1f : 1f;
        var startOffset = -direction * (0.01f + normalized * 0.018f) +
                          normal * lateralSign * (0.004f + normalized * 0.006f);
        var endOffset = -direction * (0.12f + normalized * 0.07f) +
                        normal * lateralSign * (0.024f + normalized * 0.02f);

        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(lifetime),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    KeyFrames =
                    {
                        new KeyFrame(baseColor, 0f),
                        new KeyFrame(baseColor.WithAlpha(baseAlpha * 0.72f), lifetime * 0.38f),
                        new KeyFrame(baseColor.WithAlpha(0f), lifetime, Easings.OutQuint)
                    }
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new KeyFrame(startOffset, 0f),
                        new KeyFrame((startOffset + endOffset) * 0.45f, lifetime * 0.42f),
                        new KeyFrame(endOffset, lifetime, Easings.OutQuart)
                    }
                }
            }
        };

        _animations.Play((clone, EnsureComp<AnimationPlayerComponent>(clone)), animation, $"wh40k_phantom_trail_{cloneIndex}");
    }

    private ShaderInstance CreateShaderInstance(ShaderPrototype shaderPrototype, float normalized, float duration)
    {
        var instance = shaderPrototype.InstanceUnique();
        instance.SetParameter("Tint", new Vector3(TrailTint.R, TrailTint.G, TrailTint.B));
        instance.SetParameter("EdgeStrength", 1.05f + normalized * 0.22f);
        instance.SetParameter("Smear", 0.014f + normalized * 0.01f);
        instance.SetParameter("Phase", normalized * 1.7f + duration * 9f);
        return instance;
    }

    private void StripLighting(EntityUid uid, SpriteComponent sprite)
    {
        for (var i = 0; i < sprite.AllLayers.Count(); i++)
        {
            if (_sprite.TryGetLayer((uid, sprite), i, out var layer, false) &&
                layer.ShaderPrototype != "DisplacedDraw")
            {
                sprite.LayerSetShader(i, "unshaded");
            }
        }
    }
}
