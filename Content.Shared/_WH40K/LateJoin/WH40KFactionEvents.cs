using System;
using System.Collections.Generic;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.LateJoin;

[Serializable, NetSerializable]
public enum WH40KFactionSelectionPurpose : byte
{
    Preview,
    LobbyReady,
    LateJoin,
}

[Serializable, NetSerializable]
public sealed class WH40KRequestFactionsEvent : EntityEventArgs
{
    public WH40KFactionSelectionPurpose Purpose { get; }

    public WH40KRequestFactionsEvent(WH40KFactionSelectionPurpose purpose = WH40KFactionSelectionPurpose.Preview)
    {
        Purpose = purpose;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KFactionsEvent : EntityEventArgs
{
    public WH40KFactionSelectionPurpose Purpose { get; }
    public List<WH40KFactionInfo> Factions { get; }

    public WH40KFactionsEvent(WH40KFactionSelectionPurpose purpose, List<WH40KFactionInfo> factions)
    {
        Purpose = purpose;
        Factions = factions;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KSelectFactionEvent : EntityEventArgs
{
    public string FactionId { get; }
    public WH40KFactionSelectionPurpose Purpose { get; }

    public WH40KSelectFactionEvent(string factionId, WH40KFactionSelectionPurpose purpose)
    {
        FactionId = factionId;
        Purpose = purpose;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCancelFactionSelectionEvent : EntityEventArgs
{
    public WH40KFactionSelectionPurpose Purpose { get; }

    public WH40KCancelFactionSelectionEvent(WH40KFactionSelectionPurpose purpose)
    {
        Purpose = purpose;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KFactionSelectionResultEvent : EntityEventArgs
{
    public WH40KFactionSelectionPurpose Purpose { get; }
    public string? FactionId { get; }
    public bool Accepted { get; }
    public string? MessageLocKey { get; }
    public List<WH40KFactionInfo> Factions { get; }

    public WH40KFactionSelectionResultEvent(
        WH40KFactionSelectionPurpose purpose,
        string? factionId,
        bool accepted,
        string? messageLocKey,
        List<WH40KFactionInfo> factions)
    {
        Purpose = purpose;
        FactionId = factionId;
        Accepted = accepted;
        MessageLocKey = messageLocKey;
        Factions = factions;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KFactionInfo
{
    public string Id { get; }
    public LocId Name { get; }
    public SpriteSpecifier? Logo { get; }
    public List<ProtoId<DepartmentPrototype>> Departments { get; }
    public int PlayerCount { get; }
    public bool CanSelect { get; }
    public string? DisabledReason { get; }
    public int DisabledReasonCount { get; }

    public WH40KFactionInfo(
        string id,
        LocId name,
        SpriteSpecifier? logo,
        List<ProtoId<DepartmentPrototype>> departments,
        int playerCount = 0,
        bool canSelect = true,
        string? disabledReason = null,
        int disabledReasonCount = 0)
    {
        Id = id;
        Name = name;
        Logo = logo;
        Departments = departments;
        PlayerCount = Math.Max(0, playerCount);
        CanSelect = canSelect;
        DisabledReason = disabledReason;
        DisabledReasonCount = Math.Max(0, disabledReasonCount);
    }
}
