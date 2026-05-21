using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Launcher;

/// <summary>
/// Renders its children into a texture and displays them through the WH40K CRT shader.
/// </summary>
public sealed class WH40KCrtShaderContainer : Control
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "WH40KLauncherCrt";

    private readonly IClyde _clyde;
    private readonly IPrototypeManager _prototype;
    private readonly ShaderInstance _shader;

    private IRenderTexture? _target;

    public WH40KCrtShaderContainer()
    {
        IoCManager.InjectDependencies(this);

        _clyde = IoCManager.Resolve<IClyde>();
        _prototype = IoCManager.Resolve<IPrototypeManager>();
        _shader = _prototype.Index(ShaderId).InstanceUnique();

        MouseFilter = MouseFilterMode.Ignore;
        HorizontalExpand = true;
        VerticalExpand = true;
        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);
    }

    protected override void RenderChildOverride(ref ControlRenderArguments args, int childIndex, Vector2i position)
    {
        // Children are drawn together in PostRenderChildren so the shader can affect the full screen.
    }

    protected override void PostRenderChildren(ref ControlRenderArguments args)
    {
        var size = PixelSize;
        if (size.X <= 0 || size.Y <= 0)
            return;

        EnsureTarget(size);

        var renderHandle = args.Handle;
        var screenHandle = renderHandle.DrawingHandleScreen;
        var oldTransform = screenHandle.GetTransform();
        var oldShader = screenHandle.GetShader();
        var coordinateTransform = args.CoordinateTransform;

        renderHandle.RenderInRenderTarget(_target!, () =>
        {
            screenHandle.SetTransform(Matrix3x2.Identity);
            screenHandle.UseShader(null);

            for (var i = 0; i < ChildCount; i++)
            {
                var child = GetChild(i);
                var childPos = (Vector2i) Vector2.Transform(child.PixelPosition, coordinateTransform);
                UserInterfaceManager.RenderControl(renderHandle, child, childPos);
            }

            screenHandle.SetTransform(Matrix3x2.Identity);
            screenHandle.UseShader(null);
        }, Color.Transparent);

        screenHandle.SetTransform(oldTransform);
        screenHandle.UseShader(oldShader);

        _shader.SetParameter("RenderSize", (Vector2) size);
        screenHandle.UseShader(_shader);
        screenHandle.DrawTextureRect(_target!.Texture, UIBox2.FromDimensions(Vector2.Zero, size));
        screenHandle.UseShader(oldShader);
    }

    [Obsolete("Controls should only be removed from UI tree instead of being disposed")]
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _target?.Dispose();

        base.Dispose(disposing);
    }

    private void EnsureTarget(Vector2i size)
    {
        if (_target != null && _target.Size == size)
            return;

        _target?.Dispose();
        _target = _clyde.CreateRenderTarget(
            size,
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters
            {
                Filter = true,
            },
            nameof(WH40KCrtShaderContainer));
    }
}
