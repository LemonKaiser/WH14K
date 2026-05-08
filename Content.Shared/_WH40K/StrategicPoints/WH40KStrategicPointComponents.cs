using System;
using System.Numerics;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.StrategicPoints;

[RegisterComponent]
public sealed partial class WH40KStrategicPointAnchorComponent : Component
{
    [DataField("pointType", required: true)]
    public WH40KStrategicPointType PointType;

    [DataField("callsign")]
    public string? Callsign;

    [DataField("buildRadius")]
    public float BuildRadius = 0.75f;

    [DataField("hideSpriteWhenBuilt")]
    public bool HideSpriteWhenBuilt;

    [DataField("builtOffset")]
    public Vector2 BuiltOffset = Vector2.Zero;

    [ViewVariables]
    public EntityUid? BuiltPoint;
}

[RegisterComponent]
public sealed partial class WH40KStrategicPointComponent : Component
{
    [DataField("pointType", required: true)]
    public WH40KStrategicPointType PointType;

    [DataField("tier")]
    public WH40KStrategicPointTier Tier = WH40KStrategicPointTier.T1;

    [DataField("profile", required: true)]
    public ProtoId<WH40KStrategicPointProfilePrototype> Profile;

    [DataField("ownerTeamId")]
    public string? OwnerTeamId;

    [DataField("incomeIntervalSeconds")]
    public float IncomeIntervalSeconds = 10f;

    [ViewVariables]
    public EntityUid? Anchor;

    [ViewVariables]
    public TimeSpan NextIncomeTick;

    [ViewVariables]
    public Dictionary<WH40KStrategicPointCurrency, int> IncomeRemainders = new();

    [DataField("loadedUpgradeMaterials")]
    public Dictionary<ProtoId<StackPrototype>, int> LoadedUpgradeMaterials = new();

    [ViewVariables]
    public bool UpgradeInProgress;

    [ViewVariables]
    public WH40KStrategicPointTier PendingUpgradeTier;
}

[RegisterComponent]
public sealed partial class WH40KStrategicPointUpgradeSkillComponent : Component
{
}

[RegisterComponent]
public sealed partial class WH40KStrategicPointVisualsComponent : Component
{
}
