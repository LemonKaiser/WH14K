using Content.Shared._WH40K.Psyker;

namespace Content.Client._WH40K.Psyker;

public sealed class WH40KPsykerAstralProjectionSystem : EntitySystem
{
    public void RequestExit()
    {
        RaiseNetworkEvent(new WH40KPsykerAstralExitRequestEvent());
    }

    public void RequestNodePurchase(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return;

        RaiseNetworkEvent(new WH40KPsykerAstralPurchaseNodeRequestEvent(nodeId));
    }

    public void RequestCollectibleStar(int starId)
    {
        if (starId <= 0)
            return;

        RaiseNetworkEvent(new WH40KPsykerAstralCollectStarRequestEvent(starId));
    }
}
