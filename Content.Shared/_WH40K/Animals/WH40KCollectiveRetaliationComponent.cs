namespace Content.Shared._WH40K.Animals;

/// <summary>
/// Nearby mobs with the same herd id will retaliate together when one of them is attacked.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KCollectiveRetaliationComponent : Component
{
    [DataField("herdId", required: true)]
    public string HerdId = string.Empty;

    [DataField("radius")]
    public float Radius = 7f;
}
