namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Starter action pack for the Imperium psyker path.
/// Uses reuse-first wrappers over existing baseline magic actions.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KPsykerStarterActionLoadoutComponent : Component
{
    // Spawn baseline: no free abilities at round start.
    // Psyker starts with one baseline combat discipline to stay parity-bounded
    // against the chaos branch before scaled unlocks come online.
    [DataField("starterActions")]
    public List<string> StarterActions = new()
    {
        "ActionWH40KPsykerTelekineticRepulse",
    };

    [DataField("scaledActions")]
    public List<WH40KLevelLockedAction> ScaledActions = new()
    {
        new() { ActionPrototype = "ActionWH40KPsykerAegisWall", RequiredLevel = 3 },
        new() { ActionPrototype = "ActionWH40KPsykerVeilSmoke", RequiredLevel = 5 },
        new() { ActionPrototype = "ActionWH40KPsykerMindShunt", RequiredLevel = 8 },
    };

    [DataField]
    public List<EntityUid> GrantedActions = new();

    [DataField]
    public int AppliedLevel;

    [DataField]
    public bool AppliedCatastropheLockdown;
}
