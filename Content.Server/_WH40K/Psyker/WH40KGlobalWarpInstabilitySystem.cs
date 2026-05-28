using System;
using System.Collections.Generic;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Emp;
using Content.Server.Drunk;
using Content.Server.Ghost;
using Content.Server.Hands.Systems;
using Content.Server.Mind;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Polymorph.Systems;
using Content.Server.Stunnable;
using Content.Server.Traits.Assorted;
using Content.Server.Zombies;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Drunk;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Players;
using Content.Shared.Polymorph;
using Content.Shared.Stunnable;
using Content.Shared.StatusEffectNew;
using Content.Shared.Traits.Assorted;
using Content.Shared._WH40K.Psyker;
using Robust.Shared.Audio;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Server-authoritative global warp instability pool shared by Imperium psykers and chaos leaders.
/// Individual instability components are now mirrored read-models for UI/networking.
/// </summary>
public sealed partial class WH40KGlobalWarpInstabilitySystem : EntitySystem
{
    private const float WarpMutationMinSeverity = 0.25f;
    private const float WarpMutationMaxSeverity = 0.75f;
    private const float WarpMutationNominalThresholdPenalty = 0.5f;
    private const float WarpMutationNominalMovementPenalty = 0.4f;
    private static readonly EntProtoId WarpDoppelgangerPrototype = "MobWH40KWarpDoppelganger";
    private static readonly ProtoId<PolymorphPrototype>[] WarpDaemonMorphs =
    {
        "WH40KWarpRunnerMorph",
        "WH40KWarpRunnerAlphaMorph",
        "WH40KWarpSpitterMorph",
        "WH40KWarpSpitterAlphaMorph",
        "WH40KWarpTankMorph",
        "WH40KWarpTankAlphaMorph",
        "WH40KSlaaneshDemonetteMorph",
        "WH40KSlaaneshClawDancerMorph",
        "WH40KSlaaneshRapturousDemonetteMorph",
        "WH40KSlaaneshAlluressMorph",
    };
    private static readonly EntProtoId[] WarpDaemonPrototypes =
    {
        "MobWH40KWarpRunner",
        "MobWH40KWarpRunnerAlpha",
        "MobWH40KWarpSpitter",
        "MobWH40KWarpSpitterAlpha",
        "MobWH40KWarpTank",
        "MobWH40KWarpTankAlpha",
        "MobWH40KSlaaneshDemonette",
        "MobWH40KSlaaneshClawDancer",
        "MobWH40KSlaaneshRapturousDemonette",
        "MobWH40KSlaaneshAlluress",
    };
    private static readonly ProtoId<HTNCompoundPrototype> SimpleHostileCompoundTask = "SimpleHostileCompound";
    private static readonly ProtoId<NpcFactionPrototype> SimpleHostileFaction = "SimpleHostile";
    private static readonly TimeSpan MirrorSyncCooldown = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan PossessionDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan HallucinationDuration = TimeSpan.FromSeconds(45);
    private static readonly ProtoId<DamageTypePrototype> HeatDamageType = "Heat";
    private static readonly ProtoId<WH40KWarpConfigPrototype> DefaultWarpConfigId = "WH40KWarpDefault";

    private readonly List<WarpDropCandidate> _dropCandidates = new();
    private readonly List<EntityUid> _entityBuffer = new();
    private readonly Dictionary<EntityUid, WarpPossessionState> _activePossessions = new();
    private readonly Dictionary<EntityUid, WarpHallucinationState> _activeHallucinations = new();

    [Dependency] private  BloodstreamSystem _bloodstream = default!;
    [Dependency] private  ChatSystem _chat = default!;
    [Dependency] private  DamageableSystem _damageable = default!;
    [Dependency] private  DrunkSystem _drunk = default!;
    [Dependency] private  EmpSystem _emp = default!;
    [Dependency] private  GhostSystem _ghost = default!;
    [Dependency] private  HandsSystem _hands = default!;
    [Dependency] private  InventorySystem _inventory = default!;
    [Dependency] private  HTNSystem _htn = default!;
    [Dependency] private  MindSystem _mind = default!;
    [Dependency] private  MobStateSystem _mobState = default!;
    [Dependency] private  WH40KWarpMutationSystem _mutation = default!;
    [Dependency] private  NPCSystem _npc = default!;
    [Dependency] private  NpcFactionSystem _npcFaction = default!;
    [Dependency] private  ParacusiaSystem _paracusia = default!;
    [Dependency] private  PolymorphSystem _polymorph = default!;
    [Dependency] private  IRobustRandom _random = default!;
    [Dependency] private  SharedStaminaSystem _stamina = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  IPrototypeManager _prototype = default!;
    [Dependency] private  StunSystem _stun = default!;
    [Dependency] private  SharedVisualBodySystem _visualBody = default!;
    [Dependency] private  ZombieSystem _zombie = default!;

    private DamageTypePrototype _heatDamage = default!;
    private float _currentInstability;
    private TimeSpan _nextGlobalPulseAt;
    private bool _catastropheTriggered;

    private bool _warpEnabled = true;
    private bool _personalBacklashEnabled = true;
    private bool _globalPulsesEnabled = true;
    private bool _catastropheEnabled = true;
    private float _maxInstability = 1000f;
    private float _decayPerSecond = 1.2f;
    private float _highestTierChance = WH40KWarpBacklashSelector.HighestTierChance;
    private float _mildBacklashThreshold = WH40KWarpBacklashSelector.MildBacklashThreshold;
    private float _stunBacklashThreshold = WH40KWarpBacklashSelector.StunBacklashThreshold;
    private float _collapseBacklashThreshold = WH40KWarpBacklashSelector.CollapseBacklashThreshold;
    private float _dropBacklashThreshold = WH40KWarpBacklashSelector.DropBacklashThreshold;
    private float _bleedBacklashThreshold = WH40KWarpBacklashSelector.BleedBacklashThreshold;
    private float _doppelgangerBacklashThreshold = WH40KWarpBacklashSelector.DoppelgangerBacklashThreshold;
    private float _fleshRiftBacklashThreshold = WH40KWarpBacklashSelector.FleshRiftBacklashThreshold;
    private float _possessionBacklashThreshold = WH40KWarpBacklashSelector.PossessionBacklashThreshold;
    private float _mutationBacklashThreshold = WH40KWarpBacklashSelector.MutationBacklashThreshold;
    private float _pulse500Threshold = 500f;
    private float _pulse550Threshold = 550f;
    private float _pulse600Threshold = 600f;
    private float _pulse650Threshold = 650f;
    private float _pulse700Threshold = 700f;
    private float _pulse750Threshold = 750f;
    private float _pulse800Threshold = 800f;
    private float _pulse850Threshold = 850f;
    private float _pulse900Threshold = 900f;
    private TimeSpan _pulse500Interval = TimeSpan.FromSeconds(60);
    private TimeSpan _pulse600Interval = TimeSpan.FromSeconds(45);
    private TimeSpan _pulse700Interval = TimeSpan.FromSeconds(30);
    private TimeSpan _pulse800Interval = TimeSpan.FromSeconds(20);
    private TimeSpan _pulse900Interval = TimeSpan.FromSeconds(11);
    private float _mildBurnDamage = 10f;
    private float _stunDurationSeconds = 1f;
    private float _stunDrunkennessSeconds = 10f;
    private float _collapseStunSeconds = 5f;
    private float _collapseDrunkennessSeconds = 20f;
    private float _bleedTarget = 5f;
    private float _fleshRiftDemonChance = 0.15f;
    private float _fleshRiftDeathChance = 0.35f;
    private float _fleshRiftDeathDamage = 500f;
    private int _dropMaxCount = 3;
    private float _mutationMinSeverity = WarpMutationMinSeverity;
    private float _mutationMaxSeverity = WarpMutationMaxSeverity;

    private readonly record struct WarpGlobalPulseTier(string Id, float Threshold, TimeSpan Interval, string MessageKey);
    private readonly record struct WarpDropCandidate(EntityUid Item, string? SlotName);

    private sealed class WarpPossessionState
    {
        public readonly HashSet<ProtoId<NpcFactionPrototype>> OldFactions = new();
        public bool AddedNpc;
        public string? OldRootTask;
        public EntityUid? StolenMind;
        public TimeSpan ExpiresAt;
    }

    private sealed class WarpHallucinationState
    {
        public bool AddedComponent;
        public SoundSpecifier? Sounds;
        public float MinTime;
        public float MaxTime;
        public float MaxDistance;
        public TimeSpan ExpiresAt;
    }

    public float CurrentInstability => _currentInstability;
    public float MaxInstability => _maxInstability;
    public float DecayPerSecond => _decayPerSecond;
    public bool WarpEnabled => _warpEnabled;
    public bool PersonalBacklashEnabled => _personalBacklashEnabled;
    public bool GlobalPulsesEnabled => _globalPulsesEnabled;
    public bool CatastropheEnabled => _catastropheEnabled;
    public bool CatastropheTriggered => _catastropheTriggered;
    public float HighestTierChance => _highestTierChance;
    public int ActivePossessionCount => _activePossessions.Count;
    public int ActiveHallucinationCount => _activeHallucinations.Count;

    public override void Initialize()
    {
        _heatDamage = _prototype.Index(HeatDamageType);

        LoadRuntimeConfig();

        SubscribeLocalEvent<WH40KWarpInstabilityContributionEvent>(OnInstabilityContribution);
        SubscribeLocalEvent<WH40KWarpInstabilityComponent, ComponentStartup>(OnInstabilityStartup);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WH40KWarpConfigPrototype>())
            LoadRuntimeConfig();
    }

    private void LoadRuntimeConfig()
    {
        var config = _prototype.Index<WH40KWarpConfigPrototype>(DefaultWarpConfigId);
        _warpEnabled = config.Runtime.Enabled;
        _maxInstability = Math.Max(1f, config.Runtime.MaxInstability);
        _decayPerSecond = Math.Max(0f, config.Runtime.DecayPerSecond);
        _personalBacklashEnabled = config.Runtime.PersonalBacklashEnabled;
        _globalPulsesEnabled = config.Runtime.GlobalPulsesEnabled;
        _catastropheEnabled = config.Runtime.CatastropheEnabled;
        _highestTierChance = Math.Clamp(config.Runtime.HighestTierChance, 0f, 1f);

        _mildBacklashThreshold = Math.Max(0f, config.BacklashThresholds.MildBurn);
        _stunBacklashThreshold = Math.Max(0f, config.BacklashThresholds.Stun);
        _collapseBacklashThreshold = Math.Max(0f, config.BacklashThresholds.Collapse);
        _dropBacklashThreshold = Math.Max(0f, config.BacklashThresholds.Drop);
        _bleedBacklashThreshold = Math.Max(0f, config.BacklashThresholds.Bleed);
        _doppelgangerBacklashThreshold = Math.Max(0f, config.BacklashThresholds.Doppelganger);
        _fleshRiftBacklashThreshold = Math.Max(0f, config.BacklashThresholds.FleshRift);
        _possessionBacklashThreshold = Math.Max(0f, config.BacklashThresholds.Possession);
        _mutationBacklashThreshold = Math.Max(0f, config.BacklashThresholds.Mutation);

        _pulse500Threshold = Math.Max(0f, config.GlobalPulses.Threshold500);
        _pulse550Threshold = Math.Max(0f, config.GlobalPulses.Threshold550);
        _pulse600Threshold = Math.Max(0f, config.GlobalPulses.Threshold600);
        _pulse650Threshold = Math.Max(0f, config.GlobalPulses.Threshold650);
        _pulse700Threshold = Math.Max(0f, config.GlobalPulses.Threshold700);
        _pulse750Threshold = Math.Max(0f, config.GlobalPulses.Threshold750);
        _pulse800Threshold = Math.Max(0f, config.GlobalPulses.Threshold800);
        _pulse850Threshold = Math.Max(0f, config.GlobalPulses.Threshold850);
        _pulse900Threshold = Math.Max(0f, config.GlobalPulses.Threshold900);
        _pulse500Interval = TimeSpan.FromSeconds(Math.Max(0f, config.GlobalPulses.Interval500Seconds));
        _pulse600Interval = TimeSpan.FromSeconds(Math.Max(0f, config.GlobalPulses.Interval600Seconds));
        _pulse700Interval = TimeSpan.FromSeconds(Math.Max(0f, config.GlobalPulses.Interval700Seconds));
        _pulse800Interval = TimeSpan.FromSeconds(Math.Max(0f, config.GlobalPulses.Interval800Seconds));
        _pulse900Interval = TimeSpan.FromSeconds(Math.Max(0f, config.GlobalPulses.Interval900Seconds));

        _mildBurnDamage = Math.Max(0f, config.Effects.MildBurnDamage);
        _stunDurationSeconds = Math.Max(0f, config.Effects.StunDurationSeconds);
        _stunDrunkennessSeconds = Math.Max(0f, config.Effects.StunDrunkennessSeconds);
        _collapseStunSeconds = Math.Max(0f, config.Effects.CollapseStunSeconds);
        _collapseDrunkennessSeconds = Math.Max(0f, config.Effects.CollapseDrunkennessSeconds);
        _bleedTarget = Math.Max(0f, config.Effects.BleedTarget);
        _dropMaxCount = Math.Max(1, config.Effects.DropMaxCount);
        _fleshRiftDemonChance = Math.Clamp(config.Effects.FleshRiftDemonChance, 0f, 1f);
        _fleshRiftDeathChance = Math.Clamp(config.Effects.FleshRiftDeathChance, 0f, 1f);
        _fleshRiftDeathDamage = Math.Max(0f, config.Effects.FleshRiftDeathDamage);
        _mutationMinSeverity = Math.Clamp(config.Effects.MutationMinSeverity, 0f, 1f);
        _mutationMaxSeverity = Math.Clamp(config.Effects.MutationMaxSeverity, 0f, 1f);
        if (_mutationMaxSeverity < _mutationMinSeverity)
            _mutationMaxSeverity = _mutationMinSeverity;

        ApplyRuntimeConfigChanges();
    }

    private void ApplyRuntimeConfigChanges()
    {
        if (_currentInstability > _maxInstability)
            _currentInstability = _maxInstability;

        if (_catastropheTriggered && _currentInstability < _maxInstability)
            _catastropheTriggered = false;

        RefreshGlobalPulseState(_currentInstability, _currentInstability, announceOnEscalation: false);
        SyncMirrors(immediate: true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdatePossessions();
        UpdateHallucinations();

        if (_catastropheTriggered || !_warpEnabled)
            return;

        var instabilityChanged = false;
        var previous = _currentInstability;

        if (frameTime > 0f && _currentInstability > 0f)
        {
            var next = MathF.Max(0f, _currentInstability - _decayPerSecond * frameTime);
            if (Math.Abs(next - _currentInstability) > 0.0001f)
            {
                _currentInstability = next;
                instabilityChanged = true;
            }
        }

        RefreshGlobalPulseState(previous, _currentInstability, announceOnEscalation: false);
        TryDispatchDueGlobalPulse();

        if (instabilityChanged)
            SyncMirrors(immediate: false);
    }

    public void AdminResetState()
    {
        ClearTemporaryEffects();
        _currentInstability = 0f;
        _nextGlobalPulseAt = TimeSpan.Zero;
        _catastropheTriggered = false;
        SyncMirrors(immediate: true);
    }

    public void AdminSetInstability(float instability)
    {
        var previous = _currentInstability;
        _currentInstability = Math.Clamp(instability, 0f, _maxInstability);

        if (_catastropheTriggered && _currentInstability < _maxInstability)
            _catastropheTriggered = false;

        RefreshGlobalPulseState(previous, _currentInstability, announceOnEscalation: false);
        SyncMirrors(immediate: true);
    }

    public void AdminAddInstability(float delta)
    {
        AdminSetInstability(_currentInstability + delta);
    }

    public void AdminContribute(EntityUid performer, float amount, string sourceKey)
    {
        RaiseLocalEvent(new WH40KWarpInstabilityContributionEvent(performer, amount, sourceKey));
    }

    internal bool TryAdminForceBacklash(EntityUid performer, WH40KWarpBacklashTier? tier, out string reason)
    {
        if (!CanReceivePersonalBacklash(performer))
        {
            reason = "Target cannot receive personal warp backlash.";
            return false;
        }

        var resolvedTier = tier ?? SelectBacklashTier(performer, _currentInstability);
        if (resolvedTier == WH40KWarpBacklashTier.None)
        {
            reason = "No backlash tier is unlocked at the current instability/settings.";
            return false;
        }

        ApplyBacklashTier(performer, resolvedTier);
        reason = resolvedTier.ToString();
        return true;
    }

    public bool TryAdminForceGlobalPulse(string? requestedTier, out string resolvedTier)
    {
        WarpGlobalPulseTier? tier = null;
        if (string.IsNullOrWhiteSpace(requestedTier) || string.Equals(requestedTier, "auto", StringComparison.OrdinalIgnoreCase))
        {
            tier = GetGlobalPulseTier(_currentInstability);
        }
        else if (TryGetConfiguredGlobalPulseTier(requestedTier, out var explicitTier))
        {
            tier = explicitTier;
        }

        if (tier == null)
        {
            resolvedTier = string.Empty;
            return false;
        }

        DispatchGlobalPulse(tier.Value);
        _nextGlobalPulseAt = _timing.CurTime + tier.Value.Interval;
        resolvedTier = tier.Value.Id;
        return true;
    }

    public void AdminForceCatastrophe(EntityUid? trigger = null)
    {
        TriggerWarpCatastrophe(trigger ?? default);
        SyncMirrors(immediate: true);
    }

    public bool TryGetCurrentGlobalPulse(out string tierId, out float threshold, out TimeSpan interval)
    {
        var tier = GetGlobalPulseTier(_currentInstability);
        if (tier == null)
        {
            tierId = string.Empty;
            threshold = 0f;
            interval = TimeSpan.Zero;
            return false;
        }

        tierId = tier.Value.Id;
        threshold = tier.Value.Threshold;
        interval = tier.Value.Interval;
        return true;
    }

    public TimeSpan? GetNextGlobalPulseDelay()
    {
        if (_nextGlobalPulseAt == TimeSpan.Zero)
            return null;

        var remaining = _nextGlobalPulseAt - _timing.CurTime;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        ClearTemporaryEffects();
        _currentInstability = 0f;
        _nextGlobalPulseAt = TimeSpan.Zero;
        _catastropheTriggered = false;
        SyncMirrors(immediate: true);
    }

    private void OnInstabilityStartup(EntityUid uid, WH40KWarpInstabilityComponent component, ref ComponentStartup args)
    {
        component.NextNetworkSyncAt = _timing.CurTime + MirrorSyncCooldown;
        component.MaxInstability = _maxInstability;
        component.DecayPerSecond = _catastropheTriggered || !_warpEnabled ? 0f : _decayPerSecond;
        component.CurrentInstability = _currentInstability;
        Dirty(uid, component);
    }

    private void OnInstabilityContribution(WH40KWarpInstabilityContributionEvent ev)
    {
        var adjustedAmount = AdjustContributionAmount(ev.Performer, ev.Amount);
        if (!_warpEnabled || adjustedAmount <= 0f)
            return;

        if (_catastropheTriggered)
        {
            _currentInstability = _maxInstability;
            SyncMirrors(immediate: true);
            return;
        }

        var previous = _currentInstability;
        _currentInstability = Math.Clamp(_currentInstability + adjustedAmount, 0f, _maxInstability);

        if (_currentInstability >= _maxInstability && _catastropheEnabled)
        {
            TriggerWarpCatastrophe(ev.Performer);
            SyncMirrors(immediate: true);
            return;
        }

        ApplyPersonalBacklash(ev.Performer);
        RefreshGlobalPulseState(previous, _currentInstability, announceOnEscalation: true);
        SyncMirrors(immediate: true);
    }

    private void ApplyPersonalBacklash(EntityUid performer)
    {
        if (!_personalBacklashEnabled || !CanReceivePersonalBacklash(performer))
            return;

        ApplyBacklashTier(performer, SelectBacklashTier(performer, _currentInstability));
    }

    private void ApplyBacklashTier(EntityUid performer, WH40KWarpBacklashTier tier)
    {
        switch (tier)
        {
            case WH40KWarpBacklashTier.Mutation:
                ApplyIrreversibleMutationBacklash(performer);
                return;

            case WH40KWarpBacklashTier.Possession:
                ApplyPossessionBacklash(performer);
                return;

            case WH40KWarpBacklashTier.FleshRift:
                ApplyFleshRiftBacklash(performer);
                return;

            case WH40KWarpBacklashTier.Doppelganger:
                SpawnWarpDoppelganger(performer);
                return;

            case WH40KWarpBacklashTier.Bleed:
                ApplyBleedBacklash(performer);
                return;

            case WH40KWarpBacklashTier.Drop:
                ApplyDropBacklash(performer);
                return;

            case WH40KWarpBacklashTier.Collapse:
                ApplyCollapseBacklash(performer);
                return;

            case WH40KWarpBacklashTier.Stun:
                _stun.TryAddStunDuration(performer, TimeSpan.FromSeconds(_stunDurationSeconds));
                _drunk.TryApplyDrunkenness(performer, TimeSpan.FromSeconds(_stunDrunkennessSeconds));
                return;

            case WH40KWarpBacklashTier.MildBurn:
                ApplyWarpBurn(performer, _mildBurnDamage);
                return;

            default:
                return;
        }
    }

    private WH40KWarpBacklashTier SelectBacklashTier(EntityUid performer, float instability)
    {
        Span<WH40KWarpBacklashTier> eligible = stackalloc WH40KWarpBacklashTier[9];
        var count = 0;
        var effectiveInstability = instability;

        if (TryComp<WH40KWarpControlComponent>(performer, out var control))
            effectiveInstability = Math.Max(0f, instability + control.PersonalBacklashThresholdBias);

        if (effectiveInstability >= _mildBacklashThreshold)
            eligible[count++] = WH40KWarpBacklashTier.MildBurn;
        if (effectiveInstability >= _stunBacklashThreshold)
            eligible[count++] = WH40KWarpBacklashTier.Stun;
        if (effectiveInstability >= _collapseBacklashThreshold)
            eligible[count++] = WH40KWarpBacklashTier.Collapse;
        if (effectiveInstability >= _dropBacklashThreshold)
            eligible[count++] = WH40KWarpBacklashTier.Drop;
        if (effectiveInstability >= _bleedBacklashThreshold)
            eligible[count++] = WH40KWarpBacklashTier.Bleed;
        if (effectiveInstability >= _doppelgangerBacklashThreshold)
            eligible[count++] = WH40KWarpBacklashTier.Doppelganger;
        if (effectiveInstability >= _fleshRiftBacklashThreshold)
            eligible[count++] = WH40KWarpBacklashTier.FleshRift;
        if (effectiveInstability >= _possessionBacklashThreshold)
            eligible[count++] = WH40KWarpBacklashTier.Possession;
        if (effectiveInstability >= _mutationBacklashThreshold)
            eligible[count++] = WH40KWarpBacklashTier.Mutation;

        if (count == 0)
            return WH40KWarpBacklashTier.None;

        if (count == 1 || _random.NextFloat() < _highestTierChance)
            return eligible[count - 1];

        return eligible[_random.Next(count - 1)];
    }

    private float AdjustContributionAmount(EntityUid performer, float amount)
    {
        if (amount <= 0f)
            return 0f;

        if (!TryComp<WH40KWarpControlComponent>(performer, out var control))
            return amount;

        var scaled = amount * Math.Max(0f, control.ContributionMultiplier) + control.FlatContributionBonus;
        return Math.Max(0f, scaled);
    }

    private bool CanReceivePersonalBacklash(EntityUid uid)
    {
        if (!CanAffect(uid))
            return false;

        return !TryComp<WH40KWarpControlComponent>(uid, out var control) || !control.IgnorePersonalBacklash;
    }

    private bool CanReceiveGlobalPulseEffects(EntityUid uid)
    {
        if (!CanAffect(uid))
            return false;

        return !TryComp<WH40KWarpControlComponent>(uid, out var control) || !control.IgnoreGlobalPulseEffects;
    }

    private bool CanBeCatastropheSacrificed(EntityUid uid)
    {
        if (!CanAffect(uid))
            return false;

        return !TryComp<WH40KWarpControlComponent>(uid, out var control) || !control.IgnoreCatastropheSacrifice;
    }

    private void ApplyBleedBacklash(EntityUid performer)
    {
        if (!TryComp<BloodstreamComponent>(performer, out var bloodstream))
            return;

        var targetBleed = Math.Clamp(MathF.Max(bloodstream.BleedAmount, _bleedTarget), 0f, MathF.Max(0f, bloodstream.MaxBleedAmount - 1f));
        var delta = targetBleed - bloodstream.BleedAmount;

        if (delta > 0f)
            _bloodstream.TryModifyBleedAmount((performer, bloodstream), delta);
    }

    private void ApplyFleshRiftBacklash(EntityUid performer)
    {
        var roll = _random.NextFloat();
        if (roll < _fleshRiftDemonChance)
        {
            _polymorph.PolymorphEntity(performer, PickWarpDaemonMorph());
            return;
        }

        var deathThreshold = Math.Clamp(_fleshRiftDemonChance + _fleshRiftDeathChance, 0f, 1f);
        if (roll < deathThreshold)
        {
            ApplyWarpBurn(performer, _fleshRiftDeathDamage);
            return;
        }

        _stun.TryAddParalyzeDuration(performer, TimeSpan.FromSeconds(10));
    }

    private void ApplyPossessionBacklash(EntityUid performer)
    {
        if (_activePossessions.TryGetValue(performer, out var active))
        {
            active.ExpiresAt = _timing.CurTime + PossessionDuration;
            return;
        }

        var state = new WarpPossessionState
        {
            ExpiresAt = _timing.CurTime + PossessionDuration,
        };

        if (_mind.TryGetMind(performer, out var mindId, out var mind))
        {
            if (_ghost.SpawnGhost((mindId, mind), Transform(performer).Coordinates, false) == null)
                return;

            state.StolenMind = mindId;
        }

        var faction = EnsureComp<NpcFactionMemberComponent>(performer);
        state.OldFactions.UnionWith(faction.Factions);
        _npcFaction.ClearFactions((performer, faction), false);
        _npcFaction.AddFaction((performer, faction), SimpleHostileFaction);

        state.AddedNpc = !EnsureComp<HTNComponent>(performer, out var htn);
        if (!state.AddedNpc)
            state.OldRootTask = htn.RootTask.Task;

        htn.RootTask = new HTNCompoundTask { Task = SimpleHostileCompoundTask };
        htn.Blackboard.SetValue(NPCBlackboard.Owner, performer);
        _npc.WakeNPC(performer, htn);
        _htn.Replan(htn);

        _activePossessions[performer] = state;
    }

    private void ApplyIrreversibleMutationBacklash(EntityUid performer)
    {
        if (TryComp<WH40KWarpMutationComponent>(performer, out _))
            return;

        var mutation = EnsureComp<WH40KWarpMutationComponent>(performer);
        mutation.Severity = _random.NextFloat(_mutationMinSeverity, _mutationMaxSeverity);
        mutation.ThresholdMultiplier = Math.Clamp(1f - WarpMutationNominalThresholdPenalty * mutation.Severity, 0.4f, 1f);
        mutation.MovementMultiplier = Math.Clamp(1f - WarpMutationNominalMovementPenalty * mutation.Severity, 0.45f, 1f);
        _mutation.ApplyMutation(performer, mutation);
    }

    private void ApplyCollapseBacklash(EntityUid performer)
    {
        if (_random.NextFloat() < 0.5f)
        {
            _drunk.TryApplyDrunkenness(performer, TimeSpan.FromSeconds(_collapseDrunkennessSeconds));
            return;
        }

        if (TryComp<StaminaComponent>(performer, out var stamina))
        {
            var requiredDamage = MathF.Max(0f, stamina.CritThreshold - stamina.StaminaDamage + 1f);
            if (requiredDamage > 0f)
            {
                _stamina.TakeStaminaDamage(
                    performer,
                    requiredDamage,
                    stamina,
                    source: performer,
                    visual: false,
                    log: false,
                    applyCooldown: false);
            }
        }

        _stun.TryAddStunDuration(performer, TimeSpan.FromSeconds(_collapseStunSeconds));
    }

    private void ApplyDropBacklash(EntityUid performer)
    {
        BuildDropCandidateList(performer, _dropCandidates);
        if (_dropCandidates.Count == 0)
            return;

        _random.Shuffle(_dropCandidates);
        var maxDrops = Math.Min(_dropMaxCount, _dropCandidates.Count);
        var targetDrops = _random.Next(1, maxDrops + 1);
        var dropped = 0;

        for (var index = 0; index < _dropCandidates.Count && dropped < targetDrops; index++)
        {
            var candidate = _dropCandidates[index];
            var success = candidate.SlotName == null
                ? _hands.TryDrop(performer, candidate.Item, checkActionBlocker: false, doDropInteraction: false)
                : _inventory.TryUnequip(performer, candidate.SlotName, out _, silent: true, force: true);

            if (success)
                dropped++;
        }
    }

    private void ApplyWarpBurn(EntityUid uid, float heatDamage)
    {
        if (heatDamage <= 0f || !TryComp<DamageableComponent>(uid, out var damageable))
            return;

        var damage = new DamageSpecifier(_heatDamage, FixedPoint2.New(heatDamage));
        _damageable.TryChangeDamage(
            (uid, damageable),
            damage,
            ignoreResistances: true,
            interruptsDoAfters: false,
            origin: uid,
            ignoreGlobalModifiers: true);
    }

    private bool SpawnWarpDoppelganger(EntityUid source)
    {
        if (TerminatingOrDeleted(source))
            return false;

        var clone = Spawn(WarpDoppelgangerPrototype, Transform(source).Coordinates);
        _visualBody.CopyAppearanceFrom(source, clone);
        return true;
    }

    private void BuildDropCandidateList(EntityUid performer, List<WarpDropCandidate> output)
    {
        output.Clear();

        if (TryComp<HandsComponent>(performer, out var hands))
        {
            foreach (var held in _hands.EnumerateHeld((performer, hands)))
            {
                output.Add(new WarpDropCandidate(held, null));
            }
        }

        if (!TryComp<InventoryComponent>(performer, out var inventory))
            return;

        var slots = _inventory.GetSlotEnumerator((performer, inventory), GetDroppableInventoryFlags());
        while (slots.NextItem(out var item, out var slot))
        {
            output.Add(new WarpDropCandidate(item, slot.Name));
        }
    }

    private static SlotFlags GetDroppableInventoryFlags()
    {
        return SlotFlags.HEAD |
               SlotFlags.EYES |
               SlotFlags.EARS |
               SlotFlags.MASK |
               SlotFlags.OUTERCLOTHING |
               SlotFlags.INNERCLOTHING |
               SlotFlags.NECK |
               SlotFlags.BACK |
               SlotFlags.BELT |
               SlotFlags.GLOVES |
               SlotFlags.LEGS |
               SlotFlags.FEET |
               SlotFlags.SUITSTORAGE;
    }

    private bool CanAffect(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return false;

        return !TryComp<MobStateComponent>(uid, out var mobState) || !_mobState.IsDead(uid, mobState);
    }

    private void TriggerWarpCatastrophe(EntityUid trigger)
    {
        if (_catastropheTriggered)
            return;

        _catastropheTriggered = true;
        _currentInstability = _maxInstability;
        _nextGlobalPulseAt = TimeSpan.Zero;

        var haveCoords = Exists(trigger);
        var catastropheCoords = haveCoords ? Transform(trigger).Coordinates : default;

        _entityBuffer.Clear();
        var query = EntityQueryEnumerator<WH40KWarpResourceComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!CanBeCatastropheSacrificed(uid))
                continue;

            _entityBuffer.Add(uid);
            if (haveCoords)
                continue;

            catastropheCoords = Transform(uid).Coordinates;
            haveCoords = true;
        }

        foreach (var uid in _entityBuffer)
        {
            SacrificeWarpUser(uid);
        }

        _entityBuffer.Clear();

        if (haveCoords)
            Spawn(PickWarpDaemonPrototype(), catastropheCoords);

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("wh40k-warp-instability-global-catastrophe"),
            Loc.GetString("wh40k-warp-instability-global-announcer"),
            playSound: false,
            colorOverride: Color.MediumPurple);
    }

    private void SacrificeWarpUser(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var coords = Transform(uid).Coordinates;

        _activePossessions.Remove(uid);
        _activeHallucinations.Remove(uid);

        if (_mind.TryGetMind(uid, out var mindId, out var mind))
            _ghost.SpawnGhost((mindId, mind), coords, false);

        Spawn("Ash", coords);
        QueueDel(uid);
    }

    private void RefreshGlobalPulseState(float previousInstability, float currentInstability, bool announceOnEscalation)
    {
        if (_catastropheTriggered || !_globalPulsesEnabled)
        {
            _nextGlobalPulseAt = TimeSpan.Zero;
            return;
        }

        var previousTier = GetGlobalPulseTier(previousInstability);
        var currentTier = GetGlobalPulseTier(currentInstability);

        if (currentTier == null)
        {
            _nextGlobalPulseAt = TimeSpan.Zero;
            return;
        }

        var now = _timing.CurTime;
        if (previousTier?.Threshold == currentTier.Value.Threshold)
        {
            if (_nextGlobalPulseAt == TimeSpan.Zero)
                _nextGlobalPulseAt = now + currentTier.Value.Interval;

            return;
        }

        _nextGlobalPulseAt = now + currentTier.Value.Interval;

        if (announceOnEscalation && (previousTier == null || currentTier.Value.Threshold > previousTier.Value.Threshold))
            DispatchGlobalPulse(currentTier.Value);
    }

    private void TryDispatchDueGlobalPulse()
    {
        if (_catastropheTriggered || !_globalPulsesEnabled)
        {
            _nextGlobalPulseAt = TimeSpan.Zero;
            return;
        }

        var tier = GetGlobalPulseTier(_currentInstability);
        if (tier == null)
        {
            _nextGlobalPulseAt = TimeSpan.Zero;
            return;
        }

        var now = _timing.CurTime;
        if (_nextGlobalPulseAt == TimeSpan.Zero)
        {
            _nextGlobalPulseAt = now + tier.Value.Interval;
            return;
        }

        if (now < _nextGlobalPulseAt)
            return;

        DispatchGlobalPulse(tier.Value);
        _nextGlobalPulseAt = now + tier.Value.Interval;
    }

    private void DispatchGlobalPulse(WarpGlobalPulseTier tier)
    {
        ApplyGlobalPulseEffect(tier);
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(tier.MessageKey),
            Loc.GetString("wh40k-warp-instability-global-announcer"),
            playSound: false,
            colorOverride: Color.MediumPurple);
    }

    private void ApplyGlobalPulseEffect(WarpGlobalPulseTier tier)
    {
        switch (tier.Id)
        {
            case "900":
                ApplyHallucinationPulse(TimeSpan.FromSeconds(60), 0.25f, 4f, 9f);
                ApplyEmpPulse(6f, 45000f, TimeSpan.FromSeconds(15));
                TryRaiseWarpDead();
                SpawnHellspawnBreach();
                break;
            case "850":
                SpawnHellspawnBreach();
                break;
            case "800":
                ApplyEmpPulse(5f, 35000f, TimeSpan.FromSeconds(12));
                break;
            case "750":
                TryRaiseWarpDead();
                break;
            case "700":
                ApplyHallucinationPulse(HallucinationDuration, 0.5f, 6f, 7f);
                break;
            case "650":
                if (TryPickRandomLivingActor(out var actor))
                    SpawnWarpDoppelganger(actor);
                break;
            case "600":
                ApplyEmpPulse(4f, 20000f, TimeSpan.FromSeconds(8));
                break;
        }
    }

    private void ApplyEmpPulse(float range, float energyConsumption, TimeSpan duration)
    {
        if (!TryPickRandomLivingActor(out var actor))
            return;

        _emp.EmpPulse(Transform(actor).Coordinates, range, energyConsumption, duration);
    }

    private void ApplyHallucinationPulse(TimeSpan duration, float minTimeBetweenIncidents, float maxTimeBetweenIncidents, float maxDistance)
    {
        var expiresAt = _timing.CurTime + duration;
        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out _, out var mobState))
        {
            if (_mobState.IsDead(uid, mobState) || !CanReceiveGlobalPulseEffects(uid))
                continue;

            if (_activeHallucinations.TryGetValue(uid, out var existing))
            {
                existing.ExpiresAt = existing.ExpiresAt > expiresAt ? existing.ExpiresAt : expiresAt;
                continue;
            }

            var existed = EnsureComp<ParacusiaComponent>(uid, out var paracusia);
            var state = new WarpHallucinationState
            {
                AddedComponent = !existed,
                ExpiresAt = expiresAt,
            };

            if (existed)
            {
                state.Sounds = paracusia.Sounds;
                state.MinTime = paracusia.MinTimeBetweenIncidents;
                state.MaxTime = paracusia.MaxTimeBetweenIncidents;
                state.MaxDistance = paracusia.MaxSoundDistance;
            }

            _paracusia.SetSounds(uid, new SoundCollectionSpecifier("Paracusia"), paracusia);
            _paracusia.SetTime(uid, minTimeBetweenIncidents, maxTimeBetweenIncidents, paracusia);
            _paracusia.SetDistance(uid, maxDistance, paracusia);
            _activeHallucinations[uid] = state;
        }
    }

    private void TryRaiseWarpDead()
    {
        if (!TryPickRandomDeadMob(out var corpse))
            return;

        _zombie.ZombifyEntity(corpse);
        var faction = EnsureComp<NpcFactionMemberComponent>(corpse);
        _npcFaction.ClearFactions((corpse, faction), false);
        _npcFaction.AddFaction((corpse, faction), SimpleHostileFaction);
    }

    private void SpawnHellspawnBreach()
    {
        if (!TryPickRandomLivingActor(out var actor))
            return;

        Spawn(PickWarpDaemonPrototype(), Transform(actor).Coordinates);
    }

    private ProtoId<PolymorphPrototype> PickWarpDaemonMorph()
    {
        return _random.Pick(WarpDaemonMorphs);
    }

    private EntProtoId PickWarpDaemonPrototype()
    {
        return _random.Pick(WarpDaemonPrototypes);
    }

    private WarpGlobalPulseTier? GetGlobalPulseTier(float instability)
    {
        WarpGlobalPulseTier? best = null;
        ConsiderGlobalPulseTier(ref best, instability, "500", _pulse500Threshold, _pulse500Interval, "wh40k-warp-instability-global-pulse-500");
        ConsiderGlobalPulseTier(ref best, instability, "550", _pulse550Threshold, _pulse500Interval, "wh40k-warp-instability-global-pulse-550");
        ConsiderGlobalPulseTier(ref best, instability, "600", _pulse600Threshold, _pulse600Interval, "wh40k-warp-instability-global-pulse-600");
        ConsiderGlobalPulseTier(ref best, instability, "650", _pulse650Threshold, _pulse600Interval, "wh40k-warp-instability-global-pulse-650");
        ConsiderGlobalPulseTier(ref best, instability, "700", _pulse700Threshold, _pulse700Interval, "wh40k-warp-instability-global-pulse-700");
        ConsiderGlobalPulseTier(ref best, instability, "750", _pulse750Threshold, _pulse700Interval, "wh40k-warp-instability-global-pulse-750");
        ConsiderGlobalPulseTier(ref best, instability, "800", _pulse800Threshold, _pulse800Interval, "wh40k-warp-instability-global-pulse-800");
        ConsiderGlobalPulseTier(ref best, instability, "850", _pulse850Threshold, _pulse800Interval, "wh40k-warp-instability-global-pulse-850");
        ConsiderGlobalPulseTier(ref best, instability, "900", _pulse900Threshold, _pulse900Interval, "wh40k-warp-instability-global-pulse-900");
        return best;
    }

    private bool TryGetConfiguredGlobalPulseTier(string requestedTier, out WarpGlobalPulseTier tier)
    {
        switch (requestedTier.Trim().ToLowerInvariant())
        {
            case "500":
            case "tier500":
                tier = new WarpGlobalPulseTier("500", _pulse500Threshold, _pulse500Interval, "wh40k-warp-instability-global-pulse-500");
                return true;
            case "550":
            case "tier550":
                tier = new WarpGlobalPulseTier("550", _pulse550Threshold, _pulse500Interval, "wh40k-warp-instability-global-pulse-550");
                return true;
            case "600":
            case "tier600":
                tier = new WarpGlobalPulseTier("600", _pulse600Threshold, _pulse600Interval, "wh40k-warp-instability-global-pulse-600");
                return true;
            case "650":
            case "tier650":
                tier = new WarpGlobalPulseTier("650", _pulse650Threshold, _pulse600Interval, "wh40k-warp-instability-global-pulse-650");
                return true;
            case "700":
            case "tier700":
                tier = new WarpGlobalPulseTier("700", _pulse700Threshold, _pulse700Interval, "wh40k-warp-instability-global-pulse-700");
                return true;
            case "750":
            case "tier750":
                tier = new WarpGlobalPulseTier("750", _pulse750Threshold, _pulse700Interval, "wh40k-warp-instability-global-pulse-750");
                return true;
            case "800":
            case "tier800":
                tier = new WarpGlobalPulseTier("800", _pulse800Threshold, _pulse800Interval, "wh40k-warp-instability-global-pulse-800");
                return true;
            case "850":
            case "tier850":
                tier = new WarpGlobalPulseTier("850", _pulse850Threshold, _pulse800Interval, "wh40k-warp-instability-global-pulse-850");
                return true;
            case "900":
            case "tier900":
                tier = new WarpGlobalPulseTier("900", _pulse900Threshold, _pulse900Interval, "wh40k-warp-instability-global-pulse-900");
                return true;
            default:
                tier = default;
                return false;
        }
    }

    private static void ConsiderGlobalPulseTier(ref WarpGlobalPulseTier? best, float instability, string id, float threshold, TimeSpan interval, string messageKey)
    {
        if (interval <= TimeSpan.Zero || instability < threshold)
            return;

        if (best == null || threshold > best.Value.Threshold)
            best = new WarpGlobalPulseTier(id, threshold, interval, messageKey);
    }

    private void UpdatePossessions()
    {
        if (_activePossessions.Count == 0)
            return;

        _entityBuffer.Clear();
        foreach (var uid in _activePossessions.Keys)
        {
            _entityBuffer.Add(uid);
        }

        var now = _timing.CurTime;
        foreach (var uid in _entityBuffer)
        {
            if (!_activePossessions.TryGetValue(uid, out var state))
                continue;

            if (TerminatingOrDeleted(uid))
            {
                _activePossessions.Remove(uid);
                continue;
            }

            if (now < state.ExpiresAt)
                continue;

            RestorePossession(uid, state);
            _activePossessions.Remove(uid);
        }

        _entityBuffer.Clear();
    }

    private void UpdateHallucinations()
    {
        if (_activeHallucinations.Count == 0)
            return;

        _entityBuffer.Clear();
        foreach (var uid in _activeHallucinations.Keys)
        {
            _entityBuffer.Add(uid);
        }

        var now = _timing.CurTime;
        foreach (var uid in _entityBuffer)
        {
            if (!_activeHallucinations.TryGetValue(uid, out var state))
                continue;

            if (TerminatingOrDeleted(uid))
            {
                _activeHallucinations.Remove(uid);
                continue;
            }

            if (now < state.ExpiresAt)
                continue;

            RestoreHallucination(uid, state);
            _activeHallucinations.Remove(uid);
        }

        _entityBuffer.Clear();
    }

    private void RestorePossession(EntityUid uid, WarpPossessionState state)
    {
        if (TerminatingOrDeleted(uid))
            return;

        if (state.AddedNpc)
        {
            RemComp<HTNComponent>(uid);
        }
        else if (TryComp<HTNComponent>(uid, out var htn) && state.OldRootTask != null)
        {
            htn.RootTask = new HTNCompoundTask { Task = state.OldRootTask };
            htn.Blackboard.SetValue(NPCBlackboard.Owner, uid);
            _npc.WakeNPC(uid, htn);
            _htn.Replan(htn);
        }

        var faction = EnsureComp<NpcFactionMemberComponent>(uid);
        _npcFaction.RemoveFaction((uid, faction), SimpleHostileFaction, false);
        _npcFaction.AddFactions((uid, faction), state.OldFactions);

        if (state.StolenMind != null && Exists(state.StolenMind.Value))
            _mind.TransferTo(state.StolenMind.Value, uid);
    }

    private void RestoreHallucination(EntityUid uid, WarpHallucinationState state)
    {
        if (TerminatingOrDeleted(uid))
            return;

        if (state.AddedComponent)
        {
            RemComp<ParacusiaComponent>(uid);
            return;
        }

        if (!TryComp<ParacusiaComponent>(uid, out var paracusia) || state.Sounds == null)
            return;

        _paracusia.SetSounds(uid, state.Sounds, paracusia);
        _paracusia.SetTime(uid, state.MinTime, state.MaxTime, paracusia);
        _paracusia.SetDistance(uid, state.MaxDistance, paracusia);
    }

    private void ClearTemporaryEffects()
    {
        foreach (var (uid, state) in _activePossessions)
        {
            RestorePossession(uid, state);
        }

        foreach (var (uid, state) in _activeHallucinations)
        {
            RestoreHallucination(uid, state);
        }

        _activePossessions.Clear();
        _activeHallucinations.Clear();
    }

    private bool TryPickRandomLivingActor(out EntityUid actor)
    {
        _entityBuffer.Clear();

        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out _, out var mobState))
        {
            if (_mobState.IsDead(uid, mobState) || !CanReceiveGlobalPulseEffects(uid))
                continue;

            _entityBuffer.Add(uid);
        }

        if (_entityBuffer.Count == 0)
        {
            actor = default;
            return false;
        }

        actor = _random.Pick(_entityBuffer);
        _entityBuffer.Clear();
        return true;
    }

    private bool TryPickRandomDeadMob(out EntityUid corpse)
    {
        _entityBuffer.Clear();

        var query = EntityQueryEnumerator<MobStateComponent>();
        while (query.MoveNext(out var uid, out var mobState))
        {
            if (!_mobState.IsDead(uid, mobState) || !CanReceiveGlobalPulseEffects(uid))
                continue;

            _entityBuffer.Add(uid);
        }

        if (_entityBuffer.Count == 0)
        {
            corpse = default;
            return false;
        }

        corpse = _random.Pick(_entityBuffer);
        _entityBuffer.Clear();
        return true;
    }

    private void SyncMirrors(bool immediate)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KWarpInstabilityComponent>();
        while (query.MoveNext(out var uid, out var instability))
        {
            if (!immediate && now < instability.NextNetworkSyncAt)
                continue;

            var changed = false;
            var expectedDecay = _catastropheTriggered || !_warpEnabled ? 0f : _decayPerSecond;

            if (Math.Abs(instability.MaxInstability - _maxInstability) > 0.0001f)
            {
                instability.MaxInstability = _maxInstability;
                changed = true;
            }

            if (Math.Abs(instability.DecayPerSecond - expectedDecay) > 0.0001f)
            {
                instability.DecayPerSecond = expectedDecay;
                changed = true;
            }

            if (Math.Abs(instability.CurrentInstability - _currentInstability) > 0.0001f)
            {
                instability.CurrentInstability = _currentInstability;
                changed = true;
            }

            if (!changed)
                continue;

            instability.NextNetworkSyncAt = now + MirrorSyncCooldown;
            Dirty(uid, instability);
        }
    }
}
