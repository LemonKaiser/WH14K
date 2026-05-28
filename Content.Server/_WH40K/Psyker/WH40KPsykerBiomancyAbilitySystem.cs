using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared._WH40K.Psyker;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Imperium biomancy utility action backend.
/// Keeps the healing effect separate from the discipline modifier tuning.
/// </summary>
public sealed partial class WH40KPsykerBiomancyAbilitySystem : EntitySystem
{
    [Dependency] private  DamageableSystem _damageable = default!;
    [Dependency] private  WH40KPsykerDisciplineModifierSystem _modifiers = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerRoleComponent, WH40KPsykerBiomanticSurgeActionEvent>(OnBiomanticSurge);
    }

    private void OnBiomanticSurge(Entity<WH40KPsykerRoleComponent> ent, ref WH40KPsykerBiomanticSurgeActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner)
            return;

        if (!TryComp<DamageableComponent>(ent.Owner, out var damageable))
            return;

        var healAmount = _modifiers.GetBiomanticSurgeHealAmount(ent.Owner);
        if (healAmount <= 0f)
            return;

        _damageable.HealEvenly(
            (ent.Owner, damageable),
            -FixedPoint2.New(healAmount),
            origin: ent.Owner,
            ignoreGlobalModifiers: true);

        args.Handled = true;
    }
}
