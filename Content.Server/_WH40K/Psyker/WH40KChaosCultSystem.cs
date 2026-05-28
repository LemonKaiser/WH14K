using System;
using System.Collections.Generic;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared._WH40K.Psyker;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Round-scoped shared progression state per chaos patron.
/// Actor-local progression components become a replicated projection of this state.
/// </summary>
public sealed partial class WH40KChaosCultSystem : EntitySystem
{
    [Dependency] private  SharedMindSystem _mind = default!;
    [Dependency] private  MobStateSystem _mobState = default!;

    private const float SharedPassiveXpBasePerTick = 1f;
    private const float SharedPassiveXpPerLevelBonus = 0.025f;
    private static readonly TimeSpan SharedPassiveXpInterval = TimeSpan.FromMinutes(1);

    private readonly Dictionary<WH40KChaosPatron, ChaosCultState> _cultStates = new();
    private int _leadershipSequence;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KChaosRoleStartupEvent>(OnChaosRoleStartup);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, ComponentShutdown>(OnChaosRoleShutdown);
        SubscribeLocalEvent<WH40KChaosGiftProgressionComponent, ComponentShutdown>(OnChaosProgressionShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var (patron, state) in _cultStates)
        {
            var previousLeader = state.ActiveLeader;
            var previousAwaitingSuccessor = state.AwaitingLeaderSuccessor;

            RefreshLeaderState(patron, state);

            if (previousLeader == state.ActiveLeader && previousAwaitingSuccessor == state.AwaitingLeaderSuccessor)
                continue;

            SyncCultMembers(patron);
        }
    }

    public bool IsEffectiveLeader(EntityUid uid)
    {
        if (!TryComp<WH40KChaosGiftProgressionComponent>(uid, out var progression) || progression.AttunedPatron == WH40KChaosPatron.None)
            return false;

        return IsEffectiveLeader(uid, progression);
    }

    public bool IsEffectiveLeader(EntityUid uid, WH40KChaosGiftProgressionComponent progression)
    {
        if (!HasComp<WH40KChaosLeaderRoleComponent>(uid) || progression.AttunedPatron == WH40KChaosPatron.None)
            return false;

        return ResolveActiveLeader(progression.AttunedPatron) == uid;
    }

    public EntityUid? ResolveActiveLeader(WH40KChaosPatron patron)
    {
        return ResolveLeaderState(patron).ActiveLeader;
    }

    public (EntityUid? ActiveLeader, bool AwaitingLeaderSuccessor) ResolveLeaderState(WH40KChaosPatron patron)
    {
        if (patron == WH40KChaosPatron.None)
            return (null, false);

        var state = GetOrCreateState(patron);
        RefreshLeaderState(patron, state);
        return (state.ActiveLeader, state.AwaitingLeaderSuccessor);
    }

    public void RegisterLeadershipCandidate(EntityUid uid, WH40KChaosGiftProgressionComponent progression)
    {
        if (!HasComp<WH40KChaosLeaderRoleComponent>(uid))
            return;

        progression.PatronLeadershipOrder = ++_leadershipSequence;
        Dirty(uid, progression);
    }

    public bool HasCultMembers(WH40KChaosPatron patron)
    {
        if (patron == WH40KChaosPatron.None)
            return false;

        var query = EntityQueryEnumerator<WH40KChaosGiftProgressionComponent, WH40KChaosGiftRoleComponent>();
        while (query.MoveNext(out _, out var progression, out _))
        {
            if (progression.AttunedPatron == patron)
                return true;
        }

        return false;
    }

    private void OnChaosRoleStartup(WH40KChaosRoleStartupEvent args)
    {
        var uid = args.User;

        if (!HasComp<WH40KChaosLeaderRoleComponent>(uid) ||
            !TryComp<WH40KChaosGiftProgressionComponent>(uid, out var progression) ||
            progression.AttunedPatron == WH40KChaosPatron.None)
        {
            return;
        }

        if (progression.PatronLeadershipOrder <= 0)
            RegisterLeadershipCandidate(uid, progression);

        SyncCultMembers(progression.AttunedPatron);
    }

    private void OnChaosRoleShutdown(EntityUid uid, WH40KChaosGiftRoleComponent component, ref ComponentShutdown args)
    {
        RemComp<WH40KChaosPatronStatusIconComponent>(uid);
    }

    private void OnChaosProgressionShutdown(EntityUid uid, WH40KChaosGiftProgressionComponent component, ref ComponentShutdown args)
    {
        RemComp<WH40KChaosPatronStatusIconComponent>(uid);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _cultStates.Clear();
        _leadershipSequence = 0;
    }

    public void AttachMemberToCult(EntityUid uid, WH40KChaosGiftProgressionComponent progression, WH40KChaosPatron previousPatron)
    {
        if (previousPatron != WH40KChaosPatron.None && previousPatron != progression.AttunedPatron)
            SyncCultMembers(previousPatron);

        if (progression.AttunedPatron == WH40KChaosPatron.None)
        {
            progression.EffectiveLeader = false;
            SyncPatronStatusIcon(uid, WH40KChaosPatron.None, false);
            Dirty(uid, progression);
            return;
        }

        var state = GetOrCreateState(progression.AttunedPatron, progression);
        RefreshLeaderState(progression.AttunedPatron, state);
        ApplySharedState(progression.AttunedPatron, state, uid, progression);
        SyncCultMembers(progression.AttunedPatron);
    }

    public void SyncCultMembers(WH40KChaosPatron patron)
    {
        if (patron == WH40KChaosPatron.None)
            return;

        var state = GetOrCreateState(patron);
        RefreshLeaderState(patron, state);

        var query = EntityQueryEnumerator<WH40KChaosGiftProgressionComponent, WH40KChaosGiftRoleComponent>();
        while (query.MoveNext(out var uid, out var progression, out _))
        {
            if (progression.AttunedPatron != patron)
                continue;

            ApplySharedState(patron, state, uid, progression);
        }
    }

    public void CaptureSharedProgression(EntityUid uid, WH40KChaosGiftProgressionComponent progression)
    {
        if (progression.AttunedPatron == WH40KChaosPatron.None)
            return;

        var state = GetOrCreateState(progression.AttunedPatron, progression);
        CopySharedFields(progression, state);
        state.ActiveLeader = ResolveActiveLeader(progression.AttunedPatron);
        SyncCultMembers(progression.AttunedPatron);
    }

    public void AddCultXp(WH40KChaosPatron patron, float amount)
    {
        if (patron == WH40KChaosPatron.None || amount <= 0f)
            return;

        var state = GetOrCreateState(patron);
        if (state.MaxLevel <= 0)
            return;

        state.TotalXp += amount;

        if (state.Level >= state.MaxLevel)
        {
            state.LevelXp = 0f;
            SyncCultMembers(patron);
            return;
        }

        state.LevelXp += amount;
        var levelUps = 0;

        while (state.Level < state.MaxLevel)
        {
            var needed = MathF.Max(1f, state.XpPerLevelStep * Math.Clamp(state.Level, 1, state.MaxLevel));
            if (state.LevelXp + 0.0001f < needed)
                break;

            state.LevelXp -= needed;
            state.Level++;
            levelUps++;
        }

        if (levelUps > 0 && state.PointsPerLevel > 0)
            state.DevelopmentPoints += levelUps * state.PointsPerLevel;

        if (state.Level >= state.MaxLevel)
            state.LevelXp = 0f;

        SyncCultMembers(patron);
    }

    public int GetCultSoulCount(WH40KChaosPatron patron)
    {
        if (patron == WH40KChaosPatron.None)
            return 0;

        return Math.Max(0, GetOrCreateState(patron).PatronSoulOfferCount);
    }

    public int AddCultSoulCount(WH40KChaosPatron patron, int amount)
    {
        if (patron == WH40KChaosPatron.None || amount <= 0)
            return GetCultSoulCount(patron);

        var state = GetOrCreateState(patron);
        state.PatronSoulOfferCount = Math.Max(0, state.PatronSoulOfferCount + amount);
        SyncCultMembers(patron);
        return state.PatronSoulOfferCount;
    }

    private bool IsValidLeader(EntityUid? uid, WH40KChaosPatron patron)
    {
        if (uid is not { } leader || TerminatingOrDeleted(leader))
            return false;

        return HasComp<WH40KChaosLeaderRoleComponent>(leader) &&
               TryComp<WH40KChaosGiftProgressionComponent>(leader, out var progression) &&
               progression.AttunedPatron == patron &&
               !IsLeaderUnavailable(leader);
    }

    private EntityUid? PickLeader(WH40KChaosPatron patron)
    {
        EntityUid? leader = null;
        var bestOrder = int.MaxValue;

        var query = EntityQueryEnumerator<WH40KChaosGiftProgressionComponent, WH40KChaosGiftRoleComponent>();
        while (query.MoveNext(out var uid, out var progression, out _))
        {
            if (progression.AttunedPatron != patron ||
                !HasComp<WH40KChaosLeaderRoleComponent>(uid) ||
                progression.PatronLeadershipOrder <= 0 ||
                TerminatingOrDeleted(uid) ||
                IsLeaderUnavailable(uid))
            {
                continue;
            }

            if (progression.PatronLeadershipOrder >= bestOrder)
                continue;

            bestOrder = progression.PatronLeadershipOrder;
            leader = uid;
        }

        return leader;
    }

    private void RefreshLeaderState(WH40KChaosPatron patron, ChaosCultState state)
    {
        if (patron == WH40KChaosPatron.None)
        {
            state.ActiveLeader = null;
            state.AwaitingLeaderSuccessor = false;
            return;
        }

        if (IsValidLeader(state.ActiveLeader, patron))
        {
            state.AwaitingLeaderSuccessor = false;
            return;
        }

        var awaitingLeaderSuccessor = state.AwaitingLeaderSuccessor;
        if (state.ActiveLeader is { } previousLeader && IsLeaderUnavailable(previousLeader))
            awaitingLeaderSuccessor = true;

        state.ActiveLeader = PickLeader(patron);
        state.AwaitingLeaderSuccessor = state.ActiveLeader is null && awaitingLeaderSuccessor;
    }

    private bool IsLeaderUnavailable(EntityUid uid)
    {
        if (TryComp<MobStateComponent>(uid, out var mobState) && _mobState.IsDead(uid, mobState))
            return true;

        if (_mind.TryGetMind(uid, out _, out var mind) && _mind.IsCharacterUnrevivableIc(mind))
            return true;

        return !HasComp<MobStateComponent>(uid);
    }

    private ChaosCultState GetOrCreateState(WH40KChaosPatron patron, WH40KChaosGiftProgressionComponent? seed = null)
    {
        if (_cultStates.TryGetValue(patron, out var state))
        {
            SanitizePassiveXpState(state);
            return state;
        }

        state = new ChaosCultState
        {
            Patron = patron,
        };

        if (seed != null)
            CopySharedFields(seed, state);

        SanitizePassiveXpState(state);

        _cultStates[patron] = state;
        return state;
    }

    private void ApplySharedState(
        WH40KChaosPatron patron,
        ChaosCultState state,
        EntityUid uid,
        WH40KChaosGiftProgressionComponent progression)
    {
        var changed = false;

        changed |= Apply(ref progression.Level, state.Level);
        changed |= Apply(ref progression.LevelXp, state.LevelXp);
        changed |= Apply(ref progression.TotalXp, state.TotalXp);
        changed |= Apply(ref progression.DevelopmentPoints, state.DevelopmentPoints);
        changed |= Apply(ref progression.PatronSoulOfferCount, state.PatronSoulOfferCount);
        changed |= Apply(ref progression.MaxLevel, state.MaxLevel);
        changed |= Apply(ref progression.PointsPerLevel, state.PointsPerLevel);
        changed |= Apply(ref progression.XpPerLevelStep, state.XpPerLevelStep);
        changed |= Apply(ref progression.PassiveXpBasePerTick, state.PassiveXpBasePerTick);
        changed |= Apply(ref progression.PassiveXpPerLevelBonus, state.PassiveXpPerLevelBonus);
        changed |= Apply(ref progression.PassiveXpInterval, state.PassiveXpInterval);
        changed |= Apply(ref progression.AttunedPatron, patron);
        changed |= Apply(ref progression.PrimaryGiftSlot, state.PrimaryGiftSlot);
        changed |= Apply(ref progression.GiftSlotOneUnlocked, state.GiftSlotOneUnlocked);
        changed |= Apply(ref progression.GiftSlotTwoUnlocked, state.GiftSlotTwoUnlocked);
        changed |= Apply(ref progression.GiftSlotThreeUnlocked, state.GiftSlotThreeUnlocked);
        changed |= Apply(ref progression.KhorneGiftOnePowerTier, state.KhorneGiftOnePowerTier);
        changed |= Apply(ref progression.KhorneGiftOneCooldownTier, state.KhorneGiftOneCooldownTier);
        changed |= Apply(ref progression.KhorneGiftOneUtilityTier, state.KhorneGiftOneUtilityTier);
        changed |= Apply(ref progression.KhorneGiftTwoPowerTier, state.KhorneGiftTwoPowerTier);
        changed |= Apply(ref progression.KhorneGiftTwoCooldownTier, state.KhorneGiftTwoCooldownTier);
        changed |= Apply(ref progression.KhorneGiftTwoUtilityTier, state.KhorneGiftTwoUtilityTier);
        changed |= Apply(ref progression.KhorneGiftThreePowerTier, state.KhorneGiftThreePowerTier);
        changed |= Apply(ref progression.KhorneGiftThreeCooldownTier, state.KhorneGiftThreeCooldownTier);
        changed |= Apply(ref progression.KhorneGiftThreeUtilityTier, state.KhorneGiftThreeUtilityTier);
        changed |= Apply(ref progression.KhornePassiveSpeedTier, state.KhornePassiveSpeedTier);
        changed |= Apply(ref progression.KhornePassiveHealthTier, state.KhornePassiveHealthTier);
        changed |= Apply(ref progression.KhornePassiveMeleeTier, state.KhornePassiveMeleeTier);
        changed |= Apply(ref progression.GiftUnlockCost, state.GiftUnlockCost);

        var effectiveLeader = state.ActiveLeader == uid;
        changed |= Apply(ref progression.EffectiveLeader, effectiveLeader);
        SyncPatronStatusIcon(uid, patron, effectiveLeader);

        if (changed)
            Dirty(uid, progression);
    }

    private static void CopySharedFields(WH40KChaosGiftProgressionComponent progression, ChaosCultState state)
    {
        state.Level = progression.Level;
        state.LevelXp = progression.LevelXp;
        state.TotalXp = progression.TotalXp;
        state.DevelopmentPoints = progression.DevelopmentPoints;
        state.PatronSoulOfferCount = progression.PatronSoulOfferCount;
        state.MaxLevel = progression.MaxLevel;
        state.PointsPerLevel = progression.PointsPerLevel;
        state.XpPerLevelStep = progression.XpPerLevelStep;
        state.PassiveXpBasePerTick = progression.PassiveXpBasePerTick;
        state.PassiveXpPerLevelBonus = progression.PassiveXpPerLevelBonus;
        state.PassiveXpInterval = progression.PassiveXpInterval;
        state.PrimaryGiftSlot = progression.PrimaryGiftSlot;
        state.GiftSlotOneUnlocked = progression.GiftSlotOneUnlocked;
        state.GiftSlotTwoUnlocked = progression.GiftSlotTwoUnlocked;
        state.GiftSlotThreeUnlocked = progression.GiftSlotThreeUnlocked;
        state.KhorneGiftOnePowerTier = progression.KhorneGiftOnePowerTier;
        state.KhorneGiftOneCooldownTier = progression.KhorneGiftOneCooldownTier;
        state.KhorneGiftOneUtilityTier = progression.KhorneGiftOneUtilityTier;
        state.KhorneGiftTwoPowerTier = progression.KhorneGiftTwoPowerTier;
        state.KhorneGiftTwoCooldownTier = progression.KhorneGiftTwoCooldownTier;
        state.KhorneGiftTwoUtilityTier = progression.KhorneGiftTwoUtilityTier;
        state.KhorneGiftThreePowerTier = progression.KhorneGiftThreePowerTier;
        state.KhorneGiftThreeCooldownTier = progression.KhorneGiftThreeCooldownTier;
        state.KhorneGiftThreeUtilityTier = progression.KhorneGiftThreeUtilityTier;
        state.KhornePassiveSpeedTier = progression.KhornePassiveSpeedTier;
        state.KhornePassiveHealthTier = progression.KhornePassiveHealthTier;
        state.KhornePassiveMeleeTier = progression.KhornePassiveMeleeTier;
        state.GiftUnlockCost = progression.GiftUnlockCost;
    }

    private static void SanitizePassiveXpState(ChaosCultState state)
    {
        state.PassiveXpBasePerTick = SharedPassiveXpBasePerTick;
        state.PassiveXpPerLevelBonus = SharedPassiveXpPerLevelBonus;
        state.PassiveXpInterval = SharedPassiveXpInterval;
    }

    private void SyncPatronStatusIcon(EntityUid uid, WH40KChaosPatron patron, bool isLeader)
    {
        if (!ShouldDisplayPatronStatusIcon(patron))
        {
            RemComp<WH40KChaosPatronStatusIconComponent>(uid);
            return;
        }

        var icon = EnsureComp<WH40KChaosPatronStatusIconComponent>(uid);
        var changed = false;
        changed |= Apply(ref icon.Patron, patron);
        changed |= Apply(ref icon.IsLeader, isLeader);

        if (changed)
            Dirty(uid, icon);
    }

    private static bool ShouldDisplayPatronStatusIcon(WH40KChaosPatron patron)
    {
        return patron is WH40KChaosPatron.Khorne or
            WH40KChaosPatron.Nurgle or
            WH40KChaosPatron.Slaanesh or
            WH40KChaosPatron.Tzeentch;
    }

    private static bool Apply<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        return true;
    }

    private sealed class ChaosCultState
    {
        public WH40KChaosPatron Patron;
        public EntityUid? ActiveLeader;
        public bool AwaitingLeaderSuccessor;
        public int Level = 1;
        public float LevelXp;
        public float TotalXp;
        public int DevelopmentPoints;
        public int PatronSoulOfferCount;
        public int MaxLevel = 10;
        public int PointsPerLevel = 3;
        public float XpPerLevelStep = 100f;
        public float PassiveXpBasePerTick = 1f;
        public float PassiveXpPerLevelBonus = 0.025f;
        public TimeSpan PassiveXpInterval = TimeSpan.FromMinutes(1);
        public int PrimaryGiftSlot;
        public bool GiftSlotOneUnlocked;
        public bool GiftSlotTwoUnlocked;
        public bool GiftSlotThreeUnlocked;
        public byte KhorneGiftOnePowerTier;
        public byte KhorneGiftOneCooldownTier;
        public byte KhorneGiftOneUtilityTier;
        public byte KhorneGiftTwoPowerTier;
        public byte KhorneGiftTwoCooldownTier;
        public byte KhorneGiftTwoUtilityTier;
        public byte KhorneGiftThreePowerTier;
        public byte KhorneGiftThreeCooldownTier;
        public byte KhorneGiftThreeUtilityTier;
        public byte KhornePassiveSpeedTier;
        public byte KhornePassiveHealthTier;
        public byte KhornePassiveMeleeTier;
        public int GiftUnlockCost = 3;
    }
}
