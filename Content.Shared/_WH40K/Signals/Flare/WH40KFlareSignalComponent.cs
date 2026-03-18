using System;
using System.Collections.Generic;
using Robust.Shared.Audio;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._WH40K.Signals.Flare;

[RegisterComponent]
public sealed partial class WH40KFlareSignalComponent : Component
{
    [DataField]
    public bool RequireTeam = true;

    [DataField]
    public List<string> AllowedTeamIds = new();

    [DataField]
    public EntProtoId MarkerPrototype = "WH40KSignalFlareMarker";

    [DataField]
    public TimeSpan MarkerLifetime = TimeSpan.FromSeconds(50);

    [DataField]
    public int GroundedSampleCount = 8;

    [DataField]
    public float GroundedTolerance = 0.08f;

    [DataField]
    public TimeSpan UserCooldown = TimeSpan.FromSeconds(6);

    [DataField]
    public TimeSpan RateLimitWindow = TimeSpan.FromSeconds(45);

    [DataField]
    public int MaxSignalsPerWindow = 3;

    [DataField]
    public int MaxActiveMarkersPerTeam = 3;

    [DataField]
    public string MarkerLabel = "wh40k-signal-flare-marker-label";

    [DataField]
    public float MarkerRadius = 6f;

    [DataField]
    public Color MarkerColor = Color.FromHex("#7CFF95");

    [DataField]
    public SoundSpecifier? ActivateSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
}
