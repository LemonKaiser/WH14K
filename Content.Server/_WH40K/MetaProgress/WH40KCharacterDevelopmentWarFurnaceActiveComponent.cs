namespace Content.Server._WH40K.MetaProgress;

[RegisterComponent]
public sealed partial class WH40KCharacterDevelopmentWarFurnaceActiveComponent : Component
{
    [DataField]
    public TimeSpan ExpiresAt = TimeSpan.Zero;

    [DataField]
    public TimeSpan NextTickAt = TimeSpan.Zero;
}
