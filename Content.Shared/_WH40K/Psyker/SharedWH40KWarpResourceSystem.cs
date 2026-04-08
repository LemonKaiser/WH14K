using Content.Shared.Actions.Events;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Shared validation and spending logic for warp-powered actions.
/// Server also runs passive regen/decay for warp resource channels.
/// </summary>
public sealed class SharedWH40KWarpResourceSystem : EntitySystem
{
    private static readonly TimeSpan PassiveNetworkSyncCooldown = TimeSpan.FromSeconds(0.25);

    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KWarpResourceComponent, ComponentStartup>(OnWarpResourceStartup);
        SubscribeLocalEvent<WH40KWarpActionCostComponent, ActionValidateEvent>(OnValidateWarpAction);
        SubscribeLocalEvent<WH40KWarpActionCostComponent, ActionPerformedEvent>(OnWarpActionPerformed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_netManager.IsServer || frameTime <= 0f)
            return;

        var now = _timing.CurTime;

        var warpQuery = EntityQueryEnumerator<WH40KWarpResourceComponent>();
        while (warpQuery.MoveNext(out var uid, out var warp))
        {
            if (warp.RegenPerSecond <= 0f || warp.CurrentCharge >= warp.MaxCharge)
                continue;

            var next = MathF.Min(warp.MaxCharge, warp.CurrentCharge + warp.RegenPerSecond * frameTime);
            if (next <= warp.CurrentCharge)
                continue;

            warp.CurrentCharge = next;
            if (now < warp.NextNetworkSyncAt)
                continue;

            warp.NextNetworkSyncAt = now + PassiveNetworkSyncCooldown;
            Dirty(uid, warp);
        }
    }

    private void OnValidateWarpAction(Entity<WH40KWarpActionCostComponent> ent, ref ActionValidateEvent args)
    {
        if (!HasAllowedRole(args.User, ent.Comp))
        {
            args.Invalid = true;
            return;
        }

        if (IsWarpSealed(args.User))
        {
            args.Invalid = true;
            return;
        }

        if (ent.Comp.WarpChargeCost <= 0f)
            return;

        if (!TryComp<WH40KWarpResourceComponent>(args.User, out var warp) ||
            warp.CurrentCharge + 0.001f < ent.Comp.WarpChargeCost)
        {
            args.Invalid = true;
        }
    }

    private void OnWarpActionPerformed(Entity<WH40KWarpActionCostComponent> ent, ref ActionPerformedEvent args)
    {
        if (!HasAllowedRole(args.Performer, ent.Comp))
            return;

        if (IsWarpSealed(args.Performer))
            return;

        var actionKey = ResolveActionKey(ent.Owner);

        if (ent.Comp.WarpChargeCost > 0f &&
            TryComp<WH40KWarpResourceComponent>(args.Performer, out var warp))
        {
            var next = Math.Clamp(warp.CurrentCharge - ent.Comp.WarpChargeCost, 0f, warp.MaxCharge);
            if (next < warp.CurrentCharge)
            {
                warp.CurrentCharge = next;
                warp.NextNetworkSyncAt = _timing.CurTime + PassiveNetworkSyncCooldown;
                Dirty(args.Performer, warp);
            }
        }

        var castEvent = new WH40KWarpActionCastEvent(args.Performer, ent.Owner, actionKey);
        RaiseLocalEvent(args.Performer, castEvent);

        if (ent.Comp.InstabilityGain > 0f)
            RaiseLocalEvent(new WH40KWarpInstabilityContributionEvent(args.Performer, ent.Comp.InstabilityGain, actionKey));
    }

    private bool HasAllowedRole(EntityUid uid, WH40KWarpActionCostComponent cost)
    {
        if (!cost.RequireWarpRole)
            return true;

        return cost.AllowPsykerRole && HasComp<WH40KPsykerRoleComponent>(uid) ||
               cost.AllowChaosRole && HasComp<WH40KChaosGiftRoleComponent>(uid);
    }

    private bool IsWarpSealed(EntityUid uid)
    {
        return TryComp<WH40KWarpInstabilityComponent>(uid, out var instability) &&
               instability.DecayPerSecond <= 0f &&
               instability.CurrentInstability + 0.001f >= instability.MaxInstability;
    }

    private string ResolveActionKey(EntityUid actionUid)
    {
        if (TryComp(actionUid, out MetaDataComponent? meta) &&
            meta.EntityPrototype is { } proto)
        {
            return proto.ID;
        }

        return actionUid.ToString();
    }

    private void OnPsykerRoleStartup(EntityUid uid, WH40KPsykerRoleComponent component, ref ComponentStartup args)
    {
        if (!_netManager.IsServer)
            return;

        EnsureComp<WH40KWarpResourceComponent>(uid);
        EnsureComp<WH40KWarpInstabilityComponent>(uid);
        EnsureComp<WH40KPsykerProgressionComponent>(uid);
        EnsureComp<WH40KPsykerStarterActionLoadoutComponent>(uid);
    }

    private void OnWarpResourceStartup(EntityUid uid, WH40KWarpResourceComponent component, ref ComponentStartup args)
    {
        if (!_netManager.IsServer)
            return;

        component.NextNetworkSyncAt = _timing.CurTime + PassiveNetworkSyncCooldown;
    }
}
