using System.Numerics;
using Content.Shared.Actions;
using Content.Shared._WH40K.Combat.PhantomStep;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Toggleable;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Combat.PhantomStep;

public sealed partial class WH40KPhantomStepSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KPhantomStepComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WH40KPhantomStepComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KPhantomStepComponent, ToggleActionEvent>(OnToggleAction);
        SubscribeLocalEvent<WH40KPhantomStepComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<WH40KPhantomStepComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<WH40KPhantomStepComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<HitscanBasicRaycastComponent, AttemptHitscanRaycastFiredEvent>(OnHitscanAttempt);
        SubscribeLocalEvent<WH40KPhantomStepComponent, ProjectileReflectAttemptEvent>(OnProjectileAttempt, before: [typeof(ReflectSystem)]);
        SubscribeLocalEvent<WH40KPhantomStepComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
        SubscribeLocalEvent<WH40KPhantomStepComponent, BeforeStaminaDamageEvent>(OnBeforeStaminaDamage);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KPhantomStepComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var step, out var xform))
        {
            if (step.Dashing)
                UpdateDash(uid, step, xform, now);

            if (step.Charges > step.MaxCharges)
                step.Charges = step.MaxCharges;

            if (step.Charges >= step.MaxCharges || step.NextRecharge == TimeSpan.Zero || now < step.NextRecharge)
            {
                SyncAction(step, uid);
                continue;
            }

            step.Charges = Math.Min(step.MaxCharges, step.Charges + 1);
            step.NextRecharge = step.Charges < step.MaxCharges
                ? now + step.Cooldown
                : TimeSpan.Zero;
            Dirty(uid, step);
            SyncAction(step, uid);
        }
    }

    private void OnStartup(Entity<WH40KPhantomStepComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction, ent.Owner);
        SyncAction(ent.Comp, ent.Owner);
    }

    private void OnShutdown(Entity<WH40KPhantomStepComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ToggleActionEntity != null)
            _actions.RemoveAction(ent.Owner, ent.Comp.ToggleActionEntity);

        ent.Comp.ToggleActionEntity = null;
    }

    private void OnToggleAction(Entity<WH40KPhantomStepComponent> ent, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        ent.Comp.Enabled = !ent.Comp.Enabled;
        Dirty(ent.Owner, ent.Comp);
        SyncAction(ent.Comp, ent.Owner);
        args.Handled = true;
    }

    private void OnAttacked(Entity<WH40KPhantomStepComponent> ent, ref AttackedEvent args)
    {
        TryTriggerDodge(ent, args.User, PhantomStepThreatType.Melee);
    }

    private void OnPreventCollide(Entity<WH40KPhantomStepComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled || !TryComp<ProjectileComponent>(args.OtherEntity, out var projectile))
            return;

        if (TryTriggerDodge(ent, projectile.Shooter ?? projectile.Weapon ?? args.OtherEntity, PhantomStepThreatType.Ranged))
            args.Cancelled = true;
    }

    private void OnMobStateChanged(Entity<WH40KPhantomStepComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        ent.Comp.InvulnerableUntil = TimeSpan.Zero;
        if (ent.Comp.Dashing)
            StopDash(ent.Owner, ent.Comp, Transform(ent.Owner), snapToEnd: false);
        else
            Dirty(ent.Owner, ent.Comp);
    }

    private void OnHitscanAttempt(Entity<HitscanBasicRaycastComponent> ent, ref AttemptHitscanRaycastFiredEvent args)
    {
        if (args.Cancelled || args.Data.HitEntity is not { } target)
            return;

        if (!TryComp<WH40KPhantomStepComponent>(target, out var step))
            return;

        if (TryTriggerDodge((target, step), args.Data.Shooter ?? args.Data.Gun, PhantomStepThreatType.Ranged))
            args.Cancelled = true;
    }

    private void OnProjectileAttempt(Entity<WH40KPhantomStepComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (TryTriggerDodge(ent, args.Component.Shooter ?? args.Component.Weapon ?? args.ProjUid, PhantomStepThreatType.Ranged))
            args.Cancelled = true;
    }

    private void OnBeforeDamage(Entity<WH40KPhantomStepComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (_timing.CurTime <= ent.Comp.InvulnerableUntil)
            args.Cancelled = true;
    }

    private void OnBeforeStaminaDamage(Entity<WH40KPhantomStepComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        if (_timing.CurTime <= ent.Comp.InvulnerableUntil)
            args.Cancelled = true;
    }

    private bool TryTriggerDodge(
        Entity<WH40KPhantomStepComponent> ent,
        EntityUid? source,
        PhantomStepThreatType threatType)
    {
        var now = _timing.CurTime;
        if (now <= ent.Comp.InvulnerableUntil)
            return true;

        if (TryComp<MobStateComponent>(ent.Owner, out var mobState) &&
            mobState.CurrentState != MobState.Alive)
        {
            return false;
        }

        if (!CanDodgeThreat(ent.Comp, threatType))
            return false;

        if (!ent.Comp.Enabled || ent.Comp.MaxCharges <= 0 || ent.Comp.Charges <= 0)
            return false;

        var xform = Transform(ent.Owner);
        if (!TryFindDodgeCoordinates(ent.Owner, source, ent.Comp, out var targetCoords))
            return false;

        var originCoords = xform.Coordinates;
        var originMap = _transform.GetMapCoordinates((ent.Owner, xform));
        var targetMap = _transform.ToMapCoordinates(targetCoords);
        if (originMap.MapId != targetMap.MapId || targetMap.MapId == MapId.Nullspace)
            return false;

        ent.Comp.Charges--;
        if (ent.Comp.Charges < ent.Comp.MaxCharges &&
            ent.Comp.NextRecharge == TimeSpan.Zero)
        {
            ent.Comp.NextRecharge = now + ent.Comp.Cooldown;
        }
        var dashDuration = ent.Comp.DashDuration > TimeSpan.Zero
            ? ent.Comp.DashDuration
            : TimeSpan.FromMilliseconds(1);
        ent.Comp.InvulnerableUntil = now + (ent.Comp.Invulnerability > dashDuration
            ? ent.Comp.Invulnerability
            : dashDuration);
        ent.Comp.Dashing = true;
        ent.Comp.DashStartedAt = now;
        ent.Comp.DashEndsAt = now + dashDuration;
        ent.Comp.DashStart = originMap;
        ent.Comp.DashEnd = targetMap;
        ent.Comp.DashEndCoordinates = targetCoords;

        RaiseNetworkEvent(
            new WH40KPhantomStepTrailEvent(
                GetNetEntity(ent.Owner),
                GetNetCoordinates(originCoords),
                GetNetCoordinates(targetCoords),
                (float) dashDuration.TotalSeconds,
                (float) ent.Comp.TrailLifetime.TotalSeconds,
                ent.Comp.TrailCopies),
            Filter.Pvs(ent.Owner));

        Dirty(ent.Owner, ent.Comp);
        SyncAction(ent.Comp, ent.Owner);
        return true;
    }

    private void UpdateDash(
        EntityUid uid,
        WH40KPhantomStepComponent step,
        TransformComponent xform,
        TimeSpan now)
    {
        if (!step.Dashing)
            return;

        if (step.DashStart == MapCoordinates.Nullspace ||
            step.DashEnd == MapCoordinates.Nullspace ||
            step.DashEndCoordinates == EntityCoordinates.Invalid)
        {
            StopDash(uid, step, xform, snapToEnd: false);
            return;
        }

        var totalSeconds = Math.Max(0.001f, (float) (step.DashEndsAt - step.DashStartedAt).TotalSeconds);
        var elapsedSeconds = (float) (now - step.DashStartedAt).TotalSeconds;
        var progress = Math.Clamp(elapsedSeconds / totalSeconds, 0f, 1f);
        var nextPos = Vector2.Lerp(step.DashStart.Position, step.DashEnd.Position, progress);
        _transform.SetMapCoordinates((uid, xform), new MapCoordinates(nextPos, step.DashEnd.MapId));

        if (progress < 1f)
            return;

        StopDash(uid, step, xform, snapToEnd: true);
    }

    private void StopDash(
        EntityUid uid,
        WH40KPhantomStepComponent step,
        TransformComponent xform,
        bool snapToEnd)
    {
        if (snapToEnd && step.DashEndCoordinates != EntityCoordinates.Invalid)
        {
            _transform.SetCoordinates(uid, xform, step.DashEndCoordinates);
            _transform.AttachToGridOrMap(uid, xform);
        }

        step.Dashing = false;
        step.DashStartedAt = TimeSpan.Zero;
        step.DashEndsAt = TimeSpan.Zero;
        step.DashStart = MapCoordinates.Nullspace;
        step.DashEnd = MapCoordinates.Nullspace;
        step.DashEndCoordinates = EntityCoordinates.Invalid;
        Dirty(uid, step);
    }

    private void SyncAction(WH40KPhantomStepComponent step, EntityUid owner)
    {
        if (step.ToggleActionEntity is not { } actionUid || !Exists(actionUid))
            return;

        _actions.SetToggled(actionUid, step.Enabled);

        var action = EnsureComp<WH40KPhantomStepActionComponent>(actionUid);
        var changed = false;

        if (action.Charges != step.Charges)
        {
            action.Charges = step.Charges;
            changed = true;
        }

        if (action.MaxCharges != step.MaxCharges)
        {
            action.MaxCharges = step.MaxCharges;
            changed = true;
        }

        if (action.RechargeDuration != step.Cooldown)
        {
            action.RechargeDuration = step.Cooldown;
            changed = true;
        }

        if (action.NextRecharge != step.NextRecharge)
        {
            action.NextRecharge = step.NextRecharge;
            changed = true;
        }

        if (changed)
            Dirty(actionUid, action);
    }

    private static bool CanDodgeThreat(WH40KPhantomStepComponent step, PhantomStepThreatType threatType)
    {
        return threatType switch
        {
            PhantomStepThreatType.Ranged => step.DodgeRanged,
            PhantomStepThreatType.Melee => step.DodgeMelee,
            _ => false,
        };
    }

    private bool TryFindDodgeCoordinates(
        EntityUid target,
        EntityUid? source,
        WH40KPhantomStepComponent step,
        out EntityCoordinates coordinates)
    {
        var origin = _transform.GetMapCoordinates(target);
        var directions = BuildCandidateDirections(origin, source);

        for (var distance = step.MaxDistance; distance >= step.MinDistance; distance--)
        {
            foreach (var direction in directions)
            {
                if (direction.LengthSquared() <= 0.001f)
                    continue;

                var candidate = new MapCoordinates(origin.Position + Vector2.Normalize(direction) * distance, origin.MapId);
                if (TryGetSafeCoordinates(candidate, out coordinates) &&
                    IsDashPathSafe(origin, _transform.ToMapCoordinates(coordinates)))
                {
                    return true;
                }
            }
        }

        coordinates = Transform(target).Coordinates;
        return false;
    }

    private List<Vector2> BuildCandidateDirections(MapCoordinates origin, EntityUid? source)
    {
        var away = Vector2.Zero;
        if (source is { } sourceUid && Exists(sourceUid))
        {
            var sourceMap = _transform.GetMapCoordinates(sourceUid);
            if (sourceMap.MapId == origin.MapId)
                away = origin.Position - sourceMap.Position;
        }

        if (away.LengthSquared() <= 0.001f)
            away = _random.NextAngle().ToVec();

        away = Vector2.Normalize(away);
        return new List<Vector2>
        {
            away,
            Rotate(away, MathF.PI / 4f),
            Rotate(away, -MathF.PI / 4f),
            Rotate(away, MathF.PI / 2f),
            Rotate(away, -MathF.PI / 2f),
            -away,
            _random.NextAngle().ToVec(),
            _random.NextAngle().ToVec(),
        };
    }

    private bool IsDashPathSafe(MapCoordinates start, MapCoordinates end)
    {
        if (start.MapId == MapId.Nullspace ||
            end.MapId == MapId.Nullspace ||
            start.MapId != end.MapId)
        {
            return false;
        }

        var delta = end.Position - start.Position;
        var distance = delta.Length();
        if (distance <= 0.01f)
            return true;

        var steps = Math.Max(1, (int) MathF.Ceiling(distance / 0.35f));
        for (var i = 1; i <= steps; i++)
        {
            var progress = i / (float) steps;
            var sample = new MapCoordinates(Vector2.Lerp(start.Position, end.Position, progress), start.MapId);
            if (!IsSafeMapCoordinate(sample))
                return false;
        }

        return true;
    }

    private bool TryGetSafeCoordinates(MapCoordinates candidate, out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;

        if (!IsSafeMapCoordinate(candidate))
            return false;

        if (candidate.MapId == MapId.Nullspace)
            return false;

        if (!_mapManager.TryFindGridAt(candidate, out var gridUid, out var grid))
            return false;

        var tileIndices = _map.WorldToTile(gridUid, grid, candidate.Position);
        if (!_map.TryGetTileRef(gridUid, grid, tileIndices, out var tileRef))
            return false;

        if (tileRef.Tile.IsEmpty || _turf.IsSpace(tileRef))
            return false;

        if (_turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
            return false;

        coordinates = _turf.GetTileCenter(tileRef);
        return true;
    }

    private bool IsSafeMapCoordinate(MapCoordinates candidate)
    {
        if (candidate.MapId == MapId.Nullspace)
            return false;

        if (!_mapManager.TryFindGridAt(candidate, out var gridUid, out var grid))
            return false;

        var tileIndices = _map.WorldToTile(gridUid, grid, candidate.Position);
        if (!_map.TryGetTileRef(gridUid, grid, tileIndices, out var tileRef))
            return false;

        if (tileRef.Tile.IsEmpty || _turf.IsSpace(tileRef))
            return false;

        return !_turf.IsTileBlocked(tileRef, CollisionGroup.MobMask);
    }

    private static Vector2 Rotate(Vector2 vector, float radians)
    {
        var sin = MathF.Sin(radians);
        var cos = MathF.Cos(radians);
        return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
    }

    private enum PhantomStepThreatType : byte
    {
        Ranged,
        Melee,
    }
}
