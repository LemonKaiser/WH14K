using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Combat;
using Content.Server.NPC.HTN;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.KillTracking;

/// <summary>
/// Tracks damage attribution over an entity's life and emits down / kill attribution events.
/// </summary>
public sealed class KillTrackingSystem : EntitySystem
{
    [Dependency] private readonly CombatAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public void SetKillState(EntityUid uid, MobState state, KillTrackerComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        component.KillState = state;
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        // Record damage before mob thresholds process the same delta.
        SubscribeLocalEvent<KillTrackerComponent, DamageChangedEvent>(OnDamageChanged, before: [typeof(MobThresholdSystem)]);
        SubscribeLocalEvent<KillTrackerComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnDamageChanged(EntityUid uid, KillTrackerComponent component, DamageChangedEvent args)
    {
        if (args.DamageDelta == null)
            return;

        PruneExpiredContributors(component);

        if (!args.DamageIncreased)
        {
            ApplyHealing(component, args.DamageDelta);
            return;
        }

        var totalDamage = GetPositiveDamage(args.DamageDelta);
        if (totalDamage <= FixedPoint2.Zero)
            return;

        var source = GetKillSource(args.Origin);
        if (!component.DamageLedger.TryGetValue(source, out var record))
        {
            record = new KillAttributionRecord();
            component.DamageLedger[source] = record;
        }

        record.TotalDamage += totalDamage;
        record.LastDamageTime = _timing.CurTime;
    }

    private void OnMobStateChanged(EntityUid uid, KillTrackerComponent component, MobStateChangedEvent args)
    {
        if (args.OldMobState >= args.NewMobState)
        {
            if (args.NewMobState == MobState.Alive && args.OldMobState > MobState.Alive)
                component.DamageLedger.Clear();

            return;
        }

        if (args.NewMobState == MobState.Critical)
        {
            var attribution = BuildAttribution(uid, component, args.Origin);
            var downed = new AttributedDownedEvent(uid, attribution.Primary, attribution.Assists, attribution.Suicide);
            RaiseLocalEvent(uid, ref downed, true);

            if (component.KillState == MobState.Critical)
                RaiseCompatibilityKillEvent(uid, attribution);

            return;
        }

        if (args.NewMobState == MobState.Dead)
        {
            var attribution = BuildAttribution(uid, component, args.Origin);
            var killed = new AttributedKilledEvent(uid, attribution.Primary, attribution.Assists, attribution.Suicide);
            RaiseLocalEvent(uid, ref killed, true);

            if (component.KillState == MobState.Dead)
                RaiseCompatibilityKillEvent(uid, attribution);
        }
    }

    private void RaiseCompatibilityKillEvent(EntityUid uid, KillAttributionResult attribution)
    {
        var compatibility = new KillReportedEvent(
            uid,
            attribution.Primary,
            attribution.Assists.Length > 0 ? attribution.Assists[0] : null,
            attribution.Suicide);
        RaiseLocalEvent(uid, ref compatibility, true);
    }

    private KillAttributionResult BuildAttribution(EntityUid uid, KillTrackerComponent component, EntityUid? origin)
    {
        PruneExpiredContributors(component);

        var impulse = GetKillSource(origin);
        var recentContributors = component.DamageLedger
            .Where(pair => pair.Value.TotalDamage > FixedPoint2.Zero)
            .OrderByDescending(pair => pair.Value.TotalDamage)
            .ToArray();

        var strongestContributor = recentContributors
            .FirstOrDefault(pair => pair.Key is not KillEnvironmentSource);

        var primary = impulse;
        if (primary is KillEnvironmentSource && strongestContributor.Key != null)
            primary = strongestContributor.Key;

        if (primary is KillEnvironmentSource && recentContributors.Length > 0)
            primary = recentContributors[0].Key;

        component.DamageLedger.TryGetValue(primary, out var primaryRecord);
        var primaryDamage = primaryRecord?.TotalDamage ?? FixedPoint2.Zero;
        var assistDamageThreshold = FixedPoint2.New(Math.Max(
            component.MinimumAssistDamage.Float(),
            primaryDamage.Float() * component.AssistFractionThreshold));

        var assists = recentContributors
            .Where(pair => pair.Key != primary)
            .Where(pair => pair.Key is not KillEnvironmentSource)
            .Where(pair => !IsSourceEntity(pair.Key, uid))
            .Where(pair => pair.Value.TotalDamage >= assistDamageThreshold)
            .Select(pair => pair.Key)
            .ToArray();

        var suicide = IsSourceEntity(primary, uid);
        return new KillAttributionResult(primary, assists, suicide);
    }

    private void ApplyHealing(KillTrackerComponent component, DamageSpecifier delta)
    {
        if (component.DamageLedger.Count == 0)
            return;

        var healAmount = GetHealingAmount(delta);
        if (healAmount <= FixedPoint2.Zero)
            return;

        var totalTrackedDamage = FixedPoint2.Zero;
        foreach (var record in component.DamageLedger.Values)
        {
            if (record.TotalDamage > FixedPoint2.Zero)
                totalTrackedDamage += record.TotalDamage;
        }

        if (totalTrackedDamage <= FixedPoint2.Zero)
            return;

        var healFraction = Math.Clamp(healAmount.Float() / totalTrackedDamage.Float(), 0f, 1f);
        var keys = component.DamageLedger.Keys.ToArray();

        foreach (var key in keys)
        {
            var record = component.DamageLedger[key];
            record.TotalDamage = FixedPoint2.New(record.TotalDamage.Float() * (1f - healFraction));
            if (record.TotalDamage <= FixedPoint2.Zero)
                component.DamageLedger.Remove(key);
        }
    }

    private void PruneExpiredContributors(KillTrackerComponent component)
    {
        var expiry = TimeSpan.FromSeconds(Math.Max(1f, component.AssistWindowSeconds));
        var now = _timing.CurTime;
        var expired = component.DamageLedger
            .Where(pair => now - pair.Value.LastDamageTime > expiry || pair.Value.TotalDamage <= FixedPoint2.Zero)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in expired)
        {
            component.DamageLedger.Remove(key);
        }
    }

    private FixedPoint2 GetPositiveDamage(DamageSpecifier delta)
    {
        var total = FixedPoint2.Zero;
        foreach (var damage in delta.DamageDict.Values)
        {
            if (damage > FixedPoint2.Zero)
                total += damage;
        }

        return total;
    }

    private FixedPoint2 GetHealingAmount(DamageSpecifier delta)
    {
        var total = FixedPoint2.Zero;
        foreach (var damage in delta.DamageDict.Values)
        {
            if (damage < FixedPoint2.Zero)
                total += -damage;
        }

        return total;
    }

    private KillSource GetKillSource(EntityUid? sourceEntity)
    {
        if (sourceEntity == null)
            return new KillEnvironmentSource();

        var resolved = sourceEntity.Value;
        if (_attackerResolver.TryResolveResponsibleEntity(sourceEntity.Value, out var responsible))
            resolved = responsible;

        if (TryComp<ActorComponent>(resolved, out var actor))
            return new KillPlayerSource(actor.PlayerSession.UserId);

        if (HasComp<HTNComponent>(resolved))
            return new KillNpcSource(resolved);

        return new KillEnvironmentSource();
    }

    private bool IsSourceEntity(KillSource source, EntityUid uid)
    {
        if (source is KillNpcSource npc)
            return npc.NpcEnt == uid;

        if (source is not KillPlayerSource player)
            return false;

        return player.PlayerId == CompOrNull<ActorComponent>(uid)?.PlayerSession.UserId;
    }

    private sealed record KillAttributionResult(KillSource Primary, KillSource[] Assists, bool Suicide);
}

/// <summary>
/// Raised when a tracked entity enters critical and a downing source can be attributed.
/// </summary>
[ByRefEvent]
public readonly record struct AttributedDownedEvent(EntityUid Entity, KillSource Primary, KillSource[] Assists, bool Suicide);

/// <summary>
/// Raised when a tracked entity dies and a killing source can be attributed.
/// </summary>
[ByRefEvent]
public readonly record struct AttributedKilledEvent(EntityUid Entity, KillSource Primary, KillSource[] Assists, bool Suicide);

/// <summary>
/// Compatibility kill event for systems that still expect a single optional assist.
/// </summary>
[ByRefEvent]
public readonly record struct KillReportedEvent(EntityUid Entity, KillSource Primary, KillSource? Assist, bool Suicide);
