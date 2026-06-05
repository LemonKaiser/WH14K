using Content.Shared._WH40K.GameTicking.Rules;

namespace Content.Server._WH40K.GameTicking.Rules.Components;

[RegisterComponent]
public sealed partial class WH40KFactionRecruiterComponent : Component
{
    [DataField]
    public TimeSpan DoAfter = TimeSpan.FromSeconds(5);

    [DataField]
    public int RewardMultiplier = 3;
}
