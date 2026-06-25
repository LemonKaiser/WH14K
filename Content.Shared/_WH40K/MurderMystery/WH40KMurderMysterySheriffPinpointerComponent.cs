namespace Content.Shared._WH40K.MurderMystery;

/// <summary>
/// Configures a pinpointer handed to every Murder Mystery participant so they
/// can locate the dropped sheriff revolver. While the revolver is held in any
/// player's inventory the pinpointer reports no target (so the sheriff's
/// identity is not revealed); once it is dropped/thrown the pinpointer locks on.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KMurderMysterySheriffPinpointerComponent : Component
{
    /// <summary>
    /// How often the system re-resolves the revolver target.
    /// </summary>
    [DataField]
    public TimeSpan RefreshInterval = TimeSpan.FromSeconds(0.5);

    [ViewVariables]
    public TimeSpan NextRefreshAt = TimeSpan.Zero;
}
