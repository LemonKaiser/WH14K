using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WH40K.MetaProgress;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Preferences;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Preferences.Managers
{
    public interface IServerPreferencesManager
    {
        void Init();

        Task LoadData(ICommonSession session, CancellationToken cancel);
        void FinishLoad(ICommonSession session);
        void OnClientDisconnected(ICommonSession session);

        bool TryGetCachedPreferences(NetUserId userId, [NotNullWhen(true)] out PlayerPreferences? playerPreferences);
        PlayerPreferences GetPreferences(NetUserId userId);
        PlayerPreferences? GetPreferencesOrNull(NetUserId? userId);
        IEnumerable<KeyValuePair<NetUserId, HumanoidCharacterProfile>> GetSelectedProfilesForPlayers(List<NetUserId> userIds);
        bool HavePreferencesLoaded(ICommonSession session);

        Task SetProfile(NetUserId userId, int slot, HumanoidCharacterProfile profile);
        Task SetConstructionFavorites(NetUserId userId, List<ProtoId<ConstructionPrototype>> favorites);
        Task<WH40KMetaProfileRepairResult> RevalidateWH40KMetaLoadoutsAsync(NetUserId userId, WH40KMetaProgressSnapshot snapshot, CancellationToken cancel = default);
        Task<WH40KMetaProfileRepairResult> ResetWH40KMetaSelectionsAsync(NetUserId userId, WH40KMetaProgressSnapshot snapshot, CancellationToken cancel = default);
    }
}
