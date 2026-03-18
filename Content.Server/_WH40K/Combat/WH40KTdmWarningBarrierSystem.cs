using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Popups;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.Combat;
using Content.Shared._WH40K.GameMode;
using Content.Shared.Popups;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Combat;

public sealed class WH40KTdmWarningBarrierSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamBattle = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextPopupAt = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KPreparationPhaseBarrierComponent, MapInitEvent>(OnBarrierMapInit);
        SubscribeLocalEvent<WH40KBattlePhaseChangedEvent>(OnPhaseChanged);
        SubscribeLocalEvent<WH40KTdmWarningBarrierComponent, StartCollideEvent>(OnStartCollide);
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
    }

    private void OnStartCollide(Entity<WH40KTdmWarningBarrierComponent> ent, ref StartCollideEvent args)
    {
        if (!HasComp<ActorComponent>(args.OtherEntity) ||
            TerminatingOrDeleted(args.OtherEntity))
        {
            return;
        }

        var otherXform = Transform(args.OtherEntity);
        var otherPosition = _transform.GetWorldPosition(args.OtherEntity);
        var barrierPosition = _transform.GetWorldPosition(ent.Owner);
        var pushDirection = otherPosition - barrierPosition;

        if (pushDirection.LengthSquared() < 0.0001f)
        {
            if (TryComp<PhysicsComponent>(args.OtherEntity, out var movingBody) &&
                movingBody.LinearVelocity.LengthSquared() > 0.0001f)
            {
                pushDirection = movingBody.LinearVelocity;
            }
            else if (args.WorldNormal.LengthSquared() > 0.0001f)
            {
                pushDirection = args.WorldNormal;
            }
            else
            {
                pushDirection = Vector2.UnitY;
            }
        }

        pushDirection = Vector2.Normalize(pushDirection);
        _transform.SetWorldPosition((args.OtherEntity, otherXform), otherPosition + pushDirection * MathF.Max(ent.Comp.PushbackDistance, 0.1f));

        if (TryComp<PhysicsComponent>(args.OtherEntity, out var otherPhysics) &&
            otherPhysics.BodyType != BodyType.Static)
        {
            _physics.SetLinearVelocity(args.OtherEntity, Vector2.Zero, body: otherPhysics);
        }

        if (_nextPopupAt.TryGetValue(args.OtherEntity, out var nextPopup) &&
            _timing.CurTime < nextPopup)
        {
            return;
        }

        _nextPopupAt[args.OtherEntity] = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(ent.Comp.PopupCooldownSeconds, 0.1f));
        _popup.PopupEntity(GetPopup(ent.Comp, args.OtherEntity), args.OtherEntity, args.OtherEntity, PopupType.MediumCaution);
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
}
