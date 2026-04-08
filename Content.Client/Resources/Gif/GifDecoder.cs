using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client.Resources.Gif;

/// <summary>
/// Decodes GIF image streams into full RGBA frames and per-frame delays.
/// The decoder is client-side content code, but is not tied to any particular UI.
/// </summary>
public static class GifDecoder
{
    private const int GifMaxCodeSize = 4096;
    private const int DefaultFrameSafetyLimit = 10000;
    private const float DefaultGifFrameDelay = 0.1f;
    private const float MinGifFrameDelay = 0.01f;

    private const byte GifExtension = 0x21;
    private const byte GifImageDescriptor = 0x2C;
    private const byte GifTrailer = 0x3B;
    private const byte GifGraphicControlExtension = 0xF9;

    public readonly record struct DecodedAnimation(int Width, int Height, DecodedFrame[] Frames);
    public readonly record struct DecodedFrame(byte[] Pixels, float DelaySeconds);

    public readonly record struct DecodeOptions(
        int MaxFrameCount,
        bool StopAtFrameLimit,
        float DefaultFrameDelaySeconds,
        float MinFrameDelaySeconds)
    {
        public static DecodeOptions Default => new(
            DefaultFrameSafetyLimit,
            StopAtFrameLimit: false,
            DefaultGifFrameDelay,
            MinGifFrameDelay);

        public static DecodeOptions FirstFrameOnly => new(
            MaxFrameCount: 1,
            StopAtFrameLimit: true,
            DefaultGifFrameDelay,
            MinGifFrameDelay);
    }

    public static DecodedAnimation Decode(
        ReadOnlyMemory<byte> gifData,
        CancellationToken cancellationToken = default)
    {
        return Decode(gifData, DecodeOptions.Default, cancellationToken);
    }

    public static DecodedAnimation Decode(
        ReadOnlyMemory<byte> gifData,
        DecodeOptions options,
        CancellationToken cancellationToken = default)
    {
        if (gifData.Length == 0)
            return new DecodedAnimation(0, 0, Array.Empty<DecodedFrame>());

        using var stream = OpenReadOnlyStream(gifData);
        return Decode(stream, options, cancellationToken);
    }

    public static DecodedAnimation DecodeFirstFrame(
        ReadOnlyMemory<byte> gifData,
        CancellationToken cancellationToken = default)
    {
        return Decode(gifData, DecodeOptions.FirstFrameOnly, cancellationToken);
    }

    public static DecodedAnimation Decode(
        Stream stream,
        DecodeOptions options,
        CancellationToken cancellationToken = default)
    {
        var decoded = DecodeRaw(stream, options, cancellationToken);

        if (decoded.Frames.Count == 0)
            return new DecodedAnimation(0, 0, Array.Empty<DecodedFrame>());

        var delayedFrames = BuildDelayedFrames(
            decoded.Frames,
            options.DefaultFrameDelaySeconds,
            options.MinFrameDelaySeconds);

        return new DecodedAnimation(decoded.Width, decoded.Height, delayedFrames.ToArray());
    }

    public static DecodedAnimation DecodeFirstFrame(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        return Decode(stream, DecodeOptions.FirstFrameOnly, cancellationToken);
    }

    private static MemoryStream OpenReadOnlyStream(ReadOnlyMemory<byte> gifData)
    {
        return new MemoryStream(gifData.ToArray(), writable: false);
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

    private static RawDecodedGif DecodeRaw(
        Stream stream,
        DecodeOptions options,
        CancellationToken cancellationToken)
    {
        if (options.MaxFrameCount <= 0)
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

        _ = reader.ReadByte();
        _ = reader.ReadByte();

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
                    if (frames.Count >= options.MaxFrameCount)
                    {
                        if (options.StopAtFrameLimit)
                            return new RawDecodedGif(screenWidth, screenHeight, frames);

                        throw new InvalidDataException(
                            $"GIF contains too many frames ({frames.Count + 1}). Limit is {options.MaxFrameCount}.");
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
                    var colorIndices = DecodeLzw(compressedData, lzwMinCodeSize, expectedPixels, cancellationToken);

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
                        gce.TransparentColorIndex,
                        cancellationToken);

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
        byte transparentIndex,
        CancellationToken cancellationToken)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
            return;

        var rowMap = interlaced ? BuildInterlacedRowMap(frameHeight) : null;

        for (var dataRow = 0; dataRow < frameHeight; dataRow++)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
            case 2:
                ClearRect(
                    canvas,
                    screenWidth,
                    screenHeight,
                    previous.Value.Left,
                    previous.Value.Top,
                    previous.Value.Width,
                    previous.Value.Height);
                break;
            case 3:
                if (previous.Value.RestoreSnapshot != null)
                {
                    var copyLength = Math.Min(previous.Value.RestoreSnapshot.Length, canvas.Length);
                    Array.Copy(previous.Value.RestoreSnapshot, 0, canvas, 0, copyLength);
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
        var rowLength = (endX - startX) * 4;

        if (rowLength <= 0)
            return;

        var span = canvas.AsSpan();
        for (var y = startY; y < endY; y++)
        {
            var rowStart = ((y * screenWidth) + startX) * 4;
            span.Slice(rowStart, rowLength).Clear();
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
        var raw = reader.ReadBytes(size * 3);
        if (raw.Length != size * 3)
            throw new InvalidDataException("Unexpected EOF reading GIF color table.");

        for (var i = 0; i < size; i++)
        {
            var offset = i * 3;
            table[i] = new Rgba32(raw[offset], raw[offset + 1], raw[offset + 2], 255);
        }

        return table;
    }

    private static byte[] ReadSubBlocks(BinaryReader reader)
    {
        using var stream = new MemoryStream();
        while (true)
        {
            var blockSize = reader.ReadByte();
            if (blockSize == 0)
                break;

            var block = reader.ReadBytes(blockSize);
            if (block.Length != blockSize)
                throw new InvalidDataException("Unexpected EOF in GIF sub-block.");

            stream.Write(block, 0, block.Length);
        }

        return stream.ToArray();
    }

    private static void SkipSubBlocks(BinaryReader reader)
    {
        var stream = reader.BaseStream;

        while (true)
        {
            var blockSize = reader.ReadByte();
            if (blockSize == 0)
                break;

            if (stream.CanSeek)
            {
                var newPos = stream.Position + blockSize;
                if (newPos > stream.Length)
                    throw new InvalidDataException("Unexpected EOF while skipping GIF sub-block.");

                stream.Position = newPos;
            }
            else
            {
                var skipped = reader.ReadBytes(blockSize);
                if (skipped.Length != blockSize)
                    throw new InvalidDataException("Unexpected EOF while skipping GIF sub-block.");
            }
        }
    }

    private static GraphicControlExtension ReadGraphicControlExtension(BinaryReader reader)
    {
        var blockSize = reader.ReadByte();
        if (blockSize != 4)
        {
            _ = reader.ReadBytes(blockSize);
            _ = reader.ReadByte();
            return GraphicControlExtension.Default;
        }

        var packed = reader.ReadByte();
        var delay = reader.ReadUInt16();
        var transparentIndex = reader.ReadByte();
        _ = reader.ReadByte();

        var disposal = (packed >> 2) & 0x7;
        var transparent = (packed & 0x1) != 0;

        return new GraphicControlExtension(
            delay,
            (byte) disposal,
            transparent,
            transparentIndex);
    }

    private static byte[] DecodeLzw(
        byte[] data,
        int minCodeSize,
        int expectedPixelCount,
        CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();

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
                if (code >= GifMaxCodeSize || stackTop >= pixelStack.Length)
                    return output;

                pixelStack[stackTop++] = suffix[code];
                code = prefix[code];
            }

            first = suffix[code];
            pixelStack[stackTop++] = (byte) first;

            while (stackTop > 0 && outIndex < expectedPixelCount)
                output[outIndex++] = pixelStack[--stackTop];

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
