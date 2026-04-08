using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Robust.Client.Graphics;

namespace Content.Client.Resources;

/// <summary>
/// Upload helpers for raw RGBA frame buffers.
/// Upload must happen on the main thread that owns Clyde.
/// </summary>
public static class RgbaTextureUploader
{
    public static Texture UploadTexture(
        IClyde clyde,
        int width,
        int height,
        ReadOnlySpan<byte> rgbaPixels,
        string debugName)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Frame dimensions must be positive.");

        var expectedLength = width * height * 4;
        if (rgbaPixels.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"RGBA frame pixel buffer size mismatch: expected {expectedLength}, got {rgbaPixels.Length}.");
        }

        using var frameImage = new Image<Rgba32>(width, height);
        var pixelIndex = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                frameImage[x, y] = new Rgba32(
                    rgbaPixels[pixelIndex],
                    rgbaPixels[pixelIndex + 1],
                    rgbaPixels[pixelIndex + 2],
                    rgbaPixels[pixelIndex + 3]);
                pixelIndex += 4;
            }
        }

        return clyde.LoadTextureFromImage(frameImage, debugName);
    }
}
