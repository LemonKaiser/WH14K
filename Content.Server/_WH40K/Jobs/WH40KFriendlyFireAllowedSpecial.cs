using Content.Server._WH40K.Combat;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._WH40K.Jobs;

public sealed partial class WH40KFriendlyFireAllowedSpecial : JobSpecial
{
    public override void AfterEquip(EntityUid mob)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        if (!entMan.HasComponent<WH40KFriendlyFireAllowedComponent>(mob))
            entMan.AddComponent<WH40KFriendlyFireAllowedComponent>(mob);
    }
}
