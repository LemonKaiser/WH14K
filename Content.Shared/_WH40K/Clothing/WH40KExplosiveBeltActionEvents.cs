using Content.Shared.Actions;
using JetBrains.Annotations;

namespace Content.Shared._WH40K.Clothing;

[UsedImplicitly]
public sealed partial class WH40KActivateExplosiveBeltActionEvent : InstantActionEvent
{
    [DataField("delaySeconds", required: true)]
    public float DelaySeconds { get; set; }
}
