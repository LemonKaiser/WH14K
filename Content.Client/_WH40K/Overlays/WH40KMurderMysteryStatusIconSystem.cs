using Content.Shared.Ghost;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Content.Shared._WH40K.MurderMystery;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Overlays;

public sealed partial class WH40KMurderMysteryStatusIconSystem : EntitySystem
{
    private static readonly ProtoId<FactionIconPrototype> MurderIcon = "WH40KMurderMysteryMurderIcon";

    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KMurderMysteryMurderIconComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<WH40KMurderMysteryMurderIconComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_player.LocalSession?.AttachedEntity is not { Valid: true } viewer)
            return;

        if (HasComp<GhostComponent>(viewer) || !HasComp<WH40KMurderMysteryMurderIconComponent>(viewer))
            return;

        if (_prototype.Resolve(MurderIcon, out var icon))
            args.StatusIcons.Add(icon);
    }
}
