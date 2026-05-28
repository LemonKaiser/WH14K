using Content.Shared._WH40K.Mortar;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._WH40K.Mortar;

public sealed partial class WH40KMortarSystem : EntitySystem
{
    [Dependency] private  AnimationPlayerSystem _animation = default!;
    [Dependency] private  SharedPointLightSystem _lights = default!;

    private const string AnimationKey = "wh40k_mortar_fire";
    private const string LightAnimationKey = "wh40k_mortar_fire_light";

    public override void Initialize()
    {
        SubscribeAllEvent<WH40KMortarFiredEvent>(OnMortarFired);
    }

    private void OnMortarFired(WH40KMortarFiredEvent ev)
    {
        if (!TryGetEntity(ev.Mortar, out var mortarUid))
            return;

        if (_animation.HasRunningAnimation(mortarUid.Value, AnimationKey))
            return;

        PlayFireLight(mortarUid.Value);

        _animation.Play(
            mortarUid.Value,
            new Animation
            {
                Length = TimeSpan.FromSeconds(0.3),
                AnimationTracks =
                {
                    new AnimationTrackSpriteFlick
                    {
                        LayerKey = "mortar",
                        KeyFrames =
                        {
                            new AnimationTrackSpriteFlick.KeyFrame("mortar_m402_fire", 0f),
                            new AnimationTrackSpriteFlick.KeyFrame("mortar_m402", 0.3f),
                        },
                    },
                },
            },
            AnimationKey);
    }

    private void PlayFireLight(EntityUid mortarUid)
    {
        if (!TryComp(mortarUid, out PointLightComponent? light))
        {
            light = Factory.GetComponent<PointLightComponent>();
            light.NetSyncEnabled = false;
            AddComp(mortarUid, light);
        }

        _lights.SetEnabled(mortarUid, true, light);
        _lights.SetRadius(mortarUid, 2.2f, light);
        _lights.SetColor(mortarUid, Color.FromHex("#ff8a1a"), light);
        _lights.SetEnergy(mortarUid, 4.5f, light);

        var anim = new Animation
        {
            Length = TimeSpan.FromSeconds(0.24f),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.Energy),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(4.5f, 0f),
                        new AnimationTrackProperty.KeyFrame(0f, 0.24f),
                    },
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.AnimatedEnable),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(true, 0f),
                        new AnimationTrackProperty.KeyFrame(false, 0.24f),
                    },
                },
            },
        };

        var animationPlayer = EnsureComp<AnimationPlayerComponent>(mortarUid);
        _animation.Stop(mortarUid, animationPlayer, LightAnimationKey);
        _animation.Play((mortarUid, animationPlayer), anim, LightAnimationKey);
    }
}
