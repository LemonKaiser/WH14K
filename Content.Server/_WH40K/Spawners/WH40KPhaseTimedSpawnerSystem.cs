using System;
using Content.Server.Spawners.Components;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Spawners.Components;
using Content.Shared._WH40K.GameMode;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Spawners;

/// <summary>
/// Keeps selected timed spawners disabled until a configured WH40K battle phase.
/// </summary>
public sealed class WH40KPhaseTimedSpawnerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KPhaseTimedSpawnerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KPhaseTimedSpawnerComponent, ComponentShutdown>(OnPhaseGateShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var phase = _teamRule.GetCurrentPhase();
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KPhaseTimedSpawnerComponent, TimedSpawnerComponent>();
        while (query.MoveNext(out _, out var phaseGate, out var timedSpawner))
        {
            ApplyPhase(phaseGate, timedSpawner, phase, now);
        }
    }

    private void OnMapInit(EntityUid uid, WH40KPhaseTimedSpawnerComponent component, MapInitEvent args)
    {
        if (!TryComp<TimedSpawnerComponent>(uid, out var timedSpawner))
            return;

        ApplyPhase(component, timedSpawner, _teamRule.GetCurrentPhase(), _timing.CurTime);
    }

    private void OnPhaseGateShutdown(EntityUid uid, WH40KPhaseTimedSpawnerComponent component, ComponentShutdown args)
    {
        if (!TryComp<TimedSpawnerComponent>(uid, out var timedSpawner))
            return;

        if (component.SavedChance >= 0f)
            timedSpawner.Chance = component.SavedChance;
    }

    private static void ApplyPhase(
        WH40KPhaseTimedSpawnerComponent phaseGate,
        TimedSpawnerComponent timedSpawner,
        WH40KBattlePhase phase,
        TimeSpan now)
    {
        if (phaseGate.SavedChance < 0f)
            phaseGate.SavedChance = Math.Clamp(timedSpawner.Chance, 0f, 1f);

        var enabled = phase >= phaseGate.EnabledFromPhase;
        if (!enabled)
        {
            timedSpawner.Chance = 0f;
            phaseGate.Enabled = false;
            return;
        }

        timedSpawner.Chance = phaseGate.SavedChance;
        if (!phaseGate.Enabled && phaseGate.ResetTimerOnEnable)
            timedSpawner.NextFire = now + timedSpawner.IntervalSeconds;

        phaseGate.Enabled = true;
    }
}
