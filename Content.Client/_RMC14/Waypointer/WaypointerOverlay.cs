using System.Numerics;
using Content.Shared._RMC14.Waypointer;
using Content.Shared._RMC14.Waypointer.Components;
using Content.Shared.CombatMode;
using Content.Shared.Whitelist;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._RMC14.Waypointer;

public sealed class WaypointerOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";

    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private readonly SharedCombatModeSystem _combatMode;
    private readonly SharedPhysicsSystem _physics;
    private readonly SpriteSystem _sprite;
    private readonly TransformSystem _transform;
    private readonly ShaderInstance _unshadedShader;
    private readonly EntityWhitelistSystem _whitelist;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    internal WaypointerOverlay()
    {
        IoCManager.InjectDependencies(this);

        _combatMode = _entity.System<SharedCombatModeSystem>();
        _physics = _entity.System<SharedPhysicsSystem>();
        _sprite = _entity.System<SpriteSystem>();
        _transform = _entity.System<TransformSystem>();
        _unshadedShader = _prototype.Index(UnshadedShader).Instance();
        _whitelist = _entity.System<EntityWhitelistSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        handle.UseShader(_unshadedShader);

        if (_player.LocalEntity == null ||
            !_entity.TryGetComponent<ActiveWaypointerComponent>(_player.LocalEntity, out var waypointer) ||
            !waypointer.Active ||
            waypointer.WaypointerProtoIds == null ||
            !_entity.TryGetComponent<TransformComponent>(_player.LocalEntity, out var playerXform) ||
            playerXform.MapID != args.MapId)
        {
            return;
        }

        var player = _player.LocalEntity.Value;
        var positionA = _transform.GetWorldPosition(playerXform);

        foreach (var (waypointerProtoId, isEnabled) in waypointer.WaypointerProtoIds)
        {
            if (!isEnabled)
                continue;

            if (!_prototype.Resolve(waypointerProtoId, out var proto) ||
                (!proto.WorkOnGrid && playerXform.GridUid != null) ||
                (!proto.WorkInCombat && _combatMode.IsInCombatMode(player)))
            {
                continue;
            }

            var waypointQuery = _entity.CompRegistryQueryEnumerator(proto.TrackedComponents);
            while (waypointQuery.MoveNext(out var target))
            {
                if (!_whitelist.CheckBoth(target, blacklist: proto.Blacklist, whitelist: proto.Whitelist) ||
                    !_entity.TryGetComponent<TransformComponent>(target, out var targetXform) ||
                    targetXform.MapID != args.MapId)
                {
                    continue;
                }

                var positionAndRotationB = _transform.GetWorldPositionRotation(targetXform);
                var positionB = positionAndRotationB.WorldPosition;

                float distance;
                if (_entity.TryGetComponent<MapGridComponent>(target, out var map))
                {
                    _physics.TryGetDistance(player, target, out distance, playerXform, targetXform);
                    positionB += positionAndRotationB.WorldRotation.RotateVec(map.LocalAABB.Center);
                }
                else
                {
                    distance = (positionA - positionB).Length();
                }

                if (distance > proto.MaxRange)
                    continue;

                var increments = proto.MaxRange / proto.WaypointerStates;
                var stage = (int) MathF.Truncate(distance / increments) + 1;
                stage = Math.Clamp(stage, 1, Math.Max(1, (int) proto.WaypointerStates));

                var rsi = new SpriteSpecifier.Rsi(proto.RsiPath, "marker" + stage);
                var texture = _sprite.Frame0(rsi);

                var offset = new Vector2(texture.Height * 0.5f, texture.Width * 0.5f) / EyeManager.PixelsPerMeter;
                var direction = positionA - positionB;
                var angle = direction.ToWorldAngle();

                handle.DrawTexture(texture, positionA - offset, angle, proto.Color);
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
