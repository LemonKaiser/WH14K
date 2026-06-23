using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Interface;

[Serializable, NetSerializable]
public sealed class WH40KRoundTimerEvent : EntityEventArgs
{
    public bool Visible { get; }
    public int RoundId { get; }
    public int DurationSeconds { get; }
    public int ElapsedSeconds { get; }
    public bool Stopped { get; }

    public WH40KRoundTimerEvent(bool visible, int roundId, int durationSeconds, int elapsedSeconds, bool stopped)
    {
        Visible = visible;
        RoundId = roundId;
        DurationSeconds = durationSeconds;
        ElapsedSeconds = elapsedSeconds;
        Stopped = stopped;
    }
}
