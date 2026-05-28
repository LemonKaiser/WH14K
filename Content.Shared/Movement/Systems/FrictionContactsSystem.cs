using Content.Shared.Gravity;
using Content.Shared.Movement.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.Movement.Systems;

public sealed partial class FrictionContactsSystem : EntitySystem
{
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private MovementSpeedModifierSystem _speedModifierSystem = default!;

    // Comment copied from "original" SlowContactsSystem.cs (now SpeedModifierContactsSystem.cs)
    // TODO full-game-save
    // Either these need to be processed before a map is saved, or slowed/slowing entities need to update on init.
    private readonly HashSet<EntityUid> _toUpdate = new();
    private readonly HashSet<EntityUid> _toRemove = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FrictionContactsComponent, StartCollideEvent>(OnEntityEnter);
        SubscribeLocalEvent<FrictionContactsComponent, EndCollideEvent>(OnEntityExit);
        SubscribeLocalEvent<FrictionModifiedByContactComponent, RefreshFrictionModifiersEvent>(OnRefreshFrictionModifiers);
        SubscribeLocalEvent<FrictionContactsComponent, ComponentShutdown>(OnShutdown);

        UpdatesAfter.Add(typeof(SharedPhysicsSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _toRemove.Clear();

        foreach (var ent in _toUpdate)
        {
            _speedModifierSystem.RefreshFrictionModifiers(ent);
        }

        foreach (var ent in _toRemove)
        {
            RemComp<FrictionModifiedByContactComponent>(ent);
        }

        _toUpdate.Clear();
    }

    public void ChangeFrictionModifiers(EntityUid uid, float friction, FrictionContactsComponent? component = null)
    {
        ChangeFrictionModifiers(uid, friction, null, null, component);
    }

    public void ChangeFrictionModifiers(EntityUid uid, float mobFriction, float? mobFrictionNoInput, float? acceleration, FrictionContactsComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        component.MobFriction = mobFriction;
        component.MobFrictionNoInput = mobFrictionNoInput;
        if (acceleration.HasValue)
            component.MobAcceleration = acceleration.Value;
        Dirty(uid, component);
        _toUpdate.UnionWith(_physics.GetContactingEntities(uid));
    }

    private void OnShutdown(EntityUid uid, FrictionContactsComponent component, ComponentShutdown args)
    {
        if (!TryComp(uid, out PhysicsComponent? phys))
            return;

        // Note that the entity may not be getting deleted here. E.g., glue puddles.
        _toUpdate.UnionWith(_physics.GetContactingEntities(uid, phys));
    }

    private void OnRefreshFrictionModifiers(Entity<FrictionModifiedByContactComponent> entity, ref RefreshFrictionModifiersEvent args)
    {
        if (!TryComp<PhysicsComponent>(entity, out var physicsComponent))
            return;

        var aggregate = new ContactFrictionAggregate();

        var isAirborne = physicsComponent.BodyStatus == BodyStatus.InAir || _gravity.IsWeightless(entity.Owner);

        var remove = true;
        foreach (var ent in _physics.GetContactingEntities(entity, physicsComponent))
        {
            if (!TryComp<FrictionContactsComponent>(ent, out var contacts))
                continue;

            // Entities that are airborne should not be affected by contact slowdowns that are specified to not affect airborne entities.
            if (isAirborne && !contacts.AffectAirborne)
                continue;

            aggregate.Add(
                contacts.MobFriction,
                contacts.MobFrictionNoInput ?? contacts.MobFriction,
                contacts.MobAcceleration,
                contacts.AggregationMode,
                contacts.AggregationWeight);
            remove = false;
        }

        if (aggregate.TryGet(out var friction, out var frictionNoInput, out var acceleration))
        {
            if (!MathHelper.CloseTo(friction, 1f) || !MathHelper.CloseTo(frictionNoInput, 1f))
            {
                args.ModifyFriction(friction, frictionNoInput);
            }

            if (!MathHelper.CloseTo(acceleration, 1f))
                args.ModifyAcceleration(acceleration);
        }

        // no longer colliding with anything
        if (remove)
            _toRemove.Add(entity);
    }

    private void OnEntityExit(EntityUid uid, FrictionContactsComponent component, ref EndCollideEvent args)
    {
        var otherUid = args.OtherEntity;
        _toUpdate.Add(otherUid);
    }

    private void OnEntityEnter(EntityUid uid, FrictionContactsComponent component, ref StartCollideEvent args)
    {
        AddModifiedEntity(args.OtherEntity);
    }

    public void AddModifiedEntity(EntityUid uid)
    {
        if (!HasComp<MovementSpeedModifierComponent>(uid))
            return;

        EnsureComp<FrictionModifiedByContactComponent>(uid);
        _toUpdate.Add(uid);
    }

    private struct ContactFrictionAggregate
    {
        private float _averageFriction;
        private float _averageFrictionNoInput;
        private float _averageAcceleration;
        private int _averageCount;

        private float _weightedFriction;
        private float _weightedFrictionNoInput;
        private float _weightedAcceleration;
        private float _weightedTotal;

        private float _multiplyFriction;
        private float _multiplyFrictionNoInput;
        private float _multiplyAcceleration;
        private bool _hasMultiply;

        private float _strongestFriction;
        private float _strongestFrictionNoInput;
        private float _strongestAcceleration;
        private bool _hasStrongest;

        public void Add(
            float friction,
            float frictionNoInput,
            float acceleration,
            ContactModifierAggregationMode mode,
            float weight)
        {
            switch (mode)
            {
                case ContactModifierAggregationMode.Strongest:
                    if (_hasStrongest)
                    {
                        _strongestFriction = MathF.Min(_strongestFriction, friction);
                        _strongestFrictionNoInput = MathF.Min(_strongestFrictionNoInput, frictionNoInput);
                        _strongestAcceleration = MathF.Min(_strongestAcceleration, acceleration);
                    }
                    else
                    {
                        _strongestFriction = friction;
                        _strongestFrictionNoInput = frictionNoInput;
                        _strongestAcceleration = acceleration;
                        _hasStrongest = true;
                    }
                    break;
                case ContactModifierAggregationMode.Multiply:
                    if (!_hasMultiply)
                    {
                        _multiplyFriction = 1f;
                        _multiplyFrictionNoInput = 1f;
                        _multiplyAcceleration = 1f;
                        _hasMultiply = true;
                    }

                    _multiplyFriction *= friction;
                    _multiplyFrictionNoInput *= frictionNoInput;
                    _multiplyAcceleration *= acceleration;
                    break;
                case ContactModifierAggregationMode.WeightedAverage:
                    var clamped = MathF.Max(0f, weight);
                    _weightedFriction += friction * clamped;
                    _weightedFrictionNoInput += frictionNoInput * clamped;
                    _weightedAcceleration += acceleration * clamped;
                    _weightedTotal += clamped;
                    break;
                default:
                    _averageFriction += friction;
                    _averageFrictionNoInput += frictionNoInput;
                    _averageAcceleration += acceleration;
                    _averageCount++;
                    break;
            }
        }

        public bool TryGet(out float friction, out float frictionNoInput, out float acceleration)
        {
            friction = 1f;
            frictionNoInput = 1f;
            acceleration = 1f;

            if (_averageCount > 0)
            {
                friction *= _averageFriction / _averageCount;
                frictionNoInput *= _averageFrictionNoInput / _averageCount;
                acceleration *= _averageAcceleration / _averageCount;
            }

            if (_weightedTotal > 0f)
            {
                friction *= _weightedFriction / _weightedTotal;
                frictionNoInput *= _weightedFrictionNoInput / _weightedTotal;
                acceleration *= _weightedAcceleration / _weightedTotal;
            }

            if (_hasMultiply)
            {
                friction *= _multiplyFriction;
                frictionNoInput *= _multiplyFrictionNoInput;
                acceleration *= _multiplyAcceleration;
            }

            if (_hasStrongest)
            {
                friction *= _strongestFriction;
                frictionNoInput *= _strongestFrictionNoInput;
                acceleration *= _strongestAcceleration;
            }

            return !MathHelper.CloseTo(friction, 1f)
                   || !MathHelper.CloseTo(frictionNoInput, 1f)
                   || !MathHelper.CloseTo(acceleration, 1f);
        }
    }
}
