using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Publicly replicated chaos patron display state for overhead status icons.
/// Keeps only the patron choice and leader marker, without exposing private progression data.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KChaosPatronStatusIconComponent : Component
{
    [DataField("patron"), AutoNetworkedField]
    public WH40KChaosPatron Patron = WH40KChaosPatron.None;

    [DataField("isLeader"), AutoNetworkedField]
    public bool IsLeader;
}
