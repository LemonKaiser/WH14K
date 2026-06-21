using Content.Client.Items;
using Content.Shared._WH40K.ArmorPlates;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.ArmorPlates;

public sealed partial class WH40KArmorPlateSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    private const string TierLayerKey = "wh40k-armor-plate-tier";
    private static readonly ResPath TierOverlayRsi = new("/Textures/_WH40K/Objects/ArmorPlates/tiers.rsi");

    public override void Initialize()
    {
        base.Initialize();

        Subs.ItemStatus<WH40KArmorPlateComponent>(ent => new WH40KArmorPlateStatusControl(ent));

        SubscribeLocalEvent<WH40KArmorPlateComponent, ComponentStartup>(OnPlateStartup);
        SubscribeLocalEvent<WH40KArmorPlateComponent, AfterAutoHandleStateEvent>(OnPlateHandleState);
    }

    private void OnPlateStartup(Entity<WH40KArmorPlateComponent> ent, ref ComponentStartup args)
    {
        UpdateTierOverlay(ent);
    }

    private void OnPlateHandleState(Entity<WH40KArmorPlateComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateTierOverlay(ent);
    }

    private void UpdateTierOverlay(Entity<WH40KArmorPlateComponent> ent)
    {
        if (!TryComp(ent.Owner, out SpriteComponent? sprite))
            return;

        var layer = _sprite.LayerMapReserve((ent.Owner, sprite), TierLayerKey);
        _sprite.LayerSetVisible((ent.Owner, sprite), layer, true);
        _sprite.LayerSetRsi(
            (ent.Owner, sprite),
            layer,
            TierOverlayRsi,
            new RSI.StateId(WH40KArmorPlateHelper.GetTierOverlayState(ent.Comp.Tier)));
    }
}
