using System;
using System.IO;
using System.Linq;
using System.Threading;
using Content.Client.Resources.Gif;
using NUnit.Framework;

namespace Content.Tests.Client.Resources.Gif;

[TestFixture]
public sealed class GifDecoderTests
{
    [Test]
    public void DecodeEmptyInputReturnsEmptyAnimation()
    {
        var decoded = GifDecoder.Decode(ReadOnlyMemory<byte>.Empty);
        var firstFrameOnly = GifDecoder.DecodeFirstFrame(ReadOnlyMemory<byte>.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.Width, Is.EqualTo(0));
            Assert.That(decoded.Height, Is.EqualTo(0));
            Assert.That(decoded.Frames, Is.Empty);

            Assert.That(firstFrameOnly.Width, Is.EqualTo(0));
            Assert.That(firstFrameOnly.Height, Is.EqualTo(0));
            Assert.That(firstFrameOnly.Frames, Is.Empty);
        });
    }

    [Test]
    public void DecodeAnimatedLobbySamplesSuccessfully()
    {
        var gifFiles = EnumerateAnimatedLobbyGifFiles();

        foreach (var gifFile in gifFiles)
        {
            var gifData = File.ReadAllBytes(gifFile);
            var decoded = GifDecoder.Decode(gifData);
            var expectedPixelLength = decoded.Width * decoded.Height * 4;

            Assert.Multiple(() =>
            {
                Assert.That(decoded.Width, Is.GreaterThan(0), gifFile);
                Assert.That(decoded.Height, Is.GreaterThan(0), gifFile);
                Assert.That(decoded.Frames.Length, Is.GreaterThan(0), gifFile);
            });

            foreach (var frame in decoded.Frames)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(frame.Pixels.Length, Is.EqualTo(expectedPixelLength), gifFile);
                    Assert.That(frame.DelaySeconds, Is.GreaterThan(0f), gifFile);
                });
            }
        }
    }

    [Test]
    public void DecodeFirstFrameMatchesFullDecodeFirstFrameForAnimatedLobbySamples()
    {
        var gifFiles = EnumerateAnimatedLobbyGifFiles();

        foreach (var gifFile in gifFiles)
        {
            var gifData = File.ReadAllBytes(gifFile);
            var firstFrameOnly = GifDecoder.DecodeFirstFrame(gifData);
            var fullDecode = GifDecoder.Decode(gifData);

            Assert.Multiple(() =>
            {
                Assert.That(firstFrameOnly.Width, Is.EqualTo(fullDecode.Width), gifFile);
                Assert.That(firstFrameOnly.Height, Is.EqualTo(fullDecode.Height), gifFile);
                Assert.That(firstFrameOnly.Frames.Length, Is.EqualTo(1), gifFile);
                Assert.That(fullDecode.Frames.Length, Is.GreaterThan(0), gifFile);
                Assert.That(firstFrameOnly.Frames[0].DelaySeconds, Is.EqualTo(fullDecode.Frames[0].DelaySeconds), gifFile);
                Assert.That(firstFrameOnly.Frames[0].Pixels, Is.EqualTo(fullDecode.Frames[0].Pixels), gifFile);
            });
        }
    }

    [Test]
    public void DecodeStreamMatchesMemoryDecodeForAnimatedLobbySamples()
    {
        var gifFiles = EnumerateAnimatedLobbyGifFiles();

        foreach (var gifFile in gifFiles)
        {
            var gifData = File.ReadAllBytes(gifFile);
            using var stream = new MemoryStream(gifData, writable: false);

            var fromMemory = GifDecoder.Decode(gifData);
            var fromStream = GifDecoder.Decode(stream, GifDecoder.DecodeOptions.Default);

            Assert.Multiple(() =>
            {
                Assert.That(fromStream.Width, Is.EqualTo(fromMemory.Width), gifFile);
                Assert.That(fromStream.Height, Is.EqualTo(fromMemory.Height), gifFile);
                Assert.That(fromStream.Frames.Length, Is.EqualTo(fromMemory.Frames.Length), gifFile);
                Assert.That(fromStream.Frames[0].DelaySeconds, Is.EqualTo(fromMemory.Frames[0].DelaySeconds), gifFile);
                Assert.That(fromStream.Frames[0].Pixels, Is.EqualTo(fromMemory.Frames[0].Pixels), gifFile);
            });
        }
    }

    private static string[] EnumerateAnimatedLobbyGifFiles()
    {
        var animatedDirectory = ResolveAnimatedBackgroundDirectory();
        var gifFiles = Directory
            .EnumerateFiles(animatedDirectory, "*.gif", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (gifFiles.Length == 0)
            throw new DirectoryNotFoundException("Could not locate any GIF files in Resources/Textures/LobbyScreens/Animated.");

        return gifFiles;
    }

    [Test]
    public void DecodeInvalidSignatureThrows()
    {
        var garbage = new byte[] { 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        Assert.Throws<InvalidDataException>(() => GifDecoder.Decode(garbage));
    }

    [Test]
    public void DecodeTruncatedHeaderThrows()
    {
        var truncated = new byte[] { (byte)'G', (byte)'I', (byte)'F' };
        Assert.That(() => GifDecoder.Decode(truncated),
            Throws.InstanceOf<InvalidDataException>().Or.InstanceOf<EndOfStreamException>());
    }

    [Test]
    public void DecodeWithCancellationThrows()
    {
        var gifFiles = EnumerateAnimatedLobbyGifFiles();
        if (gifFiles.Length == 0)
            return;

        var gifData = File.ReadAllBytes(gifFiles[0]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => GifDecoder.Decode(gifData, cts.Token));
    }

    [Test]
    public void DecodeMaxFrameCountLimits()
    {
        var gifFiles = EnumerateAnimatedLobbyGifFiles();
        if (gifFiles.Length == 0)
            return;

        var gifData = File.ReadAllBytes(gifFiles[0]);

        var options = new GifDecoder.DecodeOptions(
            MaxFrameCount: 2,
            StopAtFrameLimit: true,
            DefaultFrameDelaySeconds: 0.1f,
            MinFrameDelaySeconds: 0.01f);

        var decoded = GifDecoder.Decode(new ReadOnlyMemory<byte>(gifData), options);

        Assert.That(decoded.Frames.Length, Is.LessThanOrEqualTo(2));
        Assert.That(decoded.Frames.Length, Is.GreaterThan(0));
    }

    [Test]
    public void DecodeMaxFrameCountThrowsWhenNotStopping()
    {
        var gifFiles = EnumerateAnimatedLobbyGifFiles();
        if (gifFiles.Length == 0)
            return;

        var gifData = File.ReadAllBytes(gifFiles[0]);
        var fullDecode = GifDecoder.Decode(gifData);
        if (fullDecode.Frames.Length <= 1)
            return;

        var options = new GifDecoder.DecodeOptions(
            MaxFrameCount: 1,
            StopAtFrameLimit: false,
            DefaultFrameDelaySeconds: 0.1f,
            MinFrameDelaySeconds: 0.01f);

        Assert.Throws<InvalidDataException>(() =>
            GifDecoder.Decode(new ReadOnlyMemory<byte>(gifData), options));
    }

    [Test]
    public void DecodeMinDelayClampingWorks()
    {
        var gifFiles = EnumerateAnimatedLobbyGifFiles();
        if (gifFiles.Length == 0)
            return;

        var gifData = File.ReadAllBytes(gifFiles[0]);
        var decoded = GifDecoder.Decode(gifData);

        foreach (var frame in decoded.Frames)
        {
            Assert.That(frame.DelaySeconds, Is.GreaterThanOrEqualTo(0.01f));
        }
    }

    [Test]
    public void DecodeAllFramePixelsHaveCorrectLength()
    {
        var gifFiles = EnumerateAnimatedLobbyGifFiles();

        foreach (var gifFile in gifFiles)
        {
            var gifData = File.ReadAllBytes(gifFile);
            var decoded = GifDecoder.Decode(gifData);
            var expectedLength = decoded.Width * decoded.Height * 4;

            for (var i = 0; i < decoded.Frames.Length; i++)
            {
                Assert.That(decoded.Frames[i].Pixels.Length, Is.EqualTo(expectedLength),
                    $"{gifFile} frame {i}");
            }
        }
    }

    private static string ResolveAnimatedBackgroundDirectory()
    {
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);

        while (cursor != null)
        {
            var candidate = Path.Combine(
                cursor.FullName,
                "Resources",
                "Textures",
                "LobbyScreens",
                "Animated");

            if (Directory.Exists(candidate))
                return candidate;

            cursor = cursor.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Resources/Textures/LobbyScreens/Animated.");
    }
}
