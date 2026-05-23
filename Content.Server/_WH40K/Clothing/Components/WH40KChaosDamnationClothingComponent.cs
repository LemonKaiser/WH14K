using System;
using Content.Server._WH40K.Clothing;

namespace Content.Server._WH40K.Clothing.Components;

[RegisterComponent, Access(typeof(WH40KChaosDamnationClothingSystem))]
public sealed partial class WH40KChaosDamnationClothingComponent : Component
{
    [DataField]
    public float DelaySeconds = 2f;

    [DataField]
    public float FireStacks = 1f;

    [DataField]
    public float TickIntervalSeconds = 1f;

    [DataField]
    public string TargetTeamId = "Imperium";

    public EntityUid? PendingWearer;

    public TimeSpan NextIgniteAt = TimeSpan.Zero;
}
