using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Shared.Administration.Logs;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Speech;
using Content.Shared._WH40K.Administration.Mute;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Utility;

namespace Content.Server.Database
{
    public abstract class ServerDbBase
    {
        private readonly ISawmill _opsLog;
        public event Action<DatabaseNotification>? OnNotificationReceived;
        private readonly ISerializationManager _serialization;

        /// <param name="opsLog">Sawmill to trace log database operations to.</param>
        public ServerDbBase(ISawmill opsLog, ISerializationManager serialization)
        {
            _serialization = serialization;
            _opsLog = opsLog;
        }

        #region Preferences
        public async Task<Preference?> GetPlayerPreferencesAsync(
            NetUserId userId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            return await db.DbContext
                .Preference
                .Include(p => p.Profiles).ThenInclude(h => h.Jobs)
                .Include(p => p.Profiles).ThenInclude(h => h.Antags)
                .Include(p => p.Profiles).ThenInclude(h => h.Traits)
                .Include(p => p.Profiles)
                    .ThenInclude(h => h.Loadouts)
                    .ThenInclude(l => l.Groups)
                    .ThenInclude(group => group.Loadouts)
                .AsSplitQuery()
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);
        }

        public async Task SaveSelectedCharacterIndexAsync(NetUserId userId, int index)
        {
            await using var db = await GetDb();

            await SetSelectedCharacterSlotAsync(userId, index, db.DbContext);

            await db.DbContext.SaveChangesAsync();
        }

        /// <summary>
        /// Only intended for use in unit tests - drops the organ marking data from a profile in the given slot
        /// </summary>
        /// <param name="userId">The user whose profile to modify</param>
        /// <param name="slot">The slot index to modify</param>
        public async Task MakeCharacterSlotLegacyAsync(NetUserId userId, int slot)
        {
            await using var db = await GetDb();

            var oldProfile = await db.DbContext.Profile
                .Include(p => p.Preference)
                .Where(p => p.Preference.UserId == userId.UserId)
                .AsSplitQuery()
                .SingleOrDefaultAsync(h => h.Slot == slot);

            if (oldProfile == null)
                return;

            oldProfile.OrganMarkings = null;
            oldProfile.Markings = JsonSerializer.SerializeToDocument(new List<string>());

            await db.DbContext.SaveChangesAsync();
        }

        public async Task SaveCharacterSlotAsync(NetUserId userId, HumanoidCharacterProfile? humanoid, int slot)
        {
            await using var db = await GetDb();

            if (humanoid is null)
            {
                await DeleteCharacterSlot(db.DbContext, userId, slot);
                await db.DbContext.SaveChangesAsync();
                return;
            }

            var oldProfile = db.DbContext.Profile
                .Include(p => p.Preference)
                .Where(p => p.Preference.UserId == userId.UserId)
                .Include(p => p.Jobs)
                .Include(p => p.Antags)
                .Include(p => p.Traits)
                .Include(p => p.Loadouts)
                    .ThenInclude(l => l.Groups)
                    .ThenInclude(group => group.Loadouts)
                .AsSplitQuery()
                .SingleOrDefault(h => h.Slot == slot);

            var newProfile = ConvertProfiles(humanoid, slot, oldProfile);
            if (oldProfile == null)
            {
                var prefs = await db.DbContext
                    .Preference
                    .Include(p => p.Profiles)
                    .SingleAsync(p => p.UserId == userId.UserId);

                prefs.Profiles.Add(newProfile);
            }

            await db.DbContext.SaveChangesAsync();
        }

        private static async Task DeleteCharacterSlot(ServerDbContext db, NetUserId userId, int slot)
        {
            var profile = await db.Profile.Include(p => p.Preference)
                .Where(p => p.Preference.UserId == userId.UserId && p.Slot == slot)
                .SingleOrDefaultAsync();

            if (profile == null)
            {
                return;
            }

            db.Profile.Remove(profile);
        }

        public async Task<Preference> InitPrefsAsync(NetUserId userId, HumanoidCharacterProfile defaultProfile)
        {
            await using var db = await GetDb();

            var profile = ConvertProfiles((HumanoidCharacterProfile) defaultProfile, 0);
            var prefs = new Preference
            {
                UserId = userId.UserId,
                SelectedCharacterSlot = 0,
                AdminOOCColor = Color.Red.ToHex(),
                ConstructionFavorites = [],
            };

            prefs.Profiles.Add(profile);

            db.DbContext.Preference.Add(prefs);

            await db.DbContext.SaveChangesAsync();

            return prefs;
        }

        public async Task DeleteSlotAndSetSelectedIndex(NetUserId userId, int deleteSlot, int newSlot)
        {
            await using var db = await GetDb();

            await DeleteCharacterSlot(db.DbContext, userId, deleteSlot);
            await SetSelectedCharacterSlotAsync(userId, newSlot, db.DbContext);

            await db.DbContext.SaveChangesAsync();
        }

        public async Task SaveAdminOOCColorAsync(NetUserId userId, Color color)
        {
            await using var db = await GetDb();
            var prefs = await db.DbContext
                .Preference
                .Include(p => p.Profiles)
                .SingleAsync(p => p.UserId == userId.UserId);
            prefs.AdminOOCColor = color.ToHex();

            await db.DbContext.SaveChangesAsync();

        }

        public async Task SaveConstructionFavoritesAsync(NetUserId userId, List<ProtoId<ConstructionPrototype>> constructionFavorites)
        {
            await using var db = await GetDb();
            var prefs = await db.DbContext.Preference.SingleAsync(p => p.UserId == userId.UserId);

            var favorites = new List<string>(constructionFavorites.Count);
            foreach (var favorite in constructionFavorites)
                favorites.Add(favorite.Id);
            prefs.ConstructionFavorites = favorites;

            await db.DbContext.SaveChangesAsync();
        }

        private static async Task SetSelectedCharacterSlotAsync(NetUserId userId, int newSlot, ServerDbContext db)
        {
            var prefs = await db.Preference.SingleAsync(p => p.UserId == userId.UserId);
            prefs.SelectedCharacterSlot = newSlot;
        }

        private Profile ConvertProfiles(HumanoidCharacterProfile humanoid, int slot, Profile? profile = null)
        {
            profile ??= new Profile();
            var appearance = humanoid.Appearance;
            var dataNode = _serialization.WriteValue(appearance.Markings, alwaysWrite: true, notNullableOverride: true);

            profile.CharacterName = humanoid.Name;
            profile.FlavorText = humanoid.FlavorText;
            profile.Species = humanoid.Species;
            profile.Age = humanoid.Age;
            profile.Sex = humanoid.Sex.ToString();
            profile.Gender = humanoid.Gender.ToString();
            profile.VoiceTone = humanoid.VoiceTone.ToString();
            profile.EyeColor = appearance.EyeColor.ToHex();
            profile.SkinColor = appearance.SkinColor.ToHex();
            profile.SpawnPriority = (int) humanoid.SpawnPriority;
            profile.OrganMarkings = JsonSerializer.SerializeToDocument(dataNode.ToJsonNode());

            // support for downgrades - at some point this should be removed
            var legacyMarkings = appearance.Markings
                .SelectMany(organ => organ.Value.Values)
                .SelectMany(i => i)
                .Select(marking => marking.ToLegacyDbString())
                .ToList();
            var flattenedMarkings = appearance.Markings.SelectMany(it => it.Value)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var hairMarking = flattenedMarkings.FirstOrNull(kvp => kvp.Key == HumanoidVisualLayers.Hair)?.Value.FirstOrNull();
            var facialHairMarking = flattenedMarkings.FirstOrNull(kvp => kvp.Key == HumanoidVisualLayers.FacialHair)?.Value.FirstOrNull();
            profile.Markings =
                JsonSerializer.SerializeToDocument(legacyMarkings.Select(marking => marking.ToString()).ToList());
            profile.HairName = hairMarking?.MarkingId ?? HairStyles.DefaultHairStyle;
            profile.FacialHairName = facialHairMarking?.MarkingId ?? HairStyles.DefaultFacialHairStyle;
            profile.HairColor = (hairMarking?.MarkingColors[0] ?? Color.Black).ToHex();
            profile.FacialHairColor = (facialHairMarking?.MarkingColors[0] ?? Color.Black).ToHex();

            profile.Slot = slot;
            profile.PreferenceUnavailable = (DbPreferenceUnavailableMode) humanoid.PreferenceUnavailable;

            profile.Jobs.Clear();
            profile.Jobs.AddRange(
                humanoid.JobPriorities
                    .Where(j => j.Value != JobPriority.Never)
                    .Select(j => new Job {JobName = j.Key, Priority = (DbJobPriority) j.Value})
            );

            profile.Antags.Clear();
            profile.Antags.AddRange(
                humanoid.AntagPreferences
                    .Select(a => new Antag {AntagName = a})
            );

            profile.Traits.Clear();
            profile.Traits.AddRange(
                humanoid.TraitPreferences
                        .Select(t => new Trait {TraitName = t})
            );

            profile.Loadouts.Clear();

            foreach (var (role, loadouts) in humanoid.Loadouts)
            {
                var dz = new ProfileRoleLoadout()
                {
                    RoleName = role,
                    EntityName = loadouts.EntityName ?? string.Empty,
                };

                foreach (var (group, groupLoadouts) in loadouts.SelectedLoadouts)
                {
                    var profileGroup = new ProfileLoadoutGroup()
                    {
                        GroupName = group,
                    };

                    foreach (var loadout in groupLoadouts)
                    {
                        profileGroup.Loadouts.Add(new ProfileLoadout()
                        {
                            LoadoutName = loadout.Prototype,
                        });
                    }

                    dz.Groups.Add(profileGroup);
                }

                profile.Loadouts.Add(dz);
            }

            return profile;
        }
        #endregion

        #region User Ids
        public async Task<NetUserId?> GetAssignedUserIdAsync(string name)
        {
            await using var db = await GetDb();

            var assigned = await db.DbContext.AssignedUserId.SingleOrDefaultAsync(p => p.UserName == name);
            return assigned?.UserId is { } g ? new NetUserId(g) : default(NetUserId?);
        }

        public async Task AssignUserIdAsync(string name, NetUserId netUserId)
        {
            await using var db = await GetDb();

            db.DbContext.AssignedUserId.Add(new AssignedUserId
            {
                UserId = netUserId.UserId,
                UserName = name
            });

            await db.DbContext.SaveChangesAsync();
        }

        public async Task<WH40KAuthAccountMigrationResult> MigrateLegacyGuestAccountAsync(
            string userName,
            NetUserId authenticatedUserId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);

            var assigned = await db.DbContext.AssignedUserId
                .SingleOrDefaultAsync(p => p.UserName == userName, cancel);

            if (assigned == null)
                return new WH40KAuthAccountMigrationResult(WH40KAuthAccountMigrationOutcome.None);

            var legacyUserId = new NetUserId(assigned.UserId);
            _opsLog.Info(
                "Auth account migration start: userName={UserName}, legacyUserId={LegacyUserId}, authenticatedUserId={AuthenticatedUserId}.",
                userName,
                legacyUserId,
                authenticatedUserId);

            if (legacyUserId == authenticatedUserId)
            {
                db.DbContext.AssignedUserId.Remove(assigned);
                await db.DbContext.SaveChangesAsync(cancel);
                await transaction.CommitAsync(cancel);
                _opsLog.Info(
                    "Auth account migration cleanup-only: userName={UserName}, userId={UserId}. Removed matching assigned_user_id entry.",
                    userName,
                    authenticatedUserId);
                return new WH40KAuthAccountMigrationResult(WH40KAuthAccountMigrationOutcome.CleanedAssignment, legacyUserId);
            }

            var targetPlayer = await GetOrCreateMigrationTargetPlayerAsync(db.DbContext, authenticatedUserId, userName, cancel);
            var legacyPlayer = await db.DbContext.Player
                .SingleOrDefaultAsync(p => p.UserId == legacyUserId.UserId, cancel);

            _opsLog.Info(
                "Auth account migration merge phase: userName={UserName}, legacyPlayerExists={LegacyPlayerExists}, targetPlayerId={TargetPlayerId}.",
                userName,
                legacyPlayer != null,
                targetPlayer.Id);

            await MergePlayerRoundLinksAsync(db.DbContext, legacyPlayer?.Id, targetPlayer.Id);
            await MergePreferencesAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergePlayTimeAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeWH40KMetaProgressAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeWH40KMetaAchievementsAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeWH40KMetaDecorationsAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeWH40KMetaDevelopmentUnlocksAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeWH40KDiscordLinkAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeAdminStateAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeRemarksAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeRoleWhitelistsAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeWhitelistAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeBlacklistAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeBanExemptionAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeBanPlayersAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeBanAdminReferencesAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeConnectionLogsAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeAdminLogPlayersAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeUploadedResourceLogsAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);
            await MergeWH40KMutesAsync(db.DbContext, legacyUserId, authenticatedUserId, cancel);

            MergePlayerRecord(targetPlayer, legacyPlayer);

            db.DbContext.AssignedUserId.Remove(assigned);
            if (legacyPlayer != null)
                db.DbContext.Player.Remove(legacyPlayer);

            await db.DbContext.SaveChangesAsync(cancel);
            await transaction.CommitAsync(cancel);

            _opsLog.Info(
                "Auth account migration committed successfully: userName={UserName}, legacyUserId={LegacyUserId}, authenticatedUserId={AuthenticatedUserId}.",
                userName,
                legacyUserId,
                authenticatedUserId);

            return new WH40KAuthAccountMigrationResult(WH40KAuthAccountMigrationOutcome.Migrated, legacyUserId);
        }

        private static async Task<Player> GetOrCreateMigrationTargetPlayerAsync(
            ServerDbContext dbContext,
            NetUserId authenticatedUserId,
            string userName,
            CancellationToken cancel)
        {
            var targetPlayer = await dbContext.Player
                .SingleOrDefaultAsync(p => p.UserId == authenticatedUserId.UserId, cancel);

            if (targetPlayer != null)
                return targetPlayer;

            targetPlayer = new Player
            {
                UserId = authenticatedUserId.UserId,
                FirstSeenTime = DateTime.UtcNow,
                LastSeenTime = DateTime.UtcNow,
                LastSeenAddress = IPAddress.None,
                LastSeenUserName = userName,
            };

            dbContext.Player.Add(targetPlayer);
            await dbContext.SaveChangesAsync(cancel);
            return targetPlayer;
        }

        private static async Task MergePreferencesAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyPreference = await dbContext.Preference
                .SingleOrDefaultAsync(p => p.UserId == legacyUserId.UserId, cancel);

            if (legacyPreference == null)
                return;

            await dbContext.Preference
                .Where(p => p.UserId == targetUserId.UserId)
                .ExecuteDeleteAsync(cancellationToken: cancel);

            legacyPreference.UserId = targetUserId.UserId;
        }

        private static async Task MergePlayTimeAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyRows = await dbContext.PlayTime
                .Where(p => p.PlayerId == legacyUserId.UserId)
                .ToListAsync(cancel);

            if (legacyRows.Count == 0)
                return;

            var targetRows = await dbContext.PlayTime
                .Where(p => p.PlayerId == targetUserId.UserId)
                .ToDictionaryAsync(p => p.Tracker, StringComparer.Ordinal, cancel);

            foreach (var legacyRow in legacyRows)
            {
                if (targetRows.TryGetValue(legacyRow.Tracker, out var targetRow))
                {
                    targetRow.TimeSpent += legacyRow.TimeSpent;
                    dbContext.PlayTime.Remove(legacyRow);
                    continue;
                }

                legacyRow.PlayerId = targetUserId.UserId;
            }
        }

        private static async Task MergeWH40KMetaProgressAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyRow = await dbContext.WH40KMetaProgress
                .SingleOrDefaultAsync(p => p.PlayerUserId == legacyUserId.UserId, cancel);

            if (legacyRow == null)
                return;

            var targetRow = await dbContext.WH40KMetaProgress
                .SingleOrDefaultAsync(p => p.PlayerUserId == targetUserId.UserId, cancel);

            if (targetRow == null)
            {
                targetRow = new WH40KMetaProgress
                {
                    PlayerUserId = targetUserId.UserId,
                    LifetimeXp = Math.Max(0, legacyRow.LifetimeXp),
                    SeasonXp = Math.Max(0, legacyRow.SeasonXp),
                    LastProgressAt = legacyRow.LastProgressAt,
                    LastAccountResetAt = legacyRow.LastAccountResetAt,
                    SelectedGhostSkinId = legacyRow.SelectedGhostSkinId,
                    SelectedOocTitleId = legacyRow.SelectedOocTitleId,
                    SelectedOocNameColorId = legacyRow.SelectedOocNameColorId,
                };

                dbContext.WH40KMetaProgress.Add(targetRow);
            }
            else
            {
                targetRow.LifetimeXp = Math.Max(0, targetRow.LifetimeXp) + Math.Max(0, legacyRow.LifetimeXp);
                targetRow.SeasonXp = Math.Max(0, targetRow.SeasonXp) + Math.Max(0, legacyRow.SeasonXp);
                targetRow.LastProgressAt = MaxDate(targetRow.LastProgressAt, legacyRow.LastProgressAt);
                targetRow.LastAccountResetAt = MaxNullableDate(targetRow.LastAccountResetAt, legacyRow.LastAccountResetAt);
                targetRow.SelectedGhostSkinId = PickPreferredString(targetRow.SelectedGhostSkinId, legacyRow.SelectedGhostSkinId);
                targetRow.SelectedOocTitleId = PickPreferredString(targetRow.SelectedOocTitleId, legacyRow.SelectedOocTitleId);
                targetRow.SelectedOocNameColorId = PickPreferredString(targetRow.SelectedOocNameColorId, legacyRow.SelectedOocNameColorId);
            }

            dbContext.WH40KMetaProgress.Remove(legacyRow);
        }

        private static async Task MergeWH40KMetaAchievementsAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyRows = await dbContext.WH40KMetaAchievementProgress
                .Where(p => p.PlayerUserId == legacyUserId.UserId)
                .ToListAsync(cancel);

            if (legacyRows.Count == 0)
                return;

            var targetRows = await dbContext.WH40KMetaAchievementProgress
                .Where(p => p.PlayerUserId == targetUserId.UserId)
                .ToDictionaryAsync(p => p.AchievementId, StringComparer.Ordinal, cancel);

            foreach (var legacyRow in legacyRows)
            {
                if (targetRows.TryGetValue(legacyRow.AchievementId, out var targetRow))
                {
                    targetRow.ProgressValue = Math.Max(0, targetRow.ProgressValue) + Math.Max(0, legacyRow.ProgressValue);
                    targetRow.Unlocked |= legacyRow.Unlocked;
                    targetRow.Claimed |= legacyRow.Claimed;
                    targetRow.UnlockedAt = MinNullableDate(targetRow.UnlockedAt, legacyRow.UnlockedAt);
                    targetRow.Version = Math.Max(Math.Max(1, targetRow.Version), Math.Max(1, legacyRow.Version));
                    targetRow.UpdatedAt = MaxDate(targetRow.UpdatedAt, legacyRow.UpdatedAt);
                    dbContext.WH40KMetaAchievementProgress.Remove(legacyRow);
                    continue;
                }

                dbContext.WH40KMetaAchievementProgress.Add(new WH40KMetaAchievementProgress
                {
                    PlayerUserId = targetUserId.UserId,
                    AchievementId = legacyRow.AchievementId,
                    ProgressValue = Math.Max(0, legacyRow.ProgressValue),
                    Unlocked = legacyRow.Unlocked,
                    UnlockedAt = legacyRow.UnlockedAt,
                    Claimed = legacyRow.Claimed,
                    Version = Math.Max(1, legacyRow.Version),
                    UpdatedAt = legacyRow.UpdatedAt,
                });

                dbContext.WH40KMetaAchievementProgress.Remove(legacyRow);
            }
        }

        private static async Task MergeWH40KMetaDecorationsAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyRows = await dbContext.WH40KMetaDecorationUnlock
                .Where(p => p.PlayerUserId == legacyUserId.UserId)
                .ToListAsync(cancel);

            if (legacyRows.Count == 0)
                return;

            var targetRows = await dbContext.WH40KMetaDecorationUnlock
                .Where(p => p.PlayerUserId == targetUserId.UserId)
                .ToDictionaryAsync(p => p.UnlockId, StringComparer.Ordinal, cancel);

            foreach (var legacyRow in legacyRows)
            {
                if (targetRows.TryGetValue(legacyRow.UnlockId, out var targetRow))
                {
                    targetRow.Unlocked |= legacyRow.Unlocked;
                    targetRow.UnlockedAt = MinNullableDate(targetRow.UnlockedAt, legacyRow.UnlockedAt);
                    targetRow.SourceLevel = Math.Max(targetRow.SourceLevel, legacyRow.SourceLevel);
                    targetRow.UpdatedAt = MaxDate(targetRow.UpdatedAt, legacyRow.UpdatedAt);
                    dbContext.WH40KMetaDecorationUnlock.Remove(legacyRow);
                    continue;
                }

                dbContext.WH40KMetaDecorationUnlock.Add(new WH40KMetaDecorationUnlock
                {
                    PlayerUserId = targetUserId.UserId,
                    UnlockId = legacyRow.UnlockId,
                    Unlocked = legacyRow.Unlocked,
                    UnlockedAt = legacyRow.UnlockedAt,
                    SourceLevel = Math.Max(0, legacyRow.SourceLevel),
                    UpdatedAt = legacyRow.UpdatedAt,
                });

                dbContext.WH40KMetaDecorationUnlock.Remove(legacyRow);
            }
        }

        private static async Task MergeWH40KMetaDevelopmentUnlocksAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyRows = await dbContext.WH40KMetaDevelopmentUnlock
                .Where(p => p.PlayerUserId == legacyUserId.UserId)
                .ToListAsync(cancel);

            if (legacyRows.Count == 0)
                return;

            var targetRows = await dbContext.WH40KMetaDevelopmentUnlock
                .Where(p => p.PlayerUserId == targetUserId.UserId)
                .ToDictionaryAsync(p => p.NodeId, StringComparer.Ordinal, cancel);

            foreach (var legacyRow in legacyRows)
            {
                if (targetRows.TryGetValue(legacyRow.NodeId, out var targetRow))
                {
                    targetRow.UnlockedAt = MinDate(targetRow.UnlockedAt, legacyRow.UnlockedAt);
                    targetRow.SpentCost = Math.Max(targetRow.SpentCost, legacyRow.SpentCost);
                    targetRow.UpdatedAt = MaxDate(targetRow.UpdatedAt, legacyRow.UpdatedAt);
                    dbContext.WH40KMetaDevelopmentUnlock.Remove(legacyRow);
                    continue;
                }

                dbContext.WH40KMetaDevelopmentUnlock.Add(new WH40KMetaDevelopmentUnlock
                {
                    PlayerUserId = targetUserId.UserId,
                    NodeId = legacyRow.NodeId,
                    UnlockedAt = legacyRow.UnlockedAt,
                    SpentCost = Math.Max(0, legacyRow.SpentCost),
                    UpdatedAt = legacyRow.UpdatedAt,
                });

                dbContext.WH40KMetaDevelopmentUnlock.Remove(legacyRow);
            }
        }

        private static async Task MergeWH40KDiscordLinkAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyRow = await dbContext.WH40KDiscordLink
                .SingleOrDefaultAsync(p => p.PlayerUserId == legacyUserId.UserId, cancel);

            if (legacyRow == null)
                return;

            var targetRow = await dbContext.WH40KDiscordLink
                .SingleOrDefaultAsync(p => p.PlayerUserId == targetUserId.UserId, cancel);

            if (targetRow == null)
            {
                dbContext.WH40KDiscordLink.Add(new WH40KDiscordLink
                {
                    PlayerUserId = targetUserId.UserId,
                    DiscordUserId = legacyRow.DiscordUserId,
                    Username = legacyRow.Username,
                    GlobalName = legacyRow.GlobalName,
                    AvatarHash = legacyRow.AvatarHash,
                    AccessToken = legacyRow.AccessToken,
                    RefreshToken = legacyRow.RefreshToken,
                    TokenType = legacyRow.TokenType,
                    Scope = legacyRow.Scope,
                    LinkedAt = legacyRow.LinkedAt,
                    TokenExpiresAt = legacyRow.TokenExpiresAt,
                    LastRefreshAt = legacyRow.LastRefreshAt,
                    GuildIdCached = legacyRow.GuildIdCached,
                    LastGuildRefreshAt = legacyRow.LastGuildRefreshAt,
                    GuildMemberCached = legacyRow.GuildMemberCached,
                    GuildNickname = legacyRow.GuildNickname,
                    RoleCacheJson = legacyRow.RoleCacheJson,
                });
            }
            else
            {
                targetRow.GlobalName = PickPreferredString(targetRow.GlobalName, legacyRow.GlobalName);
                targetRow.AvatarHash = PickPreferredString(targetRow.AvatarHash, legacyRow.AvatarHash);
                targetRow.RefreshToken = PickPreferredString(targetRow.RefreshToken, legacyRow.RefreshToken);
                targetRow.GuildIdCached = PickPreferredString(targetRow.GuildIdCached, legacyRow.GuildIdCached);
                targetRow.GuildNickname = PickPreferredString(targetRow.GuildNickname, legacyRow.GuildNickname);
                if (string.IsNullOrWhiteSpace(targetRow.RoleCacheJson) || targetRow.RoleCacheJson == "[]")
                    targetRow.RoleCacheJson = legacyRow.RoleCacheJson;
            }

            dbContext.WH40KDiscordLink.Remove(legacyRow);
        }

        private static async Task MergeAdminStateAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyAdmin = await dbContext.Admin
                .Include(a => a.Flags)
                .SingleOrDefaultAsync(a => a.UserId == legacyUserId.UserId, cancel);

            if (legacyAdmin == null)
                return;

            var targetAdmin = await dbContext.Admin
                .Include(a => a.Flags)
                .SingleOrDefaultAsync(a => a.UserId == targetUserId.UserId, cancel);

            if (targetAdmin == null)
            {
                targetAdmin = new Admin
                {
                    UserId = targetUserId.UserId,
                    Title = legacyAdmin.Title,
                    Deadminned = legacyAdmin.Deadminned,
                    Suspended = legacyAdmin.Suspended,
                    AdminRankId = legacyAdmin.AdminRankId,
                    Flags = legacyAdmin.Flags
                        .Select(flag => new AdminFlag
                        {
                            AdminId = targetUserId.UserId,
                            Flag = flag.Flag,
                            Negative = flag.Negative,
                        })
                        .ToList()
                };

                dbContext.Admin.Add(targetAdmin);
            }
            else
            {
                targetAdmin.Title = PickPreferredString(targetAdmin.Title, legacyAdmin.Title);
                targetAdmin.Deadminned |= legacyAdmin.Deadminned;
                targetAdmin.Suspended |= legacyAdmin.Suspended;
                targetAdmin.AdminRankId ??= legacyAdmin.AdminRankId;

                var targetFlags = targetAdmin.Flags.ToDictionary(flag => flag.Flag, StringComparer.Ordinal);
                foreach (var legacyFlag in legacyAdmin.Flags)
                {
                    if (targetFlags.TryGetValue(legacyFlag.Flag, out var existing))
                    {
                        existing.Negative |= legacyFlag.Negative;
                        continue;
                    }

                    targetAdmin.Flags.Add(new AdminFlag
                    {
                        AdminId = targetUserId.UserId,
                        Flag = legacyFlag.Flag,
                        Negative = legacyFlag.Negative,
                    });
                }
            }

            dbContext.Admin.Remove(legacyAdmin);
        }

        private static async Task MergeRemarksAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyGuid = legacyUserId.UserId;
            var targetGuid = targetUserId.UserId;

            foreach (var note in await dbContext.AdminNotes
                         .Where(note => note.PlayerUserId == legacyGuid ||
                                        note.CreatedById == legacyGuid ||
                                        note.LastEditedById == legacyGuid ||
                                        note.DeletedById == legacyGuid)
                         .ToListAsync(cancel))
            {
                if (note.PlayerUserId == legacyGuid)
                    note.PlayerUserId = targetGuid;
                if (note.CreatedById == legacyGuid)
                    note.CreatedById = targetGuid;
                if (note.LastEditedById == legacyGuid)
                    note.LastEditedById = targetGuid;
                if (note.DeletedById == legacyGuid)
                    note.DeletedById = targetGuid;
            }

            foreach (var watchlist in await dbContext.AdminWatchlists
                         .Where(note => note.PlayerUserId == legacyGuid ||
                                        note.CreatedById == legacyGuid ||
                                        note.LastEditedById == legacyGuid ||
                                        note.DeletedById == legacyGuid)
                         .ToListAsync(cancel))
            {
                if (watchlist.PlayerUserId == legacyGuid)
                    watchlist.PlayerUserId = targetGuid;
                if (watchlist.CreatedById == legacyGuid)
                    watchlist.CreatedById = targetGuid;
                if (watchlist.LastEditedById == legacyGuid)
                    watchlist.LastEditedById = targetGuid;
                if (watchlist.DeletedById == legacyGuid)
                    watchlist.DeletedById = targetGuid;
            }

            foreach (var message in await dbContext.AdminMessages
                         .Where(note => note.PlayerUserId == legacyGuid ||
                                        note.CreatedById == legacyGuid ||
                                        note.LastEditedById == legacyGuid ||
                                        note.DeletedById == legacyGuid)
                         .ToListAsync(cancel))
            {
                if (message.PlayerUserId == legacyGuid)
                    message.PlayerUserId = targetGuid;
                if (message.CreatedById == legacyGuid)
                    message.CreatedById = targetGuid;
                if (message.LastEditedById == legacyGuid)
                    message.LastEditedById = targetGuid;
                if (message.DeletedById == legacyGuid)
                    message.DeletedById = targetGuid;
            }
        }

        private static async Task MergeRoleWhitelistsAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyRows = await dbContext.RoleWhitelists
                .Where(p => p.PlayerUserId == legacyUserId.UserId)
                .ToListAsync(cancel);

            if (legacyRows.Count == 0)
                return;

            var targetRoles = await dbContext.RoleWhitelists
                .Where(p => p.PlayerUserId == targetUserId.UserId)
                .Select(p => p.RoleId)
                .ToHashSetAsync(StringComparer.Ordinal, cancel);

            foreach (var legacyRow in legacyRows)
            {
                if (!targetRoles.Contains(legacyRow.RoleId))
                {
                    dbContext.RoleWhitelists.Add(new RoleWhitelist
                    {
                        PlayerUserId = targetUserId.UserId,
                        RoleId = legacyRow.RoleId,
                    });
                }

                dbContext.RoleWhitelists.Remove(legacyRow);
            }
        }

        private static async Task MergeWhitelistAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyRow = await dbContext.Whitelist.SingleOrDefaultAsync(p => p.UserId == legacyUserId.UserId, cancel);
            if (legacyRow == null)
                return;

            if (!await dbContext.Whitelist.AnyAsync(p => p.UserId == targetUserId.UserId, cancel))
                dbContext.Whitelist.Add(new Whitelist { UserId = targetUserId.UserId });

            dbContext.Whitelist.Remove(legacyRow);
        }

        private static async Task MergeBlacklistAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyRow = await dbContext.Blacklist.SingleOrDefaultAsync(p => p.UserId == legacyUserId.UserId, cancel);
            if (legacyRow == null)
                return;

            if (!await dbContext.Blacklist.AnyAsync(p => p.UserId == targetUserId.UserId, cancel))
                dbContext.Blacklist.Add(new Blacklist { UserId = targetUserId.UserId });

            dbContext.Blacklist.Remove(legacyRow);
        }

        private static async Task MergeBanExemptionAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var legacyRow = await dbContext.BanExemption.SingleOrDefaultAsync(p => p.UserId == legacyUserId.UserId, cancel);
            if (legacyRow == null)
                return;

            var targetRow = await dbContext.BanExemption.SingleOrDefaultAsync(p => p.UserId == targetUserId.UserId, cancel);
            if (targetRow == null)
            {
                dbContext.BanExemption.Add(new ServerBanExemption
                {
                    UserId = targetUserId.UserId,
                    Flags = legacyRow.Flags
                });
            }
            else
            {
                targetRow.Flags |= legacyRow.Flags;
            }

            dbContext.BanExemption.Remove(legacyRow);
        }

        private static async Task MergeBanPlayersAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var targetBanIds = await dbContext.BanPlayer
                .Where(p => p.UserId == targetUserId.UserId)
                .Select(p => p.BanId)
                .ToHashSetAsync(cancel);

            foreach (var legacyRow in await dbContext.BanPlayer
                         .Where(p => p.UserId == legacyUserId.UserId)
                         .ToListAsync(cancel))
            {
                if (targetBanIds.Contains(legacyRow.BanId))
                {
                    dbContext.BanPlayer.Remove(legacyRow);
                    continue;
                }

                legacyRow.UserId = targetUserId.UserId;
            }
        }

        private static async Task MergeBanAdminReferencesAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            foreach (var ban in await dbContext.Ban
                         .Where(ban => ban.BanningAdmin == legacyUserId.UserId || ban.LastEditedById == legacyUserId.UserId)
                         .ToListAsync(cancel))
            {
                if (ban.BanningAdmin == legacyUserId.UserId)
                    ban.BanningAdmin = targetUserId.UserId;
                if (ban.LastEditedById == legacyUserId.UserId)
                    ban.LastEditedById = targetUserId.UserId;
            }

            foreach (var unban in await dbContext.Unban
                         .Where(unban => unban.UnbanningAdmin == legacyUserId.UserId)
                         .ToListAsync(cancel))
            {
                unban.UnbanningAdmin = targetUserId.UserId;
            }
        }

        private static async Task MergeConnectionLogsAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            foreach (var log in await dbContext.ConnectionLog
                         .Where(log => log.UserId == legacyUserId.UserId)
                         .ToListAsync(cancel))
            {
                log.UserId = targetUserId.UserId;
            }
        }

        private static async Task MergeAdminLogPlayersAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            var targetKeys = await dbContext.AdminLogPlayer
                .Where(link => link.PlayerUserId == targetUserId.UserId)
                .Select(link => new { link.RoundId, link.LogId })
                .ToListAsync(cancel);

            var targetSet = targetKeys
                .Select(key => (key.RoundId, key.LogId))
                .ToHashSet();

            foreach (var legacyLink in await dbContext.AdminLogPlayer
                         .Where(link => link.PlayerUserId == legacyUserId.UserId)
                         .ToListAsync(cancel))
            {
                if (!targetSet.Contains((legacyLink.RoundId, legacyLink.LogId)))
                {
                    dbContext.AdminLogPlayer.Add(new AdminLogPlayer
                    {
                        RoundId = legacyLink.RoundId,
                        LogId = legacyLink.LogId,
                        PlayerUserId = targetUserId.UserId,
                    });
                }

                dbContext.AdminLogPlayer.Remove(legacyLink);
            }
        }

        private static async Task MergeUploadedResourceLogsAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            foreach (var row in await dbContext.UploadedResourceLog
                         .Where(row => row.UserId == legacyUserId.UserId)
                         .ToListAsync(cancel))
            {
                row.UserId = targetUserId.UserId;
            }
        }

        private static async Task MergeWH40KMutesAsync(
            ServerDbContext dbContext,
            NetUserId legacyUserId,
            NetUserId targetUserId,
            CancellationToken cancel)
        {
            foreach (var mute in await dbContext.WH40KMute
                         .Where(mute => mute.PlayerUserId == legacyUserId.UserId || mute.CreatedById == legacyUserId.UserId)
                         .ToListAsync(cancel))
            {
                if (mute.PlayerUserId == legacyUserId.UserId)
                    mute.PlayerUserId = targetUserId.UserId;
                if (mute.CreatedById == legacyUserId.UserId)
                    mute.CreatedById = targetUserId.UserId;
            }

            foreach (var unmute in await dbContext.WH40KUnmute
                         .Where(unmute => unmute.UnmutingAdminId == legacyUserId.UserId)
                         .ToListAsync(cancel))
            {
                unmute.UnmutingAdminId = targetUserId.UserId;
            }
        }

        private static void MergePlayerRecord(Player targetPlayer, Player? legacyPlayer)
        {
            if (legacyPlayer == null)
                return;

            if (targetPlayer.FirstSeenTime == default || legacyPlayer.FirstSeenTime < targetPlayer.FirstSeenTime)
                targetPlayer.FirstSeenTime = legacyPlayer.FirstSeenTime;

            if (legacyPlayer.LastSeenTime > targetPlayer.LastSeenTime)
            {
                targetPlayer.LastSeenTime = legacyPlayer.LastSeenTime;
                targetPlayer.LastSeenUserName = legacyPlayer.LastSeenUserName;
                targetPlayer.LastSeenAddress = legacyPlayer.LastSeenAddress;
                targetPlayer.LastSeenHWId = legacyPlayer.LastSeenHWId;
            }

            targetPlayer.LastReadRules = MaxNullableDate(targetPlayer.LastReadRules, legacyPlayer.LastReadRules);
        }

        private static async Task MergePlayerRoundLinksAsync(ServerDbContext dbContext, int? legacyPlayerId, int targetPlayerId)
        {
            if (legacyPlayerId == null || legacyPlayerId == targetPlayerId)
                return;

            var legacyId = legacyPlayerId.Value;
            var provider = dbContext.Database.ProviderName ?? string.Empty;
            if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                await dbContext.Database.ExecuteSqlAsync($"""
INSERT OR IGNORE INTO player_round (players_id, rounds_id)
SELECT {targetPlayerId}, rounds_id
FROM player_round
WHERE players_id = {legacyId}
""");
            }
            else
            {
                await dbContext.Database.ExecuteSqlAsync($"""
INSERT INTO player_round (players_id, rounds_id)
SELECT {targetPlayerId}, rounds_id
FROM player_round
WHERE players_id = {legacyId}
ON CONFLICT DO NOTHING
""");
            }

            await dbContext.Database.ExecuteSqlAsync($"""
DELETE FROM player_round WHERE players_id = {legacyId}
""");
        }

        private static string? PickPreferredString(string? currentValue, string? fallbackValue)
        {
            return string.IsNullOrWhiteSpace(currentValue)
                ? (string.IsNullOrWhiteSpace(fallbackValue) ? null : fallbackValue)
                : currentValue;
        }

        private static DateTime MaxDate(DateTime left, DateTime right)
        {
            return left >= right ? left : right;
        }

        private static DateTime MinDate(DateTime left, DateTime right)
        {
            return left <= right ? left : right;
        }

        private static DateTime? MaxNullableDate(DateTime? left, DateTime? right)
        {
            if (left == null)
                return right;
            if (right == null)
                return left;

            return left >= right ? left : right;
        }

        private static DateTime? MinNullableDate(DateTime? left, DateTime? right)
        {
            if (left == null)
                return right;
            if (right == null)
                return left;

            return left <= right ? left : right;
        }
        #endregion

        #region Bans
        /*
         * BAN STUFF
         */
        /// <summary>
        ///     Looks up a ban by id.
        ///     This will return a pardoned ban as well.
        /// </summary>
        /// <param name="id">The ban id to look for.</param>
        /// <returns>The ban with the given id or null if none exist.</returns>
        public abstract Task<BanDef?> GetBanAsync(int id);

        /// <summary>
        ///     Looks up an user's most recent received un-pardoned ban.
        ///     This will NOT return a pardoned ban.
        ///     One of <see cref="address"/> or <see cref="userId"/> need to not be null.
        /// </summary>
        /// <param name="address">The ip address of the user.</param>
        /// <param name="userId">The id of the user.</param>
        /// <param name="hwId">The legacy HWId of the user.</param>
        /// <param name="modernHWIds">The modern HWIDs of the user.</param>
        /// <returns>The user's latest received un-pardoned ban, or null if none exist.</returns>
        public abstract Task<BanDef?> GetBanAsync(
            IPAddress? address,
            NetUserId? userId,
            ImmutableArray<byte>? hwId,
            ImmutableArray<ImmutableArray<byte>>? modernHWIds,
            BanType type);

        /// <summary>
        ///     Looks up an user's ban history.
        ///     This will return pardoned bans as well.
        ///     One of <see cref="address"/> or <see cref="userId"/> need to not be null.
        /// </summary>
        /// <param name="address">The ip address of the user.</param>
        /// <param name="userId">The id of the user.</param>
        /// <param name="hwId">The legacy HWId of the user.</param>
        /// <param name="modernHWIds">The modern HWIDs of the user.</param>
        /// <param name="includeUnbanned">Include pardoned and expired bans.</param>
        /// <returns>The user's ban history.</returns>
        public abstract Task<List<BanDef>> GetBansAsync(
            IPAddress? address,
            NetUserId? userId,
            ImmutableArray<byte>? hwId,
            ImmutableArray<ImmutableArray<byte>>? modernHWIds,
            bool includeUnbanned,
            BanType type);

        public abstract Task<BanDef> AddBanAsync(BanDef ban);
        public abstract Task AddUnbanAsync(UnbanDef unban);

        public virtual async Task<WH40KMuteDef?> GetMuteAsync(int id)
        {
            await using var db = await GetDb();
            var mute = await WH40KMuteQuery(db.DbContext)
                .SingleOrDefaultAsync(m => m.Id == id);

            return mute == null ? null : ConvertMute(mute);
        }

        public virtual async Task<List<WH40KMuteDef>> GetMutesAsync(NetUserId userId, bool includeUnmuted, WH40KMuteType? type = null)
        {
            await using var db = await GetDb();
            var query = WH40KMuteQuery(db.DbContext)
                .Where(m => m.PlayerUserId == userId.UserId);

            if (type != null)
                query = query.Where(m => m.Type == (int) type.Value);

            if (!includeUnmuted)
            {
                query = query.Where(m =>
                    m.Unmute == null &&
                    (m.ExpirationTime == null || m.ExpirationTime > DateTime.UtcNow));
            }

            var mutes = await query
                .OrderByDescending(m => m.MuteTime)
                .ToListAsync();

            return mutes.Select(ConvertMute).ToList();
        }

        public virtual async Task<WH40KMuteDef> AddMuteAsync(WH40KMuteDef mute)
        {
            await using var db = await GetDb();

            var entity = new WH40KMute
            {
                PlayerUserId = mute.UserId.UserId,
                Type = (int) mute.Type,
                Reason = mute.Reason,
                CreatedById = mute.MutingAdmin?.UserId,
                MuteTime = mute.MuteTime.UtcDateTime,
                ExpirationTime = mute.ExpirationTime?.UtcDateTime,
            };

            db.DbContext.WH40KMute.Add(entity);
            await db.DbContext.SaveChangesAsync();

            return new WH40KMuteDef(
                entity.Id,
                mute.UserId,
                mute.Type,
                mute.Reason,
                mute.MutingAdmin,
                mute.MuteTime,
                mute.ExpirationTime,
                null);
        }

        public virtual async Task AddUnmuteAsync(WH40KUnmuteDef unmute)
        {
            await using var db = await GetDb();
            db.DbContext.WH40KUnmute.Add(new WH40KUnmute
            {
                MuteId = unmute.MuteId,
                UnmutingAdminId = unmute.UnmutingAdmin?.UserId,
                UnmuteTime = unmute.UnmuteTime.UtcDateTime,
            });
            await db.DbContext.SaveChangesAsync();
        }

        public async Task EditBan(int id, string reason, NoteSeverity severity, DateTimeOffset? expiration, Guid editedBy, DateTimeOffset editedAt)
        {
            await using var db = await GetDb();

            var ban = await db.DbContext.Ban.SingleOrDefaultAsync(b => b.Id == id);
            if (ban is null)
                return;
            ban.Severity = severity;
            ban.Reason = reason;
            ban.ExpirationTime = expiration?.UtcDateTime;
            ban.LastEditedById = editedBy;
            ban.LastEditedAt = editedAt.UtcDateTime;
            await db.DbContext.SaveChangesAsync();
        }

        protected static async Task<ServerBanExemptFlags?> GetBanExemptionCore(
            DbGuard db,
            NetUserId? userId,
            CancellationToken cancel = default)
        {
            if (userId == null)
                return null;

            var exemption = await db.DbContext.BanExemption
                .SingleOrDefaultAsync(e => e.UserId == userId.Value.UserId, cancellationToken: cancel);

            return exemption?.Flags;
        }

        private static IQueryable<WH40KMute> WH40KMuteQuery(ServerDbContext dbContext)
        {
            return dbContext.WH40KMute
                .Include(m => m.Unmute)
                .Include(m => m.CreatedBy);
        }

        private WH40KMuteDef ConvertMute(WH40KMute mute)
        {
            NetUserId? admin = mute.CreatedById == null ? null : new NetUserId(mute.CreatedById.Value);

            return new WH40KMuteDef(
                mute.Id,
                new NetUserId(mute.PlayerUserId),
                (WH40KMuteType) mute.Type,
                mute.Reason,
                admin,
                NormalizeDatabaseTime(mute.MuteTime),
                NormalizeDatabaseTime(mute.ExpirationTime),
                ConvertUnmute(mute.Unmute));
        }

        private WH40KUnmuteDef? ConvertUnmute(WH40KUnmute? unmute)
        {
            if (unmute == null)
                return null;

            NetUserId? admin = null;
            if (unmute.UnmutingAdminId is { } adminId)
                admin = new NetUserId(adminId);

            return new WH40KUnmuteDef(unmute.MuteId, admin, NormalizeDatabaseTime(unmute.UnmuteTime));
        }

        public async Task UpdateBanExemption(NetUserId userId, ServerBanExemptFlags flags)
        {
            await using var db = await GetDb();

            if (flags == 0)
            {
                // Delete whatever is there.
                await db.DbContext.BanExemption.Where(u => u.UserId == userId.UserId).ExecuteDeleteAsync();
                return;
            }

            var exemption = await db.DbContext.BanExemption.SingleOrDefaultAsync(u => u.UserId == userId.UserId);
            if (exemption == null)
            {
                exemption = new ServerBanExemption
                {
                    UserId = userId
                };

                db.DbContext.BanExemption.Add(exemption);
            }

            exemption.Flags = flags;
            await db.DbContext.SaveChangesAsync();
        }

        public async Task<ServerBanExemptFlags> GetBanExemption(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var flags = await GetBanExemptionCore(db, userId, cancel);
            return flags ?? ServerBanExemptFlags.None;
        }

        protected static List<Expression<Func<Ban, object>>> GetBanDefIncludes(BanType? type = null)
        {
            List<Expression<Func<Ban, object>>> list =
            [
                b => b.Players!,
                b => b.Rounds!,
                b => b.Hwids!,
                b => b.Unban!,
                b => b.Addresses!,
            ];

            if (type != BanType.Server)
                list.Add(b => b.Roles!);

            return list;
        }

        #endregion

        #region Playtime
        public async Task<List<PlayTime>> GetPlayTimes(Guid player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            return await db.DbContext.PlayTime
                .Where(p => p.PlayerId == player)
                .ToListAsync(cancel);
        }

        public async Task UpdatePlayTimes(IReadOnlyCollection<PlayTimeUpdate> updates)
        {
            await using var db = await GetDb();

            // Ideally I would just be able to send a bunch of UPSERT commands, but EFCore is a pile of garbage.
            // So... In the interest of not making this take forever at high update counts...
            // Bulk-load play time objects for all players involved.
            // This allows us to semi-efficiently load all entities we need in a single DB query.
            // Then we can update & insert without further round-trips to the DB.

            var players = updates.Select(u => u.User.UserId).Distinct().ToArray();
            var dbTimes = (await db.DbContext.PlayTime
                    .Where(p => players.Contains(p.PlayerId))
                    .ToArrayAsync())
                .GroupBy(p => p.PlayerId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(p => p.Tracker, p => p));

            foreach (var (user, tracker, time) in updates)
            {
                if (dbTimes.TryGetValue(user.UserId, out var userTimes)
                    && userTimes.TryGetValue(tracker, out var ent))
                {
                    // Already have a tracker in the database, update it.
                    ent.TimeSpent = time;
                    continue;
                }

                // No tracker, make a new one.
                var playTime = new PlayTime
                {
                    Tracker = tracker,
                    PlayerId = user.UserId,
                    TimeSpent = time
                };

                db.DbContext.PlayTime.Add(playTime);
            }

            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region WH40K Meta Progress

        public async Task<WH40KMetaProgressDbData?> GetWH40KMetaProgress(NetUserId player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var progress = await db.DbContext.WH40KMetaProgress
                .SingleOrDefaultAsync(p => p.PlayerUserId == player.UserId, cancel);

            if (progress == null)
                return null;

            return new WH40KMetaProgressDbData(
                Math.Max(0, progress.LifetimeXp),
                Math.Max(0, progress.SeasonXp),
                NormalizeDatabaseTime(progress.LastProgressAt),
                progress.LastAccountResetAt == null ? null : NormalizeDatabaseTime(progress.LastAccountResetAt.Value),
                progress.SelectedGhostSkinId,
                progress.SelectedOocTitleId,
                progress.SelectedOocNameColorId);
        }

        public async Task SetWH40KMetaProgress(NetUserId player, WH40KMetaProgressDbData data)
        {
            for (var attempt = 0; ; attempt++)
            {
                await using var db = await GetDb();

                var lifetimeXp = Math.Max(0, data.LifetimeXp);
                var seasonXp = Math.Max(0, data.SeasonXp);
                var lastProgressAt = data.LastProgressAt.UtcDateTime;
                var lastAccountResetAt = data.LastAccountResetAt?.UtcDateTime;
                var ghostSkinId = string.IsNullOrWhiteSpace(data.SelectedGhostSkinId)
                    ? null
                    : data.SelectedGhostSkinId;
                var oocTitleId = string.IsNullOrWhiteSpace(data.SelectedOocTitleId)
                    ? null
                    : data.SelectedOocTitleId;
                var oocColorId = string.IsNullOrWhiteSpace(data.SelectedOocNameColorId)
                    ? null
                    : data.SelectedOocNameColorId;

                try
                {
                    var metaRow = await db.DbContext.WH40KMetaProgress
                        .SingleOrDefaultAsync(p => p.PlayerUserId == player.UserId);

                    if (metaRow == null)
                    {
                        var playerRow = await db.DbContext.Player
                            .SingleOrDefaultAsync(p => p.UserId == player.UserId);

                        if (playerRow == null)
                        {
                            var now = DateTime.UtcNow;
                            db.DbContext.Player.Add(new Player
                            {
                                UserId = player.UserId,
                                FirstSeenTime = now,
                                LastSeenTime = now,
                                LastSeenAddress = IPAddress.None,
                                LastSeenUserName = player.UserId.ToString(),
                            });
                        }

                        metaRow = new WH40KMetaProgress
                        {
                            PlayerUserId = player.UserId
                        };
                        db.DbContext.WH40KMetaProgress.Add(metaRow);
                    }

                    metaRow.LifetimeXp = lifetimeXp;
                    metaRow.SeasonXp = seasonXp;
                    metaRow.LastProgressAt = lastProgressAt;
                    metaRow.LastAccountResetAt = lastAccountResetAt;
                    metaRow.SelectedGhostSkinId = ghostSkinId;
                    metaRow.SelectedOocTitleId = oocTitleId;
                    metaRow.SelectedOocNameColorId = oocColorId;

                    await db.DbContext.SaveChangesAsync();
                    return;
                }
                catch (DbUpdateException ex) when (attempt == 0)
                {
                    _opsLog.Warning(
                        "Retrying SetWH40KMetaProgress after concurrent write for userId={UserId}: {Error}",
                        player,
                        ex.InnerException?.Message ?? ex.Message);
                }
            }
        }

        public async Task<List<WH40KMetaAchievementDbData>> GetWH40KMetaAchievements(NetUserId player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var rows = await db.DbContext.WH40KMetaAchievementProgress
                .Where(a => a.PlayerUserId == player.UserId)
                .ToListAsync(cancel);

            return rows
                .Select(row => new WH40KMetaAchievementDbData(
                    row.AchievementId,
                    Math.Max(0, row.ProgressValue),
                    row.Unlocked,
                    NormalizeDatabaseTime(row.UnlockedAt),
                    row.Claimed,
                    Math.Max(1, row.Version),
                    NormalizeDatabaseTime(row.UpdatedAt)))
                .ToList();
        }

        public async Task SetWH40KMetaAchievements(NetUserId player, IReadOnlyCollection<WH40KMetaAchievementDbData> data)
        {
            await using var db = await GetDb();

            var existing = await db.DbContext.WH40KMetaAchievementProgress
                .Where(a => a.PlayerUserId == player.UserId)
                .ToListAsync();

            var existingById = existing.ToDictionary(a => a.AchievementId, StringComparer.Ordinal);
            var incomingIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in data)
            {
                if (string.IsNullOrWhiteSpace(entry.AchievementId))
                    continue;

                incomingIds.Add(entry.AchievementId);

                if (!existingById.TryGetValue(entry.AchievementId, out var row))
                {
                    row = new WH40KMetaAchievementProgress
                    {
                        PlayerUserId = player.UserId,
                        AchievementId = entry.AchievementId,
                    };
                    db.DbContext.WH40KMetaAchievementProgress.Add(row);
                }

                row.ProgressValue = Math.Max(0, entry.ProgressValue);
                row.Unlocked = entry.Unlocked;
                row.UnlockedAt = entry.UnlockedAt?.UtcDateTime;
                row.Claimed = entry.Claimed;
                row.Version = Math.Max(1, entry.Version);
                row.UpdatedAt = entry.UpdatedAt.UtcDateTime;
            }

            foreach (var row in existing)
            {
                if (!incomingIds.Contains(row.AchievementId))
                    db.DbContext.WH40KMetaAchievementProgress.Remove(row);
            }

            await db.DbContext.SaveChangesAsync();
        }

        public async Task<List<WH40KMetaDecorationDbData>> GetWH40KMetaDecorations(NetUserId player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var rows = await db.DbContext.WH40KMetaDecorationUnlock
                .Where(a => a.PlayerUserId == player.UserId)
                .ToListAsync(cancel);

            return rows
                .Select(row => new WH40KMetaDecorationDbData(
                    row.UnlockId,
                    row.Unlocked,
                    NormalizeDatabaseTime(row.UnlockedAt),
                    Math.Max(0, row.SourceLevel),
                    NormalizeDatabaseTime(row.UpdatedAt)))
                .ToList();
        }

        public async Task SetWH40KMetaDecorations(NetUserId player, IReadOnlyCollection<WH40KMetaDecorationDbData> data)
        {
            await using var db = await GetDb();

            var existing = await db.DbContext.WH40KMetaDecorationUnlock
                .Where(a => a.PlayerUserId == player.UserId)
                .ToListAsync();

            var existingById = existing.ToDictionary(a => a.UnlockId, StringComparer.Ordinal);
            var incomingIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in data)
            {
                if (string.IsNullOrWhiteSpace(entry.UnlockId))
                    continue;

                incomingIds.Add(entry.UnlockId);

                if (!existingById.TryGetValue(entry.UnlockId, out var row))
                {
                    row = new WH40KMetaDecorationUnlock
                    {
                        PlayerUserId = player.UserId,
                        UnlockId = entry.UnlockId,
                    };
                    db.DbContext.WH40KMetaDecorationUnlock.Add(row);
                }

                row.Unlocked = entry.Unlocked;
                row.UnlockedAt = entry.UnlockedAt?.UtcDateTime;
                row.SourceLevel = Math.Max(0, entry.SourceLevel);
                row.UpdatedAt = entry.UpdatedAt.UtcDateTime;
            }

            foreach (var row in existing)
            {
                if (!incomingIds.Contains(row.UnlockId))
                    db.DbContext.WH40KMetaDecorationUnlock.Remove(row);
            }

            await db.DbContext.SaveChangesAsync();
        }

        public async Task<List<WH40KMetaDevelopmentUnlockDbData>> GetWH40KMetaDevelopmentUnlocks(NetUserId player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var rows = await db.DbContext.WH40KMetaDevelopmentUnlock
                .Where(a => a.PlayerUserId == player.UserId)
                .ToListAsync(cancel);

            return rows
                .Select(row => new WH40KMetaDevelopmentUnlockDbData(
                    row.NodeId,
                    NormalizeDatabaseTime(row.UnlockedAt),
                    Math.Max(0, row.SpentCost),
                    NormalizeDatabaseTime(row.UpdatedAt)))
                .ToList();
        }

        public async Task<List<NetUserId>> GetUsersWithAnyWH40KMetaOrPreferences(CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var userIds = await db.DbContext.Preference
                .Select(pref => pref.UserId)
                .Concat(db.DbContext.WH40KMetaProgress.Select(row => row.PlayerUserId))
                .Concat(db.DbContext.WH40KMetaAchievementProgress.Select(row => row.PlayerUserId))
                .Concat(db.DbContext.WH40KMetaDecorationUnlock.Select(row => row.PlayerUserId))
                .Concat(db.DbContext.WH40KMetaDevelopmentUnlock.Select(row => row.PlayerUserId))
                .Distinct()
                .ToListAsync(cancel);

            return userIds.Select(id => new NetUserId(id)).ToList();
        }

        public async Task SetWH40KMetaDevelopmentUnlocks(NetUserId player, IReadOnlyCollection<WH40KMetaDevelopmentUnlockDbData> data)
        {
            await using var db = await GetDb();

            var existing = await db.DbContext.WH40KMetaDevelopmentUnlock
                .Where(a => a.PlayerUserId == player.UserId)
                .ToListAsync();

            var existingById = existing.ToDictionary(a => a.NodeId, StringComparer.Ordinal);
            var incomingIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in data)
            {
                if (string.IsNullOrWhiteSpace(entry.NodeId))
                    continue;

                incomingIds.Add(entry.NodeId);

                if (!existingById.TryGetValue(entry.NodeId, out var row))
                {
                    row = new WH40KMetaDevelopmentUnlock
                    {
                        PlayerUserId = player.UserId,
                        NodeId = entry.NodeId,
                    };
                    db.DbContext.WH40KMetaDevelopmentUnlock.Add(row);
                }

                row.UnlockedAt = entry.UnlockedAt.UtcDateTime;
                row.SpentCost = Math.Max(0, entry.SpentCost);
                row.UpdatedAt = entry.UpdatedAt.UtcDateTime;
            }

            foreach (var row in existing)
            {
                if (!incomingIds.Contains(row.NodeId))
                    db.DbContext.WH40KMetaDevelopmentUnlock.Remove(row);
            }

            await db.DbContext.SaveChangesAsync();
        }

        public async Task BatchSetWH40KMetaProgressAll(
            NetUserId player,
            WH40KMetaProgressDbData progressData,
            IReadOnlyCollection<WH40KMetaAchievementDbData> achievementData,
            IReadOnlyCollection<WH40KMetaDecorationDbData> decorationData,
            IReadOnlyCollection<WH40KMetaDevelopmentUnlockDbData> developmentData)
        {
            for (var attempt = 0; ; attempt++)
            {
                await using var db = await GetDb();
                await using var transaction = await db.DbContext.Database.BeginTransactionAsync();

                try
                {
                    // --- Progress ---
                    var lifetimeXp = Math.Max(0, progressData.LifetimeXp);
                    var seasonXp = Math.Max(0, progressData.SeasonXp);
                    var lastProgressAt = progressData.LastProgressAt.UtcDateTime;
                    var lastAccountResetAt = progressData.LastAccountResetAt?.UtcDateTime;
                    var ghostSkinId = string.IsNullOrWhiteSpace(progressData.SelectedGhostSkinId) ? null : progressData.SelectedGhostSkinId;
                    var oocTitleId = string.IsNullOrWhiteSpace(progressData.SelectedOocTitleId) ? null : progressData.SelectedOocTitleId;
                    var oocColorId = string.IsNullOrWhiteSpace(progressData.SelectedOocNameColorId) ? null : progressData.SelectedOocNameColorId;

                    var metaRow = await db.DbContext.WH40KMetaProgress
                        .SingleOrDefaultAsync(p => p.PlayerUserId == player.UserId);

                    if (metaRow == null)
                    {
                        var playerRow = await db.DbContext.Player
                            .SingleOrDefaultAsync(p => p.UserId == player.UserId);

                        if (playerRow == null)
                        {
                            var now = DateTime.UtcNow;
                            db.DbContext.Player.Add(new Player
                            {
                                UserId = player.UserId,
                                FirstSeenTime = now,
                                LastSeenTime = now,
                                LastSeenAddress = IPAddress.None,
                                LastSeenUserName = player.UserId.ToString(),
                            });
                        }

                        metaRow = new WH40KMetaProgress { PlayerUserId = player.UserId };
                        db.DbContext.WH40KMetaProgress.Add(metaRow);
                    }

                    metaRow.LifetimeXp = lifetimeXp;
                    metaRow.SeasonXp = seasonXp;
                    metaRow.LastProgressAt = lastProgressAt;
                    metaRow.LastAccountResetAt = lastAccountResetAt;
                    metaRow.SelectedGhostSkinId = ghostSkinId;
                    metaRow.SelectedOocTitleId = oocTitleId;
                    metaRow.SelectedOocNameColorId = oocColorId;

                    // --- Achievements ---
                    var existingAch = await db.DbContext.WH40KMetaAchievementProgress
                        .Where(a => a.PlayerUserId == player.UserId).ToListAsync();
                    var existingAchById = existingAch.ToDictionary(a => a.AchievementId, StringComparer.Ordinal);
                    var incomingAchIds = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var entry in achievementData)
                    {
                        if (string.IsNullOrWhiteSpace(entry.AchievementId)) continue;
                        incomingAchIds.Add(entry.AchievementId);
                        if (!existingAchById.TryGetValue(entry.AchievementId, out var achRow))
                        {
                            achRow = new WH40KMetaAchievementProgress { PlayerUserId = player.UserId, AchievementId = entry.AchievementId };
                            db.DbContext.WH40KMetaAchievementProgress.Add(achRow);
                        }
                        achRow.ProgressValue = Math.Max(0, entry.ProgressValue);
                        achRow.Unlocked = entry.Unlocked;
                        achRow.UnlockedAt = entry.UnlockedAt?.UtcDateTime;
                        achRow.Claimed = entry.Claimed;
                        achRow.Version = Math.Max(1, entry.Version);
                        achRow.UpdatedAt = entry.UpdatedAt.UtcDateTime;
                    }
                    foreach (var achRow in existingAch)
                    {
                        if (!incomingAchIds.Contains(achRow.AchievementId))
                            db.DbContext.WH40KMetaAchievementProgress.Remove(achRow);
                    }

                    // --- Decorations ---
                    var existingDecor = await db.DbContext.WH40KMetaDecorationUnlock
                        .Where(a => a.PlayerUserId == player.UserId).ToListAsync();
                    var existingDecorById = existingDecor.ToDictionary(a => a.UnlockId, StringComparer.Ordinal);
                    var incomingDecorIds = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var entry in decorationData)
                    {
                        if (string.IsNullOrWhiteSpace(entry.UnlockId)) continue;
                        incomingDecorIds.Add(entry.UnlockId);
                        if (!existingDecorById.TryGetValue(entry.UnlockId, out var decorRow))
                        {
                            decorRow = new WH40KMetaDecorationUnlock { PlayerUserId = player.UserId, UnlockId = entry.UnlockId };
                            db.DbContext.WH40KMetaDecorationUnlock.Add(decorRow);
                        }
                        decorRow.Unlocked = entry.Unlocked;
                        decorRow.UnlockedAt = entry.UnlockedAt?.UtcDateTime;
                        decorRow.SourceLevel = Math.Max(0, entry.SourceLevel);
                        decorRow.UpdatedAt = entry.UpdatedAt.UtcDateTime;
                    }
                    foreach (var decorRow in existingDecor)
                    {
                        if (!incomingDecorIds.Contains(decorRow.UnlockId))
                            db.DbContext.WH40KMetaDecorationUnlock.Remove(decorRow);
                    }

                    // --- Development ---
                    var existingDev = await db.DbContext.WH40KMetaDevelopmentUnlock
                        .Where(a => a.PlayerUserId == player.UserId).ToListAsync();
                    var existingDevById = existingDev.ToDictionary(a => a.NodeId, StringComparer.Ordinal);
                    var incomingDevIds = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var entry in developmentData)
                    {
                        if (string.IsNullOrWhiteSpace(entry.NodeId)) continue;
                        incomingDevIds.Add(entry.NodeId);
                        if (!existingDevById.TryGetValue(entry.NodeId, out var devRow))
                        {
                            devRow = new WH40KMetaDevelopmentUnlock { PlayerUserId = player.UserId, NodeId = entry.NodeId };
                            db.DbContext.WH40KMetaDevelopmentUnlock.Add(devRow);
                        }
                        devRow.UnlockedAt = entry.UnlockedAt.UtcDateTime;
                        devRow.SpentCost = Math.Max(0, entry.SpentCost);
                        devRow.UpdatedAt = entry.UpdatedAt.UtcDateTime;
                    }
                    foreach (var devRow in existingDev)
                    {
                        if (!incomingDevIds.Contains(devRow.NodeId))
                            db.DbContext.WH40KMetaDevelopmentUnlock.Remove(devRow);
                    }

                    await db.DbContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return;
                }
                catch (DbUpdateException ex) when (attempt == 0)
                {
                    await transaction.RollbackAsync();
                    _opsLog.Warning(
                        "Retrying BatchSetWH40KMetaProgressAll after concurrent write for userId={UserId}: {Error}",
                        player,
                        ex.InnerException?.Message ?? ex.Message);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
        }

        public async Task<WH40KDiscordAuthDbData?> GetWH40KDiscordLink(NetUserId player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var link = await db.DbContext.WH40KDiscordLink
                .SingleOrDefaultAsync(p => p.PlayerUserId == player.UserId, cancel);

            if (link == null)
                return null;

            return new WH40KDiscordAuthDbData(
                link.DiscordUserId,
                link.Username,
                link.GlobalName,
                link.AvatarHash,
                link.AccessToken,
                link.RefreshToken,
                link.TokenType,
                link.Scope,
                NormalizeDatabaseTime(link.LinkedAt),
                NormalizeDatabaseTime(link.TokenExpiresAt),
                NormalizeDatabaseTime(link.LastRefreshAt),
                link.GuildIdCached,
                NormalizeDatabaseTime(link.LastGuildRefreshAt),
                link.GuildMemberCached,
                link.GuildNickname,
                string.IsNullOrWhiteSpace(link.RoleCacheJson) ? "[]" : link.RoleCacheJson);
        }

        public async Task<NetUserId?> GetWH40KDiscordLinkOwner(string discordUserId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var normalizedDiscordUserId = discordUserId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedDiscordUserId))
                return null;

            var link = await db.DbContext.WH40KDiscordLink
                .AsNoTracking()
                .SingleOrDefaultAsync(p => p.DiscordUserId == normalizedDiscordUserId, cancel);

            return link == null ? null : new NetUserId(link.PlayerUserId);
        }

        public async Task SetWH40KDiscordLink(NetUserId player, WH40KDiscordAuthDbData data)
        {
            await using var db = await GetDb();

            var discordUserId = data.DiscordUserId.Trim();
            var username = data.Username.Trim();

            if (string.IsNullOrWhiteSpace(discordUserId))
                throw new InvalidOperationException("Discord user id cannot be empty.");

            if (string.IsNullOrWhiteSpace(username))
                throw new InvalidOperationException("Discord username cannot be empty.");

            var duplicate = await db.DbContext.WH40KDiscordLink
                .SingleOrDefaultAsync(p => p.DiscordUserId == discordUserId && p.PlayerUserId != player.UserId);

            if (duplicate != null)
                throw new InvalidOperationException("Discord account is already linked to another player.");

            var linkRow = await db.DbContext.WH40KDiscordLink
                .SingleOrDefaultAsync(p => p.PlayerUserId == player.UserId);

            if (linkRow == null)
            {
                var playerRow = await db.DbContext.Player
                    .SingleOrDefaultAsync(p => p.UserId == player.UserId);

                if (playerRow == null)
                {
                    var now = DateTime.UtcNow;
                    db.DbContext.Player.Add(new Player
                    {
                        UserId = player.UserId,
                        FirstSeenTime = now,
                        LastSeenTime = now,
                        LastSeenAddress = IPAddress.None,
                        LastSeenUserName = player.UserId.ToString(),
                    });
                }

                linkRow = new WH40KDiscordLink
                {
                    PlayerUserId = player.UserId,
                };
                db.DbContext.WH40KDiscordLink.Add(linkRow);
            }

            linkRow.DiscordUserId = discordUserId;
            linkRow.Username = username;
            linkRow.GlobalName = string.IsNullOrWhiteSpace(data.GlobalName) ? null : data.GlobalName.Trim();
            linkRow.AvatarHash = string.IsNullOrWhiteSpace(data.AvatarHash) ? null : data.AvatarHash.Trim();
            linkRow.AccessToken = data.AccessToken;
            linkRow.RefreshToken = string.IsNullOrWhiteSpace(data.RefreshToken) ? null : data.RefreshToken;
            linkRow.TokenType = string.IsNullOrWhiteSpace(data.TokenType) ? "Bearer" : data.TokenType.Trim();
            linkRow.Scope = string.IsNullOrWhiteSpace(data.Scope) ? "identify guilds.members.read" : data.Scope.Trim();
            linkRow.LinkedAt = data.LinkedAt.UtcDateTime;
            linkRow.TokenExpiresAt = data.TokenExpiresAt.UtcDateTime;
            linkRow.LastRefreshAt = data.LastRefreshAt.UtcDateTime;
            linkRow.GuildIdCached = string.IsNullOrWhiteSpace(data.GuildIdCached) ? null : data.GuildIdCached.Trim();
            linkRow.LastGuildRefreshAt = data.LastGuildRefreshAt?.UtcDateTime;
            linkRow.GuildMemberCached = data.GuildMemberCached;
            linkRow.GuildNickname = string.IsNullOrWhiteSpace(data.GuildNickname) ? null : data.GuildNickname.Trim();
            linkRow.RoleCacheJson = string.IsNullOrWhiteSpace(data.RoleCacheJson) ? "[]" : data.RoleCacheJson;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task ClearWH40KDiscordLink(NetUserId player)
        {
            await using var db = await GetDb();

            await db.DbContext.WH40KDiscordLink
                .Where(p => p.PlayerUserId == player.UserId)
                .ExecuteDeleteAsync();

            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region Player Records
        /*
         * PLAYER RECORDS
         */
        public async Task UpdatePlayerRecord(
            NetUserId userId,
            string userName,
            IPAddress address,
            ImmutableTypedHwid? hwId)
        {
            await using var db = await GetDb();

            var record = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == userId.UserId);
            if (record == null)
            {
                db.DbContext.Player.Add(record = new Player
                {
                    FirstSeenTime = DateTime.UtcNow,
                    UserId = userId.UserId,
                });
            }

            record.LastSeenTime = DateTime.UtcNow;
            record.LastSeenAddress = address;
            record.LastSeenUserName = userName;
            record.LastSeenHWId = hwId;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task<PlayerRecord?> GetPlayerRecordByUserName(string userName, CancellationToken cancel)
        {
            await using var db = await GetDb();

            // Sort by descending last seen time.
            // So if, due to account renames, we have two people with the same username in the DB,
            // the most recent one is picked.
            var record = await db.DbContext.Player
                .OrderByDescending(p => p.LastSeenTime)
                .FirstOrDefaultAsync(p => p.LastSeenUserName == userName, cancel);

            return record == null ? null : MakePlayerRecord(record);
        }

        public async Task<PlayerRecord?> GetPlayerRecordByUserId(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDb();

            var record = await db.DbContext.Player
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);

            return record == null ? null : MakePlayerRecord(record);
        }

        protected async Task<bool> PlayerRecordExists(DbGuard db, NetUserId userId)
        {
            return await db.DbContext.Player.AnyAsync(p => p.UserId == userId);
        }

        [return: NotNullIfNotNull(nameof(player))]
        protected PlayerRecord? MakePlayerRecord(Player? player)
        {
            if (player == null)
                return null;

            return MakePlayerRecord(player.UserId, player);
        }

        protected PlayerRecord MakePlayerRecord(Guid userId, Player? player)
        {
            if (player == null)
            {
                // We don't have a record for this player in the database.
                // This is possible, for example, when banning people that never connected to the server.
                // Just return fallback data here, I guess.
                return new PlayerRecord(new NetUserId(userId), default, userId.ToString(), default, null, null);
            }

            return new PlayerRecord(
                new NetUserId(player.UserId),
                new DateTimeOffset(NormalizeDatabaseTime(player.FirstSeenTime)),
                player.LastSeenUserName,
                new DateTimeOffset(NormalizeDatabaseTime(player.LastSeenTime)),
                player.LastSeenAddress,
                player.LastSeenHWId);
        }

        #endregion

        #region Connection Logs
        /*
         * CONNECTION LOG
         */
        public abstract Task<int> AddConnectionLogAsync(NetUserId userId,
            string userName,
            IPAddress address,
            ImmutableTypedHwid? hwId,
            float trust,
            ConnectionDenyReason? denied,
            int serverId);

        public async Task AddServerBanHitsAsync(int connection, IEnumerable<BanDef> bans)
        {
            await using var db = await GetDb();

            foreach (var ban in bans)
            {
                db.DbContext.ServerBanHit.Add(new ServerBanHit
                {
                    ConnectionId = connection, BanId = ban.Id!.Value
                });
            }

            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region Admin Ranks
        /*
         * ADMIN RANKS
         */
        public async Task<Admin?> GetAdminDataForAsync(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            return await db.DbContext.Admin
                .Include(p => p.Flags)
                .Include(p => p.AdminRank)
                .ThenInclude(p => p!.Flags)
                .AsSplitQuery() // tests fail because of a random warning if you dont have this!
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);
        }

        public abstract Task<((Admin admin, string? lastUserName, DateTime? lastSeenTime)[] admins, AdminRank[] ranks)>
            GetAllAdminAndRanksAsync(CancellationToken cancel);

        public async Task<AdminRank?> GetAdminRankDataForAsync(int id, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            return await db.DbContext.AdminRank
                .Include(r => r.Flags)
                .SingleOrDefaultAsync(r => r.Id == id, cancel);
        }

        public async Task RemoveAdminAsync(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var admin = await db.DbContext.Admin.SingleAsync(a => a.UserId == userId.UserId, cancel);
            db.DbContext.Admin.Remove(admin);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task AddAdminAsync(Admin admin, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            db.DbContext.Admin.Add(admin);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task UpdateAdminAsync(Admin admin, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var existing = await db.DbContext.Admin.Include(a => a.Flags).SingleAsync(a => a.UserId == admin.UserId, cancel);
            existing.Flags = admin.Flags;
            existing.Title = admin.Title;
            existing.AdminRankId = admin.AdminRankId;
            existing.Deadminned = admin.Deadminned;
            existing.Suspended = admin.Suspended;

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task UpdateAdminDeadminnedAsync(NetUserId userId, bool deadminned, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var adminRecord = db.DbContext.Admin.Where(a => a.UserId == userId);
            await adminRecord.ExecuteUpdateAsync(
                set => set.SetProperty(p => p.Deadminned, deadminned),
                cancellationToken: cancel);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task RemoveAdminRankAsync(int rankId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var admin = await db.DbContext.AdminRank.SingleAsync(a => a.Id == rankId, cancel);
            db.DbContext.AdminRank.Remove(admin);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task AddAdminRankAsync(AdminRank rank, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            db.DbContext.AdminRank.Add(rank);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task<int> AddNewRound(Server server, params Guid[] playerIds)
        {
            await using var db = await GetDb();

            var players = await db.DbContext.Player
                .Where(player => playerIds.Contains(player.UserId))
                .ToListAsync();

            var round = new Round
            {
                StartDate = DateTime.UtcNow,
                Players = players,
                ServerId = server.Id
            };

            db.DbContext.Round.Add(round);

            await db.DbContext.SaveChangesAsync();

            return round.Id;
        }

        public async Task<Round> GetRound(int id)
        {
            await using var db = await GetDb();

            var round = await db.DbContext.Round
                .Include(round => round.Players)
                .SingleAsync(round => round.Id == id);

            return round;
        }

        public async Task AddRoundPlayers(int id, Guid[] playerIds)
        {
            await using var db = await GetDb();

            // ReSharper disable once SuggestVarOrType_Elsewhere
            Dictionary<Guid, int> players = await db.DbContext.Player
                .Where(player => playerIds.Contains(player.UserId))
                .ToDictionaryAsync(player => player.UserId, player => player.Id);

            foreach (var player in playerIds)
            {
                await db.DbContext.Database.ExecuteSqlAsync($"""
INSERT INTO player_round (players_id, rounds_id) VALUES ({players[player]}, {id}) ON CONFLICT DO NOTHING
""");
            }

            await db.DbContext.SaveChangesAsync();
        }

        [return: NotNullIfNotNull(nameof(round))]
        protected RoundRecord? MakeRoundRecord(Round? round)
        {
            if (round == null)
                return null;

            return new RoundRecord(
                round.Id,
                NormalizeDatabaseTime(round.StartDate),
                MakeServerRecord(round.Server));
        }

        public async Task UpdateAdminRankAsync(AdminRank rank, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var existing = await db.DbContext.AdminRank
                .Include(r => r.Flags)
                .SingleAsync(a => a.Id == rank.Id, cancel);

            existing.Flags = rank.Flags;
            existing.Name = rank.Name;
            existing.HierarchyLevel = rank.HierarchyLevel;

            await db.DbContext.SaveChangesAsync(cancel);
        }
        #endregion

        #region Admin Logs

        public async Task<(Server, bool existed)> AddOrGetServer(string serverName)
        {
            await using var db = await GetDb();
            var server = await db.DbContext.Server
                .Where(server => server.Name.Equals(serverName))
                .SingleOrDefaultAsync();

            if (server != default)
                return (server, true);

            server = new Server
            {
                Name = serverName
            };

            db.DbContext.Server.Add(server);

            await db.DbContext.SaveChangesAsync();

            return (server, false);
        }

        [return: NotNullIfNotNull(nameof(server))]
        protected ServerRecord? MakeServerRecord(Server? server)
        {
            if (server == null)
                return null;

            return new ServerRecord(server.Id, server.Name);
        }

        public async Task AddAdminLogs(List<AdminLog> logs)
        {
            const int maxRetryAttempts = 5;
            var initialRetryDelay = TimeSpan.FromSeconds(5);

            DebugTools.Assert(logs.All(x => x.RoundId > 0), "Adding logs with invalid round ids.");

            var attempt = 0;
            var retryDelay = initialRetryDelay;

            while (attempt < maxRetryAttempts)
            {
                try
                {
                    await using var db = await GetDb();
                    db.DbContext.AdminLog.AddRange(logs);
                    await db.DbContext.SaveChangesAsync();
                    _opsLog.Debug($"Successfully saved {logs.Count} admin logs.");
                    break;
                }
                catch (Exception ex)
                {
                    attempt += 1;
                    _opsLog.Error($"Attempt {attempt} failed to save logs: {ex}");

                    if (attempt >= maxRetryAttempts)
                    {
                        _opsLog.Error($"Max retry attempts reached. Failed to save {logs.Count} admin logs.");
                        return;
                    }

                    _opsLog.Warning($"Retrying in {retryDelay.TotalSeconds} seconds...");
                    await Task.Delay(retryDelay);

                    retryDelay *= 2;
                }
            }
        }

        protected abstract IQueryable<AdminLog> StartAdminLogsQuery(ServerDbContext db, LogFilter? filter = null);

        private IQueryable<AdminLog> GetAdminLogsQuery(ServerDbContext db, LogFilter? filter = null)
        {
            // Save me from SQLite
            var query = StartAdminLogsQuery(db, filter);

            if (filter == null)
            {
                return query.OrderBy(log => log.Date);
            }

            if (filter.Round != null)
            {
                query = query.Where(log => log.RoundId == filter.Round);
            }

            if (filter.Types != null)
            {
                query = query.Where(log => filter.Types.Contains(log.Type));
            }

            if (filter.Impacts != null)
            {
                query = query.Where(log => filter.Impacts.Contains(log.Impact));
            }

            if (filter.Before != null)
            {
                query = query.Where(log => log.Date < filter.Before);
            }

            if (filter.After != null)
            {
                query = query.Where(log => log.Date > filter.After);
            }

            if (filter.IncludePlayers)
            {
                if (filter.AnyPlayers != null)
                {
                    query = query.Where(log =>
                        log.Players.Any(p => filter.AnyPlayers.Contains(p.PlayerUserId)) ||
                        log.Players.Count == 0 && filter.IncludeNonPlayers);
                }

                if (filter.AllPlayers != null)
                {
                    query = query.Where(log =>
                        log.Players.All(p => filter.AllPlayers.Contains(p.PlayerUserId)) ||
                        log.Players.Count == 0 && filter.IncludeNonPlayers);
                }
            }
            else
            {
                query = query.Where(log => log.Players.Count == 0);
            }

            if (filter.LastLogId != null)
            {
                query = filter.DateOrder switch
                {
                    DateOrder.Ascending => query.Where(log => log.Id > filter.LastLogId),
                    DateOrder.Descending => query.Where(log => log.Id < filter.LastLogId),
                    _ => throw new ArgumentOutOfRangeException(nameof(filter),
                        $"Unknown {nameof(DateOrder)} value {filter.DateOrder}")
                };
            }

            query = filter.DateOrder switch
            {
                DateOrder.Ascending => query.OrderBy(log => log.Date),
                DateOrder.Descending => query.OrderByDescending(log => log.Date),
                _ => throw new ArgumentOutOfRangeException(nameof(filter),
                    $"Unknown {nameof(DateOrder)} value {filter.DateOrder}")
            };

            const int hardLogLimit = 500_000;
            if (filter.Limit != null)
            {
                query = query.Take(Math.Min(filter.Limit.Value, hardLogLimit));
            }
            else
            {
                query = query.Take(hardLogLimit);
            }

            return query;
        }

        public async IAsyncEnumerable<string> GetAdminLogMessages(LogFilter? filter = null)
        {
            await using var db = await GetDb();
            var query = GetAdminLogsQuery(db.DbContext, filter);

            await foreach (var log in query.Select(log => log.Message).AsAsyncEnumerable())
            {
                yield return log;
            }
        }

        public async IAsyncEnumerable<SharedAdminLog> GetAdminLogs(LogFilter? filter = null)
        {
            await using var db = await GetDb();
            var query = GetAdminLogsQuery(db.DbContext, filter);
            query = query.Include(log => log.Players);

            await foreach (var log in query.AsAsyncEnumerable())
            {
                var players = new Guid[log.Players.Count];
                for (var i = 0; i < log.Players.Count; i++)
                {
                    players[i] = log.Players[i].PlayerUserId;
                }

                yield return new SharedAdminLog(log.Id, log.Type, log.Impact, log.Date, log.Message, players);
            }
        }

        public async IAsyncEnumerable<JsonDocument> GetAdminLogsJson(LogFilter? filter = null)
        {
            await using var db = await GetDb();
            var query = GetAdminLogsQuery(db.DbContext, filter);

            await foreach (var json in query.Select(log => log.Json).AsAsyncEnumerable())
            {
                yield return json;
            }
        }

        public async Task<int> CountAdminLogs(int round)
        {
            await using var db = await GetDb();
            return await db.DbContext.AdminLog.CountAsync(log => log.RoundId == round);
        }

        #endregion

        #region Whitelist

        public async Task<bool> GetWhitelistStatusAsync(NetUserId player)
        {
            await using var db = await GetDb();

            return await db.DbContext.Whitelist.AnyAsync(w => w.UserId == player);
        }

        public async Task AddToWhitelistAsync(NetUserId player)
        {
            await using var db = await GetDb();

            db.DbContext.Whitelist.Add(new Whitelist { UserId = player });
            await db.DbContext.SaveChangesAsync();
        }

        public async Task RemoveFromWhitelistAsync(NetUserId player)
        {
            await using var db = await GetDb();
            var entry = await db.DbContext.Whitelist.SingleAsync(w => w.UserId == player);
            db.DbContext.Whitelist.Remove(entry);
            await db.DbContext.SaveChangesAsync();
        }

        public async Task<DateTimeOffset?> GetLastReadRules(NetUserId player)
        {
            await using var db = await GetDb();

            return NormalizeDatabaseTime(await db.DbContext.Player
                .Where(dbPlayer => dbPlayer.UserId == player)
                .Select(dbPlayer => dbPlayer.LastReadRules)
                .SingleOrDefaultAsync());
        }

        public async Task SetLastReadRules(NetUserId player, DateTimeOffset? date)
        {
            await using var db = await GetDb();

            var dbPlayer = await db.DbContext.Player.Where(dbPlayer => dbPlayer.UserId == player).SingleOrDefaultAsync();
            if (dbPlayer == null)
            {
                return;
            }

            dbPlayer.LastReadRules = date?.UtcDateTime;
            await db.DbContext.SaveChangesAsync();
        }

        public async Task<bool> GetBlacklistStatusAsync(NetUserId player)
        {
            await using var db = await GetDb();

            return await db.DbContext.Blacklist.AnyAsync(w => w.UserId == player);
        }

        public async Task AddToBlacklistAsync(NetUserId player)
        {
            await using var db = await GetDb();

            db.DbContext.Blacklist.Add(new Blacklist() { UserId = player });
            await db.DbContext.SaveChangesAsync();
        }

        public async Task RemoveFromBlacklistAsync(NetUserId player)
        {
            await using var db = await GetDb();
            var entry = await db.DbContext.Blacklist.SingleAsync(w => w.UserId == player);
            db.DbContext.Blacklist.Remove(entry);
            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region Uploaded Resources Logs

        public async Task AddUploadedResourceLogAsync(NetUserId user, DateTimeOffset date, string path, byte[] data)
        {
            await using var db = await GetDb();

            db.DbContext.UploadedResourceLog.Add(new UploadedResourceLog() { UserId = user, Date = date.UtcDateTime, Path = path, Data = data });
            await db.DbContext.SaveChangesAsync();
        }

        public async Task PurgeUploadedResourceLogAsync(int days)
        {
            await using var db = await GetDb();

            var date = DateTime.UtcNow.Subtract(TimeSpan.FromDays(days));

            await foreach (var log in db.DbContext.UploadedResourceLog
                               .Where(l => date > l.Date)
                               .AsAsyncEnumerable())
            {
                db.DbContext.UploadedResourceLog.Remove(log);
            }

            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region Admin Notes

        public virtual async Task<int> AddAdminNote(AdminNote note)
        {
            await using var db = await GetDb();
            db.DbContext.AdminNotes.Add(note);
            await db.DbContext.SaveChangesAsync();
            return note.Id;
        }

        public virtual async Task<int> AddAdminWatchlist(AdminWatchlist watchlist)
        {
            await using var db = await GetDb();
            db.DbContext.AdminWatchlists.Add(watchlist);
            await db.DbContext.SaveChangesAsync();
            return watchlist.Id;
        }

        public virtual async Task<int> AddAdminMessage(AdminMessage message)
        {
            await using var db = await GetDb();
            db.DbContext.AdminMessages.Add(message);
            await db.DbContext.SaveChangesAsync();
            return message.Id;
        }

        public async Task<AdminNoteRecord?> GetAdminNote(int id)
        {
            await using var db = await GetDb();
            var entity = await db.DbContext.AdminNotes
                .Where(note => note.Id == id)
                .Include(note => note.Round)
                .ThenInclude(r => r!.Server)
                .Include(note => note.CreatedBy)
                .Include(note => note.LastEditedBy)
                .Include(note => note.DeletedBy)
                .Include(note => note.Player)
                .SingleOrDefaultAsync();

            return entity == null ? null : MakeAdminNoteRecord(entity);
        }

        private AdminNoteRecord MakeAdminNoteRecord(AdminNote entity)
        {
            return new AdminNoteRecord(
                entity.Id,
                MakeRoundRecord(entity.Round),
                MakePlayerRecord(entity.Player),
                entity.PlaytimeAtNote,
                entity.Message,
                entity.Severity,
                MakePlayerRecord(entity.CreatedBy),
                NormalizeDatabaseTime(entity.CreatedAt),
                MakePlayerRecord(entity.LastEditedBy),
                NormalizeDatabaseTime(entity.LastEditedAt),
                NormalizeDatabaseTime(entity.ExpirationTime),
                entity.Deleted,
                MakePlayerRecord(entity.DeletedBy),
                NormalizeDatabaseTime(entity.DeletedAt),
                entity.Secret);
        }

        public async Task<AdminWatchlistRecord?> GetAdminWatchlist(int id)
        {
            await using var db = await GetDb();
            var entity = await db.DbContext.AdminWatchlists
                .Where(note => note.Id == id)
                .Include(note => note.Round)
                .ThenInclude(r => r!.Server)
                .Include(note => note.CreatedBy)
                .Include(note => note.LastEditedBy)
                .Include(note => note.DeletedBy)
                .Include(note => note.Player)
                .SingleOrDefaultAsync();

            return entity == null ? null : MakeAdminWatchlistRecord(entity);
        }

        public async Task<AdminMessageRecord?> GetAdminMessage(int id)
        {
            await using var db = await GetDb();
            var entity = await db.DbContext.AdminMessages
                .Where(note => note.Id == id)
                .Include(note => note.Round)
                .ThenInclude(r => r!.Server)
                .Include(note => note.CreatedBy)
                .Include(note => note.LastEditedBy)
                .Include(note => note.DeletedBy)
                .Include(note => note.Player)
                .SingleOrDefaultAsync();

            return entity == null ? null : MakeAdminMessageRecord(entity);
        }

        private AdminMessageRecord MakeAdminMessageRecord(AdminMessage entity)
        {
            return new AdminMessageRecord(
                entity.Id,
                MakeRoundRecord(entity.Round),
                MakePlayerRecord(entity.Player),
                entity.PlaytimeAtNote,
                entity.Message,
                MakePlayerRecord(entity.CreatedBy),
                NormalizeDatabaseTime(entity.CreatedAt),
                MakePlayerRecord(entity.LastEditedBy),
                NormalizeDatabaseTime(entity.LastEditedAt),
                NormalizeDatabaseTime(entity.ExpirationTime),
                entity.Deleted,
                MakePlayerRecord(entity.DeletedBy),
                NormalizeDatabaseTime(entity.DeletedAt),
                entity.Seen,
                entity.Dismissed);
        }

        public async Task<BanNoteRecord?> GetBanAsNoteAsync(int id)
        {
            await using var db = await GetDb();

            var ban = await BanRecordQuery(db.DbContext)
                .SingleOrDefaultAsync(b => b.Id == id);

            if (ban is null)
                return null;

            return await MakeBanNoteRecord(db.DbContext, ban);
        }

        public async Task<List<IAdminRemarksRecord>> GetAllAdminRemarks(Guid player)
        {
            await using var db = await GetDb();
            List<IAdminRemarksRecord> notes = new();
            notes.AddRange(
                (await (from note in db.DbContext.AdminNotes
                        where note.PlayerUserId == player &&
                              !note.Deleted &&
                              (note.ExpirationTime == null || DateTime.UtcNow < note.ExpirationTime)
                        select note)
                    .Include(note => note.Round)
                    .ThenInclude(r => r!.Server)
                    .Include(note => note.CreatedBy)
                    .Include(note => note.LastEditedBy)
                    .Include(note => note.Player)
                    .ToListAsync()).Select(MakeAdminNoteRecord));
            notes.AddRange(await GetActiveWatchlistsImpl(db, player));
            notes.AddRange(await GetMessagesImpl(db, player));
            notes.AddRange(await GetBansAsNotesForUser(db, player));
            return notes;
        }
        public async Task EditAdminNote(int id, string message, NoteSeverity severity, bool secret, Guid editedBy, DateTimeOffset editedAt, DateTimeOffset? expiryTime)
        {
            await using var db = await GetDb();

            var note = await db.DbContext.AdminNotes.Where(note => note.Id == id).SingleAsync();
            note.Message = message;
            note.Severity = severity;
            note.Secret = secret;
            note.LastEditedById = editedBy;
            note.LastEditedAt = editedAt.UtcDateTime;
            note.ExpirationTime = expiryTime?.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task EditAdminWatchlist(int id, string message, Guid editedBy, DateTimeOffset editedAt, DateTimeOffset? expiryTime)
        {
            await using var db = await GetDb();

            var note = await db.DbContext.AdminWatchlists.Where(note => note.Id == id).SingleAsync();
            note.Message = message;
            note.LastEditedById = editedBy;
            note.LastEditedAt = editedAt.UtcDateTime;
            note.ExpirationTime = expiryTime?.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task EditAdminMessage(int id, string message, Guid editedBy, DateTimeOffset editedAt, DateTimeOffset? expiryTime)
        {
            await using var db = await GetDb();

            var note = await db.DbContext.AdminMessages.Where(note => note.Id == id).SingleAsync();
            note.Message = message;
            note.LastEditedById = editedBy;
            note.LastEditedAt = editedAt.UtcDateTime;
            note.ExpirationTime = expiryTime?.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task DeleteAdminNote(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var note = await db.DbContext.AdminNotes.Where(note => note.Id == id).SingleAsync();

            note.Deleted = true;
            note.DeletedById = deletedBy;
            note.DeletedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task DeleteAdminWatchlist(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var watchlist = await db.DbContext.AdminWatchlists.Where(note => note.Id == id).SingleAsync();

            watchlist.Deleted = true;
            watchlist.DeletedById = deletedBy;
            watchlist.DeletedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task DeleteAdminMessage(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var message = await db.DbContext.AdminMessages.Where(note => note.Id == id).SingleAsync();

            message.Deleted = true;
            message.DeletedById = deletedBy;
            message.DeletedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task HideBanFromNotes(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var ban = await db.DbContext.Ban.Where(ban => ban.Id == id).SingleAsync();

            ban.Hidden = true;
            ban.LastEditedById = deletedBy;
            ban.LastEditedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task<List<IAdminRemarksRecord>> GetVisibleAdminRemarks(Guid player)
        {
            await using var db = await GetDb();
            List<IAdminRemarksRecord> notesCol = new();
            notesCol.AddRange(
                (await (from note in db.DbContext.AdminNotes
                        where note.PlayerUserId == player &&
                              !note.Secret &&
                              !note.Deleted &&
                              (note.ExpirationTime == null || DateTime.UtcNow < note.ExpirationTime)
                        select note)
                    .Include(note => note.Round)
                    .ThenInclude(r => r!.Server)
                    .Include(note => note.CreatedBy)
                    .Include(note => note.Player)
                    .ToListAsync()).Select(MakeAdminNoteRecord));
            notesCol.AddRange(await GetMessagesImpl(db, player));
            notesCol.AddRange(await GetBansAsNotesForUser(db, player));
            return notesCol;
        }

        public async Task<List<AdminWatchlistRecord>> GetActiveWatchlists(Guid player)
        {
            await using var db = await GetDb();
            return await GetActiveWatchlistsImpl(db, player);
        }

        protected async Task<List<AdminWatchlistRecord>> GetActiveWatchlistsImpl(DbGuard db, Guid player)
        {
            var entities = await (from watchlist in db.DbContext.AdminWatchlists
                          where watchlist.PlayerUserId == player &&
                                !watchlist.Deleted &&
                                (watchlist.ExpirationTime == null || DateTime.UtcNow < watchlist.ExpirationTime)
                          select watchlist)
                .Include(note => note.Round)
                .ThenInclude(r => r!.Server)
                .Include(note => note.CreatedBy)
                .Include(note => note.LastEditedBy)
                .Include(note => note.Player)
                .ToListAsync();

            return entities.Select(MakeAdminWatchlistRecord).ToList();
        }

        private AdminWatchlistRecord MakeAdminWatchlistRecord(AdminWatchlist entity)
        {
            return new AdminWatchlistRecord(entity.Id, MakeRoundRecord(entity.Round), MakePlayerRecord(entity.Player), entity.PlaytimeAtNote, entity.Message, MakePlayerRecord(entity.CreatedBy), NormalizeDatabaseTime(entity.CreatedAt), MakePlayerRecord(entity.LastEditedBy), NormalizeDatabaseTime(entity.LastEditedAt), NormalizeDatabaseTime(entity.ExpirationTime), entity.Deleted, MakePlayerRecord(entity.DeletedBy), NormalizeDatabaseTime(entity.DeletedAt));
        }

        public async Task<List<AdminMessageRecord>> GetMessages(Guid player)
        {
            await using var db = await GetDb();
            return await GetMessagesImpl(db, player);
        }

        protected async Task<List<AdminMessageRecord>> GetMessagesImpl(DbGuard db, Guid player)
        {
            var entities = await (from message in db.DbContext.AdminMessages
                        where message.PlayerUserId == player && !message.Deleted &&
                              (message.ExpirationTime == null || DateTime.UtcNow < message.ExpirationTime)
                        select message).Include(note => note.Round)
                    .ThenInclude(r => r!.Server)
                    .Include(note => note.CreatedBy)
                    .Include(note => note.LastEditedBy)
                    .Include(note => note.Player)
                    .ToListAsync();

            return entities.Select(MakeAdminMessageRecord).ToList();
        }

        public async Task MarkMessageAsSeen(int id, bool dismissedToo)
        {
            await using var db = await GetDb();
            var message = await db.DbContext.AdminMessages.SingleAsync(m => m.Id == id);
            message.Seen = true;
            if (dismissedToo)
                message.Dismissed = true;
            await db.DbContext.SaveChangesAsync();
        }

        private static IQueryable<Ban> BanRecordQuery(ServerDbContext dbContext)
        {
            return dbContext.Ban
                .Include(ban => ban.Unban)
                .Include(ban => ban.Rounds!)
                .ThenInclude(r => r.Round)
                .ThenInclude(r => r!.Server)
                .Include(ban => ban.Addresses)
                .Include(ban => ban.Players)
                .Include(ban => ban.Roles)
                .Include(ban => ban.Hwids)
                .Include(ban => ban.CreatedBy)
                .Include(ban => ban.LastEditedBy)
                .Include(ban => ban.Unban);
        }

        private async Task<BanNoteRecord> MakeBanNoteRecord(ServerDbContext dbContext, Ban ban)
        {
            var playerRecords = await AsyncSelect(ban.Players,
                async bp => MakePlayerRecord(bp.UserId,
                    await dbContext.Player.SingleOrDefaultAsync(p => p.UserId == bp.UserId)));

            return new BanNoteRecord(
                ban.Id,
                ban.Type,
                [..ban.Rounds!.Select(br => MakeRoundRecord(br.Round!))],
                [..playerRecords],
                ban.PlaytimeAtNote,
                ban.Reason,
                ban.Severity,
                MakePlayerRecord(ban.CreatedBy!),
                NormalizeDatabaseTime(ban.BanTime),
                MakePlayerRecord(ban.LastEditedBy!),
                NormalizeDatabaseTime(ban.LastEditedAt),
                NormalizeDatabaseTime(ban.ExpirationTime),
                ban.Hidden,
                ban.Unban?.UnbanningAdmin == null
                    ? null
                    : MakePlayerRecord(
                        ban.Unban.UnbanningAdmin.Value,
                        await dbContext.Player.SingleOrDefaultAsync(p => p.UserId == ban.Unban.UnbanningAdmin.Value)),
                NormalizeDatabaseTime(ban.Unban?.UnbanTime),
                [..ban.Roles!.Select(br => new BanRoleDef(br.RoleType, br.RoleId))]);
        }

        // These two are here because they get converted into notes later
        protected async Task<List<BanNoteRecord>> GetBansAsNotesForUser(DbGuard db, Guid user)
        {
            // You can't group queries, as player will not always exist. When it doesn't, the
            // whole query returns nothing
            var bans = await BanRecordQuery(db.DbContext)
                .AsSplitQuery()
                .Where(ban => ban.Players!.Any(bp => bp.UserId == user) && !ban.Hidden)
                .ToArrayAsync();

            var banNotes = new List<BanNoteRecord>();
            foreach (var ban in bans)
            {
                var banNote = await MakeBanNoteRecord(db.DbContext, ban);

                banNotes.Add(banNote);
            }

            return banNotes;
        }

        #endregion

        #region Job Whitelists

        public async Task<bool> AddJobWhitelist(Guid player, ProtoId<JobPrototype> job)
        {
            await using var db = await GetDb();
            var exists = await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == job.Id)
                .AnyAsync();

            if (exists)
                return false;

            var whitelist = new RoleWhitelist
            {
                PlayerUserId = player,
                RoleId = job
            };
            db.DbContext.RoleWhitelists.Add(whitelist);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        public async Task<List<string>> GetJobWhitelists(Guid player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Select(w => w.RoleId)
                .ToListAsync(cancellationToken: cancel);
        }

        public async Task<bool> IsJobWhitelisted(Guid player, ProtoId<JobPrototype> job)
        {
            await using var db = await GetDb();
            return await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == job.Id)
                .AnyAsync();
        }

        public async Task<bool> RemoveJobWhitelist(Guid player, ProtoId<JobPrototype> job)
        {
            await using var db = await GetDb();
            var entry = await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == job.Id)
                .SingleOrDefaultAsync();

            if (entry == null)
                return false;

            db.DbContext.RoleWhitelists.Remove(entry);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        #endregion

        # region IPIntel

        public async Task<bool> UpsertIPIntelCache(DateTime time, IPAddress ip, float score)
        {
            while (true)
            {
                try
                {
                    await using var db = await GetDb();

                    var existing = await db.DbContext.IPIntelCache
                        .Where(w => ip.Equals(w.Address))
                        .SingleOrDefaultAsync();

                    if (existing == null)
                    {
                        var newCache = new IPIntelCache
                        {
                            Time = time,
                            Address = ip,
                            Score = score,
                        };
                        db.DbContext.IPIntelCache.Add(newCache);
                    }
                    else
                    {
                        existing.Time = time;
                        existing.Score = score;
                    }

                    await Task.Delay(5000);

                    await db.DbContext.SaveChangesAsync();
                    return true;
                }
                catch (DbUpdateException)
                {
                    _opsLog.Warning("IPIntel UPSERT failed with a db exception... retrying.");
                }
            }
        }

        public async Task<IPIntelCache?> GetIPIntelCache(IPAddress ip)
        {
            await using var db = await GetDb();

            return await db.DbContext.IPIntelCache
                .SingleOrDefaultAsync(w => ip.Equals(w.Address));
        }

        public async Task<bool> CleanIPIntelCache(TimeSpan range)
        {
            await using var db = await GetDb();

            // Calculating this here cause otherwise sqlite whines.
            var cutoffTime = DateTime.UtcNow.Subtract(range);

            await db.DbContext.IPIntelCache
                .Where(w => w.Time <= cutoffTime)
                .ExecuteDeleteAsync();

            await db.DbContext.SaveChangesAsync();
            return true;
        }

        #endregion

        public abstract Task SendNotification(DatabaseNotification notification);

        // SQLite returns DateTime as Kind=Unspecified, Npgsql actually knows for sure it's Kind=Utc.
        // Normalize DateTimes here so they're always Utc. Thanks.
        protected abstract DateTime NormalizeDatabaseTime(DateTime time);

        [return: NotNullIfNotNull(nameof(time))]
        protected DateTime? NormalizeDatabaseTime(DateTime? time)
        {
            return time != null ? NormalizeDatabaseTime(time.Value) : time;
        }

        public async Task<bool> HasPendingModelChanges()
        {
            await using var db = await GetDb();
            return db.DbContext.Database.HasPendingModelChanges();
        }

        protected abstract Task<DbGuard> GetDb(
            CancellationToken cancel = default,
            [CallerMemberName] string? name = null);

        protected void LogDbOp(string? name)
        {
            _opsLog.Verbose($"Running DB operation: {name ?? "unknown"}");
        }

        protected abstract class DbGuard : IAsyncDisposable
        {
            public abstract ServerDbContext DbContext { get; }

            public abstract ValueTask DisposeAsync();
        }

        protected void NotificationReceived(DatabaseNotification notification)
        {
            OnNotificationReceived?.Invoke(notification);
        }

        public virtual void Shutdown()
        {

        }

        private static async Task<IEnumerable<TResult>> AsyncSelect<T, TResult>(
            IEnumerable<T>? enumerable,
            Func<T, Task<TResult>> selector)
        {
            var results = new List<TResult>();

            foreach (var item in enumerable ?? [])
            {
                results.Add(await selector(item));
            }

            return [..results];
        }
    }
}
