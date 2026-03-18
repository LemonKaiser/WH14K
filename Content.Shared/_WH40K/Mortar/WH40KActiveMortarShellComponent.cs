using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Shared._WH40K.Mortar;

[RegisterComponent]
public sealed partial class WH40KActiveMortarShellComponent : Component
{
    [DataField]
    public EntityCoordinates Coordinates;

    [DataField]
    public TimeSpan WarnAt;

    [DataField]
    public bool Warned;

    [DataField]
    public TimeSpan ImpactWarnAt;

    [DataField]
    public bool ImpactWarned;

    [DataField]
    public TimeSpan LandAt;

    [DataField]
    public SoundSpecifier? WarnSound;
}
