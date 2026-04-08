using System;
using System.Numerics;
using Content.Client.Hands.Systems;
using Content.Shared.Inventory;
using Content.Shared._WH40K.Intel.Detector;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Intel;

public sealed class WH40KIntelDetectorOverlay : Overlay
{
    private const float ArrowRadius = 0.35f;
    private const float ArrowRadiusStackStep = 0.14f;
    private static readonly Color ArrowColor = new(110, 245, 255);
    private static readonly Color SpecialArrowColor = new(255, 210, 110);

    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IClientNetManager _net = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private TimeSpan _last;
    private readonly List<(Vector2 Direction, int Octant, bool Special)> _blips = new();
    private readonly int[] _stackPerOctant = new int[8];

    private readonly SpriteSystem _sprite;
    private static readonly ResPath IntelDetectorRsi = new("/Textures/_RMC14/Objects/Tools/intel_detector.rsi");

    public WH40KIntelDetectorOverlay()
    {
        IoCManager.InjectDependencies(this);
        _sprite = _entity.System<SpriteSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player)
            return;

        var blip = _sprite.GetFrame(new SpriteSpecifier.Rsi(IntelDetectorRsi, "data_blip"), _timing.CurTime);
        var directionalBlip = _sprite.GetFrame(new SpriteSpecifier.Rsi(IntelDetectorRsi, "data_blip_dir"), _timing.CurTime);
        DrawBlips(args.WorldHandle, player, blip, directionalBlip);
    }

    private void DrawBlips(DrawingHandleWorld handle, EntityUid player, Texture texture, Texture specialTexture)
    {
        var transform = _entity.System<TransformSystem>();
        var hands = _entity.System<HandsSystem>();
        var inventory = _entity.System<InventorySystem>();
        var playerCoords = transform.GetMapCoordinates(player);
        var time = _timing.CurTime;

        // Process held items without allocating a temporary list
        foreach (var held in hands.EnumerateHeld(player))
        {
            if (_entity.TryGetComponent(held, out WH40KIntelDetectorComponent? detector))
                DrawDetectorBlips(handle, detector, playerCoords, time, texture, specialTexture);
        }

        if (inventory.TryGetContainerSlotEnumerator(player, out var inv))
        {
            while (inv.NextItem(out var item))
            {
                if (_entity.TryGetComponent(item, out WH40KIntelDetectorComponent? detector))
                    DrawDetectorBlips(handle, detector, playerCoords, time, texture, specialTexture);
            }
        }
    }

    private void DrawDetectorBlips(DrawingHandleWorld handle, WH40KIntelDetectorComponent detector, MapCoordinates playerCoords, TimeSpan time, Texture texture, Texture specialTexture)
    {
        var duration = detector.ScanDuration;
        if (_net.ServerChannel is { } channel)
            duration += TimeSpan.FromMilliseconds(channel.Ping / 2f);

        if (time > detector.LastScan + duration)
            return;

        if (_last != detector.LastScan)
        {
            _last = detector.LastScan;
            _blips.Clear();

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
                _blips.Add((OctantDirection(octant), octant, blip.Special));
            }
        }

        Array.Clear(_stackPerOctant);
        foreach (var blip in _blips)
        {
            var textureToDraw = blip.Special ? specialTexture : texture;
            var color = blip.Special ? SpecialArrowColor : ArrowColor;
            var stack = _stackPerOctant[blip.Octant]++;

            var drawPos = playerCoords.Position + blip.Direction * (ArrowRadius + stack * ArrowRadiusStackStep);
            var offset = new Vector2(textureToDraw.Width * 0.5f, textureToDraw.Height * 0.5f) / EyeManager.PixelsPerMeter;
            handle.DrawTexture(
                textureToDraw,
                drawPos - offset,
                (-blip.Direction).ToWorldAngle(),
                color);
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
