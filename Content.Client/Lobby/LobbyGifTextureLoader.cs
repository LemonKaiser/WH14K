using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Robust.Client.Graphics;

namespace Content.Client.Lobby;

/// <summary>
/// Content-side GIF decode and upload helpers for animated lobby backgrounds.
/// Decode runs on worker threads, texture upload must run on the main thread.
/// </summary>
internal static class LobbyGifTextureLoader
{
    private const int GifMaxCodeSize = 4096;
    private const int GifFrameSafetyLimit = 10000;
    private const float DefaultGifFrameDelay = 0.1f;
    private const float MinGifFrameDelay = 0.01f;

    private const byte GifExtension = 0x21;
    private const byte GifImageDescriptor = 0x2C;
    private const byte GifTrailer = 0x3B;
    private const byte GifGraphicControlExtension = 0xF9;

    public readonly record struct DecodedAnimation(int Width, int Height, DecodedFrame[] Frames);
    public readonly record struct DecodedFrame(byte[] Pixels, float DelaySeconds);

    public static DecodedAnimation DecodeGif(
        ReadOnlyMemory<byte> gifData,
        CancellationToken cancellationToken = default)
    {
        if (gifData.Length == 0)
            return new DecodedAnimation(0, 0, Array.Empty<DecodedFrame>());

        using var stream = new MemoryStream(gifData.ToArray(), writable: false);
        var decoded = DecodeGif(stream, GifFrameSafetyLimit, stopAtFrameLimit: false, cancellationToken);

        if (decoded.Frames.Count == 0)
            return new DecodedAnimation(0, 0, Array.Empty<DecodedFrame>());

        var delayedFrames = BuildDelayedFrames(decoded.Frames, DefaultGifFrameDelay, MinGifFrameDelay);
        return new DecodedAnimation(decoded.Width, decoded.Height, delayedFrames.ToArray());
    }

    public static DecodedAnimation DecodeGifFirstFrame(
        ReadOnlyMemory<byte> gifData,
        CancellationToken cancellationToken = default)
    {
        if (gifData.Length == 0)
            return new DecodedAnimation(0, 0, Array.Empty<DecodedFrame>());

        using var stream = new MemoryStream(gifData.ToArray(), writable: false);
        var decoded = DecodeGif(stream, maxFrameCount: 1, stopAtFrameLimit: true, cancellationToken);

        if (decoded.Frames.Count == 0)
            return new DecodedAnimation(0, 0, Array.Empty<DecodedFrame>());

        var delayedFrames = BuildDelayedFrames(decoded.Frames, DefaultGifFrameDelay, MinGifFrameDelay);
        return new DecodedAnimation(decoded.Width, decoded.Height, delayedFrames.ToArray());
    }

    public static Texture UploadTextureFrame(
        IClyde clyde,
        int width,
        int height,
        byte[] rgbaPixels,
        string debugName)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Frame dimensions must be positive.");

        var expectedLength = width * height * 4;
        if (rgbaPixels.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"GIF frame pixel buffer size mismatch: expected {expectedLength}, got {rgbaPixels.Length}.");
        }

        using var frameImage = new Image<Rgba32>(width, height);
        var byteIndex = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                frameImage[x, y] = new Rgba32(
                    rgbaPixels[byteIndex + 0],
                    rgbaPixels[byteIndex + 1],
                    rgbaPixels[byteIndex + 2],
                    rgbaPixels[byteIndex + 3]);
                byteIndex += 4;
            }
        }

        return clyde.LoadTextureFromImage(frameImage, debugName);
    }

    private static List<DecodedFrame> BuildDelayedFrames(
        IReadOnlyList<RawDecodedFrame> rawFrames,
        float defaultDelay,
        float minDelay)
    {
        var result = new List<DecodedFrame>(rawFrames.Count);

        foreach (var frame in rawFrames)
        {
            var delay = frame.DelayCentiseconds > 0
                ? frame.DelayCentiseconds / 100f
                : defaultDelay;

            result.Add(new DecodedFrame(frame.Pixels, MathF.Max(delay, minDelay)));
        }

        return result;
    }

    private static RawDecodedGif DecodeGif(
        Stream stream,
        int maxFrameCount,
        bool stopAtFrameLimit,
        CancellationToken cancellationToken)
    {
        if (maxFrameCount <= 0)
            return new RawDecodedGif(0, 0, new List<RawDecodedFrame>());

        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        var signature = reader.ReadBytes(6);
        if (signature.Length != 6
            || signature[0] != (byte) 'G'
            || signature[1] != (byte) 'I'
            || signature[2] != (byte) 'F')
        {
            throw new InvalidDataException("Invalid GIF signature.");
        }

        var screenWidth = reader.ReadUInt16();
        var screenHeight = reader.ReadUInt16();
        if (screenWidth <= 0 || screenHeight <= 0)
            throw new InvalidDataException("Invalid GIF logical screen size.");

        var packed = reader.ReadByte();
        var hasGlobalColorTable = (packed & 0x80) != 0;
        var globalColorTableSize = 1 << ((packed & 0x07) + 1);

        _ = reader.ReadByte(); // background color index
        _ = reader.ReadByte(); // pixel aspect ratio

        Rgba32[]? globalColorTable = null;
        if (hasGlobalColorTable)
            globalColorTable = ReadColorTable(reader, globalColorTableSize);

        var canvas = new byte[screenWidth * screenHeight * 4];
        var frames = new List<RawDecodedFrame>();

        var gce = GraphicControlExtension.Default;
        PreviousFrameState? previousFrame = null;

        while (TryReadByte(reader, out var blockId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (blockId)
            {
                case GifExtension:
                    if (!TryReadByte(reader, out var extensionLabel))
                        throw new InvalidDataException("Unexpected EOF while reading GIF extension.");

                    if (extensionLabel == GifGraphicControlExtension)
                        gce = ReadGraphicControlExtension(reader);
                    else
                        SkipSubBlocks(reader);
                    break;

                case GifImageDescriptor:
                {
                    if (frames.Count >= maxFrameCount)
                    {
                        if (stopAtFrameLimit)
                            return new RawDecodedGif(screenWidth, screenHeight, frames);

                        throw new InvalidDataException(
                            $"GIF contains too many frames ({frames.Count + 1}). Limit is {maxFrameCount}.");
                    }

                    ApplyDisposal(canvas, screenWidth, screenHeight, previousFrame);

                    var left = reader.ReadUInt16();
                    var top = reader.ReadUInt16();
                    var width = reader.ReadUInt16();
                    var height = reader.ReadUInt16();

                    var imagePacked = reader.ReadByte();
                    var hasLocalColorTable = (imagePacked & 0x80) != 0;
                    var interlaced = (imagePacked & 0x40) != 0;
                    var localColorTableSize = 1 << ((imagePacked & 0x07) + 1);

                    var colorTable = hasLocalColorTable
                        ? ReadColorTable(reader, localColorTableSize)
                        : globalColorTable;

                    if (colorTable == null)
                        throw new InvalidDataException("GIF frame has no color table.");

                    var lzwMinCodeSize = reader.ReadByte();
                    var compressedData = ReadSubBlocks(reader);
                    var expectedPixels = width * height;
                    var colorIndices = DecodeLzw(compressedData, lzwMinCodeSize, expectedPixels);

                    byte[]? restoreSnapshot = null;
                    if (gce.DisposalMethod == 3)
                    {
                        restoreSnapshot = new byte[canvas.Length];
                        Array.Copy(canvas, restoreSnapshot, canvas.Length);
                    }

                    DrawFrame(
                        canvas,
                        screenWidth,
                        screenHeight,
                        left,
                        top,
                        width,
                        height,
                        interlaced,
                        colorIndices,
                        colorTable,
                        gce.TransparentColorFlag,
                        gce.TransparentColorIndex);

                    var framePixels = new byte[canvas.Length];
                    Array.Copy(canvas, framePixels, canvas.Length);
                    frames.Add(new RawDecodedFrame(framePixels, gce.DelayCentiseconds));

                    previousFrame = new PreviousFrameState(
                        left,
                        top,
                        width,
                        height,
                        gce.DisposalMethod,
                        restoreSnapshot);

                    // GCE applies only to the next image descriptor.
                    gce = GraphicControlExtension.Default;
                    break;
                }

                case GifTrailer:
                    return new RawDecodedGif(screenWidth, screenHeight, frames);

                default:
                    throw new InvalidDataException($"Unexpected GIF block id 0x{blockId:X2}.");
            }
        }

        return new RawDecodedGif(screenWidth, screenHeight, frames);
    }

    private static void DrawFrame(
        byte[] canvas,
        int screenWidth,
        int screenHeight,
        int left,
        int top,
        int frameWidth,
        int frameHeight,
        bool interlaced,
        byte[] colorIndices,
        Rgba32[] colorTable,
        bool hasTransparency,
        byte transparentIndex)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
            return;

        var rowMap = interlaced ? BuildInterlacedRowMap(frameHeight) : null;

        for (var dataRow = 0; dataRow < frameHeight; dataRow++)
        {
            var frameRow = rowMap == null ? dataRow : rowMap[dataRow];
            var screenY = top + frameRow;
            if (screenY < 0 || screenY >= screenHeight)
                continue;

            var rowOffset = dataRow * frameWidth;
            for (var x = 0; x < frameWidth; x++)
            {
                var screenX = left + x;
                if (screenX < 0 || screenX >= screenWidth)
                    continue;

                var colorIndex = colorIndices[rowOffset + x];
                if (hasTransparency && colorIndex == transparentIndex)
                    continue;

                if (colorIndex >= colorTable.Length)
                    continue;

                var color = colorTable[colorIndex];
                var dst = ((screenY * screenWidth) + screenX) * 4;
                canvas[dst + 0] = color.R;
                canvas[dst + 1] = color.G;
                canvas[dst + 2] = color.B;
                canvas[dst + 3] = 255;
            }
        }
    }

    private static void ApplyDisposal(
        byte[] canvas,
        int screenWidth,
        int screenHeight,
        PreviousFrameState? previous)
    {
        if (previous == null)
            return;

        switch (previous.Value.DisposalMethod)
        {
            case 2: // Restore to background color (we treat background as transparent for lobby usage)
                ClearRect(
                    canvas,
                    screenWidth,
                    screenHeight,
                    previous.Value.Left,
                    previous.Value.Top,
                    previous.Value.Width,
                    previous.Value.Height);
                break;
            case 3: // Restore to previous
                if (previous.Value.RestoreSnapshot != null)
                {
                    var copyLength = Math.Min(previous.Value.RestoreSnapshot.Length, canvas.Length);
                    Array.Copy(previous.Value.RestoreSnapshot, canvas, copyLength);
                }

                break;
        }
    }

    private static void ClearRect(
        byte[] canvas,
        int screenWidth,
        int screenHeight,
        int left,
        int top,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0)
            return;

        var startX = Math.Max(left, 0);
        var startY = Math.Max(top, 0);
        var endX = Math.Min(left + width, screenWidth);
        var endY = Math.Min(top + height, screenHeight);

        for (var y = startY; y < endY; y++)
        {
            var rowStart = ((y * screenWidth) + startX) * 4;
            var rowLength = (endX - startX) * 4;
            Array.Clear(canvas, rowStart, rowLength);
        }
    }

    private static int[] BuildInterlacedRowMap(int height)
    {
        var rows = new int[height];
        var index = 0;

        for (var y = 0; y < height; y += 8)
            rows[index++] = y;
        for (var y = 4; y < height; y += 8)
            rows[index++] = y;
        for (var y = 2; y < height; y += 4)
            rows[index++] = y;
        for (var y = 1; y < height; y += 2)
            rows[index++] = y;

        return rows;
    }

    private static Rgba32[] ReadColorTable(BinaryReader reader, int size)
    {
        var table = new Rgba32[size];
        for (var i = 0; i < size; i++)
        {
            var r = reader.ReadByte();
            var g = reader.ReadByte();
            var b = reader.ReadByte();
            table[i] = new Rgba32(r, g, b, 255);
        }

        return table;
    }

    private static byte[] ReadSubBlocks(BinaryReader reader)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            var blockSize = reader.ReadByte();
            if (blockSize == 0)
                break;

            var data = reader.ReadBytes(blockSize);
            if (data.Length != blockSize)
                throw new InvalidDataException("Unexpected EOF in GIF sub-block.");

            ms.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    private static void SkipSubBlocks(BinaryReader reader)
    {
        while (true)
        {
            var blockSize = reader.ReadByte();
            if (blockSize == 0)
                break;

            var skipped = reader.ReadBytes(blockSize);
            if (skipped.Length != blockSize)
                throw new InvalidDataException("Unexpected EOF while skipping GIF sub-block.");
        }
    }

    private static GraphicControlExtension ReadGraphicControlExtension(BinaryReader reader)
    {
        var blockSize = reader.ReadByte();
        if (blockSize != 4)
        {
            // Non-standard block, skip and ignore.
            _ = reader.ReadBytes(blockSize);
            _ = reader.ReadByte(); // terminator
            return GraphicControlExtension.Default;
        }

        var packed = reader.ReadByte();
        var delay = reader.ReadUInt16();
        var transparentIndex = reader.ReadByte();
        _ = reader.ReadByte(); // terminator

        var disposal = (packed >> 2) & 0x7;
        var transparent = (packed & 0x1) != 0;

        return new GraphicControlExtension(
            delay,
            (byte) disposal,
            transparent,
            transparentIndex);
    }

    private static byte[] DecodeLzw(byte[] data, int minCodeSize, int expectedPixelCount)
    {
        if (expectedPixelCount <= 0)
            return Array.Empty<byte>();

        if (minCodeSize <= 0 || minCodeSize > 8)
            throw new InvalidDataException($"Unsupported GIF LZW minimum code size: {minCodeSize}");

        var clearCode = 1 << minCodeSize;
        var endCode = clearCode + 1;
        var nextCode = clearCode + 2;
        var codeSize = minCodeSize + 1;
        var codeMask = (1 << codeSize) - 1;

        var prefix = new short[GifMaxCodeSize];
        var suffix = new byte[GifMaxCodeSize];
        var pixelStack = new byte[GifMaxCodeSize + 1];

        for (var i = 0; i < clearCode; i++)
            suffix[i] = (byte) i;

        var output = new byte[expectedPixelCount];
        var outIndex = 0;
        var dataIndex = 0;
        var datum = 0;
        var bits = 0;

        var oldCode = -1;
        var first = 0;
        var stackTop = 0;

        while (outIndex < expectedPixelCount)
        {
            while (bits < codeSize)
            {
                if (dataIndex >= data.Length)
                    return output;

                datum |= data[dataIndex++] << bits;
                bits += 8;
            }

            var code = datum & codeMask;
            datum >>= codeSize;
            bits -= codeSize;

            if (code == clearCode)
            {
                codeSize = minCodeSize + 1;
                codeMask = (1 << codeSize) - 1;
                nextCode = clearCode + 2;
                oldCode = -1;
                continue;
            }

            if (code == endCode)
                break;

            if (code >= GifMaxCodeSize)
                break;

            if (oldCode == -1)
            {
                output[outIndex++] = suffix[code];
                first = suffix[code];
                oldCode = code;
                continue;
            }

            var inCode = code;
            if (code >= nextCode)
            {
                pixelStack[stackTop++] = (byte) first;
                code = oldCode;
            }

            while (code >= clearCode)
            {
                if (code >= GifMaxCodeSize)
                    return output;

                pixelStack[stackTop++] = suffix[code];
                code = prefix[code];
            }

            first = suffix[code];
            pixelStack[stackTop++] = (byte) first;

            while (stackTop > 0 && outIndex < expectedPixelCount)
            {
                output[outIndex++] = pixelStack[--stackTop];
            }

            if (nextCode < GifMaxCodeSize)
            {
                prefix[nextCode] = (short) oldCode;
                suffix[nextCode] = (byte) first;
                nextCode++;

                if (nextCode == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                    codeMask = (1 << codeSize) - 1;
                }
            }

            oldCode = inCode;
        }

        return output;
    }

    private static bool TryReadByte(BinaryReader reader, out byte value)
    {
        if (reader.BaseStream.Position >= reader.BaseStream.Length)
        {
            value = default;
            return false;
        }

        value = reader.ReadByte();
        return true;
    }

    private readonly record struct RawDecodedGif(int Width, int Height, List<RawDecodedFrame> Frames);
    private readonly record struct RawDecodedFrame(byte[] Pixels, int DelayCentiseconds);

    private readonly record struct PreviousFrameState(
        int Left,
        int Top,
        int Width,
        int Height,
        byte DisposalMethod,
        byte[]? RestoreSnapshot);

    private readonly record struct GraphicControlExtension(
        ushort DelayCentiseconds,
        byte DisposalMethod,
        bool TransparentColorFlag,
        byte TransparentColorIndex)
    {
        public static GraphicControlExtension Default => new(
            DelayCentiseconds: 0,
            DisposalMethod: 0,
            TransparentColorFlag: false,
            TransparentColorIndex: 0);
    }
}
