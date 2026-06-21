using System.Collections.Generic;
using Content.Client.Weapons.Ranged.Systems;
using Content.Shared._WH40K.Weapons.Mods;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Weapons.Mods;

public sealed partial class WH40KWeaponModVisualizerSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedItemSystem _items = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private readonly Dictionary<EntityUid, PresentationRefreshState> _presentationStates = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KWeaponModHostComponent, AppearanceChangeEvent>(
            OnAppearanceChange,
            after: [typeof(GunSystem)]);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, ComponentShutdown>(OnHostShutdown);
    }

    private void OnAppearanceChange(Entity<WH40KWeaponModHostComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        var presentationActive = _appearance.TryGetData<bool>(
            ent.Owner,
            WH40KWeaponModVisuals.PresentationActive,
            out var active,
            args.Component) && active;

        var presentationState = _appearance.TryGetData<string>(
            ent.Owner,
            WH40KWeaponModVisuals.PresentationState,
            out var state,
            args.Component)
            ? state
            : string.Empty;

        RefreshHeldStatus(ent.Owner, presentationActive, presentationState);

        var overlaySprites = _appearance.TryGetData<Dictionary<string, string>>(
            ent.Owner,
            WH40KWeaponModVisuals.OverlaySprites,
            out var resolvedSprites,
            args.Component)
            ? resolvedSprites
            : null;

        var overlayStates = _appearance.TryGetData<Dictionary<string, string>>(
            ent.Owner,
            WH40KWeaponModVisuals.OverlayStates,
            out var resolvedStates,
            args.Component)
            ? resolvedStates
            : null;

        foreach (var definition in ent.Comp.SlotDefinitions)
        {
            var slotId = WH40KWeaponModHelper.GetSlotId(definition.Id);
            var layer = _sprite.LayerMapReserve(
                (ent.Owner, args.Sprite),
                WH40KWeaponModHelper.GetOverlayLayerKey(slotId));

            if (overlaySprites == null ||
                !overlaySprites.TryGetValue(slotId, out var spritePath) ||
                string.IsNullOrWhiteSpace(spritePath))
            {
                _sprite.LayerSetVisible((ent.Owner, args.Sprite), layer, false);
                continue;
            }

            var overlayState = "base";
            if (overlayStates != null &&
                overlayStates.TryGetValue(slotId, out var resolvedState) &&
                !string.IsNullOrWhiteSpace(resolvedState))
            {
                overlayState = resolvedState;
            }

            _sprite.LayerSetVisible((ent.Owner, args.Sprite), layer, true);
            _sprite.LayerSetRsi(
                (ent.Owner, args.Sprite),
                layer,
                new ResPath(spritePath),
                new RSI.StateId(overlayState));
        }
    }

    private void OnHostShutdown(Entity<WH40KWeaponModHostComponent> ent, ref ComponentShutdown args)
    {
        _presentationStates.Remove(ent.Owner);
    }

    private void RefreshHeldStatus(EntityUid uid, bool presentationActive, string presentationState)
    {
        var nextState = new PresentationRefreshState(presentationActive, presentationState);
        if (_presentationStates.TryGetValue(uid, out var previousState) &&
            previousState.Equals(nextState))
        {
            return;
        }

        _presentationStates[uid] = nextState;
        _items.VisualsChanged(uid);
    }

    private readonly record struct PresentationRefreshState(bool Active, string State);
}
