using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Mortar;

[RegisterComponent, AutoGenerateComponentState]
public sealed partial class WH40KMortarShellComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan LoadDelay = TimeSpan.FromSeconds(1.5);

    [DataField, AutoNetworkedField]
    public TimeSpan TravelDelay = TimeSpan.FromSeconds(4.5);

    [DataField, AutoNetworkedField]
    public TimeSpan ImpactWarningDelay = TimeSpan.FromSeconds(2.5);

    [DataField, AutoNetworkedField]
    public TimeSpan ImpactDelay = TimeSpan.FromSeconds(4.5);

    [DataField("incomingSoundLeadTime"), AutoNetworkedField]
    public TimeSpan IncomingSoundLeadTime = TimeSpan.FromSeconds(0.8);

    [DataField, AutoNetworkedField]
    public EntProtoId? SpawnOnLand;

    [DataField("spawnOnLandTriggerKey"), AutoNetworkedField]
    public string? SpawnOnLandTriggerKey;

    [DataField("uiShellType"), AutoNetworkedField]
    public string UiShellType = "unknown";

    [DataField, AutoNetworkedField]
    public bool TriggerExplosion = true;
}
