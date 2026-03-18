using System;
using System.Collections.Generic;
using Robust.Shared.Audio;

namespace Content.Shared._WH40K.Fulton;

[RegisterComponent]
public sealed partial class WH40KTacticalFultonComponent : Component
{
    [DataField]
    public TimeSpan AttachDelay = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan ExtractionDelay = TimeSpan.FromSeconds(12);

    [DataField]
    public TimeSpan ExtractedCleanupDelay = TimeSpan.FromSeconds(1.5);

    [DataField]
    public TimeSpan FailedCleanupDelay = TimeSpan.FromSeconds(6);

    [DataField]
    public bool RequireTeam = true;

    [DataField]
    public List<string> AllowedTeamIds = new();

    [DataField]
    public bool AllowDeadBodies = true;

    [DataField]
    public bool DenyFriendlyCorpseReward = true;

    [DataField]
    public int DefaultCorpseFrontReward = 4;

    [DataField]
    public int DefaultCorpseCommandReward = 4;

    [DataField]
    public TimeSpan UserCooldown = TimeSpan.FromSeconds(8);

    [DataField]
    public TimeSpan RateLimitWindow = TimeSpan.FromSeconds(45);

    [DataField]
    public int MaxUsesPerWindow = 3;

    [DataField]
    public int MaxPendingExtractionsPerTeam = 4;

    [DataField]
    public SoundSpecifier? AttachSound = new SoundPathSpecifier("/Audio/Items/Mining/fultext_deploy.ogg");

    [DataField]
    public SoundSpecifier? ExtractedSound = new SoundPathSpecifier("/Audio/Items/Mining/fultext_launch.ogg");

    [DataField]
    public SoundSpecifier? FailedSound = new SoundPathSpecifier("/Audio/Items/welder.ogg");
}
