namespace Content.Server._WH40K.MetaProgress;

[RegisterComponent]
public sealed partial class WH40KCharacterDevelopmentAbilityStateComponent : Component
{
    [DataField]
    public TimeSpan NextStomachImpulseTime = TimeSpan.Zero;

    [DataField]
    public TimeSpan NextKidneyPurgeReadyTime = TimeSpan.Zero;

    [DataField]
    public TimeSpan NextWarFurnaceReadyTime = TimeSpan.Zero;
}
