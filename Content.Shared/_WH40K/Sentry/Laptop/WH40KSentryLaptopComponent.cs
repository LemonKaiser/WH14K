using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Sentry.Laptop;

[RegisterComponent]
public sealed partial class WH40KSentryLaptopComponent : Component
{
    [DataField]
    public int MaxLinkedTurrets = 8;

    [DataField]
    public bool RequireTeam = true;

    [DataField]
    public List<string> AllowedTeamIds = new();

    [DataField]
    public List<string> IffTeamOptions = new() { "Imperium", "Heretics" };

    [DataField]
    public int AlertHistoryLimit = 12;

    [DataField]
    public float LowAmmoAlertThreshold = 0.25f;

    [DataField]
    public float CriticalHealthAlertThreshold = 0.35f;

    [DataField]
    public float AlertCooldownSeconds = 8f;

    [DataField]
    public HashSet<EntityUid> LinkedTurrets = new();
}

[Serializable, NetSerializable]
public enum WH40KSentryLaptopUiKey : byte
{
    Key,
}
