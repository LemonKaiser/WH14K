using System;
using Content.Server.Atmos.EntitySystems;
using Content.Server._WH40K.Clothing.Components;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared.Atmos.Components;
using Content.Shared.Clothing;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Clothing;

public sealed partial class WH40KChaosDamnationClothingSystem : EntitySystem
{
    [Dependency] private  FlammableSystem _flammable = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  WH40KTeamBattleRuleSystem _teamRule = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KChaosDamnationClothingComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<WH40KChaosDamnationClothingComponent, ClothingGotUnequippedEvent>(OnUnequipped);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KChaosDamnationClothingComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.PendingWearer is not { } wearer ||
                now < component.NextIgniteAt)
            {
                continue;
            }

            if (component.FireStacks <= 0f)
            {
                Reset(component);
                continue;
            }

            if (Deleted(wearer) ||
                !IsTargetWearer(wearer, component.TargetTeamId) ||
                !TryComp<FlammableComponent>(wearer, out var flammable))
            {
                Reset(component);
                continue;
            }

            _flammable.AdjustFireStacks(wearer, component.FireStacks, flammable, ignite: false);
            _flammable.Ignite(wearer, uid, flammable);
            component.NextIgniteAt = now + TimeSpan.FromSeconds(Math.Max(0.1f, component.TickIntervalSeconds));
        }
    }

    private void OnEquipped(Entity<WH40KChaosDamnationClothingComponent> ent, ref ClothingGotEquippedEvent args)
    {
        if (!IsTargetWearer(args.Wearer, ent.Comp.TargetTeamId))
        {
            Reset(ent.Comp);
            return;
        }

        ent.Comp.PendingWearer = args.Wearer;
        ent.Comp.NextIgniteAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0f, ent.Comp.DelaySeconds));
    }

    private void OnUnequipped(Entity<WH40KChaosDamnationClothingComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        if (ent.Comp.PendingWearer != args.Wearer)
            return;

        Reset(ent.Comp);
    }

    private bool IsTargetWearer(EntityUid wearer, string targetTeamId)
    {
        return _teamRule.TryGetTeamIdFromEntity(wearer, out var teamId) &&
               string.Equals(teamId, targetTeamId, StringComparison.OrdinalIgnoreCase);
    }

    private static void Reset(WH40KChaosDamnationClothingComponent component)
    {
        component.PendingWearer = null;
        component.NextIgniteAt = TimeSpan.Zero;
    }
}
