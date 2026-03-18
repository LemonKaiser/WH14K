using Content.Shared.Gravity;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Slippery;
using Content.Shared.Whitelist;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.Movement.Systems;

public sealed class SpeedModifierContactsSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speedModifierSystem = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    // TODO full-game-save
    // Either these need to be processed before a map is saved, or slowed/slowing entities need to update on init.
    private readonly HashSet<EntityUid> _toUpdate = new();
    private readonly HashSet<EntityUid> _toRemove = new();
    private readonly Dictionary<ContactPair, TimeSpan> _contactStartedAt = new();

    private readonly record struct ContactPair(EntityUid Source, EntityUid Affected);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SpeedModifierContactsComponent, StartCollideEvent>(OnEntityEnter);
        SubscribeLocalEvent<SpeedModifierContactsComponent, EndCollideEvent>(OnEntityExit);
        SubscribeLocalEvent<SpeedModifiedByContactComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeedModifiers);
        SubscribeLocalEvent<SpeedModifierContactsComponent, ComponentShutdown>(OnShutdown);

        UpdatesAfter.Add(typeof(SharedPhysicsSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _toRemove.Clear();

        foreach (var ent in _toUpdate)
        {
            _speedModifierSystem.RefreshMovementSpeedModifiers(ent);
        }

        foreach (var ent in _toRemove)
        {
            RemComp<SpeedModifiedByContactComponent>(ent);
        }

        _toUpdate.Clear();
    }

    public void ChangeSpeedModifiers(EntityUid uid, float speed, SpeedModifierContactsComponent? component = null)
    {
        ChangeSpeedModifiers(uid, speed, speed, component);
    }

    public void ChangeSpeedModifiers(
        EntityUid uid,
        float walkSpeed,
        float sprintSpeed,
        SpeedModifierContactsComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        component.WalkSpeedModifier = walkSpeed;
        component.SprintSpeedModifier = sprintSpeed;
        Dirty(uid, component);
        _toUpdate.UnionWith(_physics.GetContactingEntities(uid));
    }

    private void OnShutdown(EntityUid uid, SpeedModifierContactsComponent component, ComponentShutdown args)
    {
        if (!TryComp(uid, out PhysicsComponent? phys))
            return;

        // Note that the entity may not be getting deleted here. E.g., glue puddles.
        _toUpdate.UnionWith(_physics.GetContactingEntities(uid, phys));

        if (_contactStartedAt.Count == 0)
            return;

        var toRemove = new List<ContactPair>();
        foreach (var pair in _contactStartedAt.Keys)
        {
            if (pair.Source == uid)
                toRemove.Add(pair);
        }

        foreach (var pair in toRemove)
        {
            _contactStartedAt.Remove(pair);
        }
    }

    private void OnRefreshMovementSpeedModifiers(
        EntityUid uid,
        SpeedModifiedByContactComponent component,
        RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryComp<PhysicsComponent>(uid, out var physicsComponent))
            return;

        var aggregate = new ContactSpeedAggregate();

        // Cache the result of the airborne check, as it's expensive and independent of contacting entities, hence need only be done once.
        var isAirborne = physicsComponent.BodyStatus == BodyStatus.InAir || _gravity.IsWeightless(uid);

        var remove = true;
        foreach (var ent in _physics.GetContactingEntities(uid, physicsComponent))
        {
            var speedModified = false;

            if (TryComp<SpeedModifierContactsComponent>(ent, out var slowContactsComponent))
            {
                if (_whitelistSystem.IsWhitelistPass(slowContactsComponent.IgnoreWhitelist, uid))
                    continue;

                if (slowContactsComponent.RequireSameTile && !AreOnSameTile(uid, ent))
                    continue;

                if (IsIntersectingWhitelistedEntity(uid, physicsComponent, ent, slowContactsComponent.IgnoreWhenIntersectingWhitelist))
                    continue;

                // Entities that are airborne should not be affected by contact slowdowns that are specified to not affect airborne entities.
                if (isAirborne && !slowContactsComponent.AffectAirborne)
                    continue;

                var walk = GetRampedModifier(ent, uid, slowContactsComponent.WalkSpeedModifier, slowContactsComponent.RampUpDuration);
                var sprint = GetRampedModifier(ent, uid, slowContactsComponent.SprintSpeedModifier, slowContactsComponent.RampUpDuration);
                aggregate.Add(walk, sprint, slowContactsComponent.AggregationMode, slowContactsComponent.AggregationWeight);
                speedModified = true;
            }

            // SpeedModifierContactsComponent takes priority over SlowedOverSlipperyComponent, effectively overriding the slippery slow.
            if (HasComp<SlipperyComponent>(ent) && speedModified == false)
            {
                var evSlippery = new GetSlowedOverSlipperyModifierEvent();
                RaiseLocalEvent(uid, ref evSlippery);

                if (!MathHelper.CloseTo(evSlippery.SlowdownModifier, 1))
                {
                    aggregate.Add(evSlippery.SlowdownModifier, evSlippery.SlowdownModifier, ContactModifierAggregationMode.Average, 1f);
                    speedModified = true;
                }
            }

            if (speedModified)
            {
                remove = false;
            }
        }

        if (aggregate.TryGet(out var walkSpeed, out var sprintSpeed))
        {
            var evMax = new GetSpeedModifierContactCapEvent();
            RaiseLocalEvent(uid, ref evMax);

            walkSpeed = MathF.Max(walkSpeed, evMax.MaxWalkSlowdown);
            sprintSpeed = MathF.Max(sprintSpeed, evMax.MaxSprintSlowdown);

            args.ModifySpeed(walkSpeed, sprintSpeed, MovementSpeedModifierLayer.Environment);
        }

        // no longer colliding with anything
        if (remove)
            _toRemove.Add(uid);
    }

    private void OnEntityExit(EntityUid uid, SpeedModifierContactsComponent component, ref EndCollideEvent args)
    {
        var otherUid = args.OtherEntity;
        _toUpdate.Add(otherUid);
        _contactStartedAt.Remove(new ContactPair(uid, otherUid));
    }

    private void OnEntityEnter(EntityUid uid, SpeedModifierContactsComponent component, ref StartCollideEvent args)
    {
        _contactStartedAt[new ContactPair(uid, args.OtherEntity)] = _timing.CurTime;
        AddModifiedEntity(args.OtherEntity);
    }

    /// <summary>
    /// Add an entity to be checked for speed modification from contact with another entity.
    /// </summary>
    /// <param name="uid">The entity to be added.</param>
    public void AddModifiedEntity(EntityUid uid)
    {
        if (!HasComp<MovementSpeedModifierComponent>(uid))
            return;

        EnsureComp<SpeedModifiedByContactComponent>(uid);
        _toUpdate.Add(uid);
    }

    private bool IsIntersectingWhitelistedEntity(
        EntityUid uid,
        PhysicsComponent physics,
        EntityUid ignoredEntity,
        EntityWhitelist? whitelist)
    {
        if (whitelist == null)
            return false;

        foreach (var contact in _physics.GetContactingEntities(uid, physics))
        {
            if (contact == ignoredEntity)
                continue;

            if (_whitelistSystem.IsWhitelistPass(whitelist, contact))
                return true;
        }

        // Some tile overlays (e.g. catwalks) intentionally have no physics fixture.
        // Check anchored entities on the affected entity tile.
        if (!TryGetEntityTile(uid, out var affectedGridUid, out var affectedGrid, out var affectedTile))
            return false;

        var anchored = _map.GetAnchoredEntitiesEnumerator(uid, affectedGrid, affectedTile);
        while (anchored.MoveNext(out var ent))
        {
            if (ent == ignoredEntity || ent == uid)
                continue;

            if (_whitelistSystem.IsWhitelistPass(whitelist, ent.Value))
                return true;
        }

        // Also check anchored entities on the slowdown source tile itself.
        // This removes transient slowdown on catwalk-over-water when the mover center
        // is between tiles but the contacted water tile is already covered by catwalk.
        if (!TryGetEntityTile(ignoredEntity, out var sourceGridUid, out var sourceGrid, out var sourceTile))
            return false;

        if (sourceGridUid != affectedGridUid)
            return false;

        var sourceAnchored = _map.GetAnchoredEntitiesEnumerator(ignoredEntity, sourceGrid, sourceTile);
        while (sourceAnchored.MoveNext(out var ent))
        {
            if (ent == ignoredEntity || ent == uid)
                continue;

            if (_whitelistSystem.IsWhitelistPass(whitelist, ent.Value))
                return true;
        }

        return false;
    }

    private bool AreOnSameTile(EntityUid first, EntityUid second)
    {
        if (!TryGetEntityTile(first, out var firstGridUid, out _, out var firstTile))
            return false;

        if (!TryGetEntityTile(second, out var secondGridUid, out _, out var secondTile))
            return false;

        return firstGridUid == secondGridUid && firstTile == secondTile;
    }

    private bool TryGetEntityTile(EntityUid uid, out EntityUid gridUid, out MapGridComponent grid, out Vector2i tile)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUidValue || !TryComp<MapGridComponent>(gridUidValue, out var gridComp))
        {
            gridUid = default;
            grid = default!;
            tile = default;
            return false;
        }

        gridUid = gridUidValue;
        grid = gridComp;
        tile = _map.LocalToTile(gridUid, grid, xform.Coordinates);
        return true;
    }

    private float GetRampedModifier(EntityUid source, EntityUid affected, float targetModifier, float rampUpDuration)
    {
        if (rampUpDuration <= 0f || MathHelper.CloseTo(targetModifier, 1f))
            return targetModifier;

        var pair = new ContactPair(source, affected);
        if (!_contactStartedAt.TryGetValue(pair, out var started))
        {
            started = _timing.CurTime;
            _contactStartedAt[pair] = started;
        }

        var elapsed = (float) (_timing.CurTime - started).TotalSeconds;
        var t = Math.Clamp(elapsed / rampUpDuration, 0f, 1f);
        return MathHelper.Lerp(1f, targetModifier, t);
    }

    private struct ContactSpeedAggregate
    {
        private float _averageWalk;
        private float _averageSprint;
        private int _averageCount;

        private float _weightedWalk;
        private float _weightedSprint;
        private float _weightedTotal;

        private float _multiplyWalk;
        private float _multiplySprint;
        private bool _hasMultiply;

        private float _strongestWalk;
        private float _strongestSprint;
        private bool _hasStrongest;

        public void Add(float walk, float sprint, ContactModifierAggregationMode mode, float weight)
        {
            switch (mode)
            {
                case ContactModifierAggregationMode.Strongest:
                    if (_hasStrongest)
                    {
                        _strongestWalk = MathF.Min(_strongestWalk, walk);
                        _strongestSprint = MathF.Min(_strongestSprint, sprint);
                    }
                    else
                    {
                        _strongestWalk = walk;
                        _strongestSprint = sprint;
                        _hasStrongest = true;
                    }
                    break;
                case ContactModifierAggregationMode.Multiply:
                    if (!_hasMultiply)
                    {
                        _multiplyWalk = 1f;
                        _multiplySprint = 1f;
                        _hasMultiply = true;
                    }

                    _multiplyWalk *= walk;
                    _multiplySprint *= sprint;
                    break;
                case ContactModifierAggregationMode.WeightedAverage:
                    var clamped = MathF.Max(0f, weight);
                    _weightedWalk += walk * clamped;
                    _weightedSprint += sprint * clamped;
                    _weightedTotal += clamped;
                    break;
                default:
                    _averageWalk += walk;
                    _averageSprint += sprint;
                    _averageCount++;
                    break;
            }
        }

        public bool TryGet(out float walk, out float sprint)
        {
            walk = 1f;
            sprint = 1f;

            if (_averageCount > 0)
            {
                walk *= _averageWalk / _averageCount;
                sprint *= _averageSprint / _averageCount;
            }

            if (_weightedTotal > 0f)
            {
                walk *= _weightedWalk / _weightedTotal;
                sprint *= _weightedSprint / _weightedTotal;
            }

            if (_hasMultiply)
            {
                walk *= _multiplyWalk;
                sprint *= _multiplySprint;
            }

            if (_hasStrongest)
            {
                walk *= _strongestWalk;
                sprint *= _strongestSprint;
            }

            return !MathHelper.CloseTo(walk, 1f) || !MathHelper.CloseTo(sprint, 1f);
        }
    }
}
