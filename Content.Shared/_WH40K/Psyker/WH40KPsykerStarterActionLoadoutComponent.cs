namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Starter action pack for the Imperium psyker path.
/// Uses reuse-first wrappers over existing baseline magic actions.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KPsykerStarterActionLoadoutComponent : Component
{
    [DataField("starterActions")]
    public List<string> StarterActions = new();

    [DataField("scaledActions")]
    public List<WH40KLevelLockedAction> ScaledActions = new();

    [DataField]
    public List<EntityUid> GrantedActions = new();

    [DataField]
    public int AppliedLevel;

    [DataField]
    public string AppliedAstralSignature = string.Empty;

    [DataField]
    public bool AppliedCatastropheLockdown;
}
