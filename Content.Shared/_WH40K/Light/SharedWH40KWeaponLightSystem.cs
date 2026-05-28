using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.Light;

public sealed partial class SharedWH40KWeaponLightSystem : EntitySystem
{
    [Dependency] private  SharedAudioSystem _audio = default!;
    [Dependency] private  SharedPointLightSystem _lights = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KWeaponLightComponent, GetVerbsEvent<ActivationVerb>>(OnGetVerbs);
    }

    private void OnGetVerbs(Entity<WH40KWeaponLightComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!_lights.TryGetLight(ent, out var light))
            return;

        var user = args.User;

        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString(light.Enabled ? "item-toggle-deactivate" : "item-toggle-activate"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/light.svg.192dpi.png")),
            Act = () => ToggleLight(ent, user),
            Priority = -1,
        });
    }

    public void ToggleLight(Entity<WH40KWeaponLightComponent> ent, EntityUid? user = null)
    {
        if (!_lights.TryGetLight(ent.Owner, out var light))
            return;

        _lights.SetEnabled(ent.Owner, !light.Enabled, light);
        _audio.PlayPredicted(ent.Comp.ToggleSound, ent.Owner, user);
    }
}
