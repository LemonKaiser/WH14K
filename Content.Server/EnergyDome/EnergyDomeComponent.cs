namespace Content.Server.EnergyDome;

/// <summary>
/// Marker component for spawned energy domes, linking the dome back to its generator.
/// </summary>
[RegisterComponent, Access(typeof(EnergyDomeSystem))]
public sealed partial class EnergyDomeComponent : Component
{
    [DataField]
    public EntityUid? Generator;
}
