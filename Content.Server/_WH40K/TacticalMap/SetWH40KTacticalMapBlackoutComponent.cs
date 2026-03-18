namespace Content.Server._WH40K.TacticalMap;

/// <summary>
/// Applies a tactical-map blackout flag to the tile this marker is placed on, then deletes itself.
/// </summary>
[RegisterComponent]
public sealed partial class SetWH40KTacticalMapBlackoutComponent : Component
{
    [DataField(required: true)]
    public bool Value;
}
