using System;
using Content.Shared.CCVar;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Standing;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Shared.Movement.Systems
{
    public sealed partial class MovementSpeedModifierSystem : EntitySystem
    {
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private IConfigurationManager _configManager = default!;

        private float _frictionModifier;
        private float _airDamping;
        private float _offGridDamping;
        private readonly Dictionary<EntityUid, MovementModifierRefreshFlags> _queuedRefreshes = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<MovementSpeedModifierComponent, MapInitEvent>(OnModMapInit);
            SubscribeLocalEvent<MovementSpeedModifierComponent, DownedEvent>(OnDowned);
            SubscribeLocalEvent<MovementSpeedModifierComponent, StoodEvent>(OnStand);

            UpdatesAfter.Add(typeof(SpeedModifierContactsSystem));
            UpdatesAfter.Add(typeof(FrictionContactsSystem));

            Subs.CVar(_configManager, CCVars.TileFrictionModifier, value => _frictionModifier = value, true);
            Subs.CVar(_configManager, CCVars.AirFriction, value => _airDamping = value, true);
            Subs.CVar(_configManager, CCVars.OffgridFriction, value => _offGridDamping = value, true);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            if (_timing.ApplyingState || _queuedRefreshes.Count == 0)
                return;

            // Coalesce all requests per entity and apply at most once per tick.
            var pending = new List<KeyValuePair<EntityUid, MovementModifierRefreshFlags>>(_queuedRefreshes);
            _queuedRefreshes.Clear();

            foreach (var (uid, flags) in pending)
            {
                if (!TryComp(uid, out MovementSpeedModifierComponent? move))
                    continue;

                RefreshMovementProfile(uid, flags, move);
            }
        }

        private void OnModMapInit(Entity<MovementSpeedModifierComponent> ent, ref MapInitEvent args)
        {
            // TODO: Dirty these smarter.
            ent.Comp.WeightlessAcceleration = ent.Comp.BaseWeightlessAcceleration;
            ent.Comp.WeightlessModifier = ent.Comp.BaseWeightlessModifier;
            ent.Comp.WeightlessFriction = _airDamping * ent.Comp.BaseWeightlessFriction;
            ent.Comp.WeightlessFrictionNoInput = _airDamping * ent.Comp.BaseWeightlessFriction;
            ent.Comp.OffGridFriction = _offGridDamping * ent.Comp.BaseWeightlessFriction;
            ent.Comp.Acceleration = ent.Comp.BaseAcceleration;
            ent.Comp.Friction = _frictionModifier * ent.Comp.BaseFriction;
            ent.Comp.FrictionNoInput = _frictionModifier * ent.Comp.BaseFriction;
            Dirty(ent);
        }

        private void OnDowned(Entity<MovementSpeedModifierComponent> entity, ref DownedEvent args)
        {
            QueueRefresh(entity, MovementModifierRefreshFlags.Speed | MovementModifierRefreshFlags.Friction);
        }

        private void OnStand(Entity<MovementSpeedModifierComponent> entity, ref StoodEvent args)
        {
            QueueRefresh(entity, MovementModifierRefreshFlags.Speed | MovementModifierRefreshFlags.Friction);
        }

        /// <summary>
        /// Copy this component's datafields from one entity to another.
        /// This needs to refresh the modifiers after using CopyComp.
        /// <summary>
        public void CopyComponent(Entity<MovementSpeedModifierComponent?> source, EntityUid target)
        {
            if (!Resolve(source, ref source.Comp))
                return;

            CopyComp(source, target, source.Comp);
            QueueRefresh(target, MovementModifierRefreshFlags.All);
        }

        public void RefreshWeightlessModifiers(EntityUid uid, MovementSpeedModifierComponent? move = null)
        {
            QueueRefresh(uid, MovementModifierRefreshFlags.Weightless, move);
        }

        public void RefreshMovementSpeedModifiers(EntityUid uid, MovementSpeedModifierComponent? move = null)
        {
            QueueRefresh(uid, MovementModifierRefreshFlags.Speed, move);
        }

        public void ChangeBaseSpeed(EntityUid uid, float baseWalkSpeed, float baseSprintSpeed, float acceleration, MovementSpeedModifierComponent? move = null)
        {
            if (!Resolve(uid, ref move, false))
                return;

            move.BaseWalkSpeed = baseWalkSpeed;
            move.BaseSprintSpeed = baseSprintSpeed;
            move.Acceleration = acceleration;
            Dirty(uid, move);
        }

        public void RefreshFrictionModifiers(EntityUid uid, MovementSpeedModifierComponent? move = null)
        {
            if (!Resolve(uid, ref move, false))
                return;

            QueueRefresh(uid, MovementModifierRefreshFlags.Friction, move);
        }

        public void ChangeBaseFriction(EntityUid uid, float friction, float frictionNoInput, float acceleration, MovementSpeedModifierComponent? move = null)
        {
            if (!Resolve(uid, ref move, false))
                return;

            move.BaseFriction = friction;
            move.FrictionNoInput = frictionNoInput;
            move.BaseAcceleration = acceleration;
            Dirty(uid, move);
        }

        public void QueueRefresh(EntityUid uid, MovementModifierRefreshFlags flags, MovementSpeedModifierComponent? move = null)
        {
            if (!Resolve(uid, ref move, false))
                return;

            if (_timing.ApplyingState)
                return;

            if (_queuedRefreshes.TryGetValue(uid, out var existing))
                _queuedRefreshes[uid] = existing | flags;
            else
                _queuedRefreshes[uid] = flags;
        }

        private void RefreshMovementProfile(EntityUid uid, MovementModifierRefreshFlags flags, MovementSpeedModifierComponent move)
        {
            var dirty = false;

            if ((flags & MovementModifierRefreshFlags.Weightless) != 0)
            {
                var ev = new RefreshWeightlessModifiersEvent()
                {
                    WeightlessAcceleration = move.BaseWeightlessAcceleration,
                    WeightlessAccelerationMod = 1.0f,
                    WeightlessModifier = move.BaseWeightlessModifier,
                    WeightlessFriction = move.BaseWeightlessFriction,
                    WeightlessFrictionMod = 1.0f,
                    WeightlessFrictionNoInput = move.BaseWeightlessFriction,
                    WeightlessFrictionNoInputMod = 1.0f,
                };

                RaiseLocalEvent(uid, ref ev);

                var weightlessAcceleration = ev.WeightlessAcceleration * ev.WeightlessAccelerationMod;
                var weightlessModifier = ev.WeightlessModifier;
                var weightlessFriction = _airDamping * ev.WeightlessFriction * ev.WeightlessFrictionMod;
                var weightlessFrictionNoInput = _airDamping * ev.WeightlessFrictionNoInput * ev.WeightlessFrictionNoInputMod;

                if (!MathHelper.CloseTo(weightlessAcceleration, move.WeightlessAcceleration) ||
                    !MathHelper.CloseTo(weightlessModifier, move.WeightlessModifier) ||
                    !MathHelper.CloseTo(weightlessFriction, move.WeightlessFriction) ||
                    !MathHelper.CloseTo(weightlessFrictionNoInput, move.WeightlessFrictionNoInput))
                {
                    move.WeightlessAcceleration = weightlessAcceleration;
                    move.WeightlessModifier = weightlessModifier;
                    move.WeightlessFriction = weightlessFriction;
                    move.WeightlessFrictionNoInput = weightlessFrictionNoInput;
                    dirty = true;
                }
            }

            if ((flags & MovementModifierRefreshFlags.Speed) != 0)
            {
                var ev = new RefreshMovementSpeedModifiersEvent();
                RaiseLocalEvent(uid, ev);

                if (!MathHelper.CloseTo(ev.WalkSpeedModifier, move.WalkSpeedModifier) ||
                    !MathHelper.CloseTo(ev.SprintSpeedModifier, move.SprintSpeedModifier))
                {
                    move.WalkSpeedModifier = ev.WalkSpeedModifier;
                    move.SprintSpeedModifier = ev.SprintSpeedModifier;
                    dirty = true;
                }
            }

            if ((flags & MovementModifierRefreshFlags.Friction) != 0)
            {
                var ev = new RefreshFrictionModifiersEvent()
                {
                    Friction = move.BaseFriction,
                    FrictionNoInput = move.BaseFriction,
                    Acceleration = move.BaseAcceleration,
                };
                RaiseLocalEvent(uid, ref ev);

                var friction = _frictionModifier * ev.Friction;
                var frictionNoInput = _frictionModifier * ev.FrictionNoInput;
                var acceleration = ev.Acceleration;

                if (!MathHelper.CloseTo(friction, move.Friction) ||
                    !MathHelper.CloseTo(frictionNoInput, move.FrictionNoInput) ||
                    !MathHelper.CloseTo(acceleration, move.Acceleration))
                {
                    move.Friction = friction;
                    move.FrictionNoInput = frictionNoInput;
                    move.Acceleration = acceleration;
                    dirty = true;
                }
            }

            if (dirty)
                Dirty(uid, move);
        }
    }

    [Flags]
    public enum MovementModifierRefreshFlags : byte
    {
        None = 0,
        Speed = 1 << 0,
        Friction = 1 << 1,
        Weightless = 1 << 2,
        All = Speed | Friction | Weightless,
    }

    public enum MovementSpeedModifierLayer : byte
    {
        Generic = 0,
        Environment = 1,
        Status = 2,
        Equipment = 3,
    }

    /// <summary>
    ///     Raised on an entity to determine its new movement speed. Any system that wishes to change movement speed
    ///     should hook into this event and set it then. If you want this event to be raised,
    ///     call <see cref="MovementSpeedModifierSystem.RefreshMovementSpeedModifiers"/>.
    /// </summary>
    public sealed class RefreshMovementSpeedModifiersEvent : EntityEventArgs, IInventoryRelayEvent
    {
        public SlotFlags TargetSlots { get; } = ~SlotFlags.POCKET;

        private float _genericWalk = 1.0f;
        private float _genericSprint = 1.0f;
        private float _environmentWalk = 1.0f;
        private float _environmentSprint = 1.0f;
        private float _statusWalk = 1.0f;
        private float _statusSprint = 1.0f;
        private float _equipmentWalk = 1.0f;
        private float _equipmentSprint = 1.0f;

        public float WalkSpeedModifier => _environmentWalk * _statusWalk * _equipmentWalk * _genericWalk;
        public float SprintSpeedModifier => _environmentSprint * _statusSprint * _equipmentSprint * _genericSprint;

        public float EnvironmentWalkSpeedModifier => _environmentWalk;
        public float EnvironmentSprintSpeedModifier => _environmentSprint;
        public float StatusWalkSpeedModifier => _statusWalk;
        public float StatusSprintSpeedModifier => _statusSprint;
        public float EquipmentWalkSpeedModifier => _equipmentWalk;
        public float EquipmentSprintSpeedModifier => _equipmentSprint;
        public float GenericWalkSpeedModifier => _genericWalk;
        public float GenericSprintSpeedModifier => _genericSprint;

        public void ModifySpeed(float walk, float sprint)
        {
            ModifySpeed(walk, sprint, MovementSpeedModifierLayer.Generic);
        }

        public void ModifySpeed(float mod)
        {
            ModifySpeed(mod, mod);
        }

        public void ModifySpeed(float walk, float sprint, MovementSpeedModifierLayer layer)
        {
            switch (layer)
            {
                case MovementSpeedModifierLayer.Environment:
                    _environmentWalk *= walk;
                    _environmentSprint *= sprint;
                    break;
                case MovementSpeedModifierLayer.Status:
                    _statusWalk *= walk;
                    _statusSprint *= sprint;
                    break;
                case MovementSpeedModifierLayer.Equipment:
                    _equipmentWalk *= walk;
                    _equipmentSprint *= sprint;
                    break;
                default:
                    _genericWalk *= walk;
                    _genericSprint *= sprint;
                    break;
            }
        }

        public void ModifySpeed(float mod, MovementSpeedModifierLayer layer)
        {
            ModifySpeed(mod, mod, layer);
        }
    }

    [ByRefEvent]
    public record struct RefreshWeightlessModifiersEvent
    {
        public float WeightlessAcceleration;
        public float WeightlessAccelerationMod;

        public float WeightlessModifier;

        public float WeightlessFriction;
        public float WeightlessFrictionMod;

        public float WeightlessFrictionNoInput;
        public float WeightlessFrictionNoInputMod;

        public void ModifyFriction(float friction, float noInput)
        {
            WeightlessFrictionMod *= friction;
            WeightlessFrictionNoInputMod *= noInput;
        }

        public void ModifyFriction(float friction)
        {
            ModifyFriction(friction, friction);
        }

        public void ModifyAcceleration(float acceleration, float modifier)
        {
            WeightlessAcceleration *= acceleration;
            WeightlessModifier *= modifier;
        }

        public void ModifyAcceleration(float modifier)
        {
            ModifyAcceleration(modifier, modifier);
        }
    }
    [ByRefEvent]
    public record struct RefreshFrictionModifiersEvent : IInventoryRelayEvent
    {
        public float Friction;
        public float FrictionNoInput;
        public float Acceleration;

        public void ModifyFriction(float friction, float noInput)
        {
            Friction *= friction;
            FrictionNoInput *= noInput;
        }

        public void ModifyFriction(float friction)
        {
            ModifyFriction(friction, friction);
        }

        public void ModifyAcceleration(float acceleration)
        {
            Acceleration *= acceleration;
        }
        SlotFlags IInventoryRelayEvent.TargetSlots =>  ~SlotFlags.POCKET;
    }
}
