using System.Numerics;
using Content.Shared._RMC14.MotionDetector;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._RMC14.MotionDetector;

public sealed class MotionDetectorOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";

    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private TimeSpan _last;
    private readonly List<(Vector2 Direction, int Octant, bool Special)> _blips = new();

    private readonly MotionDetectorOverlaySystem _motionDetector;
    private readonly ShaderInstance _unshadedShader;
    private readonly SpriteSystem _sprite;

    public MotionDetectorOverlay()
    {
        IoCManager.InjectDependencies(this);
        _motionDetector = _entity.System<MotionDetectorOverlaySystem>();
        _unshadedShader = _prototype.Index(UnshadedShader).Instance();
        _sprite = _entity.System<SpriteSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var markerRsi = new ResPath("/Textures/_RMC14/Markers/Waypointers/waypointer.rsi");
        var stage = (int) (_timing.CurTime.TotalSeconds * 12f) % 5 + 1;
        var frame = _sprite.Frame0(new SpriteSpecifier.Rsi(markerRsi, $"marker{stage}"));
        var specialFrame = frame;

        var handle = args.WorldHandle;
        handle.UseShader(_unshadedShader);
        _motionDetector.DrawBlips<MotionDetectorComponent>(handle, ref _last, _blips, frame, specialFrame);
        handle.SetTransform(Matrix3x2.Identity);
    }
}
