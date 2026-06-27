namespace Content.Server._WH40K.MurderMystery;

using Robust.Shared.Network;

[RegisterComponent]
public sealed partial class WH40KMurderMysteryKnifeComponent : Component
{
    [ViewVariables]
    public NetUserId OwnerUserId;
}

[RegisterComponent]
public sealed partial class WH40KMurderMysterySheriffRevolverComponent : Component;

[RegisterComponent]
public sealed partial class WH40KMurderMysterySheriffBulletComponent : Component;
