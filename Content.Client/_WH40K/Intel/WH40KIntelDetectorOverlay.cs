using System;
using System.Numerics;
using Content.Client._RMC14.MotionDetector;
using Content.Shared._WH40K.Intel.Detector;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Intel;

public sealed class WH40KIntelDetectorOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private TimeSpan _lastScan;
    private readonly List<(Vector2 Direction, int Octant, bool Special)> _blips = new();

    private readonly MotionDetectorOverlaySystem _motionOverlay;
    private readonly SpriteSystem _sprite;
    private static readonly ResPath IntelDetectorRsi = new("/Textures/_RMC14/Objects/Tools/intel_detector.rsi");

    public WH40KIntelDetectorOverlay()
    {
        IoCManager.InjectDependencies(this);
        _motionOverlay = _entity.System<MotionDetectorOverlaySystem>();
        _sprite = _entity.System<SpriteSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var blip = _sprite.GetFrame(new SpriteSpecifier.Rsi(IntelDetectorRsi, "data_blip"), _timing.CurTime);
        var directionalBlip = _sprite.GetFrame(new SpriteSpecifier.Rsi(IntelDetectorRsi, "data_blip_dir"), _timing.CurTime);
        _motionOverlay.DrawBlips<WH40KIntelDetectorComponent>(
            args.WorldHandle,
            ref _lastScan,
            _blips,
            blip,
            directionalBlip);
    }
}
