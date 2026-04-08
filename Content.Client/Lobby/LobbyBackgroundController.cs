using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Content.Client.Lobby.UI;
using Content.Client.Message;
using Content.Client.Resources;
using Content.Client.Resources.Gif;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Lobby;

/// <summary>
/// Handles lobby background selection, animation and GIF runtime pipeline.
/// Kept outside LobbyState to minimize core-state modifications.
/// </summary>
internal sealed class LobbyBackgroundController
{
    private readonly IConfigurationManager _cfg;
    private readonly IPrototypeManager _protoMan;
    private readonly IResourceCache _resourceCache;
    private readonly IClyde _clyde;
    private readonly IGameTiming _gameTiming;
    private readonly Func<string> _getServerBackgroundId;
    private readonly ISawmill _sawmill = Logger.GetSawmill("lobby");

    private LobbyGui? _lobby;
    private Texture[]? _backgroundFrames;
    private float[]? _backgroundFrameDelays;
    private int _backgroundFrameIndex;
    private float _backgroundFrameTimer;
    private ResPath? _activeStaticBackground;
    private LobbyGifCacheKey? _activeGifKey;
    private LobbyGifCacheKey? _activeGifPreviewKey;
    private LobbyGifCacheKey? _requestedGifKey;
    private int _gifRequestGeneration;

    private CancellationTokenSource? _gifDecodeCts;
    private Task<GifDecoder.DecodedAnimation>? _gifDecodeTask;
    private LobbyGifCacheKey? _gifDecodeKey;
    private int _gifDecodeGeneration;
    private CancellationTokenSource? _gifFirstFrameDecodeCts;
    private Task<GifDecoder.DecodedAnimation>? _gifFirstFrameDecodeTask;
    private LobbyGifCacheKey? _gifFirstFrameDecodeKey;
    private int _gifFirstFrameDecodeGeneration;
    private PendingGifUpload? _pendingGifUpload;

    private readonly Dictionary<LobbyGifCacheKey, CachedGifAnimation> _gifCache = new();
    private readonly LinkedList<LobbyGifCacheKey> _gifCacheLru = new();
    private readonly Dictionary<LobbyGifCacheKey, LinkedListNode<LobbyGifCacheKey>> _gifCacheLruNodes = new();
    private readonly List<PendingGifDisposal> _pendingGifDisposals = new();
    private readonly Dictionary<LobbyGifCacheKey, CachedGifFirstFrame> _firstFrameCache = new();
    private readonly LinkedList<LobbyGifCacheKey> _firstFrameCacheLru = new();
    private readonly Dictionary<LobbyGifCacheKey, LinkedListNode<LobbyGifCacheKey>> _firstFrameCacheLruNodes = new();

    private const float MinBackgroundFrameDelay = 0.01f;
    private const int BackgroundTextureDisposeDelayMs = 2000;
    private const int MaxDisposalDelayMs = 30000;
    private const int GifUploadFramesPerFrameUpdate = 2;
    private const int GifCacheCapacity = 2;
    private const int FirstFrameCacheCapacity = 8;

    private const string LobbyBackgroundModeServer = "server";
    private const string LobbyBackgroundModeStatic = "static";
    private const string LobbyBackgroundModeAnimated = "animated";

    private enum LobbyBackgroundLoadPreference : byte
    {
        ServerDefault,
        PreferStatic,
        PreferAnimated
    }

    private enum GifLoadResult : byte
    {
        Loaded,
        Pending,
        Failed
    }

    private readonly record struct LobbyGifCacheKey(ResPath Path);

    private sealed class CachedGifAnimation
    {
        public CachedGifAnimation(Texture[] frames, float[] delays)
        {
            Frames = frames;
            Delays = delays;
        }

        public Texture[] Frames { get; }
        public float[] Delays { get; }
    }

    private sealed class CachedGifFirstFrame
    {
        public CachedGifFirstFrame(Texture frame, float delay)
        {
            Frame = frame;
            Delay = delay;
        }

        public Texture Frame { get; }
        public float Delay { get; }
    }

    private sealed class PendingGifUpload
    {
        public PendingGifUpload(LobbyGifCacheKey key, GifDecoder.DecodedAnimation decoded, int generation)
        {
            Key = key;
            Generation = generation;
            Width = decoded.Width;
            Height = decoded.Height;
            DecodedFrames = decoded.Frames;
            UploadedFrames = new Texture?[decoded.Frames.Length];
            Delays = new float[decoded.Frames.Length];
            NextFrameIndex = 0;
        }

        public LobbyGifCacheKey Key { get; }
        public int Generation { get; }
        public int Width { get; }
        public int Height { get; }
        public GifDecoder.DecodedFrame[] DecodedFrames { get; }
        public Texture?[] UploadedFrames { get; }
        public float[] Delays { get; }
        public int NextFrameIndex { get; set; }
    }

    private sealed class PendingGifDisposal
    {
        public PendingGifDisposal(LobbyGifCacheKey key, Texture[] frames, TimeSpan disposeAt)
        {
            Key = key;
            Frames = frames;
            DisposeAt = disposeAt;
            InitialDisposeAt = disposeAt;
        }

        public LobbyGifCacheKey Key { get; }
        public Texture[] Frames { get; }
        public TimeSpan DisposeAt { get; set; }
        public TimeSpan InitialDisposeAt { get; }
    }

    public LobbyBackgroundController(
        IConfigurationManager cfg,
        IPrototypeManager protoMan,
        IResourceCache resourceCache,
        IClyde clyde,
        IGameTiming gameTiming,
        Func<string> getServerBackgroundId)
    {
        _cfg = cfg;
        _protoMan = protoMan;
        _resourceCache = resourceCache;
        _clyde = clyde;
        _gameTiming = gameTiming;
        _getServerBackgroundId = getServerBackgroundId;
    }

    public void Startup(LobbyGui lobby)
    {
        _lobby = lobby;
        DetachCurrentBackgroundTexture();
        _cfg.OnValueChanged(CCVars.LobbyBackgroundType, OnLobbyBackgroundConfigChanged, true);
        _cfg.OnValueChanged(CCVars.LobbyStaticBackground, OnLobbyBackgroundConfigChanged, true);
        _cfg.OnValueChanged(CCVars.LobbyAnimatedBackground, OnLobbyBackgroundConfigChanged, true);
        _cfg.OnValueChanged(CCVars.LobbyPanelOpacity, OnLobbyPanelOpacityChanged, true);
    }

    public void Shutdown()
    {
        _cfg.UnsubValueChanged(CCVars.LobbyBackgroundType, OnLobbyBackgroundConfigChanged);
        _cfg.UnsubValueChanged(CCVars.LobbyStaticBackground, OnLobbyBackgroundConfigChanged);
        _cfg.UnsubValueChanged(CCVars.LobbyAnimatedBackground, OnLobbyBackgroundConfigChanged);
        _cfg.UnsubValueChanged(CCVars.LobbyPanelOpacity, OnLobbyPanelOpacityChanged);
        DetachCurrentBackgroundTexture();
        ClearBackgroundAnimationState(resetCache: true);
        _lobby = null;
    }

    public void FrameUpdate(float deltaSeconds)
    {
        ProcessCompletedGifFirstFrameDecode();
        ProcessCompletedGifDecode();
        ProcessPendingGifUpload();
        ProcessPendingGifDisposals();
        UpdateLobbyBackgroundAnimation(deltaSeconds);
    }

    public void RefreshBackground()
    {
        UpdateLobbyBackground();
    }

    private void OnLobbyBackgroundConfigChanged(string _)
    {
        UpdateLobbyBackground();
    }

    private void OnLobbyPanelOpacityChanged(float opacity)
    {
        _lobby?.ApplyPanelBackgroundOpacity(opacity);
    }

    private void UpdateLobbyBackground()
    {
        if (_lobby == null)
            return;

        var preference = GetLoadPreference();
        LobbyBackgroundPrototype? loadedProto = null;

        if (TryResolvePreferredBackground(out var preferredProto)
            && TryLoadBackgroundFromPrototype(preferredProto, preference))
        {
            loadedProto = preferredProto;
        }
        else if (TryResolveServerBackground(out var serverProto)
                 && (loadedProto == null || loadedProto.ID != serverProto.ID)
                 && TryLoadBackgroundFromPrototype(serverProto, LobbyBackgroundLoadPreference.ServerDefault))
        {
            loadedProto = serverProto;
        }

        if (loadedProto == null)
        {
            ClearBackgroundAnimationState();
            _lobby.Background.Texture = null;
            _lobby.LobbyBackground.SetMarkup(Loc.GetString("lobby-state-background-no-background-text"));
            return;
        }

        var markup = Loc.GetString("lobby-state-background-text",
            ("backgroundTitle", Loc.GetString(loadedProto.Title)),
            ("backgroundArtist", Loc.GetString(loadedProto.Artist)));

        _lobby.LobbyBackground.SetMarkup(markup);
    }

    private bool TryResolvePreferredBackground(out LobbyBackgroundPrototype proto)
    {
        var mode = _cfg.GetCVar(CCVars.LobbyBackgroundType).ToLowerInvariant();

        switch (mode)
        {
            case LobbyBackgroundModeStatic:
                if (TryResolveConfiguredBackground(CCVars.LobbyStaticBackground, preferAnimated: false, out proto))
                    return true;
                break;
            case LobbyBackgroundModeAnimated:
                if (TryResolveConfiguredBackground(CCVars.LobbyAnimatedBackground, preferAnimated: true, out proto))
                    return true;
                break;
            default:
                if (TryResolveServerBackground(out proto))
                    return true;
                if (TryResolveConfiguredBackground(CCVars.LobbyAnimatedBackground, preferAnimated: true, out proto))
                    return true;
                if (TryResolveConfiguredBackground(CCVars.LobbyStaticBackground, preferAnimated: false, out proto))
                    return true;
                break;
        }

        proto = default!;
        return false;
    }

    private bool TryResolveConfiguredBackground(
        CVarDef<string> cvar,
        bool preferAnimated,
        out LobbyBackgroundPrototype proto)
    {
        var configuredId = _cfg.GetCVar(cvar);
        if (!string.IsNullOrWhiteSpace(configuredId)
            && _protoMan.TryIndex<LobbyBackgroundPrototype>(configuredId, out var configuredProto)
            && HasRequestedBackgroundType(configuredProto, preferAnimated))
        {
            proto = configuredProto;
            return true;
        }

        if (TryResolveServerBackground(out var serverProto) && HasRequestedBackgroundType(serverProto, preferAnimated))
        {
            proto = serverProto;
            return true;
        }

        foreach (var candidate in _protoMan.EnumeratePrototypes<LobbyBackgroundPrototype>())
        {
            if (!HasRequestedBackgroundType(candidate, preferAnimated))
                continue;

            proto = candidate;
            return true;
        }

        proto = default!;
        return false;
    }

    private bool TryResolveServerBackground(out LobbyBackgroundPrototype proto)
    {
        var serverBackground = _getServerBackgroundId.Invoke();
        if (_protoMan.TryIndex(serverBackground, out LobbyBackgroundPrototype? serverProto))
        {
            proto = serverProto;
            return true;
        }

        proto = default!;
        return false;
    }

    private static bool HasRequestedBackgroundType(LobbyBackgroundPrototype proto, bool preferAnimated)
    {
        return preferAnimated ? proto.BackgroundGif != null : proto.Background != null;
    }

    private LobbyBackgroundLoadPreference GetLoadPreference()
    {
        var mode = _cfg.GetCVar(CCVars.LobbyBackgroundType).ToLowerInvariant();
        return mode switch
        {
            LobbyBackgroundModeStatic => LobbyBackgroundLoadPreference.PreferStatic,
            LobbyBackgroundModeAnimated => LobbyBackgroundLoadPreference.PreferAnimated,
            _ => LobbyBackgroundLoadPreference.ServerDefault
        };
    }

    private bool TryLoadBackgroundFromPrototype(
        LobbyBackgroundPrototype proto,
        LobbyBackgroundLoadPreference preference)
    {
        switch (preference)
        {
            case LobbyBackgroundLoadPreference.PreferStatic:
                if (proto.Background is { } staticPreferred && TryLoadStaticBackground(staticPreferred))
                    return true;

                if (proto.BackgroundGif is { } gifFallbackStatic)
                {
                    var gifResult = TryLoadGifBackground(gifFallbackStatic);
                    return gifResult is GifLoadResult.Loaded or GifLoadResult.Pending;
                }

                return false;
            case LobbyBackgroundLoadPreference.PreferAnimated:
                if (proto.BackgroundGif is { } gifPreferred)
                {
                    var gifResult = TryLoadGifBackground(gifPreferred);
                    if (gifResult == GifLoadResult.Loaded)
                        return true;

                    if (gifResult == GifLoadResult.Pending)
                    {
                        if (!IsBackgroundVisible() && proto.Background is { } staticFallbackAnimated)
                            TryLoadStaticBackground(staticFallbackAnimated);

                        return true;
                    }
                }

                if (proto.Background is { } staticFallbackPreferredAnimated && TryLoadStaticBackground(staticFallbackPreferredAnimated))
                    return true;
                return false;
            default:
                if (proto.BackgroundGif is { } gifPath)
                {
                    var gifResult = TryLoadGifBackground(gifPath);
                    if (gifResult == GifLoadResult.Loaded)
                        return true;

                    if (gifResult == GifLoadResult.Pending)
                    {
                        if (!IsBackgroundVisible() && proto.Background is { } staticFallbackDefault)
                            TryLoadStaticBackground(staticFallbackDefault);

                        return true;
                    }
                }

                if (proto.Background is { } staticBackground && TryLoadStaticBackground(staticBackground))
                    return true;
                return false;
        }
    }

    private bool TryLoadStaticBackground(ResPath path)
    {
        if (_activeStaticBackground == path
            && _activeGifKey == null
            && _activeGifPreviewKey == null
            && _lobby?.Background.Texture != null)
        {
            return true;
        }

        try
        {
            var texture = _resourceCache.GetResource<TextureResource>(path).Texture;
            SetBackgroundFrames([texture], [1f]);
            _activeStaticBackground = path;
            _activeGifKey = null;
            _activeGifPreviewKey = null;
            _requestedGifKey = null;
            EvictGifCacheIfNeeded();
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to load static lobby background '{Path}': {Error}", path, e);
            return false;
        }
    }

    private GifLoadResult TryLoadGifBackground(ResPath gifPath)
    {
        var key = BuildGifCacheKey(gifPath);
        _requestedGifKey = key;

        if (_activeGifKey == key
            && _backgroundFrames is { Length: > 0 }
            && _lobby?.Background.Texture != null)
        {
            TouchGifCacheKey(key);
            return GifLoadResult.Loaded;
        }

        if (TryGetCachedGifEntry(key, out var cacheEntry))
        {
            SetBackgroundFrames(cacheEntry.Frames, cacheEntry.Delays);
            _activeGifKey = key;
            _activeGifPreviewKey = null;
            _activeStaticBackground = null;
            return GifLoadResult.Loaded;
        }

        var needsColdStartPreview = !IsBackgroundVisible();
        var previewAppliedFromCache = needsColdStartPreview && TryApplyColdStartFirstFramePreview(key);

        if (_pendingGifUpload?.Key == key)
            return GifLoadResult.Pending;

        if (_gifDecodeKey == key && _gifDecodeTask is { IsCompleted: false })
            return GifLoadResult.Pending;

        CancelGifPipeline();
        var generation = unchecked(++_gifRequestGeneration);

        if (!TryReadGifData(key, out var gifData))
        {
            _requestedGifKey = null;
            return GifLoadResult.Failed;
        }

        if (!TryStartGifDecode(key, generation, gifData))
        {
            _requestedGifKey = null;
            return GifLoadResult.Failed;
        }

        if (needsColdStartPreview && !previewAppliedFromCache)
            TryStartGifFirstFrameDecode(key, generation, gifData);

        return GifLoadResult.Pending;
    }

    private LobbyGifCacheKey BuildGifCacheKey(ResPath gifPath)
    {
        return new LobbyGifCacheKey(gifPath);
    }

    private bool TryReadGifData(LobbyGifCacheKey key, out byte[] gifData)
    {
        gifData = Array.Empty<byte>();

        try
        {
            using (var stream = _resourceCache.ContentFileRead(key.Path))
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                gifData = ms.ToArray();
            }

            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to read animated lobby background GIF '{Path}': {Error}", key.Path, e);
            return false;
        }
    }

    private bool TryStartGifDecode(LobbyGifCacheKey key, int generation, byte[] gifData)
    {
        try
        {
            var cts = new CancellationTokenSource();
            _gifDecodeCts = cts;
            _gifDecodeKey = key;
            _gifDecodeGeneration = generation;
            _gifDecodeTask = Task.Run(() => GifDecoder.Decode(gifData, cts.Token), cts.Token);
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to start animated lobby background decode '{Path}': {Error}", key.Path, e);
            return false;
        }
    }

    private bool TryStartGifFirstFrameDecode(LobbyGifCacheKey key, int generation, byte[] gifData)
    {
        try
        {
            var cts = new CancellationTokenSource();
            _gifFirstFrameDecodeCts = cts;
            _gifFirstFrameDecodeKey = key;
            _gifFirstFrameDecodeGeneration = generation;
            _gifFirstFrameDecodeTask = Task.Run(() => GifDecoder.DecodeFirstFrame(gifData, cts.Token), cts.Token);
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to start animated lobby background first-frame decode '{Path}': {Error}", key.Path, e);
            return false;
        }
    }

    private bool IsBackgroundVisible()
    {
        return _backgroundFrames is { Length: > 0 } && _lobby?.Background.Texture != null;
    }

    private void ProcessCompletedGifFirstFrameDecode()
    {
        if (_gifFirstFrameDecodeTask == null || _gifFirstFrameDecodeKey == null)
            return;

        if (!_gifFirstFrameDecodeTask.IsCompleted)
            return;

        var key = _gifFirstFrameDecodeKey.Value;
        var task = _gifFirstFrameDecodeTask;
        var generation = _gifFirstFrameDecodeGeneration;

        _gifFirstFrameDecodeTask = null;
        _gifFirstFrameDecodeKey = null;
        _gifFirstFrameDecodeGeneration = 0;
        _gifFirstFrameDecodeCts?.Dispose();
        _gifFirstFrameDecodeCts = null;

        if (task.IsCanceled)
            return;

        GifDecoder.DecodedAnimation decoded;
        try
        {
            decoded = task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to decode animated lobby background first-frame GIF '{Path}': {Error}", key.Path, e);
            return;
        }

        if (decoded.Frames.Length <= 0)
            return;

        if (generation != _gifRequestGeneration || _requestedGifKey != key)
            return;

        try
        {
            var firstFrame = decoded.Frames[0];
            var firstTexture = RgbaTextureUploader.UploadTexture(
                _clyde,
                decoded.Width,
                decoded.Height,
                firstFrame.Pixels,
                $"{key.Path}#preview");

            var delay = MathF.Max(firstFrame.DelaySeconds, MinBackgroundFrameDelay);
            AddOrReplaceFirstFrameCacheEntry(key, firstTexture, delay);

            if (!IsBackgroundVisible() && _requestedGifKey == key)
                ApplyGifPreviewFrame(key, firstTexture, delay);
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to upload animated lobby background first-frame '{Path}': {Error}", key.Path, e);
        }
    }

    private void ProcessCompletedGifDecode()
    {
        if (_gifDecodeTask == null || _gifDecodeKey == null)
            return;

        if (!_gifDecodeTask.IsCompleted)
            return;

        var key = _gifDecodeKey.Value;
        var task = _gifDecodeTask;
        var generation = _gifDecodeGeneration;

        _gifDecodeTask = null;
        _gifDecodeKey = null;
        _gifDecodeGeneration = 0;
        _gifDecodeCts?.Dispose();
        _gifDecodeCts = null;

        if (task.IsCanceled)
            return;

        GifDecoder.DecodedAnimation decoded;
        try
        {
            decoded = task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to decode animated lobby background GIF '{Path}': {Error}", key.Path, e);
            return;
        }

        if (decoded.Frames.Length <= 0)
            return;

        if (generation != _gifRequestGeneration || _requestedGifKey != key)
            return;

        _pendingGifUpload = new PendingGifUpload(key, decoded, generation);
    }

    private void ProcessPendingGifUpload()
    {
        if (_pendingGifUpload == null)
            return;

        var pending = _pendingGifUpload;

        if (pending.Generation != _gifRequestGeneration
            || (_requestedGifKey != pending.Key && _activeGifKey != pending.Key))
        {
            QueueGifDisposal(pending.Key, pending.UploadedFrames);
            _pendingGifUpload = null;
            return;
        }

        try
        {
            var uploadedThisFrame = 0;

            while (pending.NextFrameIndex < pending.DecodedFrames.Length
                   && uploadedThisFrame < GifUploadFramesPerFrameUpdate)
            {
                var frameIndex = pending.NextFrameIndex;
                var frame = pending.DecodedFrames[frameIndex];
                var texture = RgbaTextureUploader.UploadTexture(
                    _clyde,
                    pending.Width,
                    pending.Height,
                    frame.Pixels,
                    $"{pending.Key.Path}#{frameIndex}");

                pending.UploadedFrames[frameIndex] = texture;
                pending.Delays[frameIndex] = frame.DelaySeconds;
                pending.NextFrameIndex++;
                uploadedThisFrame++;
            }

            if (pending.NextFrameIndex < pending.DecodedFrames.Length)
                return;

            var uploadedFrames = new Texture[pending.UploadedFrames.Length];
            for (var i = 0; i < pending.UploadedFrames.Length; i++)
            {
                if (pending.UploadedFrames[i] == null)
                {
                    throw new InvalidOperationException(
                        $"GIF upload completed with missing frame {i} for '{pending.Key.Path}'.");
                }

                uploadedFrames[i] = pending.UploadedFrames[i]!;
            }

            AddGifCacheEntry(pending.Key, uploadedFrames, pending.Delays);

            if (_requestedGifKey == pending.Key)
            {
                SetBackgroundFrames(uploadedFrames, pending.Delays);
                _activeGifKey = pending.Key;
                _activeGifPreviewKey = null;
                _activeStaticBackground = null;
            }

            _pendingGifUpload = null;
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to upload animated lobby background GIF '{Path}': {Error}", pending.Key.Path, e);
            QueueGifDisposal(pending.Key, pending.UploadedFrames);
            _pendingGifUpload = null;
        }
    }

    private void CancelGifPipeline(bool scheduleImmediateDispose = false)
    {
        if (_gifDecodeCts != null)
        {
            _gifDecodeCts.Cancel();
            _gifDecodeCts.Dispose();
            _gifDecodeCts = null;
        }

        _gifDecodeTask = null;
        _gifDecodeKey = null;
        _gifDecodeGeneration = 0;

        if (_gifFirstFrameDecodeCts != null)
        {
            _gifFirstFrameDecodeCts.Cancel();
            _gifFirstFrameDecodeCts.Dispose();
            _gifFirstFrameDecodeCts = null;
        }

        _gifFirstFrameDecodeTask = null;
        _gifFirstFrameDecodeKey = null;
        _gifFirstFrameDecodeGeneration = 0;

        if (_pendingGifUpload != null)
        {
            QueueGifDisposal(_pendingGifUpload.Key, _pendingGifUpload.UploadedFrames, scheduleImmediateDispose);
            _pendingGifUpload = null;
        }
    }

    private bool TryGetCachedGifEntry(LobbyGifCacheKey key, out CachedGifAnimation entry)
    {
        if (_gifCache.TryGetValue(key, out entry!))
        {
            TouchGifCacheKey(key);
            return true;
        }

        entry = default!;
        return false;
    }

    private bool TryApplyColdStartFirstFramePreview(LobbyGifCacheKey key)
    {
        if (!TryGetCachedFirstFrameEntry(key, out var entry))
            return false;

        ApplyGifPreviewFrame(key, entry.Frame, entry.Delay);
        return true;
    }

    private void ApplyGifPreviewFrame(LobbyGifCacheKey key, Texture frame, float delay)
    {
        SetBackgroundFrames([frame], [MathF.Max(delay, MinBackgroundFrameDelay)]);
        _activeStaticBackground = null;
        _activeGifKey = null;
        _activeGifPreviewKey = key;
    }

    private bool TryGetCachedFirstFrameEntry(LobbyGifCacheKey key, out CachedGifFirstFrame entry)
    {
        if (_firstFrameCache.TryGetValue(key, out entry!))
        {
            TouchFirstFrameCacheKey(key);
            return true;
        }

        entry = default!;
        return false;
    }

    private void AddOrReplaceFirstFrameCacheEntry(LobbyGifCacheKey key, Texture frame, float delay)
    {
        if (_firstFrameCache.TryGetValue(key, out var existing))
        {
            if (!ReferenceEquals(existing.Frame, frame))
                QueueGifDisposal(key, [existing.Frame]);

            _firstFrameCache[key] = new CachedGifFirstFrame(frame, delay);
            TouchFirstFrameCacheKey(key);
            return;
        }

        _firstFrameCache[key] = new CachedGifFirstFrame(frame, delay);
        _firstFrameCacheLruNodes[key] = _firstFrameCacheLru.AddFirst(key);
        EvictFirstFrameCacheIfNeeded();
    }

    private void TouchFirstFrameCacheKey(LobbyGifCacheKey key)
    {
        if (!_firstFrameCacheLruNodes.TryGetValue(key, out var node))
        {
            _firstFrameCacheLruNodes[key] = _firstFrameCacheLru.AddFirst(key);
            return;
        }

        if (node == _firstFrameCacheLru.First)
            return;

        _firstFrameCacheLru.Remove(node);
        _firstFrameCacheLru.AddFirst(node);
    }

    private void EvictFirstFrameCacheIfNeeded()
    {
        while (_firstFrameCache.Count > FirstFrameCacheCapacity)
        {
            LinkedListNode<LobbyGifCacheKey>? evictionNode = null;
            var cursor = _firstFrameCacheLru.Last;

            while (cursor != null)
            {
                if (!IsGifKeyInUse(cursor.Value))
                {
                    evictionNode = cursor;
                    break;
                }

                cursor = cursor.Previous;
            }

            if (evictionNode == null)
                break;

            RemoveFirstFrameCacheEntry(evictionNode.Value, queueDispose: true);
        }
    }

    private void RemoveFirstFrameCacheEntry(LobbyGifCacheKey key, bool queueDispose)
    {
        if (!_firstFrameCache.TryGetValue(key, out var entry))
            return;

        _firstFrameCache.Remove(key);

        if (_firstFrameCacheLruNodes.TryGetValue(key, out var node))
        {
            _firstFrameCacheLru.Remove(node);
            _firstFrameCacheLruNodes.Remove(key);
        }

        if (queueDispose)
            QueueGifDisposal(key, [entry.Frame]);
    }

    private void AddGifCacheEntry(LobbyGifCacheKey key, Texture[] frames, float[] delays)
    {
        if (_gifCache.TryGetValue(key, out var existing))
        {
            QueueGifDisposal(key, existing.Frames);
            _gifCache[key] = new CachedGifAnimation(frames, delays);
            TouchGifCacheKey(key);
            return;
        }

        _gifCache[key] = new CachedGifAnimation(frames, delays);
        var node = _gifCacheLru.AddFirst(key);
        _gifCacheLruNodes[key] = node;
        EvictGifCacheIfNeeded();
    }

    private void TouchGifCacheKey(LobbyGifCacheKey key)
    {
        if (!_gifCacheLruNodes.TryGetValue(key, out var node))
        {
            _gifCacheLruNodes[key] = _gifCacheLru.AddFirst(key);
            return;
        }

        if (node == _gifCacheLru.First)
            return;

        _gifCacheLru.Remove(node);
        _gifCacheLru.AddFirst(node);
    }

    private void EvictGifCacheIfNeeded()
    {
        while (_gifCache.Count > GifCacheCapacity)
        {
            LinkedListNode<LobbyGifCacheKey>? evictionNode = null;
            var cursor = _gifCacheLru.Last;

            while (cursor != null)
            {
                if (!IsGifKeyInUse(cursor.Value))
                {
                    evictionNode = cursor;
                    break;
                }

                cursor = cursor.Previous;
            }

            if (evictionNode == null)
                break;

            RemoveGifCacheEntry(evictionNode.Value, queueDispose: true);
        }
    }

    private void RemoveGifCacheEntry(LobbyGifCacheKey key, bool queueDispose)
    {
        if (!_gifCache.TryGetValue(key, out var entry))
            return;

        _gifCache.Remove(key);

        if (_gifCacheLruNodes.TryGetValue(key, out var node))
        {
            _gifCacheLru.Remove(node);
            _gifCacheLruNodes.Remove(key);
        }

        if (queueDispose)
            QueueGifDisposal(key, entry.Frames);
    }

    private void QueueGifDisposal(
        LobbyGifCacheKey key,
        IReadOnlyList<Texture?> frames,
        bool scheduleImmediateDispose = false)
    {
        var ownedFrames = CollectOwnedFrames(frames);
        if (ownedFrames.Count == 0)
            return;

        if (scheduleImmediateDispose)
        {
            ScheduleOwnedTextureDispose(ownedFrames);
            return;
        }

        _pendingGifDisposals.Add(new PendingGifDisposal(
            key,
            ownedFrames.ToArray(),
            _gameTiming.CurTime + TimeSpan.FromMilliseconds(BackgroundTextureDisposeDelayMs)));
    }

    private static List<Texture> CollectOwnedFrames(IReadOnlyList<Texture?> frames)
    {
        var ownedFrames = new List<Texture>(frames.Count);

        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i] is OwnedTexture owned)
                ownedFrames.Add(owned);
        }

        return ownedFrames;
    }

    private void ProcessPendingGifDisposals()
    {
        if (_pendingGifDisposals.Count == 0)
            return;

        var now = _gameTiming.CurTime;

        for (var i = _pendingGifDisposals.Count - 1; i >= 0; i--)
        {
            var pending = _pendingGifDisposals[i];

            if (pending.DisposeAt > now)
                continue;

            if (IsGifKeyInUse(pending.Key))
            {
                var pendingDuration = now - pending.InitialDisposeAt;
                if (pendingDuration < TimeSpan.FromMilliseconds(MaxDisposalDelayMs))
                {
                    pending.DisposeAt = now + TimeSpan.FromMilliseconds(BackgroundTextureDisposeDelayMs);
                    continue;
                }

                if (pending.Key != _activeGifKey
                    && pending.Key != _activeGifPreviewKey
                    && pending.Key != _requestedGifKey)
                {
                    DisposeOwnedTextures(pending.Frames);
                    _pendingGifDisposals.RemoveAt(i);
                    continue;
                }

                pending.DisposeAt = now + TimeSpan.FromMilliseconds(BackgroundTextureDisposeDelayMs);
                continue;
            }

            DisposeOwnedTextures(pending.Frames);
            _pendingGifDisposals.RemoveAt(i);
        }
    }

    private bool IsGifKeyInUse(LobbyGifCacheKey key)
    {
        if (_activeGifKey == key)
            return true;

        if (_activeGifPreviewKey == key)
            return true;

        if (_requestedGifKey == key)
            return true;

        if (_gifDecodeKey == key)
            return true;

        if (_gifFirstFrameDecodeKey == key)
            return true;

        if (_pendingGifUpload?.Key == key)
            return true;

        return false;
    }

    private void SetBackgroundFrames(Texture[] frames, float[] delays)
    {
        _backgroundFrames = frames;
        _backgroundFrameDelays = delays;
        _backgroundFrameIndex = 0;
        _backgroundFrameTimer = delays.Length > 0 ? MathF.Max(delays[0], MinBackgroundFrameDelay) : 0f;

        if (_lobby != null)
            _lobby.Background.Texture = frames.Length > 0 ? frames[0] : null;
    }

    private void UpdateLobbyBackgroundAnimation(float deltaSeconds)
    {
        if (_backgroundFrames == null || _backgroundFrameDelays == null)
            return;

        if (_backgroundFrames.Length <= 1 || _backgroundFrameDelays.Length <= 1)
            return;

        _backgroundFrameTimer -= deltaSeconds;
        var changed = false;

        while (_backgroundFrameTimer <= 0f)
        {
            _backgroundFrameIndex = (_backgroundFrameIndex + 1) % _backgroundFrames.Length;
            var delay = MathF.Max(_backgroundFrameDelays[_backgroundFrameIndex], MinBackgroundFrameDelay);
            _backgroundFrameTimer += delay;
            changed = true;
        }

        if (changed && _lobby != null)
            _lobby.Background.Texture = _backgroundFrames[_backgroundFrameIndex];
    }

    private void ClearBackgroundAnimationState(bool resetCache = false)
    {
        CancelGifPipeline();
        DetachCurrentBackgroundTexture();

        _backgroundFrames = null;
        _backgroundFrameDelays = null;
        _backgroundFrameIndex = 0;
        _backgroundFrameTimer = 0f;
        _activeStaticBackground = null;
        _activeGifKey = null;
        _activeGifPreviewKey = null;
        _requestedGifKey = null;

        if (!resetCache)
            return;

        foreach (var entry in _gifCache.Values)
        {
            ScheduleOwnedTextureDispose(entry.Frames);
        }

        _gifCache.Clear();
        _gifCacheLru.Clear();
        _gifCacheLruNodes.Clear();

        foreach (var pending in _pendingGifDisposals)
        {
            ScheduleOwnedTextureDispose(pending.Frames);
        }

        _pendingGifDisposals.Clear();
    }

    private void DetachCurrentBackgroundTexture()
    {
        if (_lobby == null)
            return;

        _lobby.Background.Texture = null;
    }

    private void ScheduleOwnedTextureDispose(IReadOnlyList<Texture> frames)
    {
        if (frames.Count == 0)
            return;

        var copy = new Texture[frames.Count];
        for (var i = 0; i < frames.Count; i++)
            copy[i] = frames[i];

        Robust.Shared.Timing.Timer.Spawn(BackgroundTextureDisposeDelayMs, () => DisposeOwnedTextures(copy));
    }

    private static void DisposeOwnedTextures(IReadOnlyList<Texture> frames)
    {
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i] is OwnedTexture owned)
                owned.Dispose();
        }
    }
}
