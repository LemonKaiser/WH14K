using System;

namespace Content.Server._WH40K.TacticalMap;

/// <summary>
/// Added to a user while the WH40K tactical map UI is open so it closes if the parent changes.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KTacticalMapUserComponent : Component
{
    [DataField("mapUid")]
    public EntityUid Map;

    [DataField("scannerUid")]
    public EntityUid? Scanner;

    [DataField("scanIndex")]
    public int ScanIndex;

    [DataField("nextRefreshAt")]
    public TimeSpan NextRefreshAt;

    [DataField("nextStateSyncAt")]
    public TimeSpan NextStateSyncAt;

    [DataField("teamId")]
    public string TeamId = string.Empty;

    [DataField("lastFogRevision")]
    public int LastFogRevision = -1;

    [DataField("lastAnnotationRevision")]
    public int LastAnnotationRevision = -1;

    [DataField("lastOverlayRevision")]
    public int LastOverlayRevision = -1;

    [DataField("lastLiveRefreshRevision")]
    public int LastLiveRefreshRevision = -1;
}
