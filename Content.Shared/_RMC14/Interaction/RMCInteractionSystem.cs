using Content.Shared.Interaction.Events;
using Content.Shared.Light.Components;
using Content.Shared.Whitelist;

namespace Content.Shared._RMC14.Interaction;

public sealed partial class RMCInteractionSystem : EntitySystem
{
    [Dependency] private  EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<InteractedBlacklistComponent, GettingInteractedWithAttemptEvent>(OnBlacklistInteractionAttempt);
    }

    private void OnBlacklistInteractionAttempt(Entity<InteractedBlacklistComponent> ent, ref GettingInteractedWithAttemptEvent args)
    {
        if (args.Cancelled || ent.Comp.Blacklist == null)
            return;

        if (!TryComp(ent, out TransformComponent? xform))
            return;

        if (ent.Comp.AnchoredOnly && !xform.Anchored)
            return;

        if (TryComp(ent, out HandheldLightComponent? handheldLight) && handheldLight.Activated)
            return;

        if (_whitelist.IsValid(ent.Comp.Blacklist, args.Uid))
            args.Cancelled = true;
    }
}
