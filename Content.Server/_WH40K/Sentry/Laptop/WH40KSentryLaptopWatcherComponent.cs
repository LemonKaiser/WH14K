using Robust.Shared.GameStates;

namespace Content.Server._WH40K.Sentry.Laptop;

/// <summary>
/// Tracks an active sentry camera subscription opened from a WH40K sentry laptop UI.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KSentryLaptopWatcherComponent : Component
{
    [ViewVariables]
    public EntityUid? Laptop;

    [ViewVariables]
    public EntityUid? CurrentTurret;
}
