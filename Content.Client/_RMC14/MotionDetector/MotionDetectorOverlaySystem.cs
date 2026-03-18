using System.Linq;
using System.Numerics;
using Content.Client.Hands.Systems;
using Content.Shared._RMC14.MotionDetector;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client._RMC14.MotionDetector;

public sealed class MotionDetectorOverlaySystem : EntitySystem
{
    private const float ArrowRadius = 0.35f;
    private const float ArrowRadiusStackStep = 0.14f;
    private static readonly Color ArrowColor = new(110, 245, 255);
    private static readonly Color SpecialArrowColor = new(255, 210, 110);

    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IClientNetManager _net = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        if (!_overlay.HasOverlay<MotionDetectorOverlay>())
            _overlay.AddOverlay(new MotionDetectorOverlay());
    }

    public override void Shutdown()
    {
        _overlay.RemoveOverlay<MotionDetectorOverlay>();
    }

    public void DrawBlips<T>(
        DrawingHandleWorld handle,
        ref TimeSpan last,
        List<(Vector2 Direction, int Octant, bool Special)> blips,
        Texture texture,
        Texture specialTexture) where T : IComponent, IDetectorComponent
    {
        if (_player.LocalEntity is not { } player)
            return;

        var transform = _entity.System<TransformSystem>();
        var playerCoords = transform.GetMapCoordinates(player);

        var hands = _entity.System<HandsSystem>();
        var inventory = _entity.System<InventorySystem>();
        var time = _timing.CurTime;

        var entities = hands.EnumerateHeld(player).ToList();
        if (inventory.TryGetContainerSlotEnumerator(player, out var inv))
        {
            while (inv.NextItem(out var item))
            {
                entities.Add(item);

                if (_entity.HasComponent<PropagateDetectorsComponent>(item) &&
                    _entity.TryGetComponent<StorageComponent>(item, out var itemStorage))
                {
                    foreach (var deepItem in itemStorage.StoredItems.Keys)
                    {
                        entities.Add(deepItem);
                    }
                }
            }
        }

        foreach (var held in entities)
        {
            if (!_entity.TryGetComponent(held, out T? detector))
                continue;

            var duration = detector.ScanDuration;
            if (_net.ServerChannel is { } channel)
                duration += TimeSpan.FromMilliseconds(channel.Ping / 2f);

            if (time > detector.LastScan + duration)
                continue;

            if (last != detector.LastScan)
            {
                last = detector.LastScan;
                blips.Clear();

                foreach (var blip in detector.Blips)
                {
                    if (playerCoords.MapId != blip.Coordinates.MapId)
                        continue;

                    var diff = blip.Coordinates.Position - playerCoords.Position;
                    if (diff.LengthSquared() <= 0.0001f)
                        diff = blip.Direction;

                    if (diff.LengthSquared() <= 0.0001f)
                        continue;

                    var octant = GetOctant(diff);
                    blips.Add((OctantDirection(octant), octant, blip.Special));
                }
            }

            var stackPerOctant = new int[8];
            foreach (var blip in blips)
            {
                var textureToDraw = blip.Special ? specialTexture : texture;
                var color = blip.Special ? SpecialArrowColor : ArrowColor;
                var stack = stackPerOctant[blip.Octant]++;

                var drawPos = playerCoords.Position + blip.Direction * (ArrowRadius + stack * ArrowRadiusStackStep);
                var offset = new Vector2(textureToDraw.Width * 0.5f, textureToDraw.Height * 0.5f) / EyeManager.PixelsPerMeter;
                handle.DrawTexture(
                    textureToDraw,
                    drawPos - offset,
                    (-blip.Direction).ToWorldAngle(),
                    color);
            }
        }
    }

    private static int GetOctant(Vector2 direction)
    {
        var angle = MathF.Atan2(direction.Y, direction.X);
        var octant = (int) MathF.Round(angle / (MathF.PI / 4f));
        octant %= 8;
        if (octant < 0)
            octant += 8;
        return octant;
    }

    private static Vector2 OctantDirection(int octant)
    {
        var angle = octant * (MathF.PI / 4f);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }
}
