using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Action-side cost profile for warp-powered abilities.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KWarpActionCostComponent : Component
{
    [DataField("warpChargeCost"), AutoNetworkedField]
    public float WarpChargeCost = 10f;

    [DataField("instabilityGain"), AutoNetworkedField]
    public float InstabilityGain = 5f;

    [DataField("requireWarpRole"), AutoNetworkedField]
    public bool RequireWarpRole = true;

    [DataField("allowPsykerRole"), AutoNetworkedField]
    public bool AllowPsykerRole = true;

    [DataField("allowChaosRole"), AutoNetworkedField]
    public bool AllowChaosRole = true;
}
