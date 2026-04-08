using Content.Server.KillTracking;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WH40K;

public sealed class WH40KKillAttributionTestSystem : EntitySystem
{
    public int DownedCount;
    public int KilledCount;
    public int CompatibilityKillCount;

    public AttributedDownedEvent? LastDowned;
    public AttributedKilledEvent? LastKilled;
    public KillReportedEvent? LastCompatibilityKill;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AttributedDownedEvent>(OnDowned);
        SubscribeLocalEvent<AttributedKilledEvent>(OnKilled);
        SubscribeLocalEvent<KillReportedEvent>(OnCompatibilityKill);
    }

    public void Reset()
    {
        DownedCount = 0;
        KilledCount = 0;
        CompatibilityKillCount = 0;
        LastDowned = null;
        LastKilled = null;
        LastCompatibilityKill = null;
    }

    private void OnDowned(ref AttributedDownedEvent ev)
    {
        DownedCount++;
        LastDowned = ev;
    }

    private void OnKilled(ref AttributedKilledEvent ev)
    {
        KilledCount++;
        LastKilled = ev;
    }

    private void OnCompatibilityKill(ref KillReportedEvent ev)
    {
        CompatibilityKillCount++;
        LastCompatibilityKill = ev;
    }
}
