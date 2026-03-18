using Content.Shared.Actions.Events;
using Robust.Shared.Network;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Shared validation and spending logic for warp-powered actions.
/// Server also runs passive regen/decay for warp resource channels.
/// </summary>
public sealed class SharedWH40KWarpResourceSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;

    public override void Initialize()
    {
        // ComponentStartup is exclusive per (component,event), so role bootstrap stays centralized here.
        SubscribeLocalEvent<WH40KPsykerRoleComponent, ComponentStartup>(OnPsykerRoleStartup);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, ComponentStartup>(OnChaosRoleStartup);
        SubscribeLocalEvent<WH40KWarpActionCostComponent, ActionValidateEvent>(OnValidateWarpAction);
        SubscribeLocalEvent<WH40KWarpActionCostComponent, ActionPerformedEvent>(OnWarpActionPerformed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_netManager.IsServer || frameTime <= 0f)
            return;

        var warpQuery = EntityQueryEnumerator<WH40KWarpResourceComponent>();
        while (warpQuery.MoveNext(out var uid, out var warp))
        {
            if (warp.RegenPerSecond <= 0f || warp.CurrentCharge >= warp.MaxCharge)
                continue;

            var next = MathF.Min(warp.MaxCharge, warp.CurrentCharge + warp.RegenPerSecond * frameTime);
            if (next <= warp.CurrentCharge)
                continue;

            warp.CurrentCharge = next;
            Dirty(uid, warp);
        }

        var instabilityQuery = EntityQueryEnumerator<WH40KWarpInstabilityComponent>();
        while (instabilityQuery.MoveNext(out var uid, out var instability))
        {
            if (instability.DecayPerSecond <= 0f || instability.CurrentInstability <= 0f)
                continue;

            var next = MathF.Max(0f, instability.CurrentInstability - instability.DecayPerSecond * frameTime);
            if (next >= instability.CurrentInstability)
                continue;

            instability.CurrentInstability = next;
            Dirty(uid, instability);
        }
    }

    private void OnValidateWarpAction(Entity<WH40KWarpActionCostComponent> ent, ref ActionValidateEvent args)
    {
        if (!HasAllowedRole(args.User, ent.Comp))
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

        if (ent.Comp.WarpChargeCost > 0f &&
            TryComp<WH40KWarpResourceComponent>(args.Performer, out var warp))
        {
            var next = Math.Clamp(warp.CurrentCharge - ent.Comp.WarpChargeCost, 0f, warp.MaxCharge);
            if (next < warp.CurrentCharge)
            {
                warp.CurrentCharge = next;
                Dirty(args.Performer, warp);
            }
        }

        var castEvent = new WH40KWarpActionCastEvent(args.Performer, ent.Owner, ResolveActionKey(ent.Owner));
        RaiseLocalEvent(args.Performer, castEvent);

        if (ent.Comp.InstabilityGain > 0f &&
            TryComp<WH40KWarpInstabilityComponent>(args.Performer, out var instability))
        {
            var next = Math.Clamp(instability.CurrentInstability + ent.Comp.InstabilityGain, 0f, instability.MaxInstability);
            if (next > instability.CurrentInstability)
            {
                instability.CurrentInstability = next;
                Dirty(args.Performer, instability);
            }
        }
    }

    private bool HasAllowedRole(EntityUid uid, WH40KWarpActionCostComponent cost)
    {
        if (!cost.RequireWarpRole)
            return true;

        return cost.AllowPsykerRole && HasComp<WH40KPsykerRoleComponent>(uid) ||
               cost.AllowChaosRole && HasComp<WH40KChaosGiftRoleComponent>(uid);
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

    private void OnChaosRoleStartup(EntityUid uid, WH40KChaosGiftRoleComponent component, ref ComponentStartup args)
    {
        if (!_netManager.IsServer)
            return;

        EnsureComp<WH40KWarpResourceComponent>(uid);
        EnsureComp<WH40KWarpInstabilityComponent>(uid);
        EnsureComp<WH40KChaosGiftProgressionComponent>(uid);
        EnsureComp<WH40KChaosGiftStarterActionLoadoutComponent>(uid);

        RaiseLocalEvent(uid, new WH40KChaosRoleStartupEvent(uid));
    }
}
