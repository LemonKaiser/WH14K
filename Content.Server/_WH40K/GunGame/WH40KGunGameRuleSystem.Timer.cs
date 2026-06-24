using System;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared._WH40K.Interface;
using Robust.Shared.Enums;

namespace Content.Server._WH40K.GunGame;

public sealed partial class WH40KGunGameRuleSystem
{
    private static readonly TimeSpan TimerSyncInterval = TimeSpan.FromSeconds(5);

    private void PushRoundTimer(WH40KGunGameRuleComponent rule, bool force = false)
    {
        var stopped = rule.RoundDuration <= TimeSpan.Zero;
        var durationSeconds = stopped
            ? 0
            : Math.Max(0, (int) Math.Ceiling(rule.RoundDuration.TotalSeconds));
        var elapsedSeconds = Math.Max(0, (int) Math.Floor(GameTicker.RoundDuration().TotalSeconds));

        var changed = rule.LastTimerStopped != stopped ||
                      rule.LastTimerDurationSeconds != durationSeconds;

        if (!force &&
            !changed &&
            _timing.CurTime < rule.NextTimerSyncAt)
        {
            return;
        }

        rule.LastTimerStopped = stopped;
        rule.LastTimerDurationSeconds = durationSeconds;
        rule.LastTimerElapsedSeconds = elapsedSeconds;
        rule.NextTimerSyncAt = _timing.CurTime + TimerSyncInterval;

        var ev = new WH40KRoundTimerEvent(true, GameTicker.RoundId, durationSeconds, elapsedSeconds, stopped);
        foreach (var session in _player.Sessions)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(ev, session);
        }
    }

    private void ClearRoundTimerHud()
    {
        var ev = new WH40KRoundTimerEvent(false, GameTicker.RoundId, 0, 0, false);
        foreach (var session in _player.Sessions)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(ev, session);
        }
    }
}
