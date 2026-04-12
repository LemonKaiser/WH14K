using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Popups;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared.Buckle.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Content.Shared._WH40K.Combat;
using Content.Shared._WH40K.GameMode;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Combat;

public sealed class WH40KTdmWarningBarrierSystem : EntitySystem
{
    private static readonly TimeSpan MoveCorrectionSuppression = TimeSpan.FromSeconds(0.2);

    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamBattle = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextPopupAt = new();
    private readonly Dictionary<EntityUid, TimeSpan> _moveSuppressedUntil = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KPreparationPhaseBarrierComponent, MapInitEvent>(OnBarrierMapInit);
        SubscribeLocalEvent<WH40KBattlePhaseChangedEvent>(OnPhaseChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<WH40KTdmWarningBarrierComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<ActorComponent, MoveEvent>(OnActorMove);
        SubscribeLocalEvent<StrapComponent, MoveEvent>(OnStrapMove);
        SubscribeLocalEvent<StrapComponent, StrapAttemptEvent>(OnStrapAttempt);
    }

    private void OnBarrierMapInit(Entity<WH40KPreparationPhaseBarrierComponent> ent, ref MapInitEvent args)
    {
        if (_teamBattle.GetCurrentPhase() <= WH40KBattlePhase.Preparation)
            return;

        QueueDel(ent.Owner);
    }

    private void OnPhaseChanged(WH40KBattlePhaseChangedEvent ev)
    {
        if (ev.NewPhase <= WH40KBattlePhase.Preparation)
            return;

        var query = EntityQueryEnumerator<WH40KPreparationPhaseBarrierComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            QueueDel(uid);
        }

        ClearRuntimeState();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        ClearRuntimeState();
    }

    private void OnStartCollide(Entity<WH40KTdmWarningBarrierComponent> ent, ref StartCollideEvent args)
    {
        if (!TryGetPopupTargets(args.OtherEntity, out var popupTargets))
            return;

        StopPhysics(args.OtherEntity);

        foreach (var popupTarget in popupTargets)
        {
            TryPopupTarget(ent.Comp, popupTarget);
        }
    }

    private void OnActorMove(Entity<ActorComponent> ent, ref MoveEvent args)
    {
        if (ShouldIgnoreBarrier(ent.Owner) ||
            IsMoveSuppressed(ent.Owner) ||
            IsBuckled(ent.Owner) ||
            !TryGetSeparatingBarrier(ent.Owner, args.OldPosition, args.NewPosition, out var barrier))
        {
            return;
        }

        HandleBlockedMovement(ent.Owner, args.OldPosition, barrier, [ent.Owner]);
    }

    private void OnStrapMove(Entity<StrapComponent> ent, ref MoveEvent args)
    {
        if (IsMoveSuppressed(ent.Owner) ||
            ent.Comp.BuckledEntities.Count == 0 ||
            !TryGetSeparatingBarrier(ent.Owner, args.OldPosition, args.NewPosition, out var barrier))
        {
            return;
        }

        var popupTargets = new List<EntityUid>();
        foreach (var buckledEntity in ent.Comp.BuckledEntities)
        {
            if (!HasComp<ActorComponent>(buckledEntity) ||
                TerminatingOrDeleted(buckledEntity) ||
                ShouldIgnoreBarrier(buckledEntity))
            {
                continue;
            }

            popupTargets.Add(buckledEntity);
        }

        HandleBlockedMovement(ent.Owner, args.OldPosition, barrier, popupTargets);
    }

    private void OnStrapAttempt(Entity<StrapComponent> ent, ref StrapAttemptEvent args)
    {
        if (ShouldIgnoreBarrier(args.Buckle.Owner) ||
            !HasComp<ActorComponent>(args.Buckle.Owner) ||
            !TryGetSeparatingBarrier(args.Buckle.Owner, ent.Owner, out var barrier))
        {
            return;
        }

        args.Cancelled = true;

        if (TryComp<WH40KTdmWarningBarrierComponent>(barrier, out var warningBarrier))
            TryPopupTarget(warningBarrier, args.Buckle.Owner);
    }

    private void HandleBlockedMovement(
        EntityUid movingEntity,
        EntityCoordinates returnCoordinates,
        EntityUid barrier,
        List<EntityUid> popupTargets)
    {
        _moveSuppressedUntil[movingEntity] = _timing.CurTime + MoveCorrectionSuppression;

        // Revert to the last known valid coordinates instead of trying to invent a push destination.
        _transform.SetCoordinates(movingEntity, returnCoordinates);
        StopPhysics(movingEntity);

        if (!TryComp<WH40KTdmWarningBarrierComponent>(barrier, out var warningBarrier))
            return;

        foreach (var popupTarget in popupTargets)
        {
            TryPopupTarget(warningBarrier, popupTarget);
        }
    }

    private bool TryGetPopupTargets(EntityUid otherEntity, out List<EntityUid> popupTargets)
    {
        popupTargets = new List<EntityUid>();

        if (HasComp<ActorComponent>(otherEntity) && !ShouldIgnoreBarrier(otherEntity))
        {
            popupTargets.Add(otherEntity);
            return true;
        }

        if (!TryComp<StrapComponent>(otherEntity, out var strap))
            return false;

        foreach (var buckledEntity in strap.BuckledEntities)
        {
            if (!HasComp<ActorComponent>(buckledEntity) ||
                TerminatingOrDeleted(buckledEntity) ||
                ShouldIgnoreBarrier(buckledEntity))
            {
                continue;
            }

            popupTargets.Add(buckledEntity);
        }

        return popupTargets.Count > 0;
    }

    private string GetPopup(WH40KTdmWarningBarrierComponent component, EntityUid target)
    {
        if (_teamBattle.TryGetTeamIdFromEntity(target, out var teamId))
        {
            var teamKey = $"{component.PopupLocPrefix}-{teamId}";
            if (Loc.HasString(teamKey))
                return Loc.GetString(teamKey);
        }

        return Loc.GetString(component.GenericPopupLocKey);
    }

    private void TryPopupTarget(WH40KTdmWarningBarrierComponent component, EntityUid target)
    {
        if (_nextPopupAt.TryGetValue(target, out var nextPopup) &&
            _timing.CurTime < nextPopup)
        {
            return;
        }

        _nextPopupAt[target] = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(component.PopupCooldownSeconds, 0.1f));
        _popup.PopupEntity(GetPopup(component, target), target, target, PopupType.MediumCaution);
    }

    private bool TryGetSeparatingBarrier(EntityUid first, EntityUid second, out EntityUid barrier)
    {
        barrier = EntityUid.Invalid;

        var firstXform = Transform(first);
        var secondXform = Transform(second);
        if (firstXform.GridUid is not { } firstGridUid ||
            secondXform.GridUid != firstGridUid ||
            !TryComp<MapGridComponent>(firstGridUid, out var grid) ||
            !_transform.TryGetGridTilePosition((first, firstXform), out var firstTile, grid) ||
            !_transform.TryGetGridTilePosition((second, secondXform), out var secondTile, grid) ||
            firstTile == secondTile)
        {
            return false;
        }

        return TryGetSeparatingBarrier(first, firstXform.Coordinates, secondXform.Coordinates, out barrier);
    }

    private bool TryGetSeparatingBarrier(EntityUid movingEntity, EntityCoordinates from, EntityCoordinates to, out EntityUid barrier)
    {
        barrier = EntityUid.Invalid;

        var movingXform = Transform(movingEntity);
        if (movingXform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return false;
        }

        var fromMap = _transform.ToMapCoordinates(from);
        var toMap = _transform.ToMapCoordinates(to);
        if (fromMap.MapId != toMap.MapId || fromMap.MapId != movingXform.MapID)
            return false;

        var fromTile = _map.CoordinatesToTile(gridUid, grid, fromMap);
        var toTile = _map.CoordinatesToTile(gridUid, grid, toMap);
        if (fromTile == toTile)
            return false;

        var query = EntityQueryEnumerator<WH40KPreparationPhaseBarrierComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var barrierXform))
        {
            if (barrierXform.GridUid != gridUid ||
                !_transform.TryGetGridTilePosition((uid, barrierXform), out var barrierTile, grid))
            {
                continue;
            }

            if (!HasBarrierBetweenTiles(fromTile, toTile, barrierTile))
                continue;

            barrier = uid;
            return true;
        }

        return false;
    }

    private static bool HasBarrierBetweenTiles(Vector2i fromTile, Vector2i toTile, Vector2i barrierTile)
    {
        if (fromTile.X == toTile.X && barrierTile.X == fromTile.X)
        {
            return barrierTile.Y >= Math.Min(fromTile.Y, toTile.Y) &&
                   barrierTile.Y <= Math.Max(fromTile.Y, toTile.Y);
        }

        if (fromTile.Y == toTile.Y && barrierTile.Y == fromTile.Y)
        {
            return barrierTile.X >= Math.Min(fromTile.X, toTile.X) &&
                   barrierTile.X <= Math.Max(fromTile.X, toTile.X);
        }

        return false;
    }

    private bool IsBuckled(EntityUid uid)
    {
        return TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled;
    }

    private bool ShouldIgnoreBarrier(EntityUid uid)
    {
        return HasComp<GhostComponent>(uid);
    }

    private bool IsMoveSuppressed(EntityUid uid)
    {
        if (!_moveSuppressedUntil.TryGetValue(uid, out var until))
            return false;

        if (_timing.CurTime >= until)
        {
            _moveSuppressedUntil.Remove(uid);
            return false;
        }

        return true;
    }

    private void StopPhysics(EntityUid uid)
    {
        if (!TryComp<PhysicsComponent>(uid, out var body))
            return;

        _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
        _physics.SetAngularVelocity(uid, 0f, body: body);
    }

    private void ClearRuntimeState()
    {
        _nextPopupAt.Clear();
        _moveSuppressedUntil.Clear();
    }
}
