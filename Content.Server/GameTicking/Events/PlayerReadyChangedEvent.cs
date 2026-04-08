using Robust.Shared.Player;

namespace Content.Server.GameTicking.Events;

[ByRefEvent]
public record struct PlayerBeforeReadyChangedEvent(
    ICommonSession Player,
    bool Ready,
    bool Cancelled = false,
    string? ReasonLocKey = null);

public readonly record struct PlayerReadyChangedEvent(ICommonSession Player, bool Ready);