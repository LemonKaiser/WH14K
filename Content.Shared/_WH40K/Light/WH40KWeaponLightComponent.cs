using Robust.Shared.Audio;

namespace Content.Shared._WH40K.Light;

[RegisterComponent]
public sealed partial class WH40KWeaponLightComponent : Component
{
    [DataField("toggleSound")]
    public SoundSpecifier ToggleSound = new SoundPathSpecifier("/Audio/Items/flashlight_pda.ogg");
}
