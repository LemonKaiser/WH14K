using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Stunnable;
using Content.Server._WH40K.Chaplain.Components;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Throwing;
using Content.Shared._WH40K.Chaplain;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Chaplain;

public sealed partial class WH40KChaplainDashSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private  SharedActionsSystem _actions = default!;
    [Dependency] private  DamageableSystem _damageable = default!;
    [Dependency] private  SharedInteractionSystem _interaction = default!;
    [Dependency] private  EntityLookupSystem _lookup = default!;
    [Dependency] private  MobStateSystem _mobState = default!;
    [Dependency] private  IPrototypeManager _prototype = default!;
    [Dependency] private  SharedPhysicsSystem _physics = default!;
    [Dependency] private  StunSystem _stun = default!;
    [Dependency] private  ThrowingSystem _throwing = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    private static readonly ProtoId<DamageTypePrototype> SlashDamageType = "Slash";

    private readonly HashSet<EntityUid> _dashTargets = new();
    private DamageTypePrototype _slashDamage = default!;

    public override void Initialize()
    {
        base.Initialize();

        _slashDamage = _prototype.Index(SlashDamageType);

        SubscribeLocalEvent<WH40KChaplainDashComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KChaplainDashComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WH40KChaplainDashComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KChaplainDashComponent, WH40KChaplainDashActionEvent>(OnDash);
    }

    private void OnMapInit(Entity<WH40KChaplainDashComponent> ent, ref MapInitEvent args)
    {
        EnsureDashAction(ent);
        CleanupDuplicateActions(ent);
    }

    private void OnStartup(Entity<WH40KChaplainDashComponent> ent, ref ComponentStartup args)
    {
        EnsureDashAction(ent);
        CleanupDuplicateActions(ent);
    }

    private void OnShutdown(Entity<WH40KChaplainDashComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnDash(Entity<WH40KChaplainDashComponent> ent, ref WH40KChaplainDashActionEvent args)
    {
        var start = _transform.GetMapCoordinates(args.Performer);
        var target = _transform.ToMapCoordinates(args.Target);
        if (target.MapId != start.MapId)
            return;

        var direction = target.Position - start.Position;
        if (direction.LengthSquared() <= 0.0001f)
            return;

        var maxRange = Math.Max(0.5f, ent.Comp.DashRange);
        var end = FindDashEndpoint(args.Performer, start, direction, maxRange);
        if ((end.Position - start.Position).LengthSquared() < 0.05f)
            return;

        var dashVector = end.Position - start.Position;

        if (TryComp<PhysicsComponent>(args.Performer, out var performerPhysics))
            _physics.SetLinearVelocity(args.Performer, Vector2.Zero, body: performerPhysics);

        _throwing.TryThrow(
            args.Performer,
            dashVector,
            baseThrowSpeed: ent.Comp.ThrowSpeed,
            user: null,
            recoil: false,
            playSound: false,
            doSpin: false);

        if (!HasComp<ThrownItemComponent>(args.Performer))
            return;

        if (ent.Comp.VoiceLine != null)
            _audio.PlayPvs(ent.Comp.VoiceLine, ent.Owner);
        ApplyDashPathDamage(args.Performer, start, end, ent.Comp);
        args.Handled = true;
    }

    private void EnsureDashAction(Entity<WH40KChaplainDashComponent> ent)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ActionEntity, ent.Comp.ActionPrototype, ent.Owner);
        var cooldown = TimeSpan.FromSeconds(Math.Max(1f, ent.Comp.CooldownSeconds));
        _actions.SetUseDelay(ent.Comp.ActionEntity, cooldown);
    }

    private void CleanupDuplicateActions(Entity<WH40KChaplainDashComponent> ent)
    {
        if (!TryComp<ActionsComponent>(ent.Owner, out var actions))
            return;

        EntityUid? primary = null;
        var duplicates = new List<EntityUid>();

        foreach (var actionUid in actions.Actions)
        {
            if (!TryComp(actionUid, out MetaDataComponent? meta) ||
                meta.EntityPrototype is not { ID: { } prototypeId } ||
                prototypeId != ent.Comp.ActionPrototype.Id)
            {
                continue;
            }

            if (primary == null)
            {
                primary = actionUid;
                continue;
            }

            duplicates.Add(actionUid);
        }

        foreach (var duplicate in duplicates)
        {
            _actions.RemoveAction(ent.Owner, duplicate);
        }

        if (primary != null)
            ent.Comp.ActionEntity = primary;
    }

    private MapCoordinates FindDashEndpoint(EntityUid caster, MapCoordinates start, Vector2 direction, float maxRange)
    {
        const float step = 0.25f;
        var norm = Vector2.Normalize(direction);
        var best = start;

        for (var travelled = step; travelled <= maxRange + 0.001f; travelled += step)
        {
            var candidate = new MapCoordinates(start.Position + norm * travelled, start.MapId);
            if (!_interaction.InRangeUnobstructed(
                    start,
                    candidate,
                    maxRange,
                    CollisionGroup.Impassable | CollisionGroup.InteractImpassable,
                    e => e == caster))
            {
                break;
            }

            best = candidate;
        }

        return best;
    }

    private void ApplyDashPathDamage(EntityUid caster, MapCoordinates start, MapCoordinates end, WH40KChaplainDashComponent component)
    {
        _dashTargets.Clear();
        var max = (end.Position - start.Position).Length() + 1f;
        _lookup.GetEntitiesInRange(
            start.MapId,
            start.Position,
            max,
            _dashTargets,
            LookupFlags.Dynamic | LookupFlags.Uncontained);

        var damage = new DamageSpecifier(_slashDamage, FixedPoint2.New(component.Damage));

        foreach (var target in _dashTargets)
        {
            if (target == caster || TerminatingOrDeleted(target))
                continue;

            if (!TryComp<MobStateComponent>(target, out var mob) || _mobState.IsDead(target, mob))
                continue;

            if (!TryComp<DamageableComponent>(target, out var damageable))
                continue;

            var targetPos = _transform.GetMapCoordinates(target);
            if (targetPos.MapId != start.MapId)
                continue;

            if (!DoesDashIntersectTarget(start.Position, end.Position, target, component.HitPadding))
                continue;

            _damageable.TryChangeDamage((target, damageable), damage, origin: caster);
            _stun.TryKnockdown(target, TimeSpan.FromSeconds(component.KnockdownSeconds), true, false, false, true);
            _stun.TryAddStunDuration(target, TimeSpan.FromSeconds(component.StunSeconds));
        }
    }

    private bool DoesDashIntersectTarget(Vector2 start, Vector2 end, EntityUid target, float hitPadding)
    {
        var xform = Transform(target);
        var (worldPos, worldRot) = _transform.GetWorldPositionRotation(xform);
        var bounds = _lookup.GetAABBNoContainer(target, worldPos, worldRot).Enlarged(hitPadding);
        return SegmentIntersectsBox(start, end, bounds);
    }

    private static bool SegmentIntersectsBox(Vector2 start, Vector2 end, Box2 box)
    {
        var direction = end - start;
        var tMin = 0f;
        var tMax = 1f;

        if (!ClipAxis(start.X, direction.X, box.Left, box.Right, ref tMin, ref tMax))
            return false;

        if (!ClipAxis(start.Y, direction.Y, box.Bottom, box.Top, ref tMin, ref tMax))
            return false;

        return true;
    }

    private static bool ClipAxis(float start, float direction, float min, float max, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(direction) < 0.0001f)
            return start >= min && start <= max;

        var inv = 1f / direction;
        var t1 = (min - start) * inv;
        var t2 = (max - start) * inv;

        if (t1 > t2)
            (t1, t2) = (t2, t1);

        tMin = MathF.Max(tMin, t1);
        tMax = MathF.Min(tMax, t2);
        return tMin <= tMax;
    }
}
