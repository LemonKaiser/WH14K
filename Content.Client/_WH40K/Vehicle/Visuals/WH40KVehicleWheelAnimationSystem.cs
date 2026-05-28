using Content.Shared._WH40K.Vehicle.Movement;
using Robust.Client.GameObjects;
using Robust.Shared.Physics.Components;

namespace Content.Client._WH40K.Vehicle.Visuals;

public sealed partial class WH40KVehicleWheelAnimationSystem : EntitySystem
{
    private const int MovementLayerIndex = 0;
    private const float MovingSpeedThreshold = 0.08f;

    [Dependency] private  SpriteSystem _sprite = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var movingSpeedThresholdSquared = MovingSpeedThreshold * MovingSpeedThreshold;
        var query = EntityQueryEnumerator<WH40KVehicleCarMovementComponent, SpriteComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite, out var physics))
        {
            var moving = physics.LinearVelocity.LengthSquared() > movingSpeedThresholdSquared;
            SetWheelAnimation((uid, sprite), moving);
        }
    }

    private void SetWheelAnimation(Entity<SpriteComponent?> sprite, bool moving)
    {
        _sprite.LayerSetAutoAnimated(sprite, MovementLayerIndex, moving);

        if (!moving)
            _sprite.LayerSetAnimationTime(sprite, MovementLayerIndex, 0f);
    }
}
