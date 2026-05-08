using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.StrategicPoints;

[TestFixture]
public sealed class WH40KStrategicPointAssetTests
{
    [TestCaseSource(nameof(RsiSpecs))]
    public void StrategicPointRsiMetadataMatchesSourceFrames(
        string relativePath,
        int frameWidth,
        int frameHeight,
        int frameCount)
    {
        var rsiPath = Path.Combine(
            FindRepoRoot(),
            "Resources",
            "Textures",
            "_WH40K",
            "StrategicPoints",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var metaPath = Path.Combine(rsiPath, "meta.json");
        var pngPath = Path.Combine(rsiPath, "base.png");

        Assert.That(metaPath, Does.Exist, $"Missing RSI metadata for {relativePath}.");
        Assert.That(pngPath, Does.Exist, $"Missing RSI base image for {relativePath}.");

        using var document = JsonDocument.Parse(File.ReadAllText(metaPath));
        var root = document.RootElement;
        var size = root.GetProperty("size");
        var metaWidth = size.GetProperty("x").GetInt32();
        var metaHeight = size.GetProperty("y").GetInt32();
        var baseState = root
            .GetProperty("states")
            .EnumerateArray()
            .Single(state => state.GetProperty("name").GetString() == "base");

        var (imageWidth, imageHeight) = ReadPngDimensions(pngPath);
        Assert.Multiple(() =>
        {
            Assert.That(metaWidth, Is.EqualTo(frameWidth), $"{relativePath} has wrong RSI frame width.");
            Assert.That(metaHeight, Is.EqualTo(frameHeight), $"{relativePath} has wrong RSI frame height.");
            Assert.That(imageWidth % metaWidth, Is.Zero, $"{relativePath} PNG width must be divisible by frame width.");
            Assert.That(imageHeight % metaHeight, Is.Zero, $"{relativePath} PNG height must be divisible by frame height.");
            Assert.That(imageWidth / metaWidth * (imageHeight / metaHeight), Is.EqualTo(frameCount), $"{relativePath} frame count mismatch.");
        });

        if (frameCount <= 1)
            return;

        Assert.That(baseState.TryGetProperty("delays", out var delays), Is.True, $"{relativePath} is animated but has no delays.");
        var delayCount = delays.EnumerateArray().SelectMany(direction => direction.EnumerateArray()).Count();
        Assert.That(delayCount, Is.EqualTo(frameCount), $"{relativePath} delay count must match animated frame count.");
    }

    [Test]
    public void StrategicPointVisualizerUsesRsiAssetsInsteadOfRawPngSheets()
    {
        var visualizerPath = Path.Combine(
            FindRepoRoot(),
            "Content.Client",
            "_WH40K",
            "StrategicPoints",
            "WH40KStrategicPointVisualizerSystem.cs");
        var source = File.ReadAllText(visualizerPath);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("LayerSetRsi"));
            Assert.That(source, Does.Contain("SetOffset"));
            Assert.That(source, Does.Not.Contain("LayerSetTexture"));
            Assert.That(source, Does.Not.Contain(".png"));
            Assert.That(source, Does.Contain("resource/{team}/t{tierValue}.rsi"));
            Assert.That(source, Does.Contain("research/t{tierValue}/{team}.rsi"));
            Assert.That(source, Does.Contain("flag/{team}/t{tierValue}.rsi"));
            Assert.That(source, Does.Contain("1.25f"));
            Assert.That(source, Does.Contain("1.0f"));
        });
    }

    private static IEnumerable<TestCaseData> RsiSpecs()
    {
        yield return Spec("resource/t0_pit.rsi", 32, 32, 1);
        yield return Spec("research/noktolit_chaos.rsi", 64, 64, 1);
        yield return Spec("flag/t0.rsi", 32, 32, 1);

        foreach (var team in new[] { "imperium", "chaos" })
        {
            yield return Spec($"resource/{team}/t1.rsi", 32, 32, 2);
            yield return Spec($"resource/{team}/t2.rsi", 64, 64, 2);
            yield return Spec($"resource/{team}/t3.rsi", 64, 64, 2);

            yield return Spec($"research/t1/{team}.rsi", 32, 64, 2);
            yield return Spec($"research/t2/{team}.rsi", 32, 80, 2);
            yield return Spec($"research/t3/{team}.rsi", 32, 112, 2);

            yield return Spec($"flag/{team}/t1.rsi", 32, 64, 1);
            yield return Spec($"flag/{team}/t2.rsi", 64, 96, 1);
            yield return Spec($"flag/{team}/t3.rsi", 64, 96, 1);
        }
    }

    private static TestCaseData Spec(string relativePath, int frameWidth, int frameHeight, int frameCount)
    {
        return new TestCaseData(relativePath, frameWidth, frameHeight, frameCount)
            .SetName($"RSI {relativePath} is {frameWidth}x{frameHeight} frames x{frameCount}");
    }

    private static (int Width, int Height) ReadPngDimensions(string pngPath)
    {
        var bytes = File.ReadAllBytes(pngPath);

        Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(24), $"{pngPath} is too small to be a PNG.");
        Assert.Multiple(() =>
        {
            Assert.That(bytes[0], Is.EqualTo(0x89));
            Assert.That(bytes[1], Is.EqualTo((byte) 'P'));
            Assert.That(bytes[2], Is.EqualTo((byte) 'N'));
            Assert.That(bytes[3], Is.EqualTo((byte) 'G'));
        });

        return (
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Resources")) &&
                File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Could not locate WH14K repository root.");
        return string.Empty;
    }
}
