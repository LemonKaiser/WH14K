using Content.Shared.Mech;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.Systems;
using Content.Shared.Movement.Components;
using Robust.Client.GameObjects;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client.Mech;

public sealed partial class MechSystem : SharedMechSystem
{
    [Dependency] private  SpriteSystem _sprite = default!;
    private const string MovementLayerKey = "movement";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MechComponent, MechToggleEquipmentEvent>(OnToggleEquipmentAction);
        SubscribeLocalEvent<MechComponent, AppearanceChangeEvent>(OnAppearanceChanged);
    }

    private void OnToggleEquipmentAction(Entity<MechComponent> ent, ref MechToggleEquipmentEvent args)
    {
        if (args.Handled)
            return;

        RaiseLocalEvent(ent.Owner, new MechOpenEquipmentRadialEvent());
        args.Handled = true;
    }

    private void OnAppearanceChanged(Entity<MechComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        var state = ent.Comp.BaseState;
        var drawDepth = DrawDepth.Mobs;

        var isBroken = false;
        var isOpen = false;

        if (args.AppearanceData.TryGetValue(MechVisuals.Broken, out var brokenObj) && brokenObj is bool brokenFlag)
            isBroken = brokenFlag;
        if (args.AppearanceData.TryGetValue(MechVisuals.Open, out var openObj) && openObj is bool openFlag)
            isOpen = openFlag;

        // Priority: Broken > Open > Base
        if (ent.Comp.BrokenState != null && isBroken)
        {
            state = ent.Comp.BrokenState;
            drawDepth = DrawDepth.SmallMobs;
        }
        else if (ent.Comp.OpenState != null && isOpen)
        {
            state = ent.Comp.OpenState;
            drawDepth = DrawDepth.SmallMobs;
        }

        var preserveMovementState = false;
        if (!isBroken &&
            !isOpen &&
            TryComp<SpriteMovementComponent>(ent.Owner, out var spriteMovement) &&
            spriteMovement.IsMoving &&
            _sprite.LayerMapTryGet((ent.Owner, args.Sprite), MechVisualLayers.Base, out var baseLayer, false) &&
            _sprite.LayerMapTryGet((ent.Owner, args.Sprite), MovementLayerKey, out var movementLayer, false) &&
            baseLayer == movementLayer)
        {
            preserveMovementState = true;
        }

        _sprite.LayerSetVisible((ent.Owner, args.Sprite), MechVisualLayers.Base, true);
        _sprite.LayerSetAutoAnimated((ent.Owner, args.Sprite), MechVisualLayers.Base, true);

        if (!preserveMovementState)
            _sprite.LayerSetRsiState((ent.Owner, args.Sprite), MechVisualLayers.Base, state);

        _sprite.SetDrawDepth((ent.Owner, args.Sprite), (int)drawDepth);
    }

}
