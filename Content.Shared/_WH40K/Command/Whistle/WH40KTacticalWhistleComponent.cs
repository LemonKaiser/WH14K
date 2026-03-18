using System;
using System.Collections.Generic;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._WH40K.Command.Whistle;

[RegisterComponent]
public sealed partial class WH40KTacticalWhistleComponent : Component
{
    [DataField]
    public string UseDelayId = "wh40k-whistle-signal";

    [DataField]
    public TimeSpan SignalDelay = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan UserCooldown = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan RateLimitWindow = TimeSpan.FromSeconds(20);

    [DataField]
    public int MaxSignalsPerWindow = 4;

    [DataField]
    public bool RequireTeam = true;

    [DataField]
    public List<string> AllowedTeamIds = new();

    [DataField]
    public bool EnableSignalVerbs = true;

    [DataField]
    public bool EnableTacticalVariants = true;

    [DataField]
    public EntProtoId RegroupMarkerPrototype = "WH40KWhistlePulseRegroupMarker";

    [DataField]
    public EntProtoId AttackMarkerPrototype = "WH40KWhistlePulseAttackMarker";

    [DataField]
    public EntProtoId RetreatMarkerPrototype = "WH40KWhistlePulseRetreatMarker";

    [DataField]
    public float RegroupRadius = 6f;

    [DataField]
    public float AttackRadius = 5.5f;

    [DataField]
    public float RetreatRadius = 5.5f;

    [DataField]
    public SoundSpecifier? SignalSound = new SoundCollectionSpecifier("TrenchWhistle");
}

public enum WH40KWhistleSignalType : byte
{
    Regroup = 0,
    Attack = 1,
    Retreat = 2,
}
