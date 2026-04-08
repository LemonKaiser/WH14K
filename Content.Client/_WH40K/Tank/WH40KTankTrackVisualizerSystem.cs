using Content.Shared._WH40K.Tank;
using Robust.Client.GameObjects;

namespace Content.Client._WH40K.Tank;

public sealed class WH40KTankTrackVisualizerSystem : VisualizerSystem<WH40KTankTrackVisualizerComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, WH40KTankTrackVisualizerComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!SpriteSystem.LayerExists((uid, args.Sprite), WH40KTankVisualLayers.Tracks))
            return;

        var state = WH40KTankVisualState.Idle;
        AppearanceSystem.TryGetData(uid, WH40KTankVisuals.State, out state, args.Component);

        var moving = state == WH40KTankVisualState.Moving;
        var spriteState = moving ? component.MovingState : component.IdleState;

        SpriteSystem.LayerSetRsiState((uid, args.Sprite), WH40KTankVisualLayers.Tracks, spriteState);
        SpriteSystem.LayerSetAutoAnimated((uid, args.Sprite), WH40KTankVisualLayers.Tracks, moving);
    }
}