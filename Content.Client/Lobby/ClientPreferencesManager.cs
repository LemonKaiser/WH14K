using System.Linq;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Client;
using Robust.Client.Player;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Lobby
{
    /// <summary>
    ///     Receives <see cref="PlayerPreferences" /> and <see cref="GameSettings" /> from the server during the initial
    ///     connection.
    ///     Stores preferences on the server through <see cref="SelectCharacter" /> and <see cref="UpdateCharacter" />.
    /// </summary>
    public sealed class ClientPreferencesManager : IClientPreferencesManager
    {
        [Dependency] private readonly IClientNetManager _netManager = default!;
        [Dependency] private readonly IBaseClient _baseClient = default!;
        [Dependency] private readonly ILocalizationManager _loc = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;

        public event Action? OnServerDataLoaded;

        public GameSettings Settings { get; private set; } = default!;
        public PlayerPreferences Preferences { get; private set; } = default!;

        public void Initialize()
        {
            _netManager.RegisterNetMessage<MsgPreferencesAndSettings>(HandlePreferencesAndSettings);
            _netManager.RegisterNetMessage<MsgUpdateCharacter>();
            _netManager.RegisterNetMessage<MsgSelectCharacter>();
            _netManager.RegisterNetMessage<MsgDeleteCharacter>();

            _baseClient.RunLevelChanged += BaseClientOnRunLevelChanged;
        }

        private void BaseClientOnRunLevelChanged(object? sender, RunLevelChangedEventArgs e)
        {
            if (e.NewLevel == ClientRunLevel.Initialize)
            {
                Settings = default!;
                Preferences = default!;
            }
        }

        public void SelectCharacter(HumanoidCharacterProfile profile)
        {
            SelectCharacter(Preferences.IndexOfCharacter(profile));
        }

        public void SelectCharacter(int slot)
        {
            Preferences = new PlayerPreferences(Preferences.Characters, slot, Preferences.AdminOOCColor, Preferences.ConstructionFavorites);
            var msg = new MsgSelectCharacter
            {
                SelectedCharacterIndex = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        public void UpdateCharacter(HumanoidCharacterProfile profile, int slot)
        {
            var collection = IoCManager.Instance!;
            profile.EnsureValid(_playerManager.LocalSession!, collection);
            var characters = new Dictionary<int, HumanoidCharacterProfile>(Preferences.Characters) {[slot] = profile};
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, Preferences.ConstructionFavorites);
            var msg = new MsgUpdateCharacter
            {
                Profile = profile,
                Slot = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        public void CreateCharacter(HumanoidCharacterProfile profile)
        {
            var characters = new Dictionary<int, HumanoidCharacterProfile>(Preferences.Characters);
            var lowest = Enumerable.Range(0, Settings.MaxCharacterSlots)
                .Except(characters.Keys)
                .FirstOrNull();

            if (lowest == null)
            {
                throw new InvalidOperationException("Out of character slots!");
            }

            var l = lowest.Value;
            characters.Add(l, profile);
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, Preferences.ConstructionFavorites);

            UpdateCharacter(profile, l);
        }

        public void DeleteCharacter(HumanoidCharacterProfile profile)
        {
            DeleteCharacter(Preferences.IndexOfCharacter(profile));
        }

        public void DeleteCharacter(int slot)
        {
            var characters = Preferences.Characters.Where(p => p.Key != slot);
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, Preferences.ConstructionFavorites);
            var msg = new MsgDeleteCharacter
            {
                Slot = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        public void UpdateConstructionFavorites(List<ProtoId<ConstructionPrototype>> favorites)
        {
            Preferences = new PlayerPreferences(Preferences.Characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, favorites);
            var msg = new MsgUpdateConstructionFavorites
            {
                Favorites = favorites
            };
            _netManager.ClientSendMessage(msg);
        }

        private void HandlePreferencesAndSettings(MsgPreferencesAndSettings message)
        {
            Preferences = message.Preferences;
            Settings = message.Settings;

            ApplyLanguageAwareInitialNameFixes(message.NewlyInitialized);

            OnServerDataLoaded?.Invoke();
        }

        private void ApplyLanguageAwareInitialNameFixes(bool repairScriptMismatch)
        {
            var culture = _loc.DefaultCulture;
            var updatedCharacters = new Dictionary<int, HumanoidCharacterProfile>(Preferences.Characters);
            var changed = new List<KeyValuePair<int, HumanoidCharacterProfile>>();

            foreach (var (slot, profile) in Preferences.Characters)
            {
                var repairedName = profile.Name;
                var hadBrokenDatasetId = HumanoidNameScriptHelper.ContainsUnresolvedDatasetId(repairedName);
                if (hadBrokenDatasetId)
                {
                    repairedName = HumanoidNameScriptHelper.ResolveUnresolvedDatasetIds(repairedName);

                    if (HumanoidNameScriptHelper.ContainsUnresolvedDatasetId(repairedName))
                        repairedName = HumanoidCharacterProfile.GetName(profile.Species, profile.Gender);
                }

                var hasScriptMismatch = repairScriptMismatch &&
                    (!HumanoidNameScriptHelper.MatchesPreferredScript(repairedName, culture) ||
                     HumanoidNameScriptHelper.IsMixedScript(repairedName));

                if (!hadBrokenDatasetId && !hasScriptMismatch)
                    continue;

                if (hasScriptMismatch)
                    repairedName = HumanoidCharacterProfile.GetName(profile.Species, profile.Gender);

                if (string.Equals(repairedName, profile.Name, StringComparison.Ordinal))
                    continue;

                var updatedProfile = profile.WithName(repairedName);
                updatedCharacters[slot] = updatedProfile;
                changed.Add(new KeyValuePair<int, HumanoidCharacterProfile>(slot, updatedProfile));
            }

            if (changed.Count == 0)
                return;

            Preferences = new PlayerPreferences(
                updatedCharacters,
                Preferences.SelectedCharacterIndex,
                Preferences.AdminOOCColor,
                Preferences.ConstructionFavorites);

            foreach (var (slot, profile) in changed)
            {
                var msg = new MsgUpdateCharacter
                {
                    Profile = profile,
                    Slot = slot
                };

                _netManager.ClientSendMessage(msg);
            }
        }
    }
}
