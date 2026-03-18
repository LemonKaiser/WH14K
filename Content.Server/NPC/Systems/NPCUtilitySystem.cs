using System;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Hands.Systems;
using Content.Server.NPC.Queries;
using Content.Server.NPC.Queries.Considerations;
using Content.Server.NPC.Queries.Curves;
using Content.Server.NPC.Queries.Queries;
using Content.Server._WH40K.Objectives.Components;
using Content.Server.Nutrition.Components;
using Content.Shared.CCVar;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Fluids.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.NPC.Components;
using Content.Shared.Storage.Components;
using Content.Shared.Stealth.Components;
using Content.Shared.Stunnable;
using Content.Shared.Tools.Systems;
using Content.Shared.Turrets;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Whitelist;
using Microsoft.Extensions.ObjectPool;
using Robust.Server.Containers;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Content.Shared.Atmos.Components;
using System.Numerics;
using System.Linq;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Temperature.Components;

namespace Content.Server.NPC.Systems;

/// <summary>
/// Handles utility queries for NPCs.
/// </summary>
public sealed class NPCUtilitySystem : EntitySystem
{
    private const float TurretStealthDetectionDivisor = 3f;
    private const float TurretStealthMinimumRange = 1f;

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly NPCBenchmarkSystem _bench = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IngestionSystem _ingestion = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly WeldableSystem _weldable = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly MobThresholdSystem _thresholdSystem = default!;
    [Dependency] private readonly TurretTargetSettingsSystem _turretTargetSettings = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;

    private EntityQuery<PuddleComponent> _puddleQuery;
    private EntityQuery<NpcFactionMemberComponent> _factionQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private ObjectPool<HashSet<EntityUid>> _entPool =
        new DefaultObjectPool<HashSet<EntityUid>>(new SetPolicy<EntityUid>(), 256);

    // Temporary caches.
    private List<EntityUid> _entityList = new();
    private HashSet<Entity<IComponent>> _entitySet = new();
    private readonly Dictionary<ComponentQuery, CompiledComponentQuery> _compiledComponentQueries = new();
    private readonly Dictionary<ComponentFilter, Type[]> _compiledComponentFilters = new();

    private const int UtilityCacheMaxEntries = 8192;
    private const int UtilitySpatialCacheMaxEntries = 4096;
    private const int UtilityHostilesCacheMaxEntries = 4096;
    private const int UtilityLosCacheMaxEntries = 16384;
    private const int UtilityWaveCoordinationCacheMaxEntries = 2048;
    private readonly Dictionary<UtilityCacheKey, UtilityCacheEntry> _queryCache = new(512);
    private readonly Dictionary<SpatialCacheKey, SpatialCacheEntry> _spatialCache = new(512);
    private readonly Dictionary<HostilesCacheKey, HostilesCacheEntry> _hostilesCache = new(512);
    private readonly Dictionary<LosCacheKey, LosCacheEntry> _losCache = new(1024);
    private readonly Dictionary<WaveCoordinationKey, WaveCoordinationEntry> _waveCoordination = new(256);
    private readonly Dictionary<string, bool> _cacheableProto = new(StringComparer.Ordinal);
    private float _queryCacheTtlSeconds = 0.20f;
    private bool _spatialCacheEnabled = true;
    private float _spatialCacheTtlSeconds = 0.12f;
    private float _spatialCacheCellSize = 6f;
    private float _hostilesCacheTtlSeconds = 0.08f;
    private bool _losCacheEnabled = true;
    private float _losCacheTtlSeconds = 0.15f;
    private float _losCacheMoveThreshold = 0.75f;
    private int _losBudgetPerTick = 512;
    private GameTick _losBudgetTick = GameTick.Zero;
    private int _losBudgetUsed;
    private bool _waveCoordinationEnabled = true;
    private float _waveCoordinationTtlSeconds = 1.20f;
    private float _waveCoordinationCellSize = 10f;
    private float _waveCoordinationOrderedBonus = 0.18f;

    public override void Initialize()
    {
        base.Initialize();
        _puddleQuery = GetEntityQuery<PuddleComponent>();
        _factionQuery = GetEntityQuery<NpcFactionMemberComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        Subs.CVar(_cfg, CCVars.NPCUtilityCacheTtlSeconds, value =>
        {
            _queryCacheTtlSeconds = MathF.Max(0f, value);
            _queryCache.Clear();
        }, true);
        Subs.CVar(_cfg, CCVars.NPCUtilitySpatialCacheEnabled, value =>
        {
            _spatialCacheEnabled = value;
            _spatialCache.Clear();
            _hostilesCache.Clear();
        }, true);
        Subs.CVar(_cfg, CCVars.NPCUtilitySpatialCacheTtlSeconds, value =>
        {
            _spatialCacheTtlSeconds = MathF.Max(0f, value);
            _spatialCache.Clear();
        }, true);
        Subs.CVar(_cfg, CCVars.NPCUtilitySpatialCacheCellSize, value =>
        {
            _spatialCacheCellSize = MathF.Max(1f, value);
            _spatialCache.Clear();
            _hostilesCache.Clear();
        }, true);
        Subs.CVar(_cfg, CCVars.NPCUtilityHostilesCacheTtlSeconds, value =>
        {
            _hostilesCacheTtlSeconds = MathF.Max(0f, value);
            _hostilesCache.Clear();
        }, true);
        Subs.CVar(_cfg, CCVars.NPCUtilityLosCacheEnabled, value =>
        {
            _losCacheEnabled = value;
            _losCache.Clear();
        }, true);
        Subs.CVar(_cfg, CCVars.NPCUtilityLosCacheTtlSeconds, value =>
        {
            _losCacheTtlSeconds = MathF.Max(0f, value);
            _losCache.Clear();
        }, true);
        Subs.CVar(_cfg, CCVars.NPCUtilityLosCacheMoveThreshold, value => _losCacheMoveThreshold = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, CCVars.NPCUtilityLosBudgetPerTick, value => _losBudgetPerTick = Math.Max(0, value), true);
        Subs.CVar(_cfg, CCVars.NPCUtilityWaveCoordinationEnabled, value =>
        {
            _waveCoordinationEnabled = value;
            _waveCoordination.Clear();
        }, true);
        Subs.CVar(_cfg, CCVars.NPCUtilityWaveCoordinationTtlSeconds, value =>
        {
            _waveCoordinationTtlSeconds = MathF.Max(0f, value);
            _waveCoordination.Clear();
        }, true);
        Subs.CVar(_cfg, CCVars.NPCUtilityWaveCoordinationCellSize, value =>
        {
            _waveCoordinationCellSize = MathF.Max(1f, value);
            _waveCoordination.Clear();
        }, true);
        Subs.CVar(_cfg, CCVars.NPCUtilityWaveCoordinationOrderedBonus, value =>
            _waveCoordinationOrderedBonus = Math.Clamp(value, 0f, 1f), true);
    }

    /// <summary>
    /// Runs the UtilityQueryPrototype and returns the best-matching entities.
    /// </summary>
    /// <param name="bestOnly">Should we only return the entity with the best score.</param>
    public UtilityResult GetEntities(
        NPCBlackboard blackboard,
        string proto,
        bool bestOnly = true)
    {
        var stage = _bench.Detailed ? $"npc.utility.query.{proto}" : "npc.utility.query";
        using var benchScope = _bench.Measure(stage);
        var weh = _proto.Index<UtilityQueryPrototype>(proto);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var now = _timing.CurTime;
        var cacheEnabled = _queryCacheTtlSeconds > 0f && IsCacheable(proto, weh);
        var cacheKey = new UtilityCacheKey(owner, proto, bestOnly);

        if (cacheEnabled)
        {
            if (_queryCache.TryGetValue(cacheKey, out var cached))
            {
                if (cached.ExpiresAt > now)
                {
                    _bench.RecordCount("npc.utility.cache.hit", 1);
                    return cached.Result;
                }

                _queryCache.Remove(cacheKey);
            }

            _bench.RecordCount("npc.utility.cache.miss", 1);
        }

        // TODO: PickHostilesop or whatever needs to juse be UtilityQueryOperator

        var ents = _entPool.Get();

        foreach (var query in weh.Query)
        {
            switch (query)
            {
                case UtilityQueryFilter filter:
                    Filter(blackboard, ents, filter);
                    break;
                default:
                    Add(blackboard, ents, query);
                    break;
            }
        }

        if (ents.Count == 0)
        {
            _bench.RecordCount("npc.utility.empty_result", 1);
            _entPool.Return(ents);
            if (cacheEnabled)
                CacheResult(cacheKey, UtilityResult.Empty, now);
            return UtilityResult.Empty;
        }

        var coordinationActive = IsWaveCoordinationApplicable(owner, blackboard, proto);
        var preserveObjectiveOrder = TryGetReservedObjectiveOrderTarget(blackboard, out var reservedObjectiveTarget);
        var orderedTarget = EntityUid.Invalid;
        if (coordinationActive)
        {
            if (preserveObjectiveOrder)
            {
                orderedTarget = reservedObjectiveTarget;
            }
            else if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out orderedTarget, EntityManager) ||
                     !ents.Contains(orderedTarget))
            {
                if (TryGetWaveSharedTarget(owner, ents, now, out var sharedTarget))
                {
                    orderedTarget = sharedTarget;
                    if (!blackboard.ReadOnly)
                        blackboard.SetValue(NPCBlackboard.CurrentOrderedTarget, sharedTarget);

                    _bench.RecordCount("npc.utility.coordination.shared_target_hit", 1);
                }
                else if (!blackboard.ReadOnly)
                {
                    blackboard.Remove<EntityUid>(NPCBlackboard.CurrentOrderedTarget);
                }
            }
        }

        var allResults = bestOnly ? null : new Dictionary<EntityUid, float>();
        var bestEntity = EntityUid.Invalid;
        var highestScore = 0f;
        var invConsiderations = weh.Considerations.Count > 0 ? 1f / weh.Considerations.Count : 1f;

        foreach (var ent in ents)
        {
            if (!bestOnly && allResults != null && allResults.Count >= weh.Limit)
                break;

            var score = 1f;

            foreach (var con in weh.Considerations)
            {
                var conScore = GetScore(blackboard, ent, con);
                var curve = con.Curve;
                var curveScore = GetScore(curve, conScore);

                var adjusted = GetAdjustedScore(curveScore, invConsiderations);
                score *= adjusted;

                // If the score is too low OR we only care about best entity then early out.
                // Due to the adjusted score only being able to decrease it can never exceed the highest from here.
                if (score <= 0f || bestOnly && score <= highestScore)
                {
                    break;
                }
            }

            if (score <= 0f)
                continue;

            if (coordinationActive && orderedTarget != EntityUid.Invalid && ent == orderedTarget)
            {
                score = Math.Clamp(score + _waveCoordinationOrderedBonus, 0f, 1f);
                _bench.RecordCount("npc.utility.coordination.ordered_boost", 1);
            }

            if (bestEntity == EntityUid.Invalid || score > highestScore)
            {
                highestScore = score;
                bestEntity = ent;
            }

            if (bestOnly)
                continue;

            allResults!.Add(ent, score);
        }

        UtilityResult result;
        var scored = 0;

        if (bestOnly)
        {
            if (bestEntity == EntityUid.Invalid)
            {
                result = UtilityResult.Empty;
            }
            else
            {
                result = new UtilityResult(new Dictionary<EntityUid, float>(1)
                {
                    [bestEntity] = highestScore
                });
                scored = 1;
            }
        }
        else
        {
            scored = allResults!.Count;
            result = new UtilityResult(allResults);
        }

        _bench.RecordCount("npc.utility.candidates", ents.Count);
        _bench.RecordCount("npc.utility.scored", scored);
        blackboard.Remove<EntityUid>(NPCBlackboard.UtilityTarget);

        if (coordinationActive)
        {
            if (bestEntity != EntityUid.Invalid)
            {
                StoreWaveSharedTarget(owner, bestEntity, now);
                if (!blackboard.ReadOnly && !preserveObjectiveOrder)
                    blackboard.SetValue(NPCBlackboard.CurrentOrderedTarget, bestEntity);
            }
            else if (!blackboard.ReadOnly && !preserveObjectiveOrder)
            {
                blackboard.Remove<EntityUid>(NPCBlackboard.CurrentOrderedTarget);
            }
        }

        _entPool.Return(ents);
        if (cacheEnabled)
            CacheResult(cacheKey, result, now);
        return result;
    }

    private void CacheResult(in UtilityCacheKey key, UtilityResult result, TimeSpan now)
    {
        if (_queryCache.Count >= UtilityCacheMaxEntries && !_queryCache.ContainsKey(key))
            _queryCache.Clear();

        _queryCache[key] = new UtilityCacheEntry(result, now + TimeSpan.FromSeconds(_queryCacheTtlSeconds));
    }

    private bool IsCacheable(string proto, UtilityQueryPrototype query)
    {
        if (_cacheableProto.TryGetValue(proto, out var cacheable))
            return cacheable;

        // Cache only hostiles-based queries; per-owner component/item scans tend to miss and add overhead.
        cacheable = query.Query.Any(x => x is NearbyHostilesQuery);
        _cacheableProto[proto] = cacheable;
        return cacheable;
    }

    private bool IsWaveCoordinationApplicable(EntityUid owner, NPCBlackboard blackboard, string proto)
    {
        if (!_waveCoordinationEnabled || _waveCoordinationTtlSeconds <= 0f)
            return false;

        if (!blackboard.TryGetValue<bool>(NPCBlackboard.WaveCoordinationEnabled, out var enabled, EntityManager) || !enabled)
            return false;

        if (proto != "NearbyGunTargets" &&
            proto != "NearbyMeleeTargets" &&
            proto != "OrderedTargets")
        {
            return false;
        }

        return _xformQuery.HasComponent(owner) && _factionQuery.HasComponent(owner);
    }

    private bool TryGetReservedObjectiveOrderTarget(NPCBlackboard blackboard, out EntityUid orderedTarget)
    {
        orderedTarget = EntityUid.Invalid;

        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out orderedTarget, EntityManager) ||
            orderedTarget == EntityUid.Invalid ||
            Deleted(orderedTarget))
        {
            return false;
        }

        if (!TryComp(orderedTarget, out WH40KObjectiveComponent? objective) ||
            objective == null ||
            objective.Destroyed ||
            objective.Destroying ||
            string.IsNullOrWhiteSpace(objective.TeamId))
        {
            orderedTarget = EntityUid.Invalid;
            return false;
        }

        return true;
    }

    private bool TryGetWaveSharedTarget(
        EntityUid owner,
        HashSet<EntityUid> candidates,
        TimeSpan now,
        out EntityUid target)
    {
        target = EntityUid.Invalid;

        if (!TryBuildWaveCoordinationKey(owner, out var key))
            return false;

        if (!_waveCoordination.TryGetValue(key, out var cached))
            return false;

        if (cached.ExpiresAt <= now || Deleted(cached.Target) || !candidates.Contains(cached.Target))
        {
            _waveCoordination.Remove(key);
            return false;
        }

        target = cached.Target;
        return true;
    }

    private void StoreWaveSharedTarget(EntityUid owner, EntityUid target, TimeSpan now)
    {
        if (!TryBuildWaveCoordinationKey(owner, out var key))
            return;

        if (_waveCoordination.Count >= UtilityWaveCoordinationCacheMaxEntries && !_waveCoordination.ContainsKey(key))
            _waveCoordination.Clear();

        _waveCoordination[key] = new WaveCoordinationEntry(target, now + TimeSpan.FromSeconds(_waveCoordinationTtlSeconds));
        _bench.RecordCount("npc.utility.coordination.shared_target_assign", 1);
    }

    private bool TryBuildWaveCoordinationKey(EntityUid owner, out WaveCoordinationKey key)
    {
        key = default;

        if (!_xformQuery.TryGetComponent(owner, out var ownerXform) ||
            !_factionQuery.TryGetComponent(owner, out var ownerFaction))
        {
            return false;
        }

        if (ownerFaction.Factions.Count == 0)
            return false;

        var map = _transform.GetMapCoordinates((owner, ownerXform));
        var cellX = (int) MathF.Floor(map.Position.X / _waveCoordinationCellSize);
        var cellY = (int) MathF.Floor(map.Position.Y / _waveCoordinationCellSize);
        var factionSignature = GetFactionSignature(ownerFaction);

        key = new WaveCoordinationKey(map.MapId, cellX, cellY, factionSignature);
        return true;
    }

    private static int GetFactionSignature(NpcFactionMemberComponent faction)
    {
        var hash = 17;
        foreach (var group in faction.Factions.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            hash = HashCode.Combine(hash, group.Id.GetHashCode(StringComparison.Ordinal));
        }

        return hash;
    }

    private float GetScore(IUtilityCurve curve, float conScore)
    {
        switch (curve)
        {
            case BoolCurve:
                return conScore > 0f ? 1f : 0f;
            case InverseBoolCurve:
                return conScore.Equals(0f) ? 1f : 0f;
            case PresetCurve presetCurve:
                return GetScore(_proto.Index<UtilityCurvePresetPrototype>(presetCurve.Preset).Curve, conScore);
            case QuadraticCurve quadraticCurve:
            {
                var x = conScore - quadraticCurve.XOffset;
                var exponent = quadraticCurve.Exponent;
                float powered;

                if (MathHelper.CloseTo(exponent, 1f))
                {
                    powered = x;
                }
                else if (MathHelper.CloseTo(exponent, 0.5f))
                {
                    powered = MathF.Sqrt(MathF.Max(0f, x));
                }
                else if (MathHelper.CloseTo(exponent, 2f))
                {
                    powered = x * x;
                }
                else
                {
                    powered = MathF.Pow(x, exponent);
                }

                return Math.Clamp(quadraticCurve.Slope * powered + quadraticCurve.YOffset, 0f, 1f);
            }
            default:
                throw new NotImplementedException();
        }
    }

    private float GetScore(NPCBlackboard blackboard, EntityUid targetUid, UtilityConsideration consideration)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        switch (consideration)
        {
            case FoodValueCon:
            {
                // do we have a mouth available? Is the food item opened?
                if (!_ingestion.CanConsume(owner, targetUid))
                    return 0f;

                var avoidBadFood = !HasComp<IgnoreBadFoodComponent>(owner);

                // only eat when hungry or if it will eat anything
                if (TryComp<HungerComponent>(owner, out var hunger) && hunger.CurrentThreshold > HungerThreshold.Okay && avoidBadFood)
                    return 0f;

                // no mouse don't eat the uranium-235
                if (avoidBadFood && HasComp<BadFoodComponent>(targetUid))
                    return 0f;

                var nutrition = _ingestion.TotalNutrition(targetUid, owner);
                if (nutrition == 0.0f)
                    return 0f;

                return 1f;
            }
            case DrinkValueCon:
            {
                // can't drink closed drinks and can't drink with a mask on...
                if (!_ingestion.CanConsume(owner, targetUid))
                    return 0f;

                // only drink when thirsty
                if (TryComp<ThirstComponent>(owner, out var thirst) && thirst.CurrentThirstThreshold > ThirstThreshold.Okay)
                    return 0f;

                // no janicow don't drink the blood puddle
                if (HasComp<BadDrinkComponent>(targetUid))
                    return 0f;

                // needs to have something that will satiate thirst, mice wont try to drink 100% pure mutagen.
                // We don't check if the solution is metabolizable cause all drinks should be currently.
                // If that changes then simply use the other overflow.
                var hydration = _ingestion.TotalHydration(targetUid);
                if (hydration <= 1.0f)
                    return 0f;

                return 1f;
            }
            case OrderedTargetCon:
            {
                if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out var orderedTarget, EntityManager))
                    return 0f;

                if (targetUid != orderedTarget)
                    return 0f;

                return 1f;
            }
            case TargetAccessibleCon:
            {
                if (_container.TryGetContainingContainer(targetUid, out var container))
                {
                    if (container.Owner == owner)
                        return 0f;

                    if (TryComp<EntityStorageComponent>(container.Owner, out var storageComponent))
                    {
                        if (storageComponent is { Open: false } && _weldable.IsWelded(container.Owner))
                        {
                            return 0.0f;
                        }
                    }
                    else
                    {
                        // If we're in a container (e.g. held or whatever) then we probably can't get it. Only exception
                        // Is a locker / crate
                        // TODO: Some mobs can break it so consider that.
                        return 0.0f;
                    }
                }

                // TODO: Pathfind there, though probably do it in a separate con.
                return 1f;
            }
            case TargetAmmoMatchesCon:
            {
                if (!blackboard.TryGetValue(NPCBlackboard.ActiveHand, out string? activeHand, EntityManager) ||
                    !_hands.TryGetHeldItem(owner, activeHand, out var heldEntity) ||
                    !TryComp<BallisticAmmoProviderComponent>(heldEntity, out var heldGun))
                {
                    return 0f;
                }

                if (_whitelistSystem.IsWhitelistFailOrNull(heldGun.Whitelist, targetUid))
                {
                    return 0f;
                }

                return 1f;
            }
            case TargetDistanceCon:
            {
                var baseRadius = blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(EntityManager), EntityManager);
                var radius = GetEffectiveVisionRadiusForTarget(owner, targetUid, baseRadius);

                if (radius <= 0f || !TryGetDistanceSquared(owner, targetUid, out var distanceSquared))
                    return 0f;

                var distance = MathF.Sqrt(distanceSquared);
                return Math.Clamp(distance / radius, 0f, 1f);
            }
            case TargetAmmoCon:
            {
                if (!HasComp<GunComponent>(targetUid))
                    return 0f;

                var ev = new GetAmmoCountEvent();
                RaiseLocalEvent(targetUid, ref ev);

                if (ev.Count == 0)
                    return 0f;

                // Wat
                if (ev.Capacity == 0)
                    return 1f;

                return (float) ev.Count / ev.Capacity;
            }
            case TargetHealthCon con:
            {
                if (!TryComp(targetUid, out DamageableComponent? damage))
                    return 0f;
                var totalDamage = _damageable.GetTotalDamage((targetUid, damage));
                if (con.TargetState != MobState.Invalid && _thresholdSystem.TryGetPercentageForState(targetUid, con.TargetState, totalDamage, out var percentage))
                    return Math.Clamp((float)(1 - percentage), 0f, 1f);
                if (_thresholdSystem.TryGetIncapPercentage(targetUid, totalDamage, out var incapPercentage))
                    return Math.Clamp((float)(1 - incapPercentage), 0f, 1f);
                return 0f;
            }
            case TargetInLOSCon:
            {
                var baseRadius = blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(EntityManager), EntityManager);
                var radius = GetEffectiveVisionRadiusForTarget(owner, targetUid, baseRadius);

                return IsInLosCached(owner, targetUid, radius + 0.5f) ? 1f : 0f;
            }
            case TargetInLOSOrCurrentCon:
            {
                var baseRadius = blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(EntityManager), EntityManager);
                var radius = GetEffectiveVisionRadiusForTarget(owner, targetUid, baseRadius);
                const float bufferRange = 0.5f;
                var range = radius + bufferRange;

                if (blackboard.TryGetValue<EntityUid>("Target", out var currentTarget, EntityManager) &&
                    currentTarget == targetUid &&
                    TryGetDistanceSquared(owner, targetUid, out var distanceSquared) &&
                    distanceSquared <= range * range)
                {
                    return 1f;
                }

                return IsInLosCached(owner, targetUid, range) ? 1f : 0f;
            }
            case TargetIsAliveCon:
            {
                return _mobState.IsAlive(targetUid) ? 1f : 0f;
            }
            case TargetIsCritCon:
            {
                return _mobState.IsCritical(targetUid) ? 1f : 0f;
            }
            case TargetIsDeadCon:
            {
                return _mobState.IsDead(targetUid) ? 1f : 0f;
            }
            case TargetMeleeCon:
            {
                if (TryComp<MeleeWeaponComponent>(targetUid, out var melee))
                {
                    return melee.Damage.GetTotal().Float() * melee.AttackRate / 100f;
                }

                return 0f;
            }
            case TargetOnFireCon:
                {
                    if (TryComp(targetUid, out FlammableComponent? fire) && fire.OnFire)
                        return 1f;
                    return 0f;
                }
            case TargetIsStunnedCon:
                {
                    return HasComp<StunnedComponent>(targetUid) ? 1f : 0f;
                }
            case TurretTargetingCon:
                {
                    if (!TryComp<TurretTargetSettingsComponent>(owner, out var turretTargetSettings) ||
                        _turretTargetSettings.EntityIsTargetForTurret((owner, turretTargetSettings), targetUid))
                        return 1f;

                    return 0f;
                }
            case TargetLowTempCon con:
                {
                    if (!TryComp<TemperatureComponent>(targetUid, out var temperature))
                        return 0f;

                    return temperature.CurrentTemperature <= con.MinTemp ? 1f : 0f;
                }
            default:
                throw new NotImplementedException();
        }
    }

    private static float GetAdjustedScore(float score, float inverseConsiderationCount)
    {
        /*
        * Now using the geometric mean
        * for n scores you take the n-th root of the scores multiplied
        * e.g. a, b, c scores you take Math.Pow(a * b * c, 1/3)
        * To get the ACTUAL geometric mean at any one stage you'd need to divide by the running consideration count
        * however, the downside to this is it will fluctuate up and down over time.
        * For our purposes if we go below the minimum threshold we want to cut it off, thus we take a
        * "running geometric mean" which can only ever go down (and by the final value will equal the actual geometric mean).
        */

        if (score <= 0f)
            return 0f;

        if (score >= 1f)
            return 1f;

        var adjusted = MathF.Pow(score, inverseConsiderationCount);
        return Math.Clamp(adjusted, 0f, 1f);
    }

    private float GetEffectiveVisionRadiusForTarget(EntityUid owner, EntityUid targetUid, float baseRadius)
    {
        if (!HasComp<TurretTargetSettingsComponent>(owner))
            return baseRadius;

        if (!TryComp<StealthComponent>(targetUid, out var stealth) || !stealth.Enabled)
            return baseRadius;

        return MathF.Max(TurretStealthMinimumRange, baseRadius / TurretStealthDetectionDivisor);
    }

    private void Add(NPCBlackboard blackboard, HashSet<EntityUid> entities, UtilityQuery query)
    {
        using var benchScope = _bench.Measure("npc.utility.add");

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var vision = blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(EntityManager), EntityManager);

        switch (query)
        {
            case ComponentQuery compQuery:
            {
                if (compQuery.Components.Count == 0)
                    return;

                var mapPos = _transform.GetMapCoordinates(owner, xform: _xformQuery.GetComponent(owner));
                var ownerWorldPos = _transform.GetWorldPosition(_xformQuery.GetComponent(owner));
                var visionSquared = vision * vision;
                var compiled = GetOrCompileComponentQuery(compQuery);
                if (compiled.PrimaryType == null)
                    return;

                var candidates = GetComponentCandidates(mapPos, vision, compiled.PrimaryType);
                foreach (var ent in candidates)
                {
                    if (ent == owner)
                        continue;

                    if (!_xformQuery.TryGetComponent(ent, out var candidateXform) ||
                        candidateXform.MapID != mapPos.MapId ||
                        (_transform.GetWorldPosition(candidateXform) - ownerWorldPos).LengthSquared() > visionSquared)
                    {
                        continue;
                    }

                    var othersFound = true;

                    foreach (var compOther in compiled.OtherTypes)
                    {
                        if (!HasComp(ent, compOther))
                        {
                            othersFound = false;
                            break;
                        }
                    }

                    if (!othersFound)
                        continue;

                    entities.Add(ent);
                }

                _bench.RecordCount("npc.utility.component_query.matches", entities.Count);

                break;
            }
            case InventoryQuery:
            {
                if (!_inventory.TryGetContainerSlotEnumerator(owner, out var enumerator))
                    break;

                while (enumerator.MoveNext(out var slot))
                {
                    foreach (var child in slot.ContainedEntities)
                    {
                        RecursiveAdd(child, entities);
                    }
                }

                _bench.RecordCount("npc.utility.inventory_query.matches", entities.Count);

                break;
            }
            case NearbyHostilesQuery:
            {
                var ownerXform = Transform(owner);
                var ownerPos = _transform.GetWorldPosition(ownerXform);
                var hostiles = GetNearbyHostiles(owner, ownerXform.MapID, mapPos: _transform.GetMapCoordinates(owner, ownerXform), vision: vision);

                foreach (var ent in hostiles)
                {
                    var effectiveRadius = GetEffectiveVisionRadiusForTarget(owner, ent, vision);
                    if (effectiveRadius < vision &&
                        TryComp(ent, out TransformComponent? targetXform) &&
                        targetXform.MapID == ownerXform.MapID &&
                        (_transform.GetWorldPosition(targetXform) - ownerPos).LengthSquared() > effectiveRadius * effectiveRadius)
                    {
                        continue;
                    }

                    entities.Add(ent);
                }
                _bench.RecordCount("npc.utility.hostile_query.matches", entities.Count);
                break;
            }
            default:
                throw new NotImplementedException();
        }
    }

    private CompiledComponentQuery GetOrCompileComponentQuery(ComponentQuery query)
    {
        if (_compiledComponentQueries.TryGetValue(query, out var compiled))
            return compiled;

        if (query.Components.Count == 0)
        {
            compiled = new CompiledComponentQuery(null, Array.Empty<Type>());
            _compiledComponentQueries[query] = compiled;
            return compiled;
        }

        Type? primary = null;
        var others = new Type[Math.Max(0, query.Components.Count - 1)];
        var i = 0;

        foreach (var component in query.Components.Values)
        {
            var type = component.Component.GetType();
            if (primary == null)
            {
                primary = type;
                continue;
            }

            others[i] = type;
            i++;
        }

        compiled = new CompiledComponentQuery(primary, others);
        _compiledComponentQueries[query] = compiled;
        return compiled;
    }

    private Type[] GetOrCompileComponentFilter(ComponentFilter filter)
    {
        if (_compiledComponentFilters.TryGetValue(filter, out var types))
            return types;

        types = new Type[filter.Components.Count];
        var i = 0;
        foreach (var component in filter.Components.Values)
        {
            types[i] = component.Component.GetType();
            i++;
        }

        _compiledComponentFilters[filter] = types;
        return types;
    }

    private IReadOnlyList<EntityUid> GetComponentCandidates(MapCoordinates mapPos, float vision, Type primaryType)
    {
        if (!_spatialCacheEnabled || _spatialCacheTtlSeconds <= 0f)
            return BuildComponentCandidates(mapPos, vision, primaryType);

        var now = _timing.CurTime;
        var cellX = GetCell(mapPos.Position.X);
        var cellY = GetCell(mapPos.Position.Y);
        var radiusBucket = QuantizeRange(vision, 0.5f);
        var key = new SpatialCacheKey(
            mapPos.MapId,
            cellX,
            cellY,
            radiusBucket,
            primaryType);

        if (_spatialCache.TryGetValue(key, out var cached))
        {
            if (cached.ExpiresAt > now)
            {
                _bench.RecordCount("npc.utility.spatial_cache.hit", 1);
                return cached.Entities;
            }

            _spatialCache.Remove(key);
        }

        _bench.RecordCount("npc.utility.spatial_cache.miss", 1);
        var cellCenter = new Vector2(
            (cellX + 0.5f) * _spatialCacheCellSize,
            (cellY + 0.5f) * _spatialCacheCellSize);
        var queryMapPos = new MapCoordinates(cellCenter, mapPos.MapId);
        var queryRange = vision + (_spatialCacheCellSize * 0.75f);
        var entities = BuildComponentCandidates(queryMapPos, queryRange, primaryType);

        if (_spatialCache.Count >= UtilitySpatialCacheMaxEntries && !_spatialCache.ContainsKey(key))
            _spatialCache.Clear();

        _spatialCache[key] = new SpatialCacheEntry(entities, now + TimeSpan.FromSeconds(_spatialCacheTtlSeconds));
        return entities;
    }

    private List<EntityUid> BuildComponentCandidates(MapCoordinates mapPos, float vision, Type primaryType)
    {
        _entitySet.Clear();
        _lookup.GetEntitiesInRange(primaryType, mapPos, vision, _entitySet);
        var entities = new List<EntityUid>(_entitySet.Count);

        foreach (var component in _entitySet)
        {
            entities.Add(component.Owner);
        }

        return entities;
    }

    private IReadOnlyList<EntityUid> GetNearbyHostiles(EntityUid owner, MapId mapId, MapCoordinates mapPos, float vision)
    {
        if (_hostilesCacheTtlSeconds <= 0f || !_spatialCacheEnabled)
            return BuildHostileCandidates(owner, vision);

        var now = _timing.CurTime;
        var key = new HostilesCacheKey(
            owner,
            mapId,
            GetCell(mapPos.Position.X),
            GetCell(mapPos.Position.Y),
            QuantizeRange(vision, 0.5f));

        if (_hostilesCache.TryGetValue(key, out var cached))
        {
            if (cached.ExpiresAt > now)
            {
                _bench.RecordCount("npc.utility.hostiles_cache.hit", 1);
                return cached.Entities;
            }

            _hostilesCache.Remove(key);
        }

        _bench.RecordCount("npc.utility.hostiles_cache.miss", 1);
        var entities = BuildHostileCandidates(owner, vision);

        if (_hostilesCache.Count >= UtilityHostilesCacheMaxEntries && !_hostilesCache.ContainsKey(key))
            _hostilesCache.Clear();

        _hostilesCache[key] = new HostilesCacheEntry(entities, now + TimeSpan.FromSeconds(_hostilesCacheTtlSeconds));
        return entities;
    }

    private List<EntityUid> BuildHostileCandidates(EntityUid owner, float vision)
    {
        var entities = new List<EntityUid>();
        foreach (var hostile in _npcFaction.GetNearbyHostiles(owner, vision))
        {
            entities.Add(hostile);
        }

        return entities;
    }

    private bool IsInLosCached(EntityUid owner, EntityUid target, float range)
    {
        if (!_losCacheEnabled || _losCacheTtlSeconds <= 0f)
            return _examine.InRangeUnOccluded(owner, target, range, null);

        if (!_xformQuery.TryGetComponent(owner, out var ownerXform) ||
            !_xformQuery.TryGetComponent(target, out var targetXform))
        {
            return false;
        }

        if (ownerXform.MapID != targetXform.MapID)
            return false;

        var now = _timing.CurTime;
        var ownerPos = _transform.GetWorldPosition(ownerXform);
        var targetPos = _transform.GetWorldPosition(targetXform);
        var key = new LosCacheKey(owner, target, QuantizeRange(range, 0.5f));
        var moveThresholdSquared = _losCacheMoveThreshold * _losCacheMoveThreshold;

        if (_losCache.TryGetValue(key, out var cached))
        {
            if (cached.ExpiresAt > now &&
                cached.MapId == ownerXform.MapID &&
                (ownerPos - cached.OwnerPosition).LengthSquared() <= moveThresholdSquared &&
                (targetPos - cached.TargetPosition).LengthSquared() <= moveThresholdSquared)
            {
                _bench.RecordCount("npc.utility.los_cache.hit", 1);
                return cached.InLos;
            }

            if (cached.ExpiresAt <= now)
                _losCache.Remove(key);
        }

        _bench.RecordCount("npc.utility.los_cache.miss", 1);

        if (!ConsumeLosBudget() && _losCache.TryGetValue(key, out cached))
        {
            _bench.RecordCount("npc.utility.los_budget.exceeded", 1);
            return cached.InLos;
        }

        var inLos = _examine.InRangeUnOccluded(owner, target, range, null);

        if (_losCache.Count >= UtilityLosCacheMaxEntries && !_losCache.ContainsKey(key))
            _losCache.Clear();

        _losCache[key] = new LosCacheEntry(
            inLos,
            ownerPos,
            targetPos,
            ownerXform.MapID,
            now + TimeSpan.FromSeconds(_losCacheTtlSeconds));

        return inLos;
    }

    private bool ConsumeLosBudget()
    {
        if (_losBudgetPerTick <= 0)
            return true;

        if (_timing.CurTick != _losBudgetTick)
        {
            _losBudgetTick = _timing.CurTick;
            _losBudgetUsed = 0;
        }

        _losBudgetUsed++;
        return _losBudgetUsed <= _losBudgetPerTick;
    }

    private bool TryGetDistanceSquared(EntityUid first, EntityUid second, out float distanceSquared)
    {
        distanceSquared = 0f;

        if (!_xformQuery.TryGetComponent(first, out var firstXform) ||
            !_xformQuery.TryGetComponent(second, out var secondXform))
        {
            return false;
        }

        if (firstXform.MapID != secondXform.MapID)
            return false;

        distanceSquared = (_transform.GetWorldPosition(firstXform) - _transform.GetWorldPosition(secondXform)).LengthSquared();
        return true;
    }

    private int GetCell(float coordinate)
    {
        return (int) MathF.Floor(coordinate / _spatialCacheCellSize);
    }

    private static int QuantizeRange(float range, float step)
    {
        if (step <= 0f)
            return 0;

        return (int) MathF.Ceiling(MathF.Max(0f, range) / step);
    }

    private void RecursiveAdd(EntityUid uid, HashSet<EntityUid> entities)
    {
        // TODO: Probably need a recursive struct enumerator on engine.
        var xform = _xformQuery.GetComponent(uid);
        var enumerator = xform.ChildEnumerator;
        entities.Add(uid);

        while (enumerator.MoveNext(out var child))
        {
            RecursiveAdd(child, entities);
        }
    }

    private void Filter(NPCBlackboard blackboard, HashSet<EntityUid> entities, UtilityQueryFilter filter)
    {
        using var benchScope = _bench.Measure("npc.utility.filter");

        switch (filter)
        {
            case Content.Server.NPC.Queries.Queries.ComponentFilter compFilter:
            {
                _entityList.Clear();
                var compTypes = GetOrCompileComponentFilter(compFilter);

                foreach (var ent in entities)
                {
                    foreach (var type in compTypes)
                    {
                        var hasComp = HasComp(ent, type);
                        if (!compFilter.RetainWithComp == hasComp)
                        {
                            _entityList.Add(ent);
                            break;
                        }
                    }
                }

                foreach (var ent in _entityList)
                {
                    entities.Remove(ent);
                }

                break;
            }
            case RemoveAnchoredFilter:
            {
                _entityList.Clear();

                foreach (var ent in entities)
                {
                    if (!TryComp(ent, out TransformComponent? xform))
                        continue;

                    if (xform.Anchored)
                        _entityList.Add(ent);
                }

                foreach (var ent in _entityList)
                {
                    entities.Remove(ent);
                }

                break;
            }
            case PuddleFilter:
            {
                _entityList.Clear();

                foreach (var ent in entities)
                {
                    if (!_puddleQuery.TryGetComponent(ent, out var puddleComp) ||
                        !_solutions.TryGetSolution(ent, puddleComp.SolutionName, out _, out var sol) ||
                        _puddle.CanFullyEvaporate(sol))
                    {
                        _entityList.Add(ent);
                    }
                }

                foreach (var ent in _entityList)
                {
                    entities.Remove(ent);
                }

                break;
            }
            default:
                throw new NotImplementedException();
        }
    }

    private readonly record struct UtilityCacheKey(EntityUid Owner, string Prototype, bool BestOnly);
    private readonly record struct UtilityCacheEntry(UtilityResult Result, TimeSpan ExpiresAt);
    private readonly record struct CompiledComponentQuery(Type? PrimaryType, Type[] OtherTypes);
    private readonly record struct SpatialCacheKey(MapId MapId, int CellX, int CellY, int RadiusBucket, Type PrimaryType);
    private readonly record struct SpatialCacheEntry(List<EntityUid> Entities, TimeSpan ExpiresAt);
    private readonly record struct HostilesCacheKey(EntityUid Owner, MapId MapId, int CellX, int CellY, int RadiusBucket);
    private readonly record struct HostilesCacheEntry(List<EntityUid> Entities, TimeSpan ExpiresAt);
    private readonly record struct LosCacheKey(EntityUid Owner, EntityUid Target, int RangeBucket);
    private readonly record struct LosCacheEntry(
        bool InLos,
        Vector2 OwnerPosition,
        Vector2 TargetPosition,
        MapId MapId,
        TimeSpan ExpiresAt);
    private readonly record struct WaveCoordinationKey(MapId MapId, int CellX, int CellY, int FactionSignature);
    private readonly record struct WaveCoordinationEntry(EntityUid Target, TimeSpan ExpiresAt);
}

public readonly record struct UtilityResult(Dictionary<EntityUid, float> Entities)
{
    public static readonly UtilityResult Empty = new(new Dictionary<EntityUid, float>());

    public readonly Dictionary<EntityUid, float> Entities = Entities;

    /// <summary>
    /// Returns the entity with the highest score.
    /// </summary>
    public EntityUid GetHighest()
    {
        if (Entities.Count == 0)
            return EntityUid.Invalid;

        return Entities.MaxBy(x => x.Value).Key;
    }

    /// <summary>
    /// Returns the entity with the lowest score. This does not consider entities with a 0 (invalid) score.
    /// </summary>
    public EntityUid GetLowest()
    {
        if (Entities.Count == 0)
            return EntityUid.Invalid;

        return Entities.MinBy(x => x.Value).Key;
    }

    /// <summary>
    /// Returns a GetEnumerable sorted in descending score.
    /// </summary>
    public IEnumerable<KeyValuePair<EntityUid, float>> GetEnumerable()
    {
        return Entities.OrderByDescending(x => x.Value);
    }
}
