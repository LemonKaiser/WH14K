using Content.Shared._WH40K.WaveDefence;
using Content.Shared.GameTicking;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._WH40K.WaveDefence;

[UsedImplicitly]
public sealed partial class WH40KWaveDefenceAiDebugOverlaySystem : SharedWH40KWaveDefenceAiDebugOverlaySystem
{
    [Dependency] private  IOverlayManager _overlayManager = default!;

    public readonly List<WH40KWaveDefenceAiDebugEntry> Entries = [];

    private WH40KWaveDefenceAiDebugOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeNetworkEvent<WH40KWaveDefenceAiDebugOverlayMessage>(OnOverlayMessage);
        SubscribeNetworkEvent<WH40KWaveDefenceAiDebugOverlayDisableMessage>(OnOverlayDisable);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Entries.Clear();
        RemoveOverlay();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        Entries.Clear();
    }

    private void OnOverlayMessage(WH40KWaveDefenceAiDebugOverlayMessage message)
    {
        Entries.Clear();
        Entries.AddRange(message.Entries);

        if (_overlay != null)
            return;

        _overlay = new WH40KWaveDefenceAiDebugOverlay(this);
        _overlayManager.AddOverlay(_overlay);
    }

    private void OnOverlayDisable(WH40KWaveDefenceAiDebugOverlayDisableMessage ev)
    {
        Entries.Clear();
        RemoveOverlay();
    }

    private void RemoveOverlay()
    {
        if (_overlay == null)
            return;

        _overlayManager.RemoveOverlay(_overlay);
        _overlay = null;
    }
}
