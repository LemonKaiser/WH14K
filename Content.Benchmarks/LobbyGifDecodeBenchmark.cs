using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using Content.Client.Lobby;
using Robust.Shared.Analyzers;

namespace Content.Benchmarks;

[SimpleJob]
[MemoryDiagnoser]
[Virtual]
public class LobbyGifDecodeBenchmark
{
    private static readonly string[] GifFiles =
    {
        "wp1.gif",
        "wp2.gif",
        "wp3.gif",
        "wp4.gif",
        "wp5.gif",
        "wp6.gif"
    };

    private byte[][] _gifData = default!;
    private int _sink;

    [GlobalSetup]
    public void Setup()
    {
        var animatedDir = ResolveAnimatedBackgroundDirectory();
        _gifData = new byte[GifFiles.Length][];

        for (var i = 0; i < GifFiles.Length; i++)
        {
            var path = Path.Combine(animatedDir, GifFiles[i]);
            _gifData[i] = File.ReadAllBytes(path);
        }
    }

    [Benchmark]
    public void DecodeAllAnimatedBackgroundsRaw()
    {
        var checksum = 0;

        for (var i = 0; i < _gifData.Length; i++)
        {
            var decoded = LobbyGifTextureLoader.DecodeGif(_gifData[i]);
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
            var decoded = LobbyGifTextureLoader.DecodeGifFirstFrame(_gifData[i]);
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
