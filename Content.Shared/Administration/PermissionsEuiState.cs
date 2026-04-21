using Content.Shared.Eui;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Administration
{
    [Serializable, NetSerializable]
    public sealed class PermissionsEuiState : EuiStateBase
    {
        public bool IsLoading;
        public DateTime ServerTimeUtc;
        public PermissionsNoticeCode NoticeCode;
        public string? NoticeSubject;

        public AdminData[] Admins = Array.Empty<AdminData>();
        public Dictionary<int, AdminRankData> AdminRanks = new();

        [Serializable, NetSerializable]
        public struct AdminData
        {
            public NetUserId UserId;
            public string? UserName;
            public string? Title;
            public bool Suspended;
            public bool Deadminned;
            public bool IsOnline;
            public bool IsHost;
            public bool CanModify;
            public byte EffectiveHierarchyLevel;
            public DateTime? LastSeenTimeUtc;
            public DateTime? OnlineSinceUtc;
            public uint Revision;
            public AdminFlags PosFlags;
            public AdminFlags NegFlags;
            public int? RankId;
        }

        [Serializable, NetSerializable]
        public struct AdminRankData
        {
            public string Name;
            public byte HierarchyLevel;
            public bool CanModify;
            public bool CanAssign;
            public int AssignedAdminCount;
            public uint Revision;
            public AdminFlags Flags;
        }
    }

    [Serializable, NetSerializable]
    public enum PermissionsNoticeCode : byte
    {
        None,
        StaleAdmin,
        StaleRank,
        ProtectedAdmin,
        ProtectedRank,
        InvalidHierarchy,
        RankNotAssignable,
        UnknownUser,
        AlreadyExists,
    }

    public static class PermissionsEuiMsg
    {
        [Serializable, NetSerializable]
        public sealed class AddAdmin : EuiMessageBase
        {
            public string UserNameOrId = string.Empty;
            public string? Title;
            public AdminFlags PosFlags;
            public AdminFlags NegFlags;
            public int? RankId;
            public bool Suspended;
        }

        [Serializable, NetSerializable]
        public sealed class RemoveAdmin : EuiMessageBase
        {
            public NetUserId UserId;
            public uint ExpectedRevision;
        }

        [Serializable, NetSerializable]
        public sealed class UpdateAdmin : EuiMessageBase
        {
            public NetUserId UserId;
            public string? Title;
            public AdminFlags PosFlags;
            public AdminFlags NegFlags;
            public int? RankId;
            public bool Suspended;
            public uint ExpectedRevision;
        }


        [Serializable, NetSerializable]
        public sealed class AddAdminRank : EuiMessageBase
        {
            public string Name = string.Empty;
            public byte HierarchyLevel = AdminHierarchy.DefaultHierarchyLevel;
            public AdminFlags Flags;
        }

        [Serializable, NetSerializable]
        public sealed class RemoveAdminRank : EuiMessageBase
        {
            public int Id;
            public uint ExpectedRevision;
        }

        [Serializable, NetSerializable]
        public sealed class UpdateAdminRank : EuiMessageBase
        {
            public int Id;

            public string Name = string.Empty;
            public byte HierarchyLevel = AdminHierarchy.DefaultHierarchyLevel;
            public AdminFlags Flags;
            public uint ExpectedRevision;
        }
    }
}
