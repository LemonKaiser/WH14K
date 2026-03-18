using System;
using System.Numerics;
using Content.Client.Animations;
using Content.Shared._WH40K.Manipulator;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Spawners;
using static Robust.Client.Animations.AnimationTrackProperty;

namespace Content.Client._WH40K.Manipulator;

/// <summary>
/// Plays client-side arc motion visuals for WH40K conveyor manipulator transfers.
/// </summary>
public sealed class WH40KConveyorManipulatorVisualizerSystem : EntitySystem
{
    [Dependency] private readonly AnimationPlayerSystem _animations = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KManipulatorArcAnimationEvent>(OnArcAnimation);
    }

    private void OnArcAnimation(WH40KManipulatorArcAnimationEvent ev)
    {
        var item = GetEntity(ev.Item);
        var start = GetCoordinates(ev.Start);
        var end = GetCoordinates(ev.End);

        if (!Exists(item) || !start.IsValid(EntityManager) || !end.IsValid(EntityManager))
            return;

        if (!TryComp(item, out SpriteComponent? sourceSprite))
            return;

        var clone = Spawn("clientsideclone", start);
        EnsureComp<EntityPickupAnimationComponent>(clone);

        if (TryComp(item, out MetaDataComponent? sourceMeta))
            _metadata.SetEntityName(clone, sourceMeta.EntityName);

        var cloneSprite = EnsureComp<SpriteComponent>(clone);
        _sprite.CopySprite((item, sourceSprite), (clone, cloneSprite));
        _sprite.SetVisible((clone, cloneSprite), true);

        var duration = MathF.Max(0.05f, ev.Duration);
        var arcHeight = MathF.Max(0f, ev.ArcHeight);

        var despawn = EnsureComp<TimedDespawnComponent>(clone);
        despawn.Lifetime = duration + 0.15f;
        _transform.SetLocalRotationNoLerp(clone, ev.InitialAngle);

        var startLocal = start.Position;
        var endMapPosition = _transform.ToMapCoordinates(end).Position;
        var endLocal = Vector2.Transform(endMapPosition, _transform.GetInvWorldMatrix(start.EntityId));
        var midLocal = (startLocal + endLocal) * 0.5f + new Vector2(0f, arcHeight);

        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(duration),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(TransformComponent),
                    Property = nameof(TransformComponent.LocalPosition),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new KeyFrame(startLocal, 0f),
                        new KeyFrame(midLocal, duration * 0.5f),
                        new KeyFrame(endLocal, duration)
                    }
                }
            }
        };

        var animator = EnsureComp<AnimationPlayerComponent>(clone);
        _animations.Play((clone, animator), animation, "wh40k_manipulator_arc");
    }
}
