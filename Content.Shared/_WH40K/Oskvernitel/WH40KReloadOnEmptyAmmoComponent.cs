using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared._WH40K.Oskvernitel;

[RegisterComponent]
public sealed partial class WH40KReloadOnEmptyAmmoComponent : Component
{
    [DataField("reloadCooldown")]
    public float ReloadCooldown = 30f;

    [ViewVariables]
    public TimeSpan? NextReloadTime;
}
