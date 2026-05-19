#nullable disable warnings

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.KillTracking;
using Content.Server.Players.PlayTimeTracking;
using Content.Server._WH40K.Command;
using Content.Server._WH40K.Command.Components;
using Content.Server._WH40K.Diagnostics;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Influence;
using Content.Server._WH40K.Stats;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared._WH40K.DiscordAuth;
using Content.Shared._WH40K.MetaProgress;
using Content.Shared._WH40K.StrategicPoints;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.MetaProgress;

public sealed class WH40KMetaProgressSystem : EntitySystem
{
	public enum AdminResetScope : byte
	{
		Progress,
		Development,
		Achievements,
		Decorations,
		All
	}

	public sealed record WH40KMetaDecorationRevalidationResult(
		WH40KMetaProgressSnapshot Snapshot,
		int GrantedDecorations,
		int RevokedDecorations,
		int ResetSelections)
	{
		public bool Changed => GrantedDecorations > 0 || RevokedDecorations > 0 || ResetSelections > 0;
	}

	public sealed record WH40KMetaSelectionResetResult(
		WH40KMetaProgressSnapshot Snapshot,
		int ResetSelections);

	private struct RateLimitWindowState
	{
		public DateTimeOffset ExpiresAt;

		public int Count;

		public RateLimitWindowState(DateTimeOffset expiresAt, int count)
		{
			ExpiresAt = expiresAt;
			Count = count;
		}
	}

	private sealed class RuntimeProgressState
	{
		public int StateVersion;

		public int Level;

		public int CurrentXp;

		public int RequiredXp;

		public int LifetimeXp;

		public int SeasonXp;

		public bool DbLoadStarted;

		public bool DbLoadCompleted;

		public string SelectedGhostSkinId = string.Empty;

		public string SelectedOocTitleId = string.Empty;

		public string SelectedOocNameColorId = string.Empty;

		public readonly Dictionary<string, int> AchievementProgress = new Dictionary<string, int>();

		public readonly Dictionary<string, long> LifetimeAchievementSourceCursor = new Dictionary<string, long>(StringComparer.Ordinal);

		public readonly HashSet<string> CompletedAchievements = new HashSet<string>();

		public readonly HashSet<string> ClaimedAchievementRewards = new HashSet<string>();

		public readonly Dictionary<string, RuntimeDecorationUnlockState> DecorationUnlockState = new Dictionary<string, RuntimeDecorationUnlockState>(StringComparer.Ordinal);

		public readonly Dictionary<string, RuntimeDevelopmentUnlockState> DevelopmentUnlockState = new Dictionary<string, RuntimeDevelopmentUnlockState>(StringComparer.Ordinal);
	}

	private sealed class RuntimeDecorationUnlockState
	{
		public bool Unlocked;

		public DateTimeOffset? UnlockedAt;

		public int SourceLevel;

		public DateTimeOffset UpdatedAt;

		public RuntimeDecorationUnlockState(bool unlocked, DateTimeOffset? unlockedAt, int sourceLevel, DateTimeOffset updatedAt)
		{
			Unlocked = unlocked;
			UnlockedAt = unlockedAt;
			SourceLevel = sourceLevel;
			UpdatedAt = updatedAt;
		}
	}

	private sealed class RuntimeDevelopmentUnlockState
	{
		public DateTimeOffset UnlockedAt;

		public int SpentCost;

		public DateTimeOffset UpdatedAt;

		public RuntimeDevelopmentUnlockState(DateTimeOffset unlockedAt, int spentCost, DateTimeOffset updatedAt)
		{
			UnlockedAt = unlockedAt;
			SpentCost = spentCost;
			UpdatedAt = updatedAt;
		}
	}

	private const string AllCompleteAchievementId = "wh40k-ach-all-complete";

	private static readonly HashSet<string> RetiredObjectiveAchievementIds = new(StringComparer.Ordinal)
	{
		"wh40k-ach-frontline-anchor",
		"wh40k-ach-point-breaker",
		"wh40k-ach-flag-keeper",
		"wh40k-ach-sector-dominator",
		"wh40k-ach-wall-of-steel",
		"wh40k-ach-objective-ace"
	};

	private const string DefaultLevelRewardTableId = "WH40KMetaLevelRewardTableDefault";

	private const string DefaultAchievementRewardLocKey = "wh40k-meta-progress-achievements-reward-none";

	private static readonly TimeSpan BackgroundSnapshotPushDelay = TimeSpan.FromSeconds(0.5);

	private const float RequestStateRateLimitPeriodSeconds = 1f;

	private const int RequestStateRateLimitCount = 8;

	private const float SetDecorationRateLimitPeriodSeconds = 1f;

	private const int SetDecorationRateLimitCount = 4;

	private const float ConfirmDevelopmentRateLimitPeriodSeconds = 1f;

	private const int ConfirmDevelopmentRateLimitCount = 4;

	private const int ValidatedHealBucketsPerPairPerRoundCap = 3;

	[Dependency]
	private readonly IConfigurationManager _config = default!;

	[Dependency]
	private readonly IServerDbManager _db = default!;

	[Dependency]
	private readonly WH40KDbDiagnosticsSystem _dbDiag = default!;

	[Dependency]
	private readonly ISharedWH40KDiscordAuthManager _discordAuth = default!;

	[Dependency]
	private readonly GameTicker _gameTicker = default!;

	[Dependency]
	private readonly IPlayerManager _players = default!;

	[Dependency]
	private readonly IPrototypeManager _proto = default!;

	[Dependency]
	private readonly WH40KPlayerStatsSystem _stats = default!;

	[Dependency]
	private readonly WH40KCombatVictimResolverSystem _combatVictims = default!;

	[Dependency]
	private readonly WH40KTeamRuleFacadeSystem _teamBattleRule = default!;

	[Dependency]
	private readonly ITaskManager _task = default!;

	[Dependency]
	private readonly PlayTimeTrackingManager _playTime = default!;

	[Dependency]
	private readonly IGameTiming _timing = default!;

	private readonly Dictionary<NetUserId, RuntimeProgressState> _states = new Dictionary<NetUserId, RuntimeProgressState>();

	private readonly Dictionary<NetUserId, int> _roundKillXpSpent = new Dictionary<NetUserId, int>();

	private readonly Dictionary<NetUserId, int> _roundObjectiveXpSpent = new Dictionary<NetUserId, int>();

	private readonly Dictionary<NetUserId, int> _roundRepeatableXpSpent = new Dictionary<NetUserId, int>();

	private readonly Dictionary<string, int> _roundKillRewardGrantXp = new Dictionary<string, int>(StringComparer.Ordinal);

	private readonly Dictionary<NetUserId, int> _roundHealRemainders = new Dictionary<NetUserId, int>();

	private readonly HashSet<(NetUserId SourceUserId, NetUserId TargetUserId)> _roundValidatedRevives = new HashSet<(NetUserId SourceUserId, NetUserId TargetUserId)>();

	private readonly HashSet<(NetUserId SourceUserId, NetUserId TargetUserId)> _roundValidatedStabilizations = new HashSet<(NetUserId SourceUserId, NetUserId TargetUserId)>();

	private readonly Dictionary<(NetUserId SourceUserId, NetUserId TargetUserId), int> _roundValidatedHealBuckets = new Dictionary<(NetUserId SourceUserId, NetUserId TargetUserId), int>();

	private readonly Dictionary<NetUserId, RateLimitWindowState> _requestStateRateLimits = new Dictionary<NetUserId, RateLimitWindowState>();

	private readonly Dictionary<NetUserId, RateLimitWindowState> _setDecorationRateLimits = new Dictionary<NetUserId, RateLimitWindowState>();

	private readonly Dictionary<NetUserId, RateLimitWindowState> _confirmDevelopmentRateLimits = new Dictionary<NetUserId, RateLimitWindowState>();

	private readonly HashSet<string> _processedMissionOutcomeRewardKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly object _pendingTasksLock = new object();

	private readonly HashSet<Task> _pendingTasks = new HashSet<Task>();

	private readonly object _persistQueueLock = new object();

	private readonly Dictionary<NetUserId, Task> _persistQueueTail = new Dictionary<NetUserId, Task>();

	private readonly Dictionary<NetUserId, TimeSpan> _queuedSnapshotPushes = new Dictionary<NetUserId, TimeSpan>();

	private readonly HashSet<NetUserId> _networkSnapshotSubscribers = new HashSet<NetUserId>();

	private ISawmill _sawmill;

	private int _levelCap;

	private int _xpObjectiveCapPerRound;

	private int _xpObjectiveFailure;

	private int _xpObjectiveMajor;

	private int _xpObjectiveMinor;

	private int _xpObjectiveTimeout;

	private int _xpStrategicPointBuild;

	private int _xpStrategicPointDestroy;

	private int _xpStrategicPointTripleHold;

	private int _xpStrategicPointUpgrade;

	private int _xpKill;

	private int _xpKillCapPerRound;

	private int _xpRepeatableCapPerRound;

	private bool _unlockRequirementsBypassed;

	private bool _statsTrace;

	private List<WH40KMetaAchievementPrototype>? _sortedAchievementPrototypes;

	private List<WH40KMetaDecorationPrototype>? _sortedDecorationPrototypes;

	private float _xpMultiplier;

	private int _xpRoundWin;

	private int _lastProcessedRoundWinRewardRoundId = -1;

	public event Action<NetUserId, WH40KMetaProgressSnapshot>? SnapshotPushed;

	public override void Initialize()
	{
		base.Initialize();
		_sawmill = Logger.GetSawmill("wh40k.meta.progress");
		base.Subs.CVar(_config, CCVars.WH40KMetaLevelCap, OnLevelCapChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpMultiplier, OnXpMultiplierChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaUnlocksEnforced, OnUnlocksEnforcedChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpRoundWin, OnXpRoundWinChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpKill, OnXpKillChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpKillCapPerRound, OnXpKillCapPerRoundChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpObjectiveMajor, OnXpObjectiveMajorChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpObjectiveMinor, OnXpObjectiveMinorChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpObjectiveTimeout, OnXpObjectiveTimeoutChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpObjectiveFailure, OnXpObjectiveFailureChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpStrategicPointBuild, OnXpStrategicPointBuildChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpStrategicPointUpgrade, OnXpStrategicPointUpgradeChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpStrategicPointDestroy, OnXpStrategicPointDestroyChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpStrategicPointTripleHold, OnXpStrategicPointTripleHoldChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpObjectiveCapPerRound, OnXpObjectiveCapPerRoundChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaXpRepeatableCapPerRound, OnXpRepeatableCapPerRoundChanged, invokeImmediately: true);
		base.Subs.CVar(_config, CCVars.WH40KMetaStatsTrace, OnStatsTraceChanged, invokeImmediately: true);
		SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
		SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
		SubscribeLocalEvent<KillReportedEvent>(OnKillReported);
		SubscribeLocalEvent<AttributedKilledEvent>(OnAttributedKilled);
		SubscribeLocalEvent<WH40KValidatedKillRewardEvent>(OnValidatedKillReward);
		SubscribeLocalEvent<WH40KValidatedKillRewardRevokedEvent>(OnValidatedKillRewardRevoked);
		SubscribeLocalEvent<WH40KConfirmedEliminationEvent>(OnConfirmedElimination);
		SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
		SubscribeLocalEvent<WH40KTeamBattleHealingDoneEvent>(OnTeamBattleHealingDone);
		SubscribeLocalEvent<WH40KInfluencePointCapturedEvent>(OnInfluencePointCaptured);
		SubscribeLocalEvent<WH40KInfluencePointRewardTickEvent>(OnInfluencePointRewardTick);
		SubscribeLocalEvent<WH40KStrategicPointBuiltEvent>(OnStrategicPointBuilt);
		SubscribeLocalEvent<WH40KStrategicPointUpgradedEvent>(OnStrategicPointUpgraded);
		SubscribeLocalEvent<WH40KStrategicPointDestroyedEvent>(OnStrategicPointDestroyed);
		SubscribeLocalEvent<WH40KStrategicPointTripleHoldCompletedEvent>(OnStrategicPointTripleHoldCompleted);
		SubscribeLocalEvent<WH40KMissionOutcomeAppliedEvent>(OnMissionOutcomeApplied);
		SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEndMessage);
		SubscribeLocalEvent<WH40KPlayerStatRecordedEvent>(OnPlayerStatRecorded);
		SubscribeNetworkEvent<WH40KMetaProgressRequestStateEvent>(OnRequestState);
		SubscribeNetworkEvent<WH40KMetaProgressSetDecorationSelectionEvent>(OnSetDecorationSelection);
		SubscribeNetworkEvent<WH40KMetaProgressConfirmDevelopmentPlanEvent>(OnConfirmDevelopmentPlan);
		_players.PlayerStatusChanged += OnPlayerStatusChanged;
		_proto.PrototypesReloaded += OnPrototypesReloaded;
	}

	public override void Shutdown()
	{
		base.Shutdown();
		_players.PlayerStatusChanged -= OnPlayerStatusChanged;
		_proto.PrototypesReloaded -= OnPrototypesReloaded;
		Task[] array = SnapshotPendingTasks();
		if (array.Length != 0)
		{
			_task.BlockWaitOnTask(Task.WhenAll(array));
		}
	}

	public override void Update(float frameTime)
	{
		base.Update(frameTime);
		if (_queuedSnapshotPushes.Count == 0)
		{
			return;
		}
		TimeSpan curTime = _timing.CurTime;
		// Avoid LINQ allocations in hot path - iterate and collect keys to remove
		var toProcess = new ValueList<NetUserId>();
		foreach (var entry in _queuedSnapshotPushes)
		{
			if (entry.Value <= curTime)
				toProcess.Add(entry.Key);
		}
		foreach (var userId in toProcess)
		{
			_queuedSnapshotPushes.Remove(userId);
			PushSnapshotIfOnline(userId);
		}
	}

	public WH40KMetaProgressSnapshot GetSnapshot(NetUserId userId)
	{
		RuntimeProgressState state = EnsureState(userId);
		ReconcileState(userId, state);
		return BuildSnapshot(userId, state);
	}

	public async Task EnsureStateLoadedForUserAsync(NetUserId userId)
	{
		await EnsureStateLoadedAsync(userId);
	}

	public void RefreshDiscordRequirementsForUser(NetUserId userId)
	{
		PushSnapshotIfOnline(userId);
	}

	public async Task<WH40KMetaDecorationRevalidationResult> RevalidateUnlocksForAdminAsync(NetUserId userId)
	{
		RuntimeProgressState state = await EnsureStateLoadedAsync(userId);
		var previousGhostSkin = state.SelectedGhostSkinId;
		var previousTitle = state.SelectedOocTitleId;
		var previousOocColor = state.SelectedOocNameColorId;
		var grantedDecorations = 0;
		var revokedDecorations = 0;
		var strictStateChanged = false;
		var updatedAt = DateTimeOffset.UtcNow;
		var rewardDecorationIds = GetActiveAchievementRewardDecorationIds(state);
		var seenDecorationIds = new HashSet<string>(StringComparer.Ordinal);

		foreach (var prototype in _proto.EnumeratePrototypes<WH40KMetaDecorationPrototype>())
		{
			seenDecorationIds.Add(prototype.ID);
			var shouldUnlock = ShouldDecorationBeUnlockedStrict(userId, state, prototype, rewardDecorationIds);

			if (state.DecorationUnlockState.TryGetValue(prototype.ID, out var unlockState))
			{
				if (unlockState.Unlocked == shouldUnlock)
					continue;

				unlockState.Unlocked = shouldUnlock;
				unlockState.UnlockedAt = shouldUnlock ? unlockState.UnlockedAt ?? updatedAt : null;
				unlockState.SourceLevel = state.Level;
				unlockState.UpdatedAt = updatedAt;

				if (shouldUnlock)
					grantedDecorations++;
				else
					revokedDecorations++;

				strictStateChanged = true;

				continue;
			}

			if (!shouldUnlock)
				continue;

			state.DecorationUnlockState[prototype.ID] = new RuntimeDecorationUnlockState(true, updatedAt, state.Level, updatedAt);
			grantedDecorations++;
			strictStateChanged = true;
		}

		var staleDecorationIds = state.DecorationUnlockState.Keys.Where(id => !seenDecorationIds.Contains(id)).ToList();
		foreach (var staleDecorationId in staleDecorationIds)
		{
			if (state.DecorationUnlockState.TryGetValue(staleDecorationId, out var staleState) && staleState.Unlocked)
				revokedDecorations++;

			state.DecorationUnlockState.Remove(staleDecorationId);
			strictStateChanged = true;
		}

		if (strictStateChanged)
		{
			state.StateVersion++;
			QueuePersistState(userId);
		}

		ReconcileState(userId, state);
		var snapshot = BuildSnapshot(userId, state);
		await AwaitPersistQueueAsync(userId);
		ReconcileState(userId, state);
		snapshot = BuildSnapshot(userId, state);
		PushSnapshotIfOnline(userId);

		var resetSelections = 0;
		if (!string.Equals(previousGhostSkin, snapshot.DecorationSelection.SelectedGhostSkinId, StringComparison.Ordinal))
			resetSelections++;
		if (!string.Equals(previousTitle, snapshot.DecorationSelection.SelectedOocTitleId, StringComparison.Ordinal))
			resetSelections++;
		if (!string.Equals(previousOocColor, snapshot.DecorationSelection.SelectedOocNameColorId, StringComparison.Ordinal))
			resetSelections++;

		return new WH40KMetaDecorationRevalidationResult(snapshot, grantedDecorations, revokedDecorations, resetSelections);
	}

	public async Task<WH40KMetaSelectionResetResult> ResetSelectionsForAdminAsync(NetUserId userId)
	{
		RuntimeProgressState state = await EnsureStateLoadedAsync(userId);
		var resetSelections = 0;

		if (!string.IsNullOrWhiteSpace(state.SelectedGhostSkinId))
		{
			state.SelectedGhostSkinId = string.Empty;
			resetSelections++;
		}

		if (!string.IsNullOrWhiteSpace(state.SelectedOocTitleId))
		{
			state.SelectedOocTitleId = string.Empty;
			resetSelections++;
		}

		if (!string.IsNullOrWhiteSpace(state.SelectedOocNameColorId))
		{
			state.SelectedOocNameColorId = string.Empty;
			resetSelections++;
		}

		if (resetSelections > 0)
		{
			state.StateVersion++;
			QueuePersistState(userId);
		}

		ReconcileState(userId, state);
		var snapshot = BuildSnapshot(userId, state);
		await AwaitPersistQueueAsync(userId);
		ReconcileState(userId, state);
		snapshot = BuildSnapshot(userId, state);
		PushSnapshotIfOnline(userId);
		return new WH40KMetaSelectionResetResult(snapshot, resetSelections);
	}

	public void SetLifetimeXp(NetUserId userId, int lifetimeXp)
	{
		RuntimeProgressState runtimeProgressState = EnsureState(userId);
		int lifetimeXp2 = runtimeProgressState.LifetimeXp;
		runtimeProgressState.LifetimeXp = Math.Max(0, lifetimeXp);
		Recalculate(runtimeProgressState);
		runtimeProgressState.StateVersion++;
		QueuePersistState(userId);
		PushSnapshotIfOnline(userId);
		int num = runtimeProgressState.LifetimeXp - lifetimeXp2;
		if (num != 0)
		{
			_stats.Record(userId, "meta.xp.manual_set_delta", num, new Dictionary<string, string>(StringComparer.Ordinal) { ["source"] = "set_lifetime_xp" });
		}
	}

	public void AddLifetimeXp(NetUserId userId, int deltaXp)
	{
		AddLifetimeXpInternal(userId, deltaXp, "meta.xp.manual_adjust", new Dictionary<string, string>(StringComparer.Ordinal) { ["source"] = "add_lifetime_xp" });
	}

	private void AddLifetimeXpInternal(NetUserId userId, int deltaXp, string statKey, IReadOnlyDictionary<string, string>? metadata = null)
	{
		if (deltaXp != 0)
		{
			RuntimeProgressState runtimeProgressState = EnsureState(userId);
			int lifetimeXp = runtimeProgressState.LifetimeXp;
			runtimeProgressState.LifetimeXp = Math.Max(0, runtimeProgressState.LifetimeXp + deltaXp);
			Recalculate(runtimeProgressState);
			runtimeProgressState.StateVersion++;
			QueuePersistState(userId);
			PushSnapshotIfOnline(userId);
			int num = runtimeProgressState.LifetimeXp - lifetimeXp;
			if (num != 0)
			{
				_stats.Record(userId, statKey, num, metadata);
			}
		}
	}

	public bool TrySetLevel(NetUserId userId, int level, out int resolvedLevel, out int resolvedLifetimeXp)
	{
		int num = Math.Max(1, level);
		if (_levelCap > 0)
		{
			num = Math.Min(num, _levelCap);
		}
		RuntimeProgressState runtimeProgressState = EnsureState(userId);
		runtimeProgressState.LifetimeXp = GetLifetimeXpForLevelStart(num);
		Recalculate(runtimeProgressState);
		runtimeProgressState.StateVersion++;
		QueuePersistState(userId);
		PushSnapshotIfOnline(userId);
		resolvedLevel = runtimeProgressState.Level;
		resolvedLifetimeXp = runtimeProgressState.LifetimeXp;
		return true;
	}

	public bool TryAddLevels(NetUserId userId, int deltaLevels, out int resolvedLevel, out int resolvedLifetimeXp)
	{
		int num = EnsureState(userId).Level + deltaLevels;
		if (num < 1)
		{
			num = 1;
		}
		if (_levelCap > 0)
		{
			num = Math.Min(num, _levelCap);
		}
		return TrySetLevel(userId, num, out resolvedLevel, out resolvedLifetimeXp);
	}

	public bool TrySetAchievementUnlocked(NetUserId userId, string achievementId, bool unlocked, out int resolvedProgress, out int target, out bool completed, out string error)
	{
		if (!TryGetAchievementPrototype(achievementId, out WH40KMetaAchievementPrototype prototype, out error))
		{
			resolvedProgress = 0;
			target = 0;
			completed = false;
			return false;
		}
		target = WH40KMetaProgressMath.NormalizeAchievementTarget(prototype.Target);
		RuntimeProgressState runtimeProgressState = EnsureState(userId);
		runtimeProgressState.AchievementProgress.TryGetValue(achievementId, out var value);
		value = WH40KMetaProgressMath.ClampAchievementProgress(value, target);
		bool previousCompleted = runtimeProgressState.CompletedAchievements.Contains(achievementId) || WH40KMetaProgressMath.IsAchievementCompleted(value, target);
		if (unlocked)
		{
			resolvedProgress = target;
			runtimeProgressState.CompletedAchievements.Add(achievementId);
			completed = true;
		}
		else
		{
			resolvedProgress = Math.Min(value, target - 1);
			resolvedProgress = WH40KMetaProgressMath.ClampAchievementProgress(resolvedProgress, target);
			runtimeProgressState.CompletedAchievements.Remove(achievementId);
			completed = false;
		}
		runtimeProgressState.AchievementProgress[achievementId] = resolvedProgress;
		RecordAchievementMutation(userId, achievementId, target, value, resolvedProgress, previousCompleted, completed, unlocked ? "unlock" : "lock");
		SyncAllCompleteAchievement(userId, runtimeProgressState);
		GrantPendingAchievementRewards(userId, runtimeProgressState, unlocked ? "unlock" : "lock");
		runtimeProgressState.StateVersion++;
		QueuePersistState(userId);
		PushSnapshotIfOnline(userId);
		error = string.Empty;
		return true;
	}

	public bool TrySetAchievementProgress(NetUserId userId, string achievementId, int progressValue, out int resolvedProgress, out int target, out bool completed, out string error)
	{
		if (!TryGetAchievementPrototype(achievementId, out WH40KMetaAchievementPrototype prototype, out error))
		{
			resolvedProgress = 0;
			target = 0;
			completed = false;
			return false;
		}
		RuntimeProgressState runtimeProgressState = EnsureState(userId);
		target = WH40KMetaProgressMath.NormalizeAchievementTarget(prototype.Target);
		runtimeProgressState.AchievementProgress.TryGetValue(achievementId, out var value);
		value = WH40KMetaProgressMath.ClampAchievementProgress(value, target);
		bool previousCompleted = runtimeProgressState.CompletedAchievements.Contains(achievementId) || WH40KMetaProgressMath.IsAchievementCompleted(value, target);
		resolvedProgress = WH40KMetaProgressMath.ClampAchievementProgress(progressValue, target);
		completed = WH40KMetaProgressMath.IsAchievementCompleted(resolvedProgress, target);
		runtimeProgressState.AchievementProgress[achievementId] = resolvedProgress;
		if (completed)
		{
			runtimeProgressState.CompletedAchievements.Add(achievementId);
		}
		else
		{
			runtimeProgressState.CompletedAchievements.Remove(achievementId);
		}
		RecordAchievementMutation(userId, achievementId, target, value, resolvedProgress, previousCompleted, completed, "set");
		SyncAllCompleteAchievement(userId, runtimeProgressState);
		GrantPendingAchievementRewards(userId, runtimeProgressState, "set");
		runtimeProgressState.StateVersion++;
		QueuePersistState(userId);
		PushSnapshotIfOnline(userId);
		error = string.Empty;
		return true;
	}

	public bool TryAddAchievementProgress(NetUserId userId, string achievementId, int deltaProgress, out int resolvedProgress, out int target, out bool completed, out string error)
	{
		if (!TryGetAchievementPrototype(achievementId, out WH40KMetaAchievementPrototype prototype, out error))
		{
			resolvedProgress = 0;
			target = 0;
			completed = false;
			return false;
		}
		target = WH40KMetaProgressMath.NormalizeAchievementTarget(prototype.Target);
		RuntimeProgressState runtimeProgressState = EnsureState(userId);
		runtimeProgressState.AchievementProgress.TryGetValue(achievementId, out var value);
		value = WH40KMetaProgressMath.ClampAchievementProgress(value, target);
		bool previousCompleted = runtimeProgressState.CompletedAchievements.Contains(achievementId) || WH40KMetaProgressMath.IsAchievementCompleted(value, target);
		resolvedProgress = WH40KMetaProgressMath.ClampAchievementProgress(value + deltaProgress, target);
		completed = WH40KMetaProgressMath.IsAchievementCompleted(resolvedProgress, target);
		runtimeProgressState.AchievementProgress[achievementId] = resolvedProgress;
		if (completed)
		{
			runtimeProgressState.CompletedAchievements.Add(achievementId);
		}
		else
		{
			runtimeProgressState.CompletedAchievements.Remove(achievementId);
		}
		RecordAchievementMutation(userId, achievementId, target, value, resolvedProgress, previousCompleted, completed, "add");
		SyncAllCompleteAchievement(userId, runtimeProgressState);
		GrantPendingAchievementRewards(userId, runtimeProgressState, "add");
		runtimeProgressState.StateVersion++;
		QueuePersistState(userId);
		PushSnapshotIfOnline(userId);
		error = string.Empty;
		return true;
	}

	public bool TrySetDecorationUnlocked(NetUserId userId, string unlockId, bool unlocked, out string error)
	{
		if (!TryGetDecorationPrototype(unlockId, out WH40KMetaDecorationPrototype _, out error))
		{
			return false;
		}
		RuntimeProgressState runtimeProgressState = EnsureState(userId);
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		runtimeProgressState.DecorationUnlockState[unlockId] = new RuntimeDecorationUnlockState(unlocked, unlocked ? new DateTimeOffset?(utcNow) : ((DateTimeOffset?)null), runtimeProgressState.Level, utcNow);
		runtimeProgressState.StateVersion++;
		QueuePersistState(userId);
		PushSnapshotIfOnline(userId);
		error = string.Empty;
		return true;
	}

	public bool TrySetDecorationSelection(NetUserId userId, WH40KMetaDecorationCategory category, string? decorationId, out string resolvedSelection, out string error)
	{
		string text = (string.IsNullOrWhiteSpace(decorationId) ? string.Empty : decorationId.Trim());
		RuntimeProgressState runtimeProgressState = EnsureState(userId);
		if (!string.IsNullOrEmpty(text))
		{
			if (!TryGetDecorationPrototype(text, out WH40KMetaDecorationPrototype prototype, out error))
			{
				resolvedSelection = string.Empty;
				return false;
			}
			if (prototype.Category != category)
			{
				error = $"Decoration '{text}' belongs to '{prototype.Category}', expected '{category}'.";
				resolvedSelection = string.Empty;
				return false;
			}

			if (!IsDecorationCurrentlyUnlocked(userId, runtimeProgressState, prototype))
			{
				error = $"Decoration '{text}' is currently locked.";
				resolvedSelection = string.Empty;
				return false;
			}
		}
		bool flag = false;
		switch (category)
		{
		case WH40KMetaDecorationCategory.GhostSkins:
			if (!string.Equals(runtimeProgressState.SelectedGhostSkinId, text, StringComparison.Ordinal))
			{
				runtimeProgressState.SelectedGhostSkinId = text;
				flag = true;
			}
			break;
		case WH40KMetaDecorationCategory.OocTitles:
			if (!string.Equals(runtimeProgressState.SelectedOocTitleId, text, StringComparison.Ordinal))
			{
				runtimeProgressState.SelectedOocTitleId = text;
				flag = true;
			}
			break;
		case WH40KMetaDecorationCategory.OocNameColors:
			if (!string.Equals(runtimeProgressState.SelectedOocNameColorId, text, StringComparison.Ordinal))
			{
				runtimeProgressState.SelectedOocNameColorId = text;
				flag = true;
			}
			break;
		default:
			error = $"Unknown decoration category '{category}'.";
			resolvedSelection = string.Empty;
			return false;
		}
		if (!flag)
		{
			resolvedSelection = text;
			error = string.Empty;
			return true;
		}
		runtimeProgressState.StateVersion++;
		QueuePersistState(userId);
		PushSnapshotIfOnline(userId);
		_stats.Record(userId, "meta.decoration.selection", 1L, new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["category"] = category.ToString(),
			["selection"] = (string.IsNullOrWhiteSpace(text) ? "none" : text)
		});
		resolvedSelection = text;
		error = string.Empty;
		return true;
	}

	public bool TrySetDevelopmentNodeUnlocked(NetUserId userId, string nodeId, bool unlocked, out string error)
	{
		if (!TryGetDevelopmentNode(nodeId, out WH40KMetaDevelopmentNodeDefinition node, out error))
		{
			return false;
		}
		RuntimeProgressState runtimeProgressState = EnsureState(userId);
		NormalizeDevelopmentUnlockState(runtimeProgressState);
		if (unlocked)
		{
			List<string> requestedNodeIds = new List<string> { node.Id };
			if (!TryConfirmDevelopmentPlan(userId, requestedNodeIds, out var _, out error))
			{
				return false;
			}
			return true;
		}
		if (!RemoveDevelopmentNodeRecursive(runtimeProgressState, node.Id))
		{
			error = string.Empty;
			return true;
		}
		runtimeProgressState.StateVersion++;
		QueuePersistState(userId);
		PushSnapshotIfOnline(userId);
		error = string.Empty;
		return true;
	}

	public bool TryConfirmDevelopmentPlan(NetUserId userId, IReadOnlyCollection<string> requestedNodeIds, out int unlockedCount, out string error)
	{
		unlockedCount = 0;
		RuntimeProgressState runtimeProgressState = EnsureState(userId);
		NormalizeDevelopmentUnlockState(runtimeProgressState);
		if (requestedNodeIds.Count == 0)
		{
			error = "Development plan is empty.";
			return false;
		}
		WH40KMetaDevelopmentNodeDefinition node;
		List<WH40KMetaDevelopmentNodeDefinition> source = (from id in (from id in requestedNodeIds
				where !string.IsNullOrWhiteSpace(id)
				select id.Trim()).Distinct<string>(StringComparer.Ordinal)
			select (!WH40KMetaDevelopmentCatalog.TryGetNode(id, out node)) ? null : node).ToList();
		if (source.Any((WH40KMetaDevelopmentNodeDefinition candidate) => candidate == null))
		{
			string text = requestedNodeIds.First((string id) => !WH40KMetaDevelopmentCatalog.TryGetNode(id, out node));
			error = $"Development node '{text}' was not found.";
			return false;
		}
		List<WH40KMetaDevelopmentNodeDefinition> list = (from candidate in source
			select (candidate) into candidate
			orderby candidate.SortOrder
			select candidate).ThenBy<WH40KMetaDevelopmentNodeDefinition, string>((WH40KMetaDevelopmentNodeDefinition candidate) => candidate.Id, StringComparer.Ordinal).ToList();
		HashSet<string> hashSet = new HashSet<string>(runtimeProgressState.DevelopmentUnlockState.Keys, StringComparer.Ordinal);
		int num = CalculateDevelopmentCost(hashSet);
		int totalDevelopmentSkillPoints = GetTotalDevelopmentSkillPoints(runtimeProgressState.Level);
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		List<WH40KMetaDevelopmentNodeDefinition> list2 = new List<WH40KMetaDevelopmentNodeDefinition>();
		foreach (WH40KMetaDevelopmentNodeDefinition item in list)
		{
			if (!hashSet.Contains(item.Id))
			{
				if (item.ParentId != null && !hashSet.Contains(item.ParentId))
				{
					error = $"Development node '{item.Id}' requires parent '{item.ParentId}'.";
					return false;
				}
				if (num + item.Cost > totalDevelopmentSkillPoints)
				{
					error = $"Development node '{item.Id}' exceeds available skill points.";
					return false;
				}
				hashSet.Add(item.Id);
				num += item.Cost;
				list2.Add(item);
			}
		}
		if (list2.Count == 0)
		{
			error = string.Empty;
			return true;
		}
		foreach (WH40KMetaDevelopmentNodeDefinition item2 in list2)
		{
			runtimeProgressState.DevelopmentUnlockState[item2.Id] = new RuntimeDevelopmentUnlockState(utcNow, item2.Cost, utcNow);
		}
		unlockedCount = list2.Count;
		runtimeProgressState.StateVersion++;
		QueuePersistState(userId);
		PushSnapshotIfOnline(userId);
		error = string.Empty;
		return true;
	}

	public void ResetForAdmin(NetUserId userId, AdminResetScope scope)
	{
		RuntimeProgressState runtimeProgressState = EnsureState(userId);
		bool flag = false;
		if ((scope == AdminResetScope.Progress || scope == AdminResetScope.All) ? true : false)
		{
			runtimeProgressState.LifetimeXp = 0;
			runtimeProgressState.SeasonXp = 0;
			Recalculate(runtimeProgressState);
			flag = true;
		}
		bool flag2 = ((scope == AdminResetScope.Development || scope == AdminResetScope.All) ? true : false);
		if (flag2 && runtimeProgressState.DevelopmentUnlockState.Count > 0)
		{
			runtimeProgressState.DevelopmentUnlockState.Clear();
			flag = true;
		}
		flag2 = ((scope == AdminResetScope.Achievements || scope == AdminResetScope.All) ? true : false);
		if (flag2 && (runtimeProgressState.AchievementProgress.Count > 0 || runtimeProgressState.CompletedAchievements.Count > 0))
		{
			runtimeProgressState.AchievementProgress.Clear();
			runtimeProgressState.CompletedAchievements.Clear();
			runtimeProgressState.LifetimeAchievementSourceCursor.Clear();
			SyncAllCompleteAchievement(userId, runtimeProgressState);
			flag = true;
		}
		flag2 = scope == AdminResetScope.Decorations || scope == AdminResetScope.All;
		if (flag2 && (runtimeProgressState.DecorationUnlockState.Count > 0 || !string.IsNullOrWhiteSpace(runtimeProgressState.SelectedGhostSkinId) || !string.IsNullOrWhiteSpace(runtimeProgressState.SelectedOocTitleId) || !string.IsNullOrWhiteSpace(runtimeProgressState.SelectedOocNameColorId)))
		{
			runtimeProgressState.DecorationUnlockState.Clear();
			runtimeProgressState.SelectedGhostSkinId = string.Empty;
			runtimeProgressState.SelectedOocTitleId = string.Empty;
			runtimeProgressState.SelectedOocNameColorId = string.Empty;
			flag = true;
		}
		if (flag)
		{
			runtimeProgressState.StateVersion++;
			QueuePersistState(userId);
			PushSnapshotIfOnline(userId);
		}
	}

	private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
	{
		TraceStats($"Round restart cleanup: states={_states.Count}, killXpSpent={_roundKillXpSpent.Count}, objectiveXpSpent={_roundObjectiveXpSpent.Count}, repeatableXpSpent={_roundRepeatableXpSpent.Count}, healRemainders={_roundHealRemainders.Count}.");
		HashSet<NetUserId> connectedUsers = new HashSet<NetUserId>(_players.Sessions.Select((ICommonSession session) => session.UserId));
		if (_states.Count > 0)
		{
			NetUserId[] array = _states.Keys.Where((NetUserId userId) => !connectedUsers.Contains(userId)).ToArray();
			foreach (NetUserId key in array)
			{
				_states.Remove(key);
			}
		}
		ICommonSession[] sessions = _players.Sessions;
		foreach (ICommonSession commonSession in sessions)
		{
			if (commonSession.Status != SessionStatus.Disconnected)
			{
				EnsureState(commonSession.UserId, commonSession);
			}
		}
		_roundKillXpSpent.Clear();
		_roundObjectiveXpSpent.Clear();
		_roundRepeatableXpSpent.Clear();
		_roundKillRewardGrantXp.Clear();
		_roundHealRemainders.Clear();
		_roundValidatedRevives.Clear();
		_roundValidatedStabilizations.Clear();
		_roundValidatedHealBuckets.Clear();
		_requestStateRateLimits.Clear();
		_setDecorationRateLimits.Clear();
		_confirmDevelopmentRateLimits.Clear();
		_processedMissionOutcomeRewardKeys.Clear();
		_networkSnapshotSubscribers.IntersectWith(connectedUsers);
		if (_queuedSnapshotPushes.Count > 0)
		{
			NetUserId[] array2 = _queuedSnapshotPushes.Keys.Where((NetUserId userId) => !connectedUsers.Contains(userId)).ToArray();
			foreach (NetUserId key2 in array2)
			{
				_queuedSnapshotPushes.Remove(key2);
			}
		}
		_lastProcessedRoundWinRewardRoundId = -1;
	}

	private void OnRoundStarting(RoundStartingEvent ev)
	{
		int num = 0;
		int num2 = 0;
		ICommonSession[] sessions = _players.Sessions;
		foreach (ICommonSession commonSession in sessions)
		{
			if (commonSession.Status != SessionStatus.InGame)
			{
				num2++;
				continue;
			}
			EnsureState(commonSession.UserId, commonSession);
			num++;
		}
		TraceStats($"RoundStarting seed round={ev.Id}: seeded={num}, skippedNotInGame={num2}.");
	}

	private void OnLevelCapChanged(int value)
	{
		_levelCap = Math.Max(0, value);
		RecalculateAll();
		PushSnapshotToAllInGame();
	}

	private void OnXpMultiplierChanged(float value)
	{
		_xpMultiplier = Math.Max(0f, value);
	}

	private void OnUnlocksEnforcedChanged(bool value)
	{
		_unlockRequirementsBypassed = value;
		PushSnapshotToAllInGame();
	}

	private void OnXpRoundWinChanged(int value)
	{
		_xpRoundWin = Math.Max(0, value);
	}

	private void OnXpKillChanged(int value)
	{
		_xpKill = Math.Max(0, value);
	}

	private void OnXpKillCapPerRoundChanged(int value)
	{
		_xpKillCapPerRound = Math.Max(0, value);
	}

	private void OnXpObjectiveMajorChanged(int value)
	{
		_xpObjectiveMajor = Math.Max(0, value);
	}

	private void OnXpObjectiveMinorChanged(int value)
	{
		_xpObjectiveMinor = Math.Max(0, value);
	}

	private void OnXpObjectiveTimeoutChanged(int value)
	{
		_xpObjectiveTimeout = Math.Max(0, value);
	}

	private void OnXpObjectiveFailureChanged(int value)
	{
		_xpObjectiveFailure = Math.Max(0, value);
	}

	private void OnXpStrategicPointBuildChanged(int value)
	{
		_xpStrategicPointBuild = Math.Max(0, value);
	}

	private void OnXpStrategicPointUpgradeChanged(int value)
	{
		_xpStrategicPointUpgrade = Math.Max(0, value);
	}

	private void OnXpStrategicPointDestroyChanged(int value)
	{
		_xpStrategicPointDestroy = Math.Max(0, value);
	}

	private void OnXpStrategicPointTripleHoldChanged(int value)
	{
		_xpStrategicPointTripleHold = Math.Max(0, value);
	}

	private void OnXpObjectiveCapPerRoundChanged(int value)
	{
		_xpObjectiveCapPerRound = Math.Max(0, value);
	}

	private void OnXpRepeatableCapPerRoundChanged(int value)
	{
		_xpRepeatableCapPerRound = Math.Max(0, value);
	}

	private void OnStatsTraceChanged(bool value)
	{
		_statsTrace = value;
		_sawmill.Info("WH40K meta stats trace logging " + (value ? "enabled" : "disabled") + ".");
	}

	private void TraceStats(string message)
	{
		if (_statsTrace)
		{
			_sawmill.Info("[trace] " + message);
		}
	}

	private void OnMobStateChanged(MobStateChangedEvent ev)
	{
		if (_gameTicker.RunLevel != GameRunLevel.InRound)
		{
			return;
		}
		EntityUid? origin = ev.Origin;
		if (!origin.HasValue)
		{
			return;
		}
		EntityUid valueOrDefault = origin.GetValueOrDefault();
		if (!TryComp(valueOrDefault, out ActorComponent comp))
		{
			return;
		}
		NetUserId userId = comp.PlayerSession.UserId;
		if (_states.ContainsKey(userId) && _players.TryGetSessionByEntity(ev.Target, out ICommonSession session) && !(session.UserId == userId) && _teamBattleRule.TryGetTeamIdForUser(userId, out string teamId) && _teamBattleRule.TryGetTeamIdFromEntity(ev.Target, out string teamId2) && string.Equals(teamId, teamId2, StringComparison.Ordinal))
		{
			var pair = (userId, session.UserId);
			Dictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["sourceTeamId"] = teamId,
				["targetTeamId"] = teamId2,
				["targetUserId"] = session.UserId.ToString(),
				["oldState"] = ev.OldMobState.ToString(),
				["newState"] = ev.NewMobState.ToString()
			};
			if (ev.OldMobState == MobState.Dead && (ev.NewMobState == MobState.Critical || ev.NewMobState == MobState.Alive))
			{
				_stats.Record(userId, "support.revive", 1L, metadata);
				if (_roundValidatedRevives.Add(pair))
				{
					_stats.Record(userId, WH40KPlayerStatKeys.SupportRevivesValidated, 1L, metadata);
				}
				TraceStats($"Recorded revive stat: source={userId}, target={session.UserId}, state={ev.OldMobState}->{ev.NewMobState}.");
			}
			if (ev.OldMobState == MobState.Critical && ev.NewMobState == MobState.Alive)
			{
				_stats.Record(userId, "support.stabilize", 1L, metadata);
				if (_roundValidatedStabilizations.Add(pair))
				{
					_stats.Record(userId, WH40KPlayerStatKeys.SupportStabilizationsValidated, 1L, metadata);
				}
				TraceStats($"Recorded stabilize stat: source={userId}, target={session.UserId}, state={ev.OldMobState}->{ev.NewMobState}.");
			}
		}
	}

	private void OnTeamBattleHealingDone(WH40KTeamBattleHealingDoneEvent ev)
	{
		if (_gameTicker.RunLevel == GameRunLevel.InRound && _states.ContainsKey(ev.SourceUserId) && !(ev.SourceUserId == ev.TargetUserId) && ev.HealedAmount > 0 && !string.IsNullOrWhiteSpace(ev.TeamId))
		{
			_roundHealRemainders.TryGetValue(ev.SourceUserId, out var value);
			int num = value + ev.HealedAmount;
			int num2 = num / 100;
			_roundHealRemainders[ev.SourceUserId] = num % 100;
			if (num2 > 0)
			{
				_stats.Record(ev.SourceUserId, "support.heal.bucket100", num2, new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["teamId"] = ev.TeamId,
					["targetUserId"] = ev.TargetUserId.ToString(),
					["healed"] = ev.HealedAmount.ToString(),
					["bucketSize"] = "100"
				});
				var pair = (ev.SourceUserId, ev.TargetUserId);
				_roundValidatedHealBuckets.TryGetValue(pair, out var validatedBuckets);
				int validatedGrant = Math.Min(num2, Math.Max(0, ValidatedHealBucketsPerPairPerRoundCap - validatedBuckets));
				if (validatedGrant > 0)
				{
					_roundValidatedHealBuckets[pair] = validatedBuckets + validatedGrant;
					_stats.Record(ev.SourceUserId, WH40KPlayerStatKeys.SupportHealBucket100Validated, validatedGrant, new Dictionary<string, string>(StringComparer.Ordinal)
					{
						["teamId"] = ev.TeamId,
						["targetUserId"] = ev.TargetUserId.ToString(),
						["healed"] = ev.HealedAmount.ToString(),
						["bucketSize"] = "100"
					});
				}
				TraceStats($"Recorded heal bucket stat: source={ev.SourceUserId}, target={ev.TargetUserId}, healed={ev.HealedAmount}, buckets={num2}, remainder={_roundHealRemainders[ev.SourceUserId]}.");
			}
		}
	}

	private void OnInfluencePointCaptured(WH40KInfluencePointCapturedEvent ev)
	{
		if (_gameTicker.RunLevel != GameRunLevel.InRound || string.IsNullOrWhiteSpace(ev.TeamId))
		{
			return;
		}
		int num = 0;
		ICommonSession[] sessions = _players.Sessions;
		foreach (ICommonSession commonSession in sessions)
		{
			if (commonSession.Status != SessionStatus.InGame)
			{
				continue;
			}
			NetUserId userId = commonSession.UserId;
			if (!_states.ContainsKey(userId))
			{
				EnsureState(userId, commonSession);
				if (!_states.ContainsKey(userId))
				{
					continue;
				}
			}
			if (_teamBattleRule.TryGetTeamIdForUser(userId, out string teamId) && string.Equals(teamId, ev.TeamId, StringComparison.OrdinalIgnoreCase))
			{
				_stats.Record(userId, "objective.capture.success", 1L, new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["teamId"] = ev.TeamId,
					["pointUid"] = ev.PointUid.ToString()
				});
				_stats.Record(userId, WH40KPlayerStatKeys.ObjectiveCaptureSuccessValidated, 1L, new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["teamId"] = ev.TeamId,
					["pointUid"] = ev.PointUid.ToString()
				});
				num++;
			}
		}
		TraceStats($"Influence capture stats recorded: team={ev.TeamId}, point={ev.PointUid}, users={num}.");
	}

	private void OnInfluencePointRewardTick(WH40KInfluencePointRewardTickEvent ev)
	{
		if (_gameTicker.RunLevel != GameRunLevel.InRound || string.IsNullOrWhiteSpace(ev.TeamId))
		{
			return;
		}
		int num = 0;
		ICommonSession[] sessions = _players.Sessions;
		foreach (ICommonSession commonSession in sessions)
		{
			if (commonSession.Status != SessionStatus.InGame)
			{
				continue;
			}
			NetUserId userId = commonSession.UserId;
			if (!_states.ContainsKey(userId))
			{
				EnsureState(userId, commonSession);
				if (!_states.ContainsKey(userId))
				{
					continue;
				}
			}
			if (_teamBattleRule.TryGetTeamIdForUser(userId, out string teamId) && string.Equals(teamId, ev.TeamId, StringComparison.OrdinalIgnoreCase))
			{
				_stats.Record(userId, "objective.defense.success", 1L, new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["teamId"] = ev.TeamId,
					["pointUid"] = ev.PointUid.ToString(),
					["reward"] = ev.FrontPointReward.ToString()
				});
				_stats.Record(userId, WH40KPlayerStatKeys.ObjectiveDefenseSuccessValidated, 1L, new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["teamId"] = ev.TeamId,
					["pointUid"] = ev.PointUid.ToString(),
					["reward"] = ev.FrontPointReward.ToString()
				});
				num++;
			}
		}
		TraceStats($"Influence defense stats recorded: team={ev.TeamId}, point={ev.PointUid}, users={num}, reward={ev.FrontPointReward}.");
	}

	private void OnStrategicPointBuilt(WH40KStrategicPointBuiltEvent ev)
	{
		if (_gameTicker.RunLevel != GameRunLevel.InRound ||
			string.IsNullOrWhiteSpace(ev.TeamId) ||
			!TryEnsureTrackedInRoundUser(ev.UserUid, out var userId))
		{
			return;
		}

		if (!_teamBattleRule.TryGetTeamIdForUser(userId, out var teamId) ||
			!string.Equals(teamId, ev.TeamId, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["teamId"] = ev.TeamId,
			["pointUid"] = ev.PointUid.ToString(),
			["pointType"] = ev.PointType.ToString(),
			["tier"] = ((int) ev.Tier).ToString()
		};

		_stats.Record(userId, WH40KPlayerStatKeys.StrategicPointBuildValidated, 1L, metadata);
		GrantStrategicPointXp(userId, _xpStrategicPointBuild, WH40KPlayerStatKeys.MetaXpStrategicPointBuild, metadata, "strategic-point-build");
		TraceStats($"Strategic point build recorded: user={userId}, team={ev.TeamId}, point={ev.PointUid}, type={ev.PointType}, tier={ev.Tier}.");
	}

	private void OnStrategicPointUpgraded(WH40KStrategicPointUpgradedEvent ev)
	{
		if (_gameTicker.RunLevel != GameRunLevel.InRound ||
			string.IsNullOrWhiteSpace(ev.TeamId) ||
			!TryEnsureTrackedInRoundUser(ev.UserUid, out var userId))
		{
			return;
		}

		if (!_teamBattleRule.TryGetTeamIdForUser(userId, out var teamId) ||
			!string.Equals(teamId, ev.TeamId, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["teamId"] = ev.TeamId,
			["pointUid"] = ev.PointUid.ToString(),
			["pointType"] = ev.PointType.ToString(),
			["tier"] = ((int) ev.Tier).ToString()
		};

		_stats.Record(userId, WH40KPlayerStatKeys.StrategicPointUpgradeValidated, 1L, metadata);
		GrantStrategicPointXp(userId, _xpStrategicPointUpgrade, WH40KPlayerStatKeys.MetaXpStrategicPointUpgrade, metadata, "strategic-point-upgrade");
		TraceStats($"Strategic point upgrade recorded: user={userId}, team={ev.TeamId}, point={ev.PointUid}, type={ev.PointType}, tier={ev.Tier}.");
	}

	private void OnStrategicPointDestroyed(WH40KStrategicPointDestroyedEvent ev)
	{
		if (_gameTicker.RunLevel != GameRunLevel.InRound ||
			string.IsNullOrWhiteSpace(ev.AttackerTeamId) ||
			string.IsNullOrWhiteSpace(ev.OwnerTeamId) ||
			!TryEnsureTrackedInRoundUser(ev.AttackerUid, out var userId))
		{
			return;
		}

		if (!_teamBattleRule.TryGetTeamIdForUser(userId, out var teamId) ||
			!string.Equals(teamId, ev.AttackerTeamId, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["teamId"] = ev.AttackerTeamId,
			["ownerTeamId"] = ev.OwnerTeamId,
			["pointUid"] = ev.PointUid.ToString(),
			["pointType"] = ev.PointType.ToString(),
			["tier"] = ((int) ev.Tier).ToString()
		};

		_stats.Record(userId, WH40KPlayerStatKeys.StrategicPointDestroyValidated, 1L, metadata);
		GrantStrategicPointXp(userId, _xpStrategicPointDestroy, WH40KPlayerStatKeys.MetaXpStrategicPointDestroy, metadata, "strategic-point-destroy");
		TraceStats($"Strategic point destroy recorded: user={userId}, team={ev.AttackerTeamId}, owner={ev.OwnerTeamId}, point={ev.PointUid}, type={ev.PointType}, tier={ev.Tier}.");
	}

	private void OnStrategicPointTripleHoldCompleted(WH40KStrategicPointTripleHoldCompletedEvent ev)
	{
		if (_gameTicker.RunLevel != GameRunLevel.InRound || string.IsNullOrWhiteSpace(ev.TeamId))
		{
			return;
		}

		var rewardedPlayers = 0;
		ICommonSession[] sessions = _players.Sessions;
		foreach (ICommonSession commonSession in sessions)
		{
			if (commonSession.Status != SessionStatus.InGame)
				continue;

			var userId = commonSession.UserId;
			if (!_states.ContainsKey(userId))
			{
				EnsureState(userId, commonSession);
				if (!_states.ContainsKey(userId))
					continue;
			}

			if (!_teamBattleRule.TryGetTeamIdForUser(userId, out var teamId) ||
				!string.Equals(teamId, ev.TeamId, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["teamId"] = ev.TeamId,
				["ownedPointCount"] = ev.OwnedPointCount.ToString(),
				["heldSeconds"] = ((int) Math.Round(ev.HeldDuration.TotalSeconds)).ToString()
			};

			_stats.Record(userId, WH40KPlayerStatKeys.StrategicPointHoldTripleTenMinutesValidated, 1L, metadata);
			GrantStrategicPointXp(userId, _xpStrategicPointTripleHold, WH40KPlayerStatKeys.MetaXpStrategicPointHold, metadata, "strategic-point-triple-hold");
			rewardedPlayers++;
		}

		TraceStats($"Strategic point triple hold recorded: team={ev.TeamId}, players={rewardedPlayers}, heldSeconds={(int) Math.Round(ev.HeldDuration.TotalSeconds)}, ownedPoints={ev.OwnedPointCount}.");
	}

	private void OnKillReported(ref KillReportedEvent ev)
	{
		if (_gameTicker.RunLevel != GameRunLevel.InRound)
		{
			TraceStats($"KillReported ignored: runLevel={_gameTicker.RunLevel}.");
			return;
		}

		var victim = _combatVictims.ResolveForRawCombatStats(ev.Entity);
		if (!victim.CountsForRawStats)
		{
			TraceStats($"KillReported raw stats suppressed for victim={ev.Entity}, reason={victim.Reason}.");
			return;
		}

		var victimUserId = victim.UserId!.Value;
		if (_states.ContainsKey(victimUserId))
		{
			_stats.Record(victimUserId, "combat.death", 1L);
			TraceStats($"Recorded death stat for victim user={victimUserId}.");
		}
		if (ev.Suicide || !(ev.Primary is KillPlayerSource killPlayerSource))
		{
			TraceStats($"KillReported ignored for raw kill stats: suicide={ev.Suicide}, primary={ev.Primary?.GetType().Name ?? "null"}.");
			return;
		}
		if (!_states.ContainsKey(killPlayerSource.PlayerId))
		{
			TraceStats($"KillReported ignored for raw kill stats: killer user={killPlayerSource.PlayerId} has no tracked runtime state.");
			return;
		}
		if (!_teamBattleRule.TryGetTeamIdForUser(killPlayerSource.PlayerId, out string teamId))
		{
			TraceStats($"KillReported ignored for raw kill stats: killer team not resolved for user={killPlayerSource.PlayerId}.");
			return;
		}
		if (!_teamBattleRule.TryGetTeamIdFromEntity(ev.Entity, out string teamId2))
		{
			TraceStats($"KillReported ignored for raw kill stats: victim team not resolved for entity={ev.Entity}.");
			return;
		}
		if (string.Equals(teamId, teamId2, StringComparison.Ordinal))
		{
			TraceStats($"KillReported ignored for raw kill stats: friendly fire killer={killPlayerSource.PlayerId}, team={teamId}.");
			return;
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["killerTeamId"] = teamId,
			["victimTeamId"] = teamId2
		};
		_stats.Record(killPlayerSource.PlayerId, "combat.kill.enemy", 1L, dictionary);
		TraceStats($"Recorded raw enemy kill stat for user={killPlayerSource.PlayerId}, team={teamId}->{teamId2}.");
	}

	private void OnAttributedKilled(ref AttributedKilledEvent ev)
	{
		if (_gameTicker.RunLevel != GameRunLevel.InRound || ev.Suicide)
			return;

		var victim = _combatVictims.ResolveForRawCombatStats(ev.Entity);
		if (!victim.CountsForRawStats)
		{
			TraceStats($"AttributedKilled raw assist stats suppressed for victim={ev.Entity}, reason={victim.Reason}.");
			return;
		}

		if (ev.Primary is not KillPlayerSource primaryPlayer)
			return;

		if (!_teamBattleRule.TryGetTeamIdForUser(primaryPlayer.PlayerId, out var killerTeamId))
			return;

		if (!_teamBattleRule.TryGetTeamIdFromEntity(ev.Entity, out var victimTeamId))
			return;

		if (string.Equals(killerTeamId, victimTeamId, StringComparison.Ordinal))
			return;

		var recordedAssistIds = new HashSet<NetUserId>();

		var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["killerTeamId"] = killerTeamId,
			["victimTeamId"] = victimTeamId
		};

		foreach (var assist in ev.Assists)
		{
			if (assist is not KillPlayerSource assistPlayer)
				continue;

			if (assistPlayer.PlayerId == primaryPlayer.PlayerId || !recordedAssistIds.Add(assistPlayer.PlayerId))
				continue;

			if (!_states.ContainsKey(assistPlayer.PlayerId))
				continue;

			if (!_teamBattleRule.TryGetTeamIdForUser(assistPlayer.PlayerId, out var assistTeamId))
				continue;

			if (!string.Equals(assistTeamId, killerTeamId, StringComparison.Ordinal) ||
			    string.Equals(assistTeamId, victimTeamId, StringComparison.Ordinal))
			{
				continue;
			}

			_stats.Record(
				assistPlayer.PlayerId,
				"combat.assist.enemy",
				1L,
				new Dictionary<string, string>(metadata, StringComparer.Ordinal) { ["assistTeamId"] = assistTeamId });
			TraceStats($"Recorded extra enemy assist stat for user={assistPlayer.PlayerId}, killer={primaryPlayer.PlayerId}, team={assistTeamId}->{victimTeamId}.");
		}
	}

	private void OnValidatedKillReward(WH40KValidatedKillRewardEvent ev)
	{
		if (_gameTicker.RunLevel != GameRunLevel.InRound)
			return;

		if (!_states.ContainsKey(ev.KillerUserId))
		{
			TraceStats($"Validated kill reward ignored: killer user={ev.KillerUserId} has no tracked runtime state.");
			return;
		}

		if (_xpKill <= 0)
		{
			TraceStats($"Validated kill reward ignored: kill XP disabled for user={ev.KillerUserId}.");
			return;
		}

		int scaledXp = ScaleAwardXp(_xpKill);
		if (scaledXp <= 0)
		{
			TraceStats($"Validated kill reward ignored: scaled kill XP <= 0 for user={ev.KillerUserId}, base={_xpKill}, mult={_xpMultiplier}.");
			return;
		}

		int grantedXp = ClampRepeatableXpGrant(ev.KillerUserId, scaledXp, _xpKillCapPerRound, _roundKillXpSpent);
		if (grantedXp <= 0)
		{
			TraceStats($"Validated kill reward denied by XP budget for user={ev.KillerUserId}: requested={scaledXp}, killCap={_xpKillCapPerRound}, totalCap={_xpRepeatableCapPerRound}.");
			return;
		}

		var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["killerTeamId"] = ev.KillerTeamId,
			["victimTeamId"] = ev.VictimTeamId,
			["pairToken"] = ev.PairToken
		};
		if (ev.VictimUserId.HasValue)
			metadata["victimUserId"] = ev.VictimUserId.Value.ToString();

		AddLifetimeXpInternal(ev.KillerUserId, grantedXp, "meta.xp.kill", metadata);
		_roundKillRewardGrantXp[ev.PairToken] = grantedXp;
		TraceStats($"Granted provisional kill XP for user={ev.KillerUserId}: xp={grantedXp}, pair={ev.PairToken}.");
	}

	private void OnValidatedKillRewardRevoked(WH40KValidatedKillRewardRevokedEvent ev)
	{
		if (!_roundKillRewardGrantXp.Remove(ev.PairToken, out var grantedXp) || grantedXp <= 0)
		{
			TraceStats($"Validated kill reward revoke ignored: no recorded grant for pair={ev.PairToken}.");
			return;
		}

		RefundRepeatableXpGrant(ev.KillerUserId, grantedXp, _roundKillXpSpent);

		var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["killerTeamId"] = ev.KillerTeamId,
			["victimTeamId"] = ev.VictimTeamId,
			["pairToken"] = ev.PairToken
		};
		if (ev.VictimUserId.HasValue)
			metadata["victimUserId"] = ev.VictimUserId.Value.ToString();

		AddLifetimeXpInternal(ev.KillerUserId, -grantedXp, "meta.xp.kill.revoked", metadata);
		TraceStats($"Revoked provisional kill XP for user={ev.KillerUserId}: xp={grantedXp}, pair={ev.PairToken}.");
	}

	private void OnConfirmedElimination(WH40KConfirmedEliminationEvent ev)
	{
		if (!_states.ContainsKey(ev.Primary.PlayerId))
		{
			TraceStats($"Confirmed elimination ignored: killer user={ev.Primary.PlayerId} has no tracked runtime state.");
			return;
		}

		var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["killerTeamId"] = ev.KillerTeamId,
			["victimTeamId"] = ev.VictimTeamId
		};
		if (ev.VictimUserId.HasValue)
			metadata["victimUserId"] = ev.VictimUserId.Value.ToString();

		_stats.Record(ev.Primary.PlayerId, WH40KPlayerStatKeys.CombatEnemyEliminations, 1L, metadata);
		TraceStats($"Recorded confirmed enemy elimination for user={ev.Primary.PlayerId}, team={ev.KillerTeamId}->{ev.VictimTeamId}.");

		if (ev.Assists.Length == 0)
			return;

		var recordedAssistIds = new HashSet<NetUserId>();
		foreach (var assist in ev.Assists)
		{
			if (assist is not KillPlayerSource assistPlayer)
				continue;

			if (assistPlayer.PlayerId == ev.Primary.PlayerId || !recordedAssistIds.Add(assistPlayer.PlayerId))
				continue;

			if (!_states.ContainsKey(assistPlayer.PlayerId))
				continue;

			if (!_teamBattleRule.TryGetTeamIdForUser(assistPlayer.PlayerId, out var assistTeamId))
				continue;

			if (!string.Equals(assistTeamId, ev.KillerTeamId, StringComparison.Ordinal) ||
			    string.Equals(assistTeamId, ev.VictimTeamId, StringComparison.Ordinal))
			{
				continue;
			}

			_stats.Record(
				assistPlayer.PlayerId,
				WH40KPlayerStatKeys.CombatEnemyAssistsValidated,
				1L,
				new Dictionary<string, string>(metadata, StringComparer.Ordinal)
				{
					["assistTeamId"] = assistTeamId
				});
			TraceStats($"Recorded confirmed enemy assist for user={assistPlayer.PlayerId}, killer={ev.Primary.PlayerId}, team={assistTeamId}->{ev.VictimTeamId}.");
		}
	}

	private void OnMissionOutcomeApplied(WH40KMissionOutcomeAppliedEvent ev)
	{
		if (_gameTicker.RunLevel != GameRunLevel.InRound)
		{
			TraceStats($"MissionOutcome ignored: runLevel={_gameTicker.RunLevel}.");
			return;
		}
		if (string.IsNullOrWhiteSpace(ev.TeamId))
		{
			TraceStats($"MissionOutcome ignored: empty team id, mission={ev.MissionId}.");
			return;
		}
		string text = $"{ev.TeamId}|{ev.MissionId}|{ev.MissionStartedAtTicks}|{ev.Tier}";
		if (!_processedMissionOutcomeRewardKeys.Add(text))
		{
			TraceStats($"MissionOutcome ignored by dedupe: {text}.");
			return;
		}
		TraceStats($"MissionOutcome processing: team={ev.TeamId}, mission={ev.MissionId}, tier={ev.Tier}, key={text}.");
		int num = ResolveObjectiveOutcomeBaseXp(ev.Tier);
		int num2 = ((num > 0) ? ScaleAwardXp(num) : 0);
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		ICommonSession[] sessions = _players.Sessions;
		foreach (ICommonSession commonSession in sessions)
		{
			if (commonSession.Status != SessionStatus.InGame)
			{
				num4++;
				continue;
			}
			NetUserId userId = commonSession.UserId;
			if (!_states.ContainsKey(userId))
			{
				EnsureState(userId, commonSession);
				if (!_states.ContainsKey(userId))
				{
					num5++;
					continue;
				}
				TraceStats($"MissionOutcome hydrated runtime state for user={userId} from active session.");
			}
			if (!_teamBattleRule.TryGetTeamIdForUser(userId, out string teamId) || !string.Equals(teamId, ev.TeamId, StringComparison.OrdinalIgnoreCase))
			{
				num6++;
				continue;
			}
			Dictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["teamId"] = ev.TeamId,
				["missionId"] = ev.MissionId,
				["tier"] = ev.Tier.ToString()
			};
			_stats.Record(userId, "mission.outcome", 1L, metadata);
			TraceStats($"Recorded mission outcome stat for user={userId}, team={teamId}, mission={ev.MissionId}, tier={ev.Tier}.");
			if (ev.ObjectiveType == WH40KMissionObjectiveType.CargoDelivery)
			{
				WH40KMissionOutcomeTier tier = ev.Tier;
				if (tier <= WH40KMissionOutcomeTier.Minor)
				{
					_stats.Record(userId, "logistics.delivery.success", 1L, metadata);
					TraceStats($"Recorded logistics delivery success stat for user={userId}, mission={ev.MissionId}, tier={ev.Tier}.");
				}
				if (ev.AwardedDevelopmentPoints > 0)
				{
					_stats.Record(userId, "logistics.delivery.value", ev.AwardedDevelopmentPoints, metadata);
					TraceStats($"Recorded logistics delivery value stat for user={userId}, mission={ev.MissionId}, value={ev.AwardedDevelopmentPoints}.");
				}
			}
			int num7 = ((num2 > 0) ? ClampObjectiveXpByRoundCap(userId, num2) : 0);
			if (num7 > 0)
			{
				AddLifetimeXpInternal(userId, num7, "meta.xp.objective", metadata);
				TraceStats($"Granted objective XP for user={userId}: xp={num7} (base={num}, scaled={num2}).");
			}
			else
			{
				TraceStats($"Objective XP not granted for user={userId}: scaled={num2}, capPerRound={_xpObjectiveCapPerRound}.");
			}
			num3++;
		}
		if (num3 > 0 && num2 > 0)
		{
			_sawmill.Info($"Granted objective meta XP ({num2}) to {num3} players. Team={ev.TeamId}, Mission={ev.MissionId}, Tier={ev.Tier}.");
		}
		TraceStats($"MissionOutcome summary: team={ev.TeamId}, mission={ev.MissionId}, tier={ev.Tier}, rewarded={num3}, skippedNotInGame={num4}, skippedNoState={num5}, skippedTeamMismatch={num6}, baseXp={num}, scaledXp={num2}.");
	}

	private void OnRoundEndMessage(RoundEndMessageEvent ev)
	{
		if (ev.RoundId == _lastProcessedRoundWinRewardRoundId)
		{
			TraceStats($"RoundEnd ignored by dedupe: roundId={ev.RoundId}.");
			return;
		}
		_lastProcessedRoundWinRewardRoundId = ev.RoundId;
		string winnerTeamId;
		bool draw;
		bool timeLimitReached;
		bool flag = _teamBattleRule.TryGetRoundOutcome(out winnerTeamId, out draw, out timeLimitReached);
		if (!flag)
		{
			winnerTeamId = string.Empty;
			draw = true;
			TraceStats($"RoundEnd: round outcome unavailable for roundId={ev.RoundId}. " + "Will still record 'round.completed.faction', but skip 'round.win' rewards.");
		}
		else
		{
			TraceStats($"RoundEnd: outcome roundId={ev.RoundId}, draw={draw}, winnerTeamId='{winnerTeamId}'.");
		}
		int num = ((_xpRoundWin > 0) ? ScaleAwardXp(_xpRoundWin) : 0);
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		int num8 = 0;
		int num9 = 0;
		int num10 = 0;
		HashSet<NetUserId> hashSet = new HashSet<NetUserId>();
		RoundEndMessageEvent.RoundEndPlayerInfo[] allPlayersEndInfo = ev.AllPlayersEndInfo;
		for (int i = 0; i < allPlayersEndInfo.Length; i++)
		{
			RoundEndMessageEvent.RoundEndPlayerInfo roundEndPlayerInfo = allPlayersEndInfo[i];
			num10++;
			NetUserId? playerGuid = roundEndPlayerInfo.PlayerGuid;
			if (playerGuid.HasValue)
			{
				NetUserId valueOrDefault = playerGuid.GetValueOrDefault();
				if (!hashSet.Add(valueOrDefault))
				{
					num5++;
					TraceStats($"RoundEnd duplicate player entry skipped: user={valueOrDefault}, round={ev.RoundId}.");
					continue;
				}
				if (!_states.ContainsKey(valueOrDefault))
				{
					if (_players.TryGetSessionById(valueOrDefault, out ICommonSession session))
					{
						EnsureState(valueOrDefault, session);
					}
					if (!_states.ContainsKey(valueOrDefault))
					{
						num6++;
						continue;
					}
					TraceStats($"RoundEnd hydrated runtime state for user={valueOrDefault} from active session.");
				}
				if (!_teamBattleRule.TryGetTeamIdForUser(valueOrDefault, out string teamId))
				{
					num7++;
					continue;
				}
				Dictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["teamId"] = teamId,
					["roundId"] = ev.RoundId.ToString()
				};
				_stats.Record(valueOrDefault, "round.participation.active", 1L, metadata);
				_stats.Record(valueOrDefault, "meta.session.rounds_played", 1L, metadata);
				_stats.Record(valueOrDefault, "round.completed.faction", 1L, metadata);
				num3++;
				TraceStats($"Recorded round completion stat for user={valueOrDefault}, team={teamId}, round={ev.RoundId}.");
				if (!flag || draw || string.IsNullOrWhiteSpace(winnerTeamId))
				{
					num8++;
					continue;
				}
				if (!string.Equals(teamId, winnerTeamId, StringComparison.Ordinal))
				{
					num9++;
					continue;
				}
				Dictionary<string, string> metadata2 = new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["winnerTeamId"] = winnerTeamId,
					["roundId"] = ev.RoundId.ToString()
				};
				_stats.Record(valueOrDefault, "round.win", 1L, metadata2);
				TraceStats($"Recorded round win stat for user={valueOrDefault}, winnerTeam={winnerTeamId}, round={ev.RoundId}.");
				if (num > 0)
				{
					int grantedRoundWinXp = ClampRepeatableXpGrant(valueOrDefault, num, num, null);
					if (grantedRoundWinXp > 0)
					{
						AddLifetimeXpInternal(valueOrDefault, grantedRoundWinXp, "meta.xp.round_win", metadata2);
						TraceStats($"Granted round-win XP for user={valueOrDefault}: xp={grantedRoundWinXp}, round={ev.RoundId}.");
					}
					else
					{
						TraceStats($"Round-win XP denied by repeatable cap for user={valueOrDefault}, round={ev.RoundId}, requested={num}, totalCap={_xpRepeatableCapPerRound}.");
					}
				}
				else
				{
					TraceStats($"Round-win XP disabled/scaled to zero for user={valueOrDefault}, round={ev.RoundId}.");
				}
				num2++;
			}
			else
			{
				num4++;
			}
		}
		if (num2 > 0 && num > 0)
		{
			_sawmill.Info($"Granted round-win meta XP ({num}) to {num2} players. WinnerTeam={winnerTeamId}, RoundId={ev.RoundId}.");
		}
		TraceStats($"RoundEnd summary round={ev.RoundId}: entries={num10}, completed+={num3}, win+={num2}, skippedNoGuid={num4}, skippedDuplicateUser={num5}, skippedNoState={num6}, skippedNoTeam={num7}, skippedNoOutcome={num8}, skippedTeamMismatch={num9}, hasOutcome={flag}, draw={draw}, winner='{winnerTeamId}', winXp={num}.");
	}

	private void OnPlayerStatRecorded(WH40KPlayerStatRecordedEvent ev)
	{
		NetUserId userId = ev.Entry.UserId;
		if (!_states.TryGetValue(userId, out RuntimeProgressState value))
		{
			TraceStats($"Stat event ignored: no runtime state for user={userId}, key={ev.Entry.Key}.");
		}
		else if (string.IsNullOrWhiteSpace(ev.Entry.Key))
		{
			TraceStats($"Stat event ignored: empty key for user={userId}.");
		}
		else
		{
			bool changed = RefreshStatDrivenAchievements(userId, value, (IReadOnlyCollection<string>?)(object)new string[1] { ev.Entry.Key });
			changed |= GrantPendingAchievementRewards(userId, value, "stats:" + ev.Entry.Key);
			if (!changed)
			{
				TraceStats($"Stat event processed with no achievement changes: user={userId}, key={ev.Entry.Key}.");
				return;
			}

			TraceStats($"Stat event changed achievements: user={userId}, key={ev.Entry.Key}.");
			value.StateVersion++;
			QueuePersistState(userId);
			QueueSnapshotIfOnline(userId, BackgroundSnapshotPushDelay);
		}
	}

	private void OnRequestState(WH40KMetaProgressRequestStateEvent ev, EntitySessionEventArgs args)
	{
		NetUserId userId = args.SenderSession.UserId;
		if (!IsRateLimited(userId, _requestStateRateLimits, 1f, 8))
		{
			MarkNetworkSnapshotInterested(userId);
			EnsureState(userId, args.SenderSession);
			SendSnapshot(args.SenderSession);
		}
	}

	private void OnSetDecorationSelection(WH40KMetaProgressSetDecorationSelectionEvent ev, EntitySessionEventArgs args)
	{
		NetUserId userId = args.SenderSession.UserId;
		if (!IsRateLimited(userId, _setDecorationRateLimits, 1f, 4))
		{
			MarkNetworkSnapshotInterested(userId);
			var state = EnsureState(userId, args.SenderSession);
			if (!state.DbLoadCompleted)
			{
				SendSnapshot(args.SenderSession);
				return;
			}
			if (!TrySetDecorationSelection(userId, ev.Category, ev.DecorationId, out string _, out string error))
			{
				_sawmill.Warning($"Rejected decoration selection from {userId}. Category={ev.Category}, Id='{ev.DecorationId}'. Error={error}");
				SendSnapshot(args.SenderSession);
			}
		}
	}

	private void OnConfirmDevelopmentPlan(WH40KMetaProgressConfirmDevelopmentPlanEvent ev, EntitySessionEventArgs args)
	{
		NetUserId userId = args.SenderSession.UserId;
		if (!IsRateLimited(userId, _confirmDevelopmentRateLimits, 1f, 4))
		{
			MarkNetworkSnapshotInterested(userId);
			var state = EnsureState(userId, args.SenderSession);
			if (!state.DbLoadCompleted)
			{
				SendSnapshot(args.SenderSession);
				return;
			}
			if (!TryConfirmDevelopmentPlan(userId, ev.NodeIds, out int unlockedCount, out string error))
			{
				_sawmill.Warning($"Rejected development confirm from {userId}. Nodes=[{string.Join(", ", ev.NodeIds)}]. Error={error}");
				SendSnapshot(args.SenderSession);
			}
			else if (unlockedCount == 0)
			{
				SendSnapshot(args.SenderSession);
			}
		}
	}

	private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
	{
		TraceStats($"Player status changed: user={args.Session.UserId}, newStatus={args.NewStatus}.");
		if (args.NewStatus == SessionStatus.Disconnected)
		{
			if (_gameTicker.RunLevel == GameRunLevel.InRound && _states.ContainsKey(args.Session.UserId))
			{
				Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal) { ["roundId"] = _gameTicker.RoundId.ToString() };
				if (_teamBattleRule.TryGetTeamIdForUser(args.Session.UserId, out string teamId))
				{
					dictionary["teamId"] = teamId;
				}
				_stats.Record(args.Session.UserId, "round.left.early", 1L, dictionary);
				TraceStats($"Recorded early-leave stat for user={args.Session.UserId}, round={_gameTicker.RoundId}.");
			}
			_requestStateRateLimits.Remove(args.Session.UserId);
			_setDecorationRateLimits.Remove(args.Session.UserId);
			_confirmDevelopmentRateLimits.Remove(args.Session.UserId);
			_networkSnapshotSubscribers.Remove(args.Session.UserId);
			_queuedSnapshotPushes.Remove(args.Session.UserId);
		}
		else
		{
			EnsureState(args.Session.UserId, args.Session);
			if (args.NewStatus == SessionStatus.InGame)
			{
				SendSnapshot(args.Session);
			}
		}
	}

	private bool TryEnsureTrackedInRoundUser(EntityUid userUid, out NetUserId userId)
	{
		userId = default;
		if (!_players.TryGetSessionByEntity(userUid, out ICommonSession session) ||
			session.Status != SessionStatus.InGame)
		{
			return false;
		}

		userId = session.UserId;
		if (!_states.ContainsKey(userId))
		{
			EnsureState(userId, session);
		}

		return _states.ContainsKey(userId);
	}

	private void GrantStrategicPointXp(
		NetUserId userId,
		int baseXp,
		string statKey,
		IReadOnlyDictionary<string, string> metadata,
		string source)
	{
		if (baseXp <= 0)
		{
			TraceStats($"Strategic point XP disabled: user={userId}, source={source}.");
			return;
		}

		int scaledXp = ScaleAwardXp(baseXp);
		if (scaledXp <= 0)
		{
			TraceStats($"Strategic point XP scaled to zero: user={userId}, source={source}, baseXp={baseXp}, multiplier={_xpMultiplier}.");
			return;
		}

		int grantedXp = ClampObjectiveXpByRoundCap(userId, scaledXp);
		if (grantedXp <= 0)
		{
			TraceStats($"Strategic point XP denied by objective cap: user={userId}, source={source}, requested={scaledXp}, capPerRound={_xpObjectiveCapPerRound}, repeatableCap={_xpRepeatableCapPerRound}.");
			return;
		}

		AddLifetimeXpInternal(userId, grantedXp, statKey, metadata);
		TraceStats($"Granted strategic point XP: user={userId}, source={source}, granted={grantedXp}, requested={scaledXp}.");
	}

	private static bool IsRateLimited(NetUserId userId, Dictionary<NetUserId, RateLimitWindowState> limits, float periodSeconds, int maxCount)
	{
		if (maxCount <= 0 || periodSeconds <= 0f)
		{
			return false;
		}
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		TimeSpan timeSpan = TimeSpan.FromSeconds(periodSeconds);
		if (timeSpan <= TimeSpan.Zero)
		{
			return false;
		}
		if (!limits.TryGetValue(userId, out var value) || utcNow >= value.ExpiresAt)
		{
			limits[userId] = new RateLimitWindowState(utcNow + timeSpan, 1);
			return false;
		}
		if (value.Count >= maxCount)
		{
			return true;
		}
		value.Count++;
		limits[userId] = value;
		return false;
	}

	private RuntimeProgressState EnsureState(NetUserId userId, ICommonSession? session = null)
	{
		if (_states.TryGetValue(userId, out RuntimeProgressState value))
		{
			StartLoadFromDb(userId);
			return value;
		}
		RuntimeProgressState runtimeProgressState = new RuntimeProgressState();
		_states[userId] = runtimeProgressState;
		if (session != null)
		{
			InitializeStateFromPlaytime(runtimeProgressState, session);
		}
		Recalculate(runtimeProgressState);
		StartLoadFromDb(userId);
		return runtimeProgressState;
	}

	private void StartLoadFromDb(NetUserId userId)
	{
		if (_states.TryGetValue(userId, out RuntimeProgressState value) && !value.DbLoadStarted && !value.DbLoadCompleted)
		{
			value.DbLoadStarted = true;
			TrackPending(LoadStateFromDbAsync(userId, value.StateVersion));
		}
	}

	private async Task<RuntimeProgressState> EnsureStateLoadedAsync(NetUserId userId)
	{
		var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
		var state = EnsureState(userId);

		while (!state.DbLoadCompleted)
		{
			if (DateTimeOffset.UtcNow >= deadline)
				throw new TimeoutException($"Timed out loading WH40K meta progression for {userId}.");

			if (!state.DbLoadStarted)
				StartLoadFromDb(userId);

			await Task.Delay(10);
			state = EnsureState(userId);
		}

		return state;
	}

	private async Task AwaitPersistQueueAsync(NetUserId userId)
	{
		Task? persistTask;
		lock (_persistQueueLock)
		{
			_persistQueueTail.TryGetValue(userId, out persistTask);
		}

		if (persistTask == null)
			return;

		await persistTask;
	}

	private async Task LoadStateFromDbAsync(NetUserId userId, int expectedStateVersion)
	{
		try
		{
			WH40KMetaProgressDbData progress = await _dbDiag.MeasureAsync(
				"meta_progress.db.get_progress",
				() => _db.GetWH40KMetaProgress(userId));
			List<WH40KMetaAchievementDbData> achievements = await _dbDiag.MeasureAsync(
				"meta_progress.db.get_achievements",
				() => _db.GetWH40KMetaAchievements(userId));
			List<WH40KMetaDecorationDbData> decorations = await _dbDiag.MeasureAsync(
				"meta_progress.db.get_decorations",
				() => _db.GetWH40KMetaDecorations(userId));
			List<WH40KMetaDevelopmentUnlockDbData> developmentUnlocks = await _dbDiag.MeasureAsync(
				"meta_progress.db.get_development_unlocks",
				() => _db.GetWH40KMetaDevelopmentUnlocks(userId));
			_task.RunOnMainThread(delegate
			{
				if (_states.TryGetValue(userId, out RuntimeProgressState value2))
				{
					value2.DbLoadCompleted = true;
					if (value2.StateVersion == expectedStateVersion)
					{
						if (progress != null)
						{
							value2.LifetimeXp = Math.Max(0, progress.LifetimeXp);
							value2.SeasonXp = Math.Max(0, progress.SeasonXp);
							value2.SelectedGhostSkinId = progress.SelectedGhostSkinId ?? string.Empty;
							value2.SelectedOocTitleId = progress.SelectedOocTitleId ?? string.Empty;
							value2.SelectedOocNameColorId = progress.SelectedOocNameColorId ?? string.Empty;
						}
						value2.AchievementProgress.Clear();
						value2.CompletedAchievements.Clear();
						value2.ClaimedAchievementRewards.Clear();
						value2.LifetimeAchievementSourceCursor.Clear();
						value2.DecorationUnlockState.Clear();
						value2.DevelopmentUnlockState.Clear();
						bool flag = false;
						bool flag2 = false;
						foreach (WH40KMetaAchievementDbData item in achievements)
						{
							if (string.IsNullOrWhiteSpace(item.AchievementId))
							{
								flag = true;
							}
							else
							{
								int num = Math.Max(0, item.ProgressValue);
								bool flag3 = item.Unlocked;
								if (_proto.TryIndex(item.AchievementId, out WH40KMetaAchievementPrototype prototype) && prototype != null)
								{
									int num2 = WH40KMetaProgressMath.NormalizeAchievementTarget(prototype.Target);
									int num3 = WH40KMetaProgressMath.ClampAchievementProgress(num, num2);
									if (num3 != num)
									{
										flag = true;
									}
									num = (flag3 ? num2 : num3);
									flag3 = flag3 || WH40KMetaProgressMath.IsAchievementCompleted(num, num2);
								}
								value2.AchievementProgress[item.AchievementId] = num;
								if (flag3)
								{
									value2.CompletedAchievements.Add(item.AchievementId);
								}
								if (item.Claimed)
								{
									value2.ClaimedAchievementRewards.Add(item.AchievementId);
								}
							}
						}
						if (PruneRetiredAchievementState(value2))
						{
							flag = true;
						}
						if (RefreshStatDrivenAchievements(userId, value2, null, emitTelemetry: false))
						{
							flag = true;
						}
						foreach (WH40KMetaDecorationDbData item2 in decorations)
						{
							if (!string.IsNullOrWhiteSpace(item2.UnlockId))
							{
								value2.DecorationUnlockState[item2.UnlockId] = new RuntimeDecorationUnlockState(item2.Unlocked, item2.UnlockedAt, Math.Max(0, item2.SourceLevel), item2.UpdatedAt);
							}
						}
						foreach (WH40KMetaDevelopmentUnlockDbData item3 in developmentUnlocks)
						{
							WH40KMetaDevelopmentNodeDefinition node;
							if (string.IsNullOrWhiteSpace(item3.NodeId))
							{
								flag2 = true;
							}
							else if (!WH40KMetaDevelopmentCatalog.TryGetNode(item3.NodeId, out node))
							{
								flag2 = true;
							}
							else
							{
								value2.DevelopmentUnlockState[item3.NodeId] = new RuntimeDevelopmentUnlockState(item3.UnlockedAt, Math.Max(0, (item3.SpentCost == 0) ? node.Cost : item3.SpentCost), item3.UpdatedAt);
							}
						}
						Recalculate(value2);
						flag2 |= NormalizeDevelopmentUnlockState(value2);
						if (SyncAchievementRewardDecorations(value2))
						{
							flag = true;
						}
						if (GrantPendingAchievementRewards(userId, value2, "db_load", emitTelemetry: false))
						{
							flag = true;
						}
						PushSnapshotIfOnline(userId);
						if (progress == null || flag || flag2)
						{
							QueuePersistState(userId);
						}
					}
				}
			});
		}
		catch (Exception value)
		{
			_sawmill.Error($"Failed loading WH40K meta progression for {userId}: {value}");
			_task.RunOnMainThread(delegate
			{
				if (_states.TryGetValue(userId, out RuntimeProgressState value2))
				{
					value2.DbLoadStarted = false;
				}
			});
		}
	}

	private void QueuePersistState(NetUserId userId)
	{
		if (!_states.TryGetValue(userId, out RuntimeProgressState value))
		{
			return;
		}
		if (!value.DbLoadCompleted)
		{
			_sawmill.Warning($"Skipping persist for {userId}: DB load not yet completed.");
			return;
		}
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		WH40KMetaProgressDbData progressData = new WH40KMetaProgressDbData(value.LifetimeXp, value.SeasonXp, utcNow, NormalizeSelectedId(value.SelectedGhostSkinId), NormalizeSelectedId(value.SelectedOocTitleId), NormalizeSelectedId(value.SelectedOocNameColorId));
		HashSet<string> hashSet = new HashSet<string>(value.AchievementProgress.Keys, StringComparer.Ordinal);
		hashSet.UnionWith(value.CompletedAchievements);
		hashSet.UnionWith(value.ClaimedAchievementRewards);
		List<WH40KMetaAchievementDbData> list = new List<WH40KMetaAchievementDbData>(hashSet.Count);
		foreach (string item in hashSet)
		{
			if (!string.IsNullOrWhiteSpace(item))
			{
				value.AchievementProgress.TryGetValue(item, out var value2);
				int num = Math.Max(0, value2);
				bool flag = value.CompletedAchievements.Contains(item);
				if (_proto.TryIndex(item, out WH40KMetaAchievementPrototype prototype) && prototype != null)
				{
					int num2 = WH40KMetaProgressMath.NormalizeAchievementTarget(prototype.Target);
					num = (flag ? num2 : WH40KMetaProgressMath.ClampAchievementProgress(num, num2));
					flag = flag || WH40KMetaProgressMath.IsAchievementCompleted(num, num2);
				}
				list.Add(new WH40KMetaAchievementDbData(item, num, flag, flag ? new DateTimeOffset?(utcNow) : ((DateTimeOffset?)null), Claimed: value.ClaimedAchievementRewards.Contains(item), 1, utcNow));
			}
		}
		List<WH40KMetaDecorationDbData> list2 = new List<WH40KMetaDecorationDbData>(value.DecorationUnlockState.Count);
		string key;
		foreach (KeyValuePair<string, RuntimeDecorationUnlockState> item2 in value.DecorationUnlockState)
		{
			item2.Deconstruct(out key, out var value3);
			string text = key;
			RuntimeDecorationUnlockState runtimeDecorationUnlockState = value3;
			if (!string.IsNullOrWhiteSpace(text))
			{
				DateTimeOffset? unlockedAt = (runtimeDecorationUnlockState.Unlocked ? new DateTimeOffset?(runtimeDecorationUnlockState.UnlockedAt ?? utcNow) : ((DateTimeOffset?)null));
				list2.Add(new WH40KMetaDecorationDbData(text, runtimeDecorationUnlockState.Unlocked, unlockedAt, Math.Max(0, runtimeDecorationUnlockState.SourceLevel), (runtimeDecorationUnlockState.UpdatedAt == default(DateTimeOffset)) ? utcNow : runtimeDecorationUnlockState.UpdatedAt));
			}
		}
		List<WH40KMetaDevelopmentUnlockDbData> list3 = new List<WH40KMetaDevelopmentUnlockDbData>(value.DevelopmentUnlockState.Count);
		foreach (KeyValuePair<string, RuntimeDevelopmentUnlockState> item3 in value.DevelopmentUnlockState)
		{
			item3.Deconstruct(out key, out var value4);
			string text2 = key;
			RuntimeDevelopmentUnlockState runtimeDevelopmentUnlockState = value4;
			if (!string.IsNullOrWhiteSpace(text2))
			{
				list3.Add(new WH40KMetaDevelopmentUnlockDbData(text2, (runtimeDevelopmentUnlockState.UnlockedAt == default(DateTimeOffset)) ? utcNow : runtimeDevelopmentUnlockState.UnlockedAt, Math.Max(0, runtimeDevelopmentUnlockState.SpentCost), (runtimeDevelopmentUnlockState.UpdatedAt == default(DateTimeOffset)) ? utcNow : runtimeDevelopmentUnlockState.UpdatedAt));
			}
		}
		EnqueuePersistState(userId, progressData, list, list2, list3);
	}

	private void EnqueuePersistState(NetUserId userId, WH40KMetaProgressDbData progressData, IReadOnlyCollection<WH40KMetaAchievementDbData> achievementData, IReadOnlyCollection<WH40KMetaDecorationDbData> decorationData, IReadOnlyCollection<WH40KMetaDevelopmentUnlockDbData> developmentData)
	{
		Task persistTask;
		lock (_persistQueueLock)
		{
			if (!_persistQueueTail.TryGetValue(userId, out Task value))
			{
				value = Task.CompletedTask;
			}
			persistTask = PersistAfterTailAsync(value, userId, progressData, achievementData, decorationData, developmentData);
			_persistQueueTail[userId] = persistTask;
		}
		TrackPending(persistTask);
		persistTask.ContinueWith(delegate
		{
			lock (_persistQueueLock)
			{
				if (_persistQueueTail.TryGetValue(userId, out Task value2) && value2 == persistTask)
				{
					_persistQueueTail.Remove(userId);
				}
			}
		}, TaskScheduler.Default);
	}

	private async Task PersistAfterTailAsync(Task tailTask, NetUserId userId, WH40KMetaProgressDbData progressData, IReadOnlyCollection<WH40KMetaAchievementDbData> achievementData, IReadOnlyCollection<WH40KMetaDecorationDbData> decorationData, IReadOnlyCollection<WH40KMetaDevelopmentUnlockDbData> developmentData)
	{
		try
		{
			await tailTask;
		}
		catch (Exception value)
		{
			_sawmill.Warning($"Previous WH40K meta persist task failed for {userId}: {value}");
		}
		await PersistStateAsync(userId, progressData, achievementData, decorationData, developmentData);
	}

	private async Task PersistStateAsync(NetUserId userId, WH40KMetaProgressDbData progressData, IReadOnlyCollection<WH40KMetaAchievementDbData> achievementData, IReadOnlyCollection<WH40KMetaDecorationDbData> decorationData, IReadOnlyCollection<WH40KMetaDevelopmentUnlockDbData> developmentData)
	{
		_ = 3;
		try
		{
			await _dbDiag.MeasureAsync(
				"meta_progress.db.batch_set_all",
				() => _db.BatchSetWH40KMetaProgressAll(userId, progressData, achievementData, decorationData, developmentData));
		}
		catch (Exception value)
		{
			_sawmill.Error($"Failed persisting WH40K meta progression for {userId}: {value}");
		}
	}

	private static string? NormalizeSelectedId(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		return null;
	}

	private int ScaleAwardXp(int baseXp)
	{
		if (baseXp <= 0)
		{
			return 0;
		}
		int val = (int)Math.Round((float)baseXp * _xpMultiplier);
		return Math.Max(0, val);
	}

	private int ResolveObjectiveOutcomeBaseXp(WH40KMissionOutcomeTier tier)
	{
		return tier switch
		{
			WH40KMissionOutcomeTier.Major => _xpObjectiveMajor,
			WH40KMissionOutcomeTier.Minor => _xpObjectiveMinor,
			WH40KMissionOutcomeTier.Timeout => _xpObjectiveTimeout,
			WH40KMissionOutcomeTier.Failure => _xpObjectiveFailure,
			_ => 0,
		};
	}

	private int ClampObjectiveXpByRoundCap(NetUserId userId, int awardXp)
	{
		return ClampRepeatableXpGrant(userId, awardXp, _xpObjectiveCapPerRound, _roundObjectiveXpSpent);
	}

	private int ClampRepeatableXpGrant(NetUserId userId, int awardXp, int sourceCapPerRound, Dictionary<NetUserId, int>? sourceSpent)
	{
		if (awardXp <= 0)
			return 0;

		int sourceRemaining = int.MaxValue;
		if (sourceCapPerRound > 0)
		{
			var alreadySpentForSource = 0;
			sourceSpent?.TryGetValue(userId, out alreadySpentForSource);
			sourceRemaining = Math.Max(0, sourceCapPerRound - alreadySpentForSource);
		}

		_roundRepeatableXpSpent.TryGetValue(userId, out var alreadySpentTotal);
		int totalRemaining = _xpRepeatableCapPerRound > 0
			? Math.Max(0, _xpRepeatableCapPerRound - alreadySpentTotal)
			: int.MaxValue;

		int grantedXp = Math.Min(awardXp, Math.Min(sourceRemaining, totalRemaining));
		if (grantedXp <= 0)
			return 0;

		if (sourceSpent != null)
			sourceSpent[userId] = sourceSpent.GetValueOrDefault(userId) + grantedXp;

		_roundRepeatableXpSpent[userId] = alreadySpentTotal + grantedXp;
		return grantedXp;
	}

	private void RefundRepeatableXpGrant(NetUserId userId, int grantedXp, Dictionary<NetUserId, int>? sourceSpent)
	{
		if (grantedXp <= 0)
			return;

		if (sourceSpent != null && sourceSpent.TryGetValue(userId, out var sourceValue))
		{
			sourceValue -= grantedXp;
			if (sourceValue > 0)
				sourceSpent[userId] = sourceValue;
			else
				sourceSpent.Remove(userId);
		}

		if (_roundRepeatableXpSpent.TryGetValue(userId, out var totalValue))
		{
			totalValue -= grantedXp;
			if (totalValue > 0)
				_roundRepeatableXpSpent[userId] = totalValue;
			else
				_roundRepeatableXpSpent.Remove(userId);
		}
	}

	private void TrackPending(Task task)
	{
		lock (_pendingTasksLock)
		{
			_pendingTasks.Add(task);
		}
		task.ContinueWith(delegate
		{
			lock (_pendingTasksLock)
			{
				_pendingTasks.Remove(task);
			}
		}, TaskScheduler.Default);
	}

	private Task[] SnapshotPendingTasks()
	{
		lock (_pendingTasksLock)
		{
			return _pendingTasks.ToArray();
		}
	}

	private void InitializeStateFromPlaytime(RuntimeProgressState state, ICommonSession session)
	{
		if (_playTime.TryGetTrackerTimes(session, out Dictionary<string, TimeSpan> time) && time.TryGetValue(PlayTimeTrackingShared.TrackerOverall, out var value))
		{
			int val = (int)Math.Round((float)WH40KMetaProgressMath.LifetimeXpFromOverallPlaytime(value) * _xpMultiplier);
			state.LifetimeXp = Math.Max(0, val);
		}
	}

	private void Recalculate(RuntimeProgressState state)
	{
		(int Level, int CurrentXp, int RequiredXp, int LifetimeXp) tuple = WH40KMetaProgressMath.CalculateFromLifetimeXp(state.LifetimeXp, _levelCap);
		int item = tuple.Level;
		int item2 = tuple.CurrentXp;
		int item3 = tuple.RequiredXp;
		int item4 = tuple.LifetimeXp;
		state.Level = item;
		state.CurrentXp = item2;
		state.RequiredXp = item3;
		state.LifetimeXp = item4;
	}

	private void RecalculateAll()
	{
		foreach (RuntimeProgressState value in _states.Values)
		{
			Recalculate(value);
		}
	}

	private bool RefreshStatDrivenAchievements(NetUserId userId, RuntimeProgressState state, IReadOnlyCollection<string>? changedStatKeys = null, bool emitTelemetry = true)
	{
		bool result = false;
		HashSet<string> hashSet = null;
		if (changedStatKeys != null && changedStatKeys.Count > 0)
		{
			hashSet = new HashSet<string>(changedStatKeys, StringComparer.Ordinal);
		}
		foreach (WH40KMetaAchievementPrototype item in _proto.EnumeratePrototypes<WH40KMetaAchievementPrototype>())
		{
			if (string.IsNullOrWhiteSpace(item.ProgressStatKey))
			{
				continue;
			}
			string text = item.ProgressStatKey.Trim();
			List<string> list = (from key in item.RoundBlockerStatKeys
				where !string.IsNullOrWhiteSpace(key)
				select key.Trim()).ToList();
			if (hashSet != null)
			{
				bool flag = hashSet.Contains(text);
				if (!flag && item.ProgressScope == WH40KMetaAchievementProgressScope.Round)
				{
					flag = list.Any(hashSet.Contains);
				}
				if (!flag)
				{
					continue;
				}
			}
			int num = WH40KMetaProgressMath.NormalizeAchievementTarget(item.Target);
			state.AchievementProgress.TryGetValue(item.ID, out var value);
			int num2 = WH40KMetaProgressMath.ClampAchievementProgress(value, num);
			bool flag2 = state.CompletedAchievements.Contains(item.ID) || WH40KMetaProgressMath.IsAchievementCompleted(num2, num);
			long num3 = ((item.ProgressScope != WH40KMetaAchievementProgressScope.Round) ? _stats.GetLifetimeCounter(userId, text) : _stats.GetRoundCounter(userId, text));
			long num4 = num3;
			if (item.ProgressScope == WH40KMetaAchievementProgressScope.Round && list.Count > 0)
			{
				foreach (string item2 in list)
				{
					if (_stats.GetRoundCounter(userId, item2) > 0)
					{
						num4 = 0L;
						break;
					}
				}
			}
			int num7;
			if (item.ProgressScope == WH40KMetaAchievementProgressScope.Lifetime)
			{
				long num5 = Math.Max(0L, num4);
				if (!state.LifetimeAchievementSourceCursor.TryGetValue(item.ID, out var value2) && hashSet == null)
				{
					value2 = num5;
				}
				long num6 = num5 - value2;
				if (num6 < 0)
				{
					num6 = 0L;
				}
				state.LifetimeAchievementSourceCursor[item.ID] = num5;
				num7 = WH40KMetaProgressMath.ClampAchievementProgress(num2 + (int)Math.Clamp(num6, 0L, 2147483647L), num);
			}
			else
			{
				num7 = WH40KMetaProgressMath.ClampAchievementProgress((int)Math.Clamp(num4, 0L, 2147483647L), num);
			}
			bool flag3 = WH40KMetaProgressMath.IsAchievementCompleted(num7, num);
			if (flag2 && !flag3)
			{
				flag3 = true;
				num7 = num;
			}
			if (num7 != num2 || flag3 != flag2)
			{
				state.AchievementProgress[item.ID] = num7;
				if (flag3)
				{
					state.CompletedAchievements.Add(item.ID);
				}
				else
				{
					state.CompletedAchievements.Remove(item.ID);
				}
				RecordAchievementMutation(userId, item.ID, num, num2, num7, flag2, flag3, "stats:" + text, emitTelemetry);
				result = true;
			}
		}
		if (SyncAllCompleteAchievement(userId, state, emitTelemetry))
		{
			result = true;
		}
		return result;
	}

	private bool SyncAllCompleteAchievement(NetUserId userId, RuntimeProgressState state, bool emitTelemetry = true)
	{
		if (!_proto.TryIndex(AllCompleteAchievementId, out WH40KMetaAchievementPrototype prototype) || prototype == null)
		{
			return false;
		}
		var (value, num) = GetAllCompleteProgress(state);
		state.AchievementProgress.TryGetValue(AllCompleteAchievementId, out var value2);
		int num2 = Math.Max(0, value2);
		bool flag = state.CompletedAchievements.Contains(AllCompleteAchievementId) || WH40KMetaProgressMath.IsAchievementCompleted(num2, num);
		int num3 = Math.Clamp(value, 0, num);
		bool flag2 = num3 >= num;
		if (num3 == num2 && flag2 == flag)
		{
			return false;
		}
		state.AchievementProgress[AllCompleteAchievementId] = num3;
		if (flag2)
		{
			state.CompletedAchievements.Add(AllCompleteAchievementId);
		}
		else
		{
			state.CompletedAchievements.Remove(AllCompleteAchievementId);
		}
		RecordAchievementMutation(userId, AllCompleteAchievementId, num, num2, num3, flag, flag2, "special:all_complete", emitTelemetry);
		return true;
	}

	private (int CompletedOther, int TotalOther) GetAllCompleteProgress(RuntimeProgressState state)
	{
		int num = 0;
		int num2 = 0;
		foreach (WH40KMetaAchievementPrototype item in _proto.EnumeratePrototypes<WH40KMetaAchievementPrototype>())
		{
			if (string.Equals(item.ID, AllCompleteAchievementId, StringComparison.Ordinal) || !item.CountForAllComplete)
				continue;

			num2++;
			state.AchievementProgress.TryGetValue(item.ID, out var value);
			int target = WH40KMetaProgressMath.NormalizeAchievementTarget(item.Target);
			int progress = WH40KMetaProgressMath.ClampAchievementProgress(value, target);
			if (state.CompletedAchievements.Contains(item.ID) || WH40KMetaProgressMath.IsAchievementCompleted(progress, target))
			{
				num++;
			}
		}
		return (CompletedOther: num, TotalOther: Math.Max(1, num2));
	}

	private void RecordAchievementMutation(NetUserId userId, string achievementId, int target, int previousProgress, int resolvedProgress, bool previousCompleted, bool completed, string source, bool emitTelemetry = true)
	{
		if (emitTelemetry)
		{
			int num = resolvedProgress - previousProgress;
			if (num != 0)
			{
				_stats.Record(userId, "meta.achievement.progress_delta", num, new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["achievementId"] = achievementId,
					["target"] = target.ToString(),
					["source"] = source
				});
			}
			if (!previousCompleted && completed)
			{
				_stats.Record(userId, "meta.achievement.completed", 1L, new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["achievementId"] = achievementId,
					["target"] = target.ToString(),
					["source"] = source
				});
			}
			else if (previousCompleted && !completed)
			{
				_stats.Record(userId, "meta.achievement.revoked", 1L, new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["achievementId"] = achievementId,
					["target"] = target.ToString(),
					["source"] = source
				});
			}
		}
	}

	private void OnPrototypesReloaded(PrototypesReloadedEventArgs _)
	{
		_sortedAchievementPrototypes = null;
		_sortedDecorationPrototypes = null;
	}

	private List<WH40KMetaAchievementPrototype> GetSortedAchievementPrototypes()
	{
		return _sortedAchievementPrototypes ??= _proto.EnumeratePrototypes<WH40KMetaAchievementPrototype>()
			.OrderBy(e => e.SortOrder)
			.ThenBy(e => e.ID, StringComparer.Ordinal)
			.ToList();
	}

	private List<WH40KMetaDecorationPrototype> GetSortedDecorationPrototypes()
	{
		return _sortedDecorationPrototypes ??= _proto.EnumeratePrototypes<WH40KMetaDecorationPrototype>()
			.OrderBy(e => e.SortOrder)
			.ThenBy(e => e.ID, StringComparer.Ordinal)
			.ToList();
	}

	private static bool PruneRetiredAchievementState(RuntimeProgressState state)
	{
		var changed = false;
		foreach (var achievementId in RetiredObjectiveAchievementIds)
		{
			changed |= state.AchievementProgress.Remove(achievementId);
			changed |= state.CompletedAchievements.Remove(achievementId);
			changed |= state.ClaimedAchievementRewards.Remove(achievementId);
			changed |= state.LifetimeAchievementSourceCursor.Remove(achievementId);
		}

		return changed;
	}

	private void ReconcileState(NetUserId userId, RuntimeProgressState state)
	{
		if (!state.DbLoadCompleted)
			return;

		bool retiredAchievementStateChanged = PruneRetiredAchievementState(state);
		bool rewardDecorationStateChanged = SyncAchievementRewardDecorations(state);
		bool rewardClaimStateChanged = GrantPendingAchievementRewards(userId, state, "snapshot_reconcile", emitTelemetry: false);

		if (retiredAchievementStateChanged || rewardDecorationStateChanged || rewardClaimStateChanged)
		{
			state.StateVersion++;
			QueuePersistState(userId);
		}
	}

	private WH40KMetaProgressSnapshot BuildSnapshot(NetUserId userId, RuntimeProgressState state)
	{
		int completedAchievements;
		int totalAchievements;
		bool achievementStateChanged;
		List<WH40KMetaAchievementSnapshotEntry> achievements = BuildAchievementSnapshotEntries(state, out completedAchievements, out totalAchievements, out achievementStateChanged);
		bool unlockStateChanged;
		List<WH40KMetaDecorationSnapshotEntry> decorations = BuildDecorationSnapshotEntries(userId, state, out unlockStateChanged);
		bool selectionChanged;
		WH40KMetaDecorationSelectionSnapshot decorationSelection = BuildDecorationSelectionSnapshot(state, decorations, out selectionChanged);
		bool developmentStateChanged;
		WH40KMetaDevelopmentSnapshot development = BuildDevelopmentSnapshot(state, out developmentStateChanged);
		if ((achievementStateChanged || unlockStateChanged || selectionChanged || developmentStateChanged) && state.DbLoadCompleted)
		{
			state.StateVersion++;
			QueuePersistState(userId);
		}
		return new WH40KMetaProgressSnapshot(state.Level, state.CurrentXp, state.RequiredXp, state.LifetimeXp, _levelCap, completedAchievements, totalAchievements, achievements, BuildNextRewardPreview(state.Level), decorations, decorationSelection, development);
	}

	private bool SyncAchievementRewardDecorations(RuntimeProgressState state)
	{
		var changed = false;
		var rewardedAt = DateTimeOffset.UtcNow;

		foreach (var prototype in _proto.EnumeratePrototypes<WH40KMetaAchievementPrototype>())
		{
			if (!state.CompletedAchievements.Contains(prototype.ID))
				continue;

			foreach (var decorationId in prototype.RewardDecorationIds
				.Where(id => !string.IsNullOrWhiteSpace(id))
				.Select(id => id.Trim())
				.Distinct(StringComparer.Ordinal))
			{
				if (!_proto.TryIndex(decorationId, out WH40KMetaDecorationPrototype _))
					continue;

				if (state.DecorationUnlockState.TryGetValue(decorationId, out var existing) && existing.Unlocked)
					continue;

				state.DecorationUnlockState[decorationId] = new RuntimeDecorationUnlockState(
					unlocked: true,
					rewardedAt,
					state.Level,
					rewardedAt);
				changed = true;
			}
		}

		return changed;
	}

	private bool GrantPendingAchievementRewards(NetUserId userId, RuntimeProgressState state, string source, bool emitTelemetry = true)
	{
		var changed = false;
		var totalRewardXp = 0;
		var rewardedAchievementIds = new List<string>();
		var rewardedAt = DateTimeOffset.UtcNow;

		foreach (var prototype in _proto.EnumeratePrototypes<WH40KMetaAchievementPrototype>())
		{
			if (!state.CompletedAchievements.Contains(prototype.ID) ||
				state.ClaimedAchievementRewards.Contains(prototype.ID))
			{
				continue;
			}

			var rewardXp = Math.Max(0, prototype.RewardXp);
			var rewardDecorationIds = prototype.RewardDecorationIds
				.Where(id => !string.IsNullOrWhiteSpace(id))
				.Select(id => id.Trim())
				.Distinct(StringComparer.Ordinal)
				.ToList();

			if (rewardXp <= 0 && rewardDecorationIds.Count == 0)
				continue;

			state.ClaimedAchievementRewards.Add(prototype.ID);
			rewardedAchievementIds.Add(prototype.ID);
			totalRewardXp += rewardXp;
			changed = true;

			foreach (var decorationId in rewardDecorationIds)
			{
				if (!_proto.TryIndex(decorationId, out WH40KMetaDecorationPrototype _))
					continue;

				state.DecorationUnlockState[decorationId] = new RuntimeDecorationUnlockState(
					unlocked: true,
					rewardedAt,
					state.Level,
					rewardedAt);
			}
		}

		if (!changed)
			return false;

		if (totalRewardXp > 0)
		{
			state.LifetimeXp = Math.Max(0, state.LifetimeXp + totalRewardXp);
			Recalculate(state);
		}

		if (!emitTelemetry)
			return true;

		foreach (var achievementId in rewardedAchievementIds)
		{
			if (!_proto.TryIndex(achievementId, out WH40KMetaAchievementPrototype prototype))
				continue;

			var rewardXp = Math.Max(0, prototype.RewardXp);
			var rewardDecorationCount = prototype.RewardDecorationIds
				.Where(id => !string.IsNullOrWhiteSpace(id))
				.Select(id => id.Trim())
				.Distinct(StringComparer.Ordinal)
				.Count();

			var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["achievementId"] = achievementId,
				["source"] = source,
				["rewardXp"] = rewardXp.ToString(),
				["rewardDecorations"] = rewardDecorationCount.ToString()
			};

			_stats.Record(userId, "meta.achievement.reward_claimed", 1L, metadata);

			if (rewardXp > 0)
				_stats.Record(userId, "meta.xp.achievement", rewardXp, metadata);
		}

		return true;
	}

	private List<WH40KMetaAchievementSnapshotEntry> BuildAchievementSnapshotEntries(RuntimeProgressState state, out int completedAchievements, out int totalAchievements, out bool achievementStateChanged)
	{
		completedAchievements = 0;
		totalAchievements = 0;
		achievementStateChanged = false;
		List<WH40KMetaAchievementSnapshotEntry> list = new List<WH40KMetaAchievementSnapshotEntry>();
		foreach (WH40KMetaAchievementPrototype item2 in GetSortedAchievementPrototypes())
		{
			bool flag = string.Equals(item2.ID, "wh40k-ach-all-complete", StringComparison.Ordinal);
			state.AchievementProgress.TryGetValue(item2.ID, out var value);
			value = Math.Max(0, value);
			int num = WH40KMetaProgressMath.NormalizeAchievementTarget(item2.Target);
			int num2 = WH40KMetaProgressMath.ClampAchievementProgress(value, num);
			if (flag)
			{
				(int CompletedOther, int TotalOther) allCompleteProgress = GetAllCompleteProgress(state);
				int item = allCompleteProgress.CompletedOther;
				num = allCompleteProgress.TotalOther;
				num2 = Math.Clamp(item, 0, num);
			}
			if (num2 != value)
			{
				state.AchievementProgress[item2.ID] = num2;
				achievementStateChanged = true;
			}
			bool flag2 = state.CompletedAchievements.Contains(item2.ID);
			bool flag3 = WH40KMetaProgressMath.IsAchievementCompleted(num2, num);
			if (!flag && flag2 && !flag3)
			{
				num2 = num;
				state.AchievementProgress[item2.ID] = num2;
				flag3 = true;
				achievementStateChanged = true;
			}
			bool flag4 = flag3 || (!flag && flag2);
			if (flag4 && !flag2)
			{
				state.CompletedAchievements.Add(item2.ID);
				achievementStateChanged = true;
			}
			else if (flag && !flag4 && flag2)
			{
				state.CompletedAchievements.Remove(item2.ID);
				achievementStateChanged = true;
			}
			if (item2.CountInTotalAchievements)
			{
				totalAchievements++;
				if (flag4)
				{
					completedAchievements++;
				}
			}
			string rewardKey = (string.IsNullOrWhiteSpace(item2.RewardKey) ? "wh40k-meta-progress-achievements-reward-none" : item2.RewardKey);
			list.Add(new WH40KMetaAchievementSnapshotEntry(item2.ID, item2.Category, item2.TitleKey, item2.DescriptionKey, item2.TaskKey, rewardKey, Math.Max(0, item2.RewardXp), new List<string>(item2.RewardDecorationIds), num2, num, item2.Hidden, flag4));
		}
		return list;
	}

	private List<WH40KMetaDecorationSnapshotEntry> BuildDecorationSnapshotEntries(NetUserId userId, RuntimeProgressState state, out bool unlockStateChanged)
	{
		unlockStateChanged = false;
		List<WH40KMetaDecorationSnapshotEntry> list = new List<WH40KMetaDecorationSnapshotEntry>();
		bool discordRequirementsActive = AreDecorationDiscordRequirementsActive();
		WH40KDiscordAuthSnapshot? discordSnapshot = _discordAuth.TryGetSnapshot(userId, out var cachedDiscordSnapshot)
			? cachedDiscordSnapshot
			: null;
		var sortedDecorations = GetSortedDecorationPrototypes();
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (WH40KMetaDecorationPrototype item in sortedDecorations)
		{
			int num = Math.Max(1, item.RequiredLevel);
			bool adminOnly = item.AdminOnly;
			List<string> list2 = item.RequiredAchievements.Where((string id) => !string.IsNullOrWhiteSpace(id)).Distinct<string>(StringComparer.Ordinal).ToList();
			bool flag = discordRequirementsActive && item.RequiredDiscordGuildMember;
			List<string> list3 = discordRequirementsActive
				? WH40KDiscordAuthRequirementEvaluator.NormalizeRoleIds(item.RequiredDiscordRoleIds)
				: new List<string>();
			int num2 = (_unlockRequirementsBypassed ? 1 : num);
			List<string> list4 = (_unlockRequirementsBypassed ? new List<string>() : list2);
			bool flag2 = state.Level >= num2;
			bool flag3 = list4.All(state.CompletedAchievements.Contains);
			bool flag4 = !adminOnly && flag2 && flag3;
			RuntimeDecorationUnlockState value;
			bool flag5 = state.DecorationUnlockState.TryGetValue(item.ID, out value);
			bool flag6 = flag5 && value.Unlocked;
			bool flag7 = WH40KDiscordAuthRequirementEvaluator.MeetsRequirements(discordSnapshot, flag, list3);
			bool flag8 = (flag4 || flag6) && flag7;
			seenIds.Add(item.ID);
			if (flag8 && flag4 && !flag5)
			{
				state.DecorationUnlockState[item.ID] = new RuntimeDecorationUnlockState(unlocked: true, utcNow, state.Level, utcNow);
				unlockStateChanged = true;
			}
			list.Add(new WH40KMetaDecorationSnapshotEntry(item.ID, item.Category, item.TitleKey, item.PreviewKey, item.OocColorHex, new List<string>(item.OocGradientColors), item.OocGradientAnimated, item.OocGradientDurationMs, item.OocAuraHex, item.OocAuraRadius, item.OocAuraAlphaPercent, item.OocTitleEffect, item.OocTitleEffectRevealMs, item.OocTitleEffectHoldMs, item.OocTitleEffectDissolveMs, item.OocTitleOutlineHex, item.OocTitleOutlineWidth, item.OocTitleOutlineAlphaPercent, item.GhostRsiPath, item.GhostState, item.GhostTintHex, item.SortOrder, item.SuppressTitlePrefix, flag8, new WH40KMetaDecorationRequirementSnapshot(num2, list4, flag, list3, adminOnly)));
		}
		List<string> list5 = state.DecorationUnlockState.Keys.Where((string id) => !seenIds.Contains(id)).ToList();
		if (list5.Count > 0)
		{
			foreach (string item2 in list5)
			{
				state.DecorationUnlockState.Remove(item2);
			}
			unlockStateChanged = true;
		}
		return list;
	}

	private WH40KMetaDecorationSelectionSnapshot BuildDecorationSelectionSnapshot(RuntimeProgressState state, IReadOnlyCollection<WH40KMetaDecorationSnapshotEntry> decorations, out bool selectionChanged)
	{
		selectionChanged = false;
		string text = ResolveDecorationSelection(decorations, WH40KMetaDecorationCategory.GhostSkins, state.SelectedGhostSkinId);
		if (!string.Equals(text, state.SelectedGhostSkinId, StringComparison.Ordinal))
		{
			state.SelectedGhostSkinId = text;
			selectionChanged = true;
		}
		string text2 = ResolveDecorationSelection(decorations, WH40KMetaDecorationCategory.OocTitles, state.SelectedOocTitleId);
		if (!string.Equals(text2, state.SelectedOocTitleId, StringComparison.Ordinal))
		{
			state.SelectedOocTitleId = text2;
			selectionChanged = true;
		}
		string text3 = ResolveDecorationSelection(decorations, WH40KMetaDecorationCategory.OocNameColors, state.SelectedOocNameColorId);
		if (!string.Equals(text3, state.SelectedOocNameColorId, StringComparison.Ordinal))
		{
			state.SelectedOocNameColorId = text3;
			selectionChanged = true;
		}
		return new WH40KMetaDecorationSelectionSnapshot(state.SelectedGhostSkinId, state.SelectedOocTitleId, state.SelectedOocNameColorId);
	}

	private string ResolveDecorationSelection(IReadOnlyCollection<WH40KMetaDecorationSnapshotEntry> decorations, WH40KMetaDecorationCategory category, string currentSelection)
	{
		List<WH40KMetaDecorationSnapshotEntry> list = (from entry in decorations
			where entry.Category == category
			orderby entry.SortOrder
			select entry).ThenBy<WH40KMetaDecorationSnapshotEntry, string>((WH40KMetaDecorationSnapshotEntry entry) => entry.Id, StringComparer.Ordinal).ToList();
		if (list.Count == 0)
		{
			return string.Empty;
		}
		if (!string.IsNullOrWhiteSpace(currentSelection))
		{
			WH40KMetaDecorationSnapshotEntry wH40KMetaDecorationSnapshotEntry = list.FirstOrDefault((WH40KMetaDecorationSnapshotEntry entry) => string.Equals(entry.Id, currentSelection, StringComparison.Ordinal));
			if (wH40KMetaDecorationSnapshotEntry != null && wH40KMetaDecorationSnapshotEntry.Unlocked)
			{
				return currentSelection;
			}
		}
		WH40KMetaDecorationPrototype prototype;
		WH40KMetaDecorationSnapshotEntry wH40KMetaDecorationSnapshotEntry2 = list.FirstOrDefault((WH40KMetaDecorationSnapshotEntry entry) => entry.Unlocked && _proto.TryIndex(entry.Id, out prototype) && prototype.DefaultSelected);
		if (wH40KMetaDecorationSnapshotEntry2 != null)
		{
			return wH40KMetaDecorationSnapshotEntry2.Id;
		}
		WH40KMetaDecorationSnapshotEntry wH40KMetaDecorationSnapshotEntry3 = list.FirstOrDefault((WH40KMetaDecorationSnapshotEntry entry) => entry.Unlocked);
		if (wH40KMetaDecorationSnapshotEntry3 != null)
		{
			return wH40KMetaDecorationSnapshotEntry3.Id;
		}
		return list[0].Id;
	}

	private bool IsDecorationCurrentlyUnlocked(NetUserId userId, RuntimeProgressState state, WH40KMetaDecorationPrototype prototype)
	{
		int requiredLevel = _unlockRequirementsBypassed ? 1 : Math.Max(1, prototype.RequiredLevel);
		List<string> requiredAchievements = _unlockRequirementsBypassed
			? new List<string>()
			: prototype.RequiredAchievements.Where((string id) => !string.IsNullOrWhiteSpace(id)).Distinct<string>(StringComparer.Ordinal).ToList();
		bool discordRequirementsActive = AreDecorationDiscordRequirementsActive();
		bool requireDiscordGuildMember = discordRequirementsActive && prototype.RequiredDiscordGuildMember;
		List<string> requiredDiscordRoleIds = discordRequirementsActive
			? WH40KDiscordAuthRequirementEvaluator.NormalizeRoleIds(prototype.RequiredDiscordRoleIds)
			: new List<string>();
		bool baseUnlocked = (!prototype.AdminOnly && state.Level >= requiredLevel && requiredAchievements.All(state.CompletedAchievements.Contains))
			|| (state.DecorationUnlockState.TryGetValue(prototype.ID, out var unlockState) && unlockState.Unlocked);
		if (!baseUnlocked)
			return false;

		WH40KDiscordAuthSnapshot? discordSnapshot = _discordAuth.TryGetSnapshot(userId, out var cachedDiscordSnapshot)
			? cachedDiscordSnapshot
			: null;
		return WH40KDiscordAuthRequirementEvaluator.MeetsRequirements(discordSnapshot, requireDiscordGuildMember, requiredDiscordRoleIds);
	}

	private HashSet<string> GetActiveAchievementRewardDecorationIds(RuntimeProgressState state)
	{
		var rewardDecorationIds = new HashSet<string>(StringComparer.Ordinal);

		foreach (var achievement in _proto.EnumeratePrototypes<WH40KMetaAchievementPrototype>())
		{
			if (!state.CompletedAchievements.Contains(achievement.ID))
				continue;

			foreach (var decorationId in achievement.RewardDecorationIds)
			{
				if (string.IsNullOrWhiteSpace(decorationId))
					continue;

				rewardDecorationIds.Add(decorationId.Trim());
			}
		}

		return rewardDecorationIds;
	}

	private bool ShouldDecorationBeUnlockedStrict(NetUserId userId, RuntimeProgressState state, WH40KMetaDecorationPrototype prototype, IReadOnlySet<string> rewardDecorationIds)
	{
		var requiredLevel = _unlockRequirementsBypassed ? 1 : Math.Max(1, prototype.RequiredLevel);
		List<string> requiredAchievements = _unlockRequirementsBypassed
			? new List<string>()
			: prototype.RequiredAchievements
				.Where(id => !string.IsNullOrWhiteSpace(id))
				.Select(id => id.Trim())
				.Distinct(StringComparer.Ordinal)
				.ToList();

		var discordRequirementsActive = AreDecorationDiscordRequirementsActive();
		var requireDiscordGuildMember = discordRequirementsActive && prototype.RequiredDiscordGuildMember;
		List<string> requiredDiscordRoleIds = discordRequirementsActive
			? WH40KDiscordAuthRequirementEvaluator.NormalizeRoleIds(prototype.RequiredDiscordRoleIds)
			: new List<string>();

		var baseUnlocked = !prototype.AdminOnly
			&& state.Level >= requiredLevel
			&& requiredAchievements.All(state.CompletedAchievements.Contains);
		var rewardUnlocked = rewardDecorationIds.Contains(prototype.ID);

		if (!baseUnlocked && !rewardUnlocked)
			return false;

		WH40KDiscordAuthSnapshot? discordSnapshot = _discordAuth.TryGetSnapshot(userId, out var cachedDiscordSnapshot)
			? cachedDiscordSnapshot
			: null;
		return WH40KDiscordAuthRequirementEvaluator.MeetsRequirements(discordSnapshot, requireDiscordGuildMember, requiredDiscordRoleIds);
	}

	private bool AreDecorationDiscordRequirementsActive()
	{
		return !_unlockRequirementsBypassed && _config.GetCVar(CCVars.WH40KDiscordAuthEnabled);
	}

	private WH40KMetaNextRewardPreview? BuildNextRewardPreview(int currentLevel)
	{
		if (_levelCap > 0 && currentLevel >= _levelCap)
		{
			return null;
		}
		if (!_proto.TryIndex(DefaultLevelRewardTableId, out WH40KMetaLevelRewardTablePrototype prototype))
		{
			return null;
		}
		int nextLevel = currentLevel + 1;
		WH40KMetaLevelRewardEntry? wH40KMetaLevelRewardEntry = prototype.Entries.FirstOrDefault((WH40KMetaLevelRewardEntry entry) => entry.Level == nextLevel);
		int val = wH40KMetaLevelRewardEntry?.Decorations ?? prototype.DefaultDecorations;
		int val2 = wH40KMetaLevelRewardEntry?.SkillPoints ?? prototype.DefaultSkillPoints;
		return new WH40KMetaNextRewardPreview(nextLevel, Math.Max(0, val), Math.Max(0, val2));
	}

	private int GetTotalDevelopmentSkillPoints(int level)
	{
		if (!_proto.TryIndex(DefaultLevelRewardTableId, out WH40KMetaLevelRewardTablePrototype prototype))
		{
			return 0;
		}
		return WH40KMetaProgressMath.CalculateTotalSkillPointsForLevel(level, prototype);
	}

	private static int CalculateDevelopmentCost(IEnumerable<string> nodeIds)
	{
		int num = 0;
		foreach (string nodeId in nodeIds)
		{
			if (WH40KMetaDevelopmentCatalog.TryGetNode(nodeId, out WH40KMetaDevelopmentNodeDefinition node))
			{
				num += Math.Max(0, node.Cost);
			}
		}
		return num;
	}

	private bool NormalizeDevelopmentUnlockState(RuntimeProgressState state)
	{
		if (state.DevelopmentUnlockState.Count == 0)
		{
			return false;
		}
		int totalDevelopmentSkillPoints = GetTotalDevelopmentSkillPoints(state.Level);
		int num = 0;
		bool flag = false;
		Dictionary<string, RuntimeDevelopmentUnlockState> dictionary = new Dictionary<string, RuntimeDevelopmentUnlockState>(StringComparer.Ordinal);
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		foreach (WH40KMetaDevelopmentNodeDefinition item in WH40KMetaDevelopmentCatalog.NodesInValidationOrder)
		{
			if (!state.DevelopmentUnlockState.TryGetValue(item.Id, out RuntimeDevelopmentUnlockState value))
			{
				continue;
			}
			if (item.ParentId != null && !dictionary.ContainsKey(item.ParentId))
			{
				flag = true;
				continue;
			}
			if (num + item.Cost > totalDevelopmentSkillPoints)
			{
				flag = true;
				continue;
			}
			int num2 = Math.Max(0, item.Cost);
			DateTimeOffset dateTimeOffset = ((value.UpdatedAt == default(DateTimeOffset)) ? utcNow : value.UpdatedAt);
			DateTimeOffset dateTimeOffset2 = ((value.UnlockedAt == default(DateTimeOffset)) ? dateTimeOffset : value.UnlockedAt);
			if (value.SpentCost != num2 || value.UpdatedAt != dateTimeOffset || value.UnlockedAt != dateTimeOffset2)
			{
				flag = true;
			}
			dictionary[item.Id] = new RuntimeDevelopmentUnlockState(dateTimeOffset2, num2, dateTimeOffset);
			num += num2;
		}
		if (!flag && dictionary.Count == state.DevelopmentUnlockState.Count)
		{
			return false;
		}
		state.DevelopmentUnlockState.Clear();
		foreach (var (key, value2) in dictionary)
		{
			state.DevelopmentUnlockState[key] = value2;
		}
		return true;
	}

	private bool RemoveDevelopmentNodeRecursive(RuntimeProgressState state, string nodeId)
	{
		bool flag = false;
		foreach (WH40KMetaDevelopmentNodeDefinition item in WH40KMetaDevelopmentCatalog.NodesInValidationOrder)
		{
			if (string.Equals(item.ParentId, nodeId, StringComparison.Ordinal))
			{
				flag |= RemoveDevelopmentNodeRecursive(state, item.Id);
			}
		}
		return flag | state.DevelopmentUnlockState.Remove(nodeId);
	}

	private WH40KMetaDevelopmentSnapshot BuildDevelopmentSnapshot(RuntimeProgressState state, out bool developmentStateChanged)
	{
		developmentStateChanged = NormalizeDevelopmentUnlockState(state);
		int totalDevelopmentSkillPoints = GetTotalDevelopmentSkillPoints(state.Level);
		WH40KMetaDevelopmentNodeDefinition node;
		List<string> list = (from id in state.DevelopmentUnlockState.Keys
			where WH40KMetaDevelopmentCatalog.TryGetNode(id, out node)
			orderby WH40KMetaDevelopmentCatalog.Nodes[id].SortOrder
			select id).ThenBy<string, string>((string id) => id, StringComparer.Ordinal).ToList();
		int num = CalculateDevelopmentCost(list);
		int availableSkillPoints = Math.Max(0, totalDevelopmentSkillPoints - num);
		return new WH40KMetaDevelopmentSnapshot(totalDevelopmentSkillPoints, num, availableSkillPoints, list);
	}

	private bool TryGetDevelopmentNode(string nodeId, out WH40KMetaDevelopmentNodeDefinition node, out string error)
	{
		if (string.IsNullOrWhiteSpace(nodeId))
		{
			node = null;
			error = "Development node id cannot be empty.";
			return false;
		}
		if (!WH40KMetaDevelopmentCatalog.TryGetNode(nodeId, out WH40KMetaDevelopmentNodeDefinition node2))
		{
			node = null;
			error = "Development node '" + nodeId + "' was not found.";
			return false;
		}
		node = node2;
		error = string.Empty;
		return true;
	}

	private int GetLifetimeXpForLevelStart(int level)
	{
		int num = Math.Max(1, level);
		long num2 = 0L;
		for (int i = 1; i < num; i++)
		{
			num2 += WH40KMetaProgressMath.GetRequiredXpForLevel(i);
			if (num2 >= int.MaxValue)
			{
				return int.MaxValue;
			}
		}
		return (int)num2;
	}

	private bool TryGetAchievementPrototype(string achievementId, out WH40KMetaAchievementPrototype prototype, out string error)
	{
		if (string.IsNullOrWhiteSpace(achievementId))
		{
			prototype = null;
			error = "Achievement id cannot be empty.";
			return false;
		}
		if (!_proto.TryIndex(achievementId, out WH40KMetaAchievementPrototype prototype2) || prototype2 == null)
		{
			prototype = null;
			error = "Achievement '" + achievementId + "' was not found.";
			return false;
		}
		prototype = prototype2;
		error = string.Empty;
		return true;
	}

	private bool TryGetDecorationPrototype(string unlockId, out WH40KMetaDecorationPrototype prototype, out string error)
	{
		if (string.IsNullOrWhiteSpace(unlockId))
		{
			prototype = null;
			error = "Decoration id cannot be empty.";
			return false;
		}
		if (!_proto.TryIndex(unlockId, out WH40KMetaDecorationPrototype prototype2) || prototype2 == null)
		{
			prototype = null;
			error = "Decoration '" + unlockId + "' was not found.";
			return false;
		}
		prototype = prototype2;
		error = string.Empty;
		return true;
	}

	private void MarkNetworkSnapshotInterested(NetUserId userId)
	{
		_networkSnapshotSubscribers.Add(userId);
	}

	private void QueueSnapshotIfOnline(NetUserId userId, TimeSpan delay)
	{
		if (!_players.TryGetSessionById(userId, out ICommonSession session) || session.Status == SessionStatus.Disconnected)
		{
			return;
		}
		TimeSpan value = _timing.CurTime + delay;
		if (_queuedSnapshotPushes.TryGetValue(userId, out var value2) && value2 <= value)
		{
			return;
		}
		_queuedSnapshotPushes[userId] = value;
	}

	private bool ShouldSendSnapshotToClient(ICommonSession session)
	{
		return session.Status != SessionStatus.Disconnected && _networkSnapshotSubscribers.Contains(session.UserId);
	}

	private void SendSnapshot(ICommonSession session)
	{
		_queuedSnapshotPushes.Remove(session.UserId);
		WH40KMetaProgressSnapshot snapshot = GetSnapshot(session.UserId);
		if (ShouldSendSnapshotToClient(session))
		{
			RaiseNetworkEvent(new WH40KMetaProgressStateEvent(snapshot), session);
		}
		this.SnapshotPushed?.Invoke(session.UserId, snapshot);
	}

	private void PushSnapshotIfOnline(NetUserId userId)
	{
		if (_players.TryGetSessionById(userId, out ICommonSession session) && session.Status != SessionStatus.Disconnected)
		{
			SendSnapshot(session);
		}
	}

	private void PushSnapshotToAllInGame()
	{
		ICommonSession[] sessions = _players.Sessions;
		foreach (ICommonSession commonSession in sessions)
		{
			if (commonSession.Status == SessionStatus.InGame)
			{
				SendSnapshot(commonSession);
			}
		}
	}
}
