using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Psyker;

public sealed partial class WH40KPsykerAstralProjectionActionEvent : InstantActionEvent;

public sealed partial class WH40KPsykerBiomanticSurgeActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed class WH40KPsykerAstralExitRequestEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class WH40KPsykerAstralPurchaseNodeRequestEvent : EntityEventArgs
{
    public string NodeId { get; }

    public WH40KPsykerAstralPurchaseNodeRequestEvent(string nodeId)
    {
        NodeId = nodeId;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KPsykerAstralCollectStarRequestEvent : EntityEventArgs
{
    public int StarId { get; }

    public WH40KPsykerAstralCollectStarRequestEvent(int starId)
    {
        StarId = starId;
    }
}
