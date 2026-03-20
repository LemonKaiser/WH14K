using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Squads;

[Serializable, NetSerializable]
public enum WH40KSquadUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class WH40KSquadMemberEntry
{
    public NetEntity Entity { get; }
    public string Name { get; }
    public string RoleName { get; }
    public bool Alive { get; }

    public WH40KSquadMemberEntry(NetEntity entity, string name, string roleName, bool alive)
    {
        Entity = entity;
        Name = name;
        RoleName = roleName;
        Alive = alive;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KSquadSlotEntry
{
    public byte SlotIndex { get; }
    public NetEntity Entity { get; }
    public string Name { get; }
    public string RoleName { get; }
    public bool Occupied { get; }
    public bool Alive { get; }

    public WH40KSquadSlotEntry(
        byte slotIndex,
        NetEntity entity,
        string name,
        string roleName,
        bool occupied,
        bool alive)
    {
        SlotIndex = slotIndex;
        Entity = entity;
        Name = name;
        RoleName = roleName;
        Occupied = occupied;
        Alive = alive;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KSquadBuiState : BoundUserInterfaceState
{
    public string TeamId { get; }
    public string AccentHex { get; }
    public bool SquadActive { get; }
    public string LeaderName { get; }
    public string LeaderRoleName { get; }
    public bool LeaderAlive { get; }
    public int MemberCount { get; }
    public int MaxMembers { get; }
    public int AvailableCount { get; }
    public WH40KSquadSlotEntry[] Slots { get; }
    public WH40KSquadMemberEntry[] Candidates { get; }

    public WH40KSquadBuiState(
        string teamId,
        string accentHex,
        bool squadActive,
        string leaderName,
        string leaderRoleName,
        bool leaderAlive,
        int memberCount,
        int maxMembers,
        int availableCount,
        WH40KSquadSlotEntry[] slots,
        WH40KSquadMemberEntry[] candidates)
    {
        TeamId = teamId;
        AccentHex = accentHex;
        SquadActive = squadActive;
        LeaderName = leaderName;
        LeaderRoleName = leaderRoleName;
        LeaderAlive = leaderAlive;
        MemberCount = memberCount;
        MaxMembers = maxMembers;
        AvailableCount = availableCount;
        Slots = slots ?? Array.Empty<WH40KSquadSlotEntry>();
        Candidates = candidates ?? Array.Empty<WH40KSquadMemberEntry>();
    }
}

[Serializable, NetSerializable]
public sealed class WH40KSquadCreateMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WH40KSquadDisbandMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WH40KSquadAssignMessage(NetEntity target) : BoundUserInterfaceMessage
{
    public NetEntity Target { get; } = target;
}

[Serializable, NetSerializable]
public sealed class WH40KSquadRemoveMessage(byte slotIndex) : BoundUserInterfaceMessage
{
    public byte SlotIndex { get; } = slotIndex;
}

[Serializable, NetSerializable]
public sealed class WH40KSquadRefreshMessage : BoundUserInterfaceMessage;
