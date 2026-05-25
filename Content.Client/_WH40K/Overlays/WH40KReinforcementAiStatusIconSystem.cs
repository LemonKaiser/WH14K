using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Content.Shared._WH40K.Command;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Overlays;

public sealed class WH40KReinforcementAiStatusIconSystem : EntitySystem
{
    private static readonly ProtoId<FactionIconPrototype> ReinforcementAiIcon = "WH40KReinforcementAiActiveIcon";

    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KReinforcementAiStatusIconComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<WH40KReinforcementAiStatusIconComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_prototype.TryIndex<FactionIconPrototype>(ReinforcementAiIcon, out var icon))
            args.StatusIcons.Add(icon);
    }
}
