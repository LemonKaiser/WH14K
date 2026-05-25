using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.Utility;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static Robust.UnitTesting.RobustIntegrationTest;
using ImageColor = SixLabors.ImageSharp.Color;

namespace Content.MapRenderer.Painters;

public sealed class EntityPainter
{
    private readonly IResourceManager _resManager;

    private readonly Dictionary<(string path, string state), Image> _images;
    private readonly Image _errorImage;

    private readonly IEntityManager _sEntityManager;

    public EntityPainter(ClientIntegrationInstance client, ServerIntegrationInstance server)
    {
        _resManager = client.ResolveDependency<IResourceManager>();

        _sEntityManager = server.ResolveDependency<IEntityManager>();

        _images = new Dictionary<(string path, string state), Image>();
        _errorImage = Image.Load<Rgba32>(_resManager.ContentFileRead("/Textures/error.rsi/error.png"));
    }

    public void Run(Image canvas, List<EntityData> entities, Vector2 customOffset = default)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        // TODO cache this shit what are we insane
        entities.Sort(Comparer<EntityData>.Create((x, y) => x.Sprite.DrawDepth.CompareTo(y.Sprite.DrawDepth)));
        var xformSystem = _sEntityManager.System<SharedTransformSystem>();

        foreach (var entity in entities)
        {
            Run(canvas, entity, xformSystem, customOffset);
        }

        Console.WriteLine($"{nameof(EntityPainter)} painted {entities.Count} entities in {(int)stopwatch.Elapsed.TotalMilliseconds} ms");
    }

    private static Matrix3x2 GetSpriteMatrix(EntityData entity, Vector2 worldPosition, Angle worldRotation, SpriteComponent.Layer layer)
    {
        var angle = worldRotation.Reduced().FlipPositive();
        var snappedRotation = !entity.Sprite.NoRotation && entity.Sprite.SnapCardinals
            ? angle.RoundToCardinalAngle()
            : Angle.Zero;

        var spriteMatrix = Matrix3x2.Multiply(
            entity.Sprite.LocalMatrix,
            Matrix3Helpers.CreateTransform(
                worldPosition,
                entity.Sprite.NoRotation ? Angle.Zero : worldRotation - snappedRotation));

        if (!entity.Sprite.GranularLayersRendering)
            return spriteMatrix;

        return layer.RenderingStrategy switch
        {
            LayerRenderingStrategy.UseSpriteStrategy => spriteMatrix,
            LayerRenderingStrategy.Default => Matrix3x2.Multiply(
                entity.Sprite.LocalMatrix,
                Matrix3Helpers.CreateTransform(worldPosition, worldRotation)),
            LayerRenderingStrategy.SnapToCardinals => Matrix3x2.Multiply(
                entity.Sprite.LocalMatrix,
                Matrix3Helpers.CreateTransform(worldPosition, worldRotation - angle.RoundToCardinalAngle())),
            LayerRenderingStrategy.NoRotation => Matrix3x2.Multiply(
                entity.Sprite.LocalMatrix,
                Matrix3Helpers.CreateTransform(worldPosition, Angle.Zero)),
            _ => spriteMatrix
        };
    }

    private static (RsiDirection BaseDirection, RsiDirection FinalDirection) GetDirections(SpriteComponent.Layer layer, Angle worldRotation)
    {
        var state = layer.ActualState;
        if (state == null)
            return (RsiDirection.South, RsiDirection.South);

        var angle = worldRotation.Reduced().FlipPositive();
        var baseDirection = SpriteComponent.Layer.GetDirection(state.RsiDirections, angle);
        return (baseDirection, baseDirection.OffsetRsiDir(layer.DirOffset));
    }

    private static (int X, int Y, int Width, int Height) GetFrameRect(
        Image image,
        RSI.State? state,
        RsiDirection direction,
        int animationFrame)
    {
        if (state == null)
            return (0, 0, image.Width, image.Height);

        var frameWidth = state.Size.X;
        var frameHeight = state.Size.Y;
        var statesX = Math.Max(1, image.Width / frameWidth);
        var frameCount = Math.Max(1, state.DelayCount);
        var clampedFrame = Math.Clamp(animationFrame, 0, frameCount - 1);
        var target = ((int) direction * frameCount) + clampedFrame;
        var targetY = target / statesX;
        var targetX = target % statesX;

        return (targetX * frameWidth, targetY * frameHeight, frameWidth, frameHeight);
    }

    public void Run(Image canvas, EntityData entity, SharedTransformSystem xformSystem, Vector2 customOffset = default)
    {
        if (!entity.Sprite.Visible || entity.Sprite.ContainerOccluded)
        {
            return;
        }

        var worldRotation = xformSystem.GetWorldRotation(entity.Owner);
        var worldPosition = new Vector2(
            entity.X / EyeManager.PixelsPerMeter + customOffset.X,
            entity.Y / EyeManager.PixelsPerMeter + customOffset.Y);

        foreach (var layerBase in entity.Sprite.AllLayers)
        {
            var layer = (SpriteComponent.Layer) layerBase;
            if (!layer.Visible)
            {
                continue;
            }

            if (!layer.State.IsValid)
            {
                continue;
            }

            var rsi = layer.ActualRsi;
            RSI.State? state = null;
            Image baseImage;

            if (rsi == null || !rsi.TryGetState(layer.State, out state))
            {
                baseImage = _errorImage;
            }
            else
            {
                var rsiPath = rsi.Path.ToString();
                var key = (rsiPath, state!.StateId.Name!);

                if (!_images.TryGetValue(key, out baseImage!))
                {
                    var stream = _resManager.ContentFileRead($"{rsiPath}/{state.StateId}.png");
                    baseImage = Image.Load<Rgba32>(stream);

                    _images[key] = baseImage;
                }
            }

            var (baseDirection, finalDirection) = GetDirections(layer, worldRotation);
            var (x, y, width, height) = GetFrameRect(baseImage, state, finalDirection, layer.AnimationFrame);
            var rect = new Rectangle(x, y, width, height);
            if (!new Rectangle(Point.Empty, baseImage.Size).Contains(rect))
            {
                var invalidPath = rsi != null ? rsi.Path.ToString() : "<error>";
                Console.WriteLine($"Invalid layer {invalidPath}/{layer.State.Name}.png for entity {_sEntityManager.ToPrettyString(entity.Owner)} at ({entity.X}, {entity.Y})");
                return;
            }

            using var image = baseImage.CloneAs<Rgba32>();
            image.Mutate(o => o.Crop(rect));

            layer.GetLayerDrawMatrix(baseDirection, out var layerMatrix);
            var transformMatrix = Matrix3x2.Multiply(layerMatrix, GetSpriteMatrix(entity, worldPosition, worldRotation, layer));
            var spriteRotation = 0f;
            var scaleX = MathF.Sqrt(transformMatrix.M11 * transformMatrix.M11 + transformMatrix.M12 * transformMatrix.M12);
            var scaleY = MathF.Sqrt(transformMatrix.M21 * transformMatrix.M21 + transformMatrix.M22 * transformMatrix.M22);

            if (scaleX <= 0.0001f || scaleY <= 0.0001f)
                continue;

            spriteRotation = (float) transformMatrix.Rotation().Degrees;

            var colorMix = entity.Sprite.Color * layer.Color;
            var imageColor = ImageColor.FromRgba(colorMix.RByte, colorMix.GByte, colorMix.BByte, colorMix.AByte);
            var coloredImage = new Image<Rgba32>(image.Width, image.Height);
            coloredImage.Mutate(o => o.BackgroundColor(imageColor));

            var imgX = Math.Max(1, (int) MathF.Round(width * scaleX));
            var imgY = Math.Max(1, (int) MathF.Round(height * scaleY));
            image.Mutate(o => o
                .DrawImage(coloredImage, PixelColorBlendingMode.Multiply, PixelAlphaCompositionMode.SrcAtop, 1)
                .Resize(imgX, imgY)
                .Flip(FlipMode.Vertical));

            if (MathF.Abs(spriteRotation) > 0.01f)
                image.Mutate(o => o.Rotate(spriteRotation));

            coloredImage.Dispose();

            var pointX = (int) MathF.Round(transformMatrix.M31 * EyeManager.PixelsPerMeter) - image.Width / 2;
            var pointY = (int) MathF.Round(transformMatrix.M32 * EyeManager.PixelsPerMeter) - image.Height / 2;
            canvas.Mutate(o => o.DrawImage(image, new Point(pointX, pointY), 1));
        }
    }
}
