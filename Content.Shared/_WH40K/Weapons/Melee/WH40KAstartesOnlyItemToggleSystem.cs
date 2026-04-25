using Content.Shared.Humanoid;
using Content.Shared.Item.ItemToggle.Components;

namespace Content.Shared._WH40K.Weapons.Melee;

public sealed class WH40KAstartesOnlyItemToggleSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KAstartesOnlyItemToggleComponent, ItemToggleActivateAttemptEvent>(OnActivateAttempt);
    }

    private void OnActivateAttempt(Entity<WH40KAstartesOnlyItemToggleComponent> ent, ref ItemToggleActivateAttemptEvent args)
    {
        if (args.Cancelled || args.User is not { } user)
            return;

        if (TryComp<HumanoidProfileComponent>(user, out var profile) &&
            ent.Comp.Species.Contains(profile.Species))
            return;

        args.Cancelled = true;
        args.Popup = Loc.GetString(ent.Comp.Popup);
    }
}
