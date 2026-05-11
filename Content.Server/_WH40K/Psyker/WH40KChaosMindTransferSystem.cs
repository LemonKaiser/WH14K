using Content.Shared.Actions;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared._WH40K.Psyker;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Keeps chaos-gift runtime attached to the mind when a player changes bodies.
/// </summary>
public sealed class WH40KChaosMindTransferSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    private readonly Dictionary<EntityUid, ChaosMindTransferState> _pendingTransfers = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<MindContainerComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<MindContainerComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnMindRemoved(Entity<MindContainerComponent> ent, ref MindRemovedMessage args)
    {
        if (!TryCaptureTransferState(ent.Owner, out var state))
            return;

        _pendingTransfers[args.Mind.Owner] = state;
        ClearChaosBodyState(ent.Owner);
    }

    private void OnMindAdded(Entity<MindContainerComponent> ent, ref MindAddedMessage args)
    {
        if (!_pendingTransfers.TryGetValue(args.Mind.Owner, out var state))
            return;

        if (HasComp<GhostComponent>(ent.Owner))
            return;

        _pendingTransfers.Remove(args.Mind.Owner);
        ClearChaosBodyState(ent.Owner);
        ApplyTransferState(ent.Owner, state);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _pendingTransfers.Clear();
    }

    private bool TryCaptureTransferState(EntityUid uid, out ChaosMindTransferState state)
    {
        state = default!;

        if (!HasComp<WH40KChaosGiftRoleComponent>(uid))
            return false;

        state = new ChaosMindTransferState
        {
            HasLeaderRole = HasComp<WH40KChaosLeaderRoleComponent>(uid),
            Progression = TryComp<WH40KChaosGiftProgressionComponent>(uid, out var progression)
                ? CaptureProgression(progression)
                : null,
            WarpResource = TryComp<WH40KWarpResourceComponent>(uid, out var warp)
                ? CaptureWarpResource(warp)
                : null,
            WarpInstability = TryComp<WH40KWarpInstabilityComponent>(uid, out var instability)
                ? CaptureWarpInstability(instability)
                : null,
        };

        return true;
    }

    private void ApplyTransferState(EntityUid uid, ChaosMindTransferState state)
    {
        if (state.Progression != null)
        {
            var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(uid);
            ApplyProgression(progression, state.Progression);
            Dirty(uid, progression);
        }

        if (state.WarpResource != null)
        {
            var warp = EnsureComp<WH40KWarpResourceComponent>(uid);
            ApplyWarpResource(warp, state.WarpResource);
            Dirty(uid, warp);
        }

        if (state.WarpInstability != null)
        {
            var instability = EnsureComp<WH40KWarpInstabilityComponent>(uid);
            ApplyWarpInstability(instability, state.WarpInstability);
            Dirty(uid, instability);
        }

        if (state.HasLeaderRole)
            EnsureComp<WH40KChaosLeaderRoleComponent>(uid);

        EnsureComp<WH40KChaosGiftRoleComponent>(uid);
    }

    private void ClearChaosBodyState(EntityUid uid)
    {
        if (TryComp<WH40KChaosGiftStarterActionLoadoutComponent>(uid, out var loadout))
        {
            for (var i = 0; i < loadout.GrantedActions.Count; i++)
            {
                _actions.RemoveAction(uid, loadout.GrantedActions[i]);
            }

            loadout.GrantedActions.Clear();
        }

        RemComp<WH40KChaosGiftStarterActionLoadoutComponent>(uid);
        RemComp<WH40KChaosSlaaneshRuntimeComponent>(uid);
        RemComp<WH40KChaosKhorneRuntimeComponent>(uid);
        RemComp<WH40KChaosKhorneChosenRuntimeComponent>(uid);
        RemComp<WH40KChaosNurgleRuntimeComponent>(uid);
        RemComp<WH40KWarpResourceComponent>(uid);
        RemComp<WH40KWarpInstabilityComponent>(uid);
        RemComp<WH40KChaosGiftProgressionComponent>(uid);
        RemComp<WH40KChaosLeaderRoleComponent>(uid);
        RemComp<WH40KChaosGiftRoleComponent>(uid);
    }

    private static ChaosProgressionState CaptureProgression(WH40KChaosGiftProgressionComponent progression)
    {
        return new ChaosProgressionState
        {
            Level = progression.Level,
            LevelXp = progression.LevelXp,
            TotalXp = progression.TotalXp,
            DevelopmentPoints = progression.DevelopmentPoints,
            PatronSoulOfferCount = progression.PatronSoulOfferCount,
            BoundSkrizhal = progression.BoundSkrizhal,
            StarterSkrizhalIssued = progression.StarterSkrizhalIssued,
            MaxLevel = progression.MaxLevel,
            PointsPerLevel = progression.PointsPerLevel,
            XpPerLevelStep = progression.XpPerLevelStep,
            PassiveXpBasePerTick = progression.PassiveXpBasePerTick,
            PassiveXpPerLevelBonus = progression.PassiveXpPerLevelBonus,
            PassiveXpInterval = progression.PassiveXpInterval,
            NextPassiveXpAt = progression.NextPassiveXpAt,
            AttunedPatron = progression.AttunedPatron,
            PatronSelectionLocked = progression.PatronSelectionLocked,
            PrimaryGiftSlot = progression.PrimaryGiftSlot,
            GiftSlotOneUnlocked = progression.GiftSlotOneUnlocked,
            GiftSlotTwoUnlocked = progression.GiftSlotTwoUnlocked,
            GiftSlotThreeUnlocked = progression.GiftSlotThreeUnlocked,
            KhorneGiftOnePowerTier = progression.KhorneGiftOnePowerTier,
            KhorneGiftOneCooldownTier = progression.KhorneGiftOneCooldownTier,
            KhorneGiftOneUtilityTier = progression.KhorneGiftOneUtilityTier,
            KhorneGiftOneExUnlocked = progression.KhorneGiftOneExUnlocked,
            KhorneGiftTwoPowerTier = progression.KhorneGiftTwoPowerTier,
            KhorneGiftTwoCooldownTier = progression.KhorneGiftTwoCooldownTier,
            KhorneGiftTwoUtilityTier = progression.KhorneGiftTwoUtilityTier,
            KhorneGiftTwoExUnlocked = progression.KhorneGiftTwoExUnlocked,
            KhorneGiftThreePowerTier = progression.KhorneGiftThreePowerTier,
            KhorneGiftThreeCooldownTier = progression.KhorneGiftThreeCooldownTier,
            KhorneGiftThreeUtilityTier = progression.KhorneGiftThreeUtilityTier,
            KhorneGiftThreeExUnlocked = progression.KhorneGiftThreeExUnlocked,
            KhornePassiveSpeedTier = progression.KhornePassiveSpeedTier,
            KhornePassiveHealthTier = progression.KhornePassiveHealthTier,
            KhornePassiveMeleeTier = progression.KhornePassiveMeleeTier,
            KhornePassiveExUnlocked = progression.KhornePassiveExUnlocked,
            GiftUnlockCost = progression.GiftUnlockCost,
            AttunementXpMultiplier = progression.AttunementXpMultiplier,
            AllowPatronSwitch = progression.AllowPatronSwitch,
            PatronLeadershipOrder = progression.PatronLeadershipOrder,
            EffectiveLeader = progression.EffectiveLeader,
            RitualBonusMultiplier = progression.RitualBonusMultiplier,
            RitualBonusExpiresAt = progression.RitualBonusExpiresAt,
            NextSacrificeAt = progression.NextSacrificeAt,
        };
    }

    private static void ApplyProgression(WH40KChaosGiftProgressionComponent progression, ChaosProgressionState state)
    {
        progression.Level = state.Level;
        progression.LevelXp = state.LevelXp;
        progression.TotalXp = state.TotalXp;
        progression.DevelopmentPoints = state.DevelopmentPoints;
        progression.PatronSoulOfferCount = state.PatronSoulOfferCount;
        progression.BoundSkrizhal = state.BoundSkrizhal;
        progression.StarterSkrizhalIssued = state.StarterSkrizhalIssued;
        progression.MaxLevel = state.MaxLevel;
        progression.PointsPerLevel = state.PointsPerLevel;
        progression.XpPerLevelStep = state.XpPerLevelStep;
        progression.PassiveXpBasePerTick = state.PassiveXpBasePerTick;
        progression.PassiveXpPerLevelBonus = state.PassiveXpPerLevelBonus;
        progression.PassiveXpInterval = state.PassiveXpInterval;
        progression.NextPassiveXpAt = state.NextPassiveXpAt;
        progression.AttunedPatron = state.AttunedPatron;
        progression.PatronSelectionLocked = state.PatronSelectionLocked;
        progression.PrimaryGiftSlot = state.PrimaryGiftSlot;
        progression.GiftSlotOneUnlocked = state.GiftSlotOneUnlocked;
        progression.GiftSlotTwoUnlocked = state.GiftSlotTwoUnlocked;
        progression.GiftSlotThreeUnlocked = state.GiftSlotThreeUnlocked;
        progression.KhorneGiftOnePowerTier = state.KhorneGiftOnePowerTier;
        progression.KhorneGiftOneCooldownTier = state.KhorneGiftOneCooldownTier;
        progression.KhorneGiftOneUtilityTier = state.KhorneGiftOneUtilityTier;
        progression.KhorneGiftOneExUnlocked = state.KhorneGiftOneExUnlocked;
        progression.KhorneGiftTwoPowerTier = state.KhorneGiftTwoPowerTier;
        progression.KhorneGiftTwoCooldownTier = state.KhorneGiftTwoCooldownTier;
        progression.KhorneGiftTwoUtilityTier = state.KhorneGiftTwoUtilityTier;
        progression.KhorneGiftTwoExUnlocked = state.KhorneGiftTwoExUnlocked;
        progression.KhorneGiftThreePowerTier = state.KhorneGiftThreePowerTier;
        progression.KhorneGiftThreeCooldownTier = state.KhorneGiftThreeCooldownTier;
        progression.KhorneGiftThreeUtilityTier = state.KhorneGiftThreeUtilityTier;
        progression.KhorneGiftThreeExUnlocked = state.KhorneGiftThreeExUnlocked;
        progression.KhornePassiveSpeedTier = state.KhornePassiveSpeedTier;
        progression.KhornePassiveHealthTier = state.KhornePassiveHealthTier;
        progression.KhornePassiveMeleeTier = state.KhornePassiveMeleeTier;
        progression.KhornePassiveExUnlocked = state.KhornePassiveExUnlocked;
        progression.GiftUnlockCost = state.GiftUnlockCost;
        progression.AttunementXpMultiplier = state.AttunementXpMultiplier;
        progression.AllowPatronSwitch = state.AllowPatronSwitch;
        progression.PatronLeadershipOrder = state.PatronLeadershipOrder;
        progression.EffectiveLeader = state.EffectiveLeader;
        progression.RitualBonusMultiplier = state.RitualBonusMultiplier;
        progression.RitualBonusExpiresAt = state.RitualBonusExpiresAt;
        progression.NextSacrificeAt = state.NextSacrificeAt;
    }

    private static ChaosWarpResourceState CaptureWarpResource(WH40KWarpResourceComponent warp)
    {
        return new ChaosWarpResourceState
        {
            CurrentCharge = warp.CurrentCharge,
            MaxCharge = warp.MaxCharge,
            RegenPerSecond = warp.RegenPerSecond,
            NextNetworkSyncAt = warp.NextNetworkSyncAt,
        };
    }

    private static void ApplyWarpResource(WH40KWarpResourceComponent warp, ChaosWarpResourceState state)
    {
        warp.CurrentCharge = state.CurrentCharge;
        warp.MaxCharge = state.MaxCharge;
        warp.RegenPerSecond = state.RegenPerSecond;
        warp.NextNetworkSyncAt = state.NextNetworkSyncAt;
    }

    private static ChaosWarpInstabilityState CaptureWarpInstability(WH40KWarpInstabilityComponent instability)
    {
        return new ChaosWarpInstabilityState
        {
            CurrentInstability = instability.CurrentInstability,
            MaxInstability = instability.MaxInstability,
            DecayPerSecond = instability.DecayPerSecond,
            NextNetworkSyncAt = instability.NextNetworkSyncAt,
        };
    }

    private static void ApplyWarpInstability(WH40KWarpInstabilityComponent instability, ChaosWarpInstabilityState state)
    {
        instability.CurrentInstability = state.CurrentInstability;
        instability.MaxInstability = state.MaxInstability;
        instability.DecayPerSecond = state.DecayPerSecond;
        instability.NextNetworkSyncAt = state.NextNetworkSyncAt;
    }

    private sealed class ChaosMindTransferState
    {
        public bool HasLeaderRole;
        public ChaosProgressionState? Progression;
        public ChaosWarpResourceState? WarpResource;
        public ChaosWarpInstabilityState? WarpInstability;
    }

    private sealed class ChaosProgressionState
    {
        public int Level;
        public float LevelXp;
        public float TotalXp;
        public int DevelopmentPoints;
        public int PatronSoulOfferCount;
        public EntityUid? BoundSkrizhal;
        public bool StarterSkrizhalIssued;
        public int MaxLevel;
        public int PointsPerLevel;
        public float XpPerLevelStep;
        public float PassiveXpBasePerTick;
        public float PassiveXpPerLevelBonus;
        public TimeSpan PassiveXpInterval;
        public TimeSpan NextPassiveXpAt;
        public WH40KChaosPatron AttunedPatron;
        public bool PatronSelectionLocked;
        public int PrimaryGiftSlot;
        public bool GiftSlotOneUnlocked;
        public bool GiftSlotTwoUnlocked;
        public bool GiftSlotThreeUnlocked;
        public byte KhorneGiftOnePowerTier;
        public byte KhorneGiftOneCooldownTier;
        public byte KhorneGiftOneUtilityTier;
        public bool KhorneGiftOneExUnlocked;
        public byte KhorneGiftTwoPowerTier;
        public byte KhorneGiftTwoCooldownTier;
        public byte KhorneGiftTwoUtilityTier;
        public bool KhorneGiftTwoExUnlocked;
        public byte KhorneGiftThreePowerTier;
        public byte KhorneGiftThreeCooldownTier;
        public byte KhorneGiftThreeUtilityTier;
        public bool KhorneGiftThreeExUnlocked;
        public byte KhornePassiveSpeedTier;
        public byte KhornePassiveHealthTier;
        public byte KhornePassiveMeleeTier;
        public bool KhornePassiveExUnlocked;
        public int GiftUnlockCost;
        public float AttunementXpMultiplier;
        public bool AllowPatronSwitch;
        public int PatronLeadershipOrder;
        public bool EffectiveLeader;
        public float RitualBonusMultiplier;
        public TimeSpan RitualBonusExpiresAt;
        public TimeSpan NextSacrificeAt;
    }

    private sealed class ChaosWarpResourceState
    {
        public float CurrentCharge;
        public float MaxCharge;
        public float RegenPerSecond;
        public TimeSpan NextNetworkSyncAt;
    }

    private sealed class ChaosWarpInstabilityState
    {
        public float CurrentInstability;
        public float MaxInstability;
        public float DecayPerSecond;
        public TimeSpan NextNetworkSyncAt;
    }
}
