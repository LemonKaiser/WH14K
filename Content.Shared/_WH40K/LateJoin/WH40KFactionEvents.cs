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
public sealed class WH40KRequestFactionsEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class WH40KFactionsEvent : EntityEventArgs
{
    public List<WH40KFactionInfo> Factions { get; }

    public WH40KFactionsEvent(List<WH40KFactionInfo> factions)
    {
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

    public WH40KFactionInfo(
        string id,
        LocId name,
        SpriteSpecifier? logo,
        List<ProtoId<DepartmentPrototype>> departments)
    {
        Id = id;
        Name = name;
        Logo = logo;
        Departments = departments;
    }
}
