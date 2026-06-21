using Content.Shared._WH40K.ArmorPlates;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.ArmorPlates;

public sealed partial class WH40KArmorPlateHolderVisualizerSystem : VisualizerSystem<WH40KArmorPlateHolderComponent>
{
    [Dependency] private SpriteSystem _sprite = default!;

    private const string OverlayLayerKey = "wh40k-armor-plate-overlay";

    protected override void OnAppearanceChange(EntityUid uid, WH40KArmorPlateHolderComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        var layer = _sprite.LayerMapReserve((uid, args.Sprite), OverlayLayerKey);

        if (!args.AppearanceData.TryGetValue(WH40KArmorPlateVisuals.OverlayVisible, out var visibleObj) ||
            visibleObj is not bool visible ||
            !visible ||
            !args.AppearanceData.TryGetValue(WH40KArmorPlateVisuals.OverlayType, out var typeObj) ||
            typeObj is not WH40KArmorPlateType plateType)
        {
            _sprite.LayerSetVisible((uid, args.Sprite), layer, false);
            return;
        }

        _sprite.LayerSetVisible((uid, args.Sprite), layer, true);
        _sprite.LayerSetRsi(
            (uid, args.Sprite),
            layer,
            new ResPath(WH40KArmorPlateHelper.GetPlateTexturePath(plateType)),
            new RSI.StateId("inventory"));
    }
}
