using Robust.Shared.Audio;
using Robust.Shared.ViewVariables;

namespace Content.Shared._WH40K.Audio;

[RegisterComponent]
public sealed partial class WH40KAmbientFieldSourceComponent : Component
{
    [DataField("enabled")]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Enabled = true;

    [DataField("sound", required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier Sound = default!;

    [DataField("range")]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Range = 8f;

    [DataField("volume")]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Volume = -6f;

    [DataField("emitterSpacing")]
    [ViewVariables(VVAccess.ReadWrite)]
    public float EmitterSpacing = 6f;

    [DataField("loop")]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Loop = true;

    [DataField("oneShotMinInterval")]
    [ViewVariables(VVAccess.ReadWrite)]
    public float OneShotMinIntervalSeconds = 2f;

    [DataField("oneShotMaxInterval")]
    [ViewVariables(VVAccess.ReadWrite)]
    public float OneShotMaxIntervalSeconds = 4f;
}
