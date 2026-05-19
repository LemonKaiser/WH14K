#nullable disable warnings

using System;
using System.Collections.Generic;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.KillTracking;
using Content.Server._WH40K.Command.Components;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Network;

namespace Content.Server._WH40K.MetaProgress;

public sealed class WH40KValidatedKillRewardEvent : EntityEventArgs
{
    public EntityUid Victim { get; }
    public NetUserId KillerUserId { get; }
    public NetUserId? VictimUserId { get; }
    public string KillerTeamId { get; }
    public string VictimTeamId { get; }
    public string PairToken { get; }

    public WH40KValidatedKillRewardEvent(
        EntityUid victim,
        NetUserId killerUserId,
        NetUserId? victimUserId,
        string killerTeamId,
        string victimTeamId,
        string pairToken)
    {
        Victim = victim;
        KillerUserId = killerUserId;
        VictimUserId = victimUserId;
        KillerTeamId = killerTeamId;
        VictimTeamId = victimTeamId;
        PairToken = pairToken;
    }
}

public sealed class WH40KValidatedKillRewardRevokedEvent : EntityEventArgs
{
    public EntityUid Victim { get; }
    public NetUserId KillerUserId { get; }
    public NetUserId? VictimUserId { get; }
    public string KillerTeamId { get; }
    public string VictimTeamId { get; }
    public string PairToken { get; }

    public WH40KValidatedKillRewardRevokedEvent(
        EntityUid victim,
        NetUserId killerUserId,
        NetUserId? victimUserId,
        string killerTeamId,
        string victimTeamId,
        string pairToken)
    {
        Victim = victim;
        KillerUserId = killerUserId;
        VictimUserId = victimUserId;
        KillerTeamId = killerTeamId;
        VictimTeamId = victimTeamId;
        PairToken = pairToken;
    }
}

public sealed class WH40KConfirmedEliminationEvent : EntityEventArgs
{
    public EntityUid Victim { get; }
    public KillPlayerSource Primary { get; }
    public KillSource[] Assists { get; }
    public NetUserId? VictimUserId { get; }
    public string KillerTeamId { get; }
    public string VictimTeamId { get; }
    public bool Suicide { get; }

    public WH40KConfirmedEliminationEvent(
        EntityUid victim,
        KillPlayerSource primary,
        KillSource[] assists,
        NetUserId? victimUserId,
        string killerTeamId,
        string victimTeamId,
        bool suicide)
    {
        Victim = victim;
        Primary = primary;
        Assists = assists;
        VictimUserId = victimUserId;
        KillerTeamId = killerTeamId;
        VictimTeamId = victimTeamId;
        Suicide = suicide;
    }
}

public sealed class WH40KRoundRewardValidationSystem : EntitySystem
{
    private const int ClaimedReinforcementRewardCapPerRound = 3;

    private readonly record struct RewardTargetKey(string Kind, string Value)
    {
        public string ToToken()
        {
            return $"{Kind}:{Value}";
        }
    }

    private readonly record struct RewardPairKey(NetUserId KillerUserId, RewardTargetKey TargetKey)
    {
        public string ToToken()
        {
            return $"{KillerUserId}|{TargetKey.ToToken()}";
        }
    }

    private readonly record struct RewardEvaluation(
        bool Suppressed,
        bool UsePairwiseSuppression,
        bool ReservesClaimedReinforcementSlot,
        RewardTargetKey RewardTarget,
        NetUserId? VictimUserId,
        string Reason)
    {
        public static RewardEvaluation SuppressedWith(string reason)
        {
            return new RewardEvaluation(
                true,
                false,
                false,
                new RewardTargetKey("suppressed", reason),
                null,
                reason);
        }
    }

    private sealed class PendingDeathState
    {
        public EntityUid Victim;
        public NetUserId? VictimUserId;
        public KillPlayerSource Primary;
        public KillSource[] Assists = Array.Empty<KillSource>();
        public string KillerTeamId = string.Empty;
        public string VictimTeamId = string.Empty;
        public bool Suicide;
        public bool RewardGranted;
        public string PairToken = string.Empty;
        public bool ReservesClaimedReinforcementSlot;
    }

    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly WH40KCombatVictimResolverSystem _combatVictims = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly KillTrackingSystem _killTracking = default!;
    [Dependency] private readonly WH40KTeamRuleFacadeSystem _teamBattle = default!;

    private readonly HashSet<RewardPairKey> _consumedRewardPairs = new();
    private readonly Dictionary<EntityUid, PendingDeathState> _pendingDeaths = new();
    private readonly Dictionary<NetUserId, int> _confirmedClaimedReinforcementEliminations = new();

    private ISawmill _sawmill = default!;
    private bool _traceEnabled;
    private int _lastConfirmedRoundId = -1;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("wh40k.round.reward.validation");
        Subs.CVar(_config, CCVars.WH40KMetaAntiFarmTrace, OnTraceChanged, true);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<AttributedKilledEvent>(OnAttributedKilled);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEndMessage);
    }

    private void OnTraceChanged(bool value)
    {
        _traceEnabled = value;
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        Trace($"Round cleanup: pendingDeaths={_pendingDeaths.Count}, consumedPairs={_consumedRewardPairs.Count}, claimedReinforcementConfirmed={_confirmedClaimedReinforcementEliminations.Count}.");
        _pendingDeaths.Clear();
        _consumedRewardPairs.Clear();
        _confirmedClaimedReinforcementEliminations.Clear();
        _lastConfirmedRoundId = -1;
    }

    private void OnAttributedKilled(ref AttributedKilledEvent ev)
    {
        if (_gameTicker.RunLevel != GameRunLevel.InRound || ev.Suicide)
            return;

        if (ev.Primary is not KillPlayerSource primary)
            return;

        if (!_teamBattle.TryGetTeamIdForUser(primary.PlayerId, out var killerTeamId))
            return;

        if (!_teamBattle.TryGetTeamIdFromEntity(ev.Entity, out var victimTeamId))
            return;

        if (string.Equals(killerTeamId, victimTeamId, StringComparison.Ordinal))
        {
            Trace($"Ignored same-team kill attribution for victim={ev.Entity}, killer={primary.PlayerId}, team={killerTeamId}.");
            return;
        }

        if (_pendingDeaths.ContainsKey(ev.Entity))
        {
            Trace($"Ignored duplicate pending death attribution for victim={ev.Entity}, killer={primary.PlayerId}.");
            return;
        }

        var evaluation = EvaluateReward(ev.Entity);
        if (evaluation.Suppressed)
        {
            Trace($"Suppressed kill reward for victim={ev.Entity}, killer={primary.PlayerId}, reason={evaluation.Reason}.");
            return;
        }

        var rewardGranted = true;
        var pairToken = evaluation.RewardTarget.ToToken();
        if (evaluation.UsePairwiseSuppression)
        {
            var pairKey = new RewardPairKey(primary.PlayerId, evaluation.RewardTarget);
            pairToken = pairKey.ToToken();
            rewardGranted = _consumedRewardPairs.Add(pairKey);
        }

        _pendingDeaths[ev.Entity] = new PendingDeathState
        {
            Victim = ev.Entity,
            VictimUserId = evaluation.VictimUserId,
            Primary = primary,
            Assists = ev.Assists,
            KillerTeamId = killerTeamId,
            VictimTeamId = victimTeamId,
            Suicide = ev.Suicide,
            RewardGranted = rewardGranted,
            PairToken = pairToken,
            ReservesClaimedReinforcementSlot = evaluation.ReservesClaimedReinforcementSlot
        };

        if (rewardGranted)
        {
            var rewardEv = new WH40KValidatedKillRewardEvent(
                ev.Entity,
                primary.PlayerId,
                evaluation.VictimUserId,
                killerTeamId,
                victimTeamId,
                pairToken);
            RaiseLocalEvent(rewardEv);
        }

        Trace($"Pending death tracked: victim={ev.Entity}, victimUser={evaluation.VictimUserId?.ToString() ?? "none"}, killer={primary.PlayerId}, granted={rewardGranted}, pair={pairToken}, reinforcementSlot={evaluation.ReservesClaimedReinforcementSlot}.");
    }

    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (ev.OldMobState != MobState.Dead ||
            (ev.NewMobState != MobState.Critical && ev.NewMobState != MobState.Alive))
        {
            return;
        }

        if (!_pendingDeaths.Remove(ev.Target, out var pending))
            return;

        _killTracking.ClearDamageLedger(ev.Target);

        if (pending.RewardGranted)
        {
            var revoked = new WH40KValidatedKillRewardRevokedEvent(
                pending.Victim,
                pending.Primary.PlayerId,
                pending.VictimUserId,
                pending.KillerTeamId,
                pending.VictimTeamId,
                pending.PairToken);
            RaiseLocalEvent(revoked);
        }

        Trace($"Pending death cleared by revive/recovery: victim={ev.Target}, newState={ev.NewMobState}, pair={pending.PairToken}.");
    }

    private void OnRoundEndMessage(RoundEndMessageEvent ev)
    {
        FinalizePendingEliminations(ev.RoundId);
    }

    public void FinalizePendingEliminations(int roundId = -1)
    {
        if (roundId >= 0 && roundId == _lastConfirmedRoundId)
            return;

        if (roundId >= 0)
            _lastConfirmedRoundId = roundId;

        foreach (var pending in _pendingDeaths.Values)
        {
            if (pending.ReservesClaimedReinforcementSlot &&
                pending.VictimUserId is { } reinforcementUserId)
            {
                _confirmedClaimedReinforcementEliminations[reinforcementUserId] =
                    _confirmedClaimedReinforcementEliminations.GetValueOrDefault(reinforcementUserId) + 1;
            }

            var confirmed = new WH40KConfirmedEliminationEvent(
                pending.Victim,
                pending.Primary,
                pending.Assists,
                pending.VictimUserId,
                pending.KillerTeamId,
                pending.VictimTeamId,
                pending.Suicide);
            RaiseLocalEvent(confirmed);

            Trace($"Confirmed elimination emitted: victim={pending.Victim}, victimUser={pending.VictimUserId?.ToString() ?? "none"}, killer={pending.Primary.PlayerId}, pair={pending.PairToken}.");
        }

        _pendingDeaths.Clear();
    }

    private RewardEvaluation EvaluateReward(EntityUid victim)
    {
        var resolution = _combatVictims.ResolveForValidatedRewards(victim);
        if (!resolution.CountsForValidatedRewards)
            return RewardEvaluation.SuppressedWith(resolution.Reason);

        if (resolution.Kind == WH40KCombatVictimKind.ClaimedReinforcement)
        {
            var claimedUserId = resolution.UserId!.Value;

            var reserved = GetReservedClaimedReinforcementSlots(claimedUserId, victim);
            var confirmed = _confirmedClaimedReinforcementEliminations.GetValueOrDefault(claimedUserId);
            if (confirmed + reserved >= ClaimedReinforcementRewardCapPerRound)
                return RewardEvaluation.SuppressedWith("reinforcement-claimed-cap");

            return new RewardEvaluation(
                false,
                false,
                true,
                new RewardTargetKey("reinforcement", victim.ToString()),
                claimedUserId,
                "reinforcement-claimed");
        }

        return new RewardEvaluation(
            false,
            true,
            false,
            new RewardTargetKey("user", resolution.UserId!.Value.ToString()),
            resolution.UserId,
            "player-owned");
    }

    private int GetReservedClaimedReinforcementSlots(NetUserId claimedUserId, EntityUid excludeVictim)
    {
        var reserved = 0;
        foreach (var pending in _pendingDeaths.Values)
        {
            if (!pending.ReservesClaimedReinforcementSlot ||
                pending.Victim == excludeVictim ||
                pending.VictimUserId != claimedUserId)
            {
                continue;
            }

            reserved++;
        }

        return reserved;
    }
    private void Trace(string message)
    {
        if (_traceEnabled)
            _sawmill.Info($"[trace] {message}");
    }
}
