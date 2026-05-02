using System;

namespace Content.Server._WH40K.Audio;

[RegisterComponent]
public sealed partial class WH40KAmbientFieldEmitterComponent : Component
{
    public bool Loop;
    public TimeSpan NextOneShotAt = TimeSpan.Zero;
}
