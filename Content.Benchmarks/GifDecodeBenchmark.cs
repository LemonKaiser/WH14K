using System;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Content.Client.Resources.Gif;
using Robust.Shared.Analyzers;

namespace Content.Benchmarks;

[SimpleJob]
[MemoryDiagnoser]
[Virtual]
public class GifDecodeBenchmark
{
    private byte[][] _gifData = default!;
    private int _sink;

    [GlobalSetup]
    public void Setup()
    {
        var animatedDir = ResolveAnimatedBackgroundDirectory();
        _gifData = Directory
            .EnumerateFiles(animatedDir, "*.gif", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(File.ReadAllBytes)
            .ToArray();

        if (_gifData.Length == 0)
            throw new DirectoryNotFoundException("Could not locate any GIF files in Resources/Textures/LobbyScreens/Animated.");
    }

    [Benchmark]
    public void DecodeAllAnimatedBackgroundsRaw()
    {
        var checksum = 0;

        for (var i = 0; i < _gifData.Length; i++)
        {
            var decoded = GifDecoder.Decode(_gifData[i]);
            checksum ^= decoded.Width;
            checksum ^= decoded.Height;
            checksum ^= decoded.Frames.Length;
        }

        _sink = checksum;
    }

    [Benchmark]
    public void DecodeAllAnimatedBackgroundsFirstFrameOnly()
    {
        var checksum = 0;

        for (var i = 0; i < _gifData.Length; i++)
        {
            var decoded = GifDecoder.DecodeFirstFrame(_gifData[i]);
            checksum ^= decoded.Width;
            checksum ^= decoded.Height;
            checksum ^= decoded.Frames.Length;
        }

        _sink = checksum;
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
