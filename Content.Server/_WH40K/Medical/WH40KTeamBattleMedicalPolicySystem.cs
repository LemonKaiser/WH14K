#nullable disable warnings

using Content.Server._WH40K.Combat;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Medical;
using Content.Shared.Medical.Healing;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Medical;

public sealed class WH40KTeamBattleMedicalPolicySystem : EntitySystem
{
    [Dependency] private readonly WH40KAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamBattle = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KTeamMemberComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WH40KTeamMemberComponent, TargetBeforeDefibrillatorZapsEvent>(OnTargetBeforeDefibrillatorZaps);
        SubscribeLocalEvent<WH40KTeamMemberComponent, TargetBeforeInjectEvent>(OnTargetBeforeInject);
        SubscribeLocalEvent<WH40KTeamMemberComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
    }

    private void OnInteractUsing(Entity<WH40KTeamMemberComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !IsRestrictedMedicalTool(args.Used))
            return;

        if (!ShouldBlockEnemyMedicalInteraction(args.User, ent.Owner))
            return;

        args.Handled = true;
    }

    private void OnTargetBeforeDefibrillatorZaps(Entity<WH40KTeamMemberComponent> ent, ref TargetBeforeDefibrillatorZapsEvent args)
    {
        if (ShouldBlockEnemyMedicalInteraction(args.EntityUsingDefib, ent.Owner))
            args.Cancel();
    }

    private void OnTargetBeforeInject(Entity<WH40KTeamMemberComponent> ent, ref TargetBeforeInjectEvent args)
    {
        if (ShouldBlockEnemyMedicalInteraction(args.EntityUsingInjector, ent.Owner))
            args.Cancel();
    }

    private void OnBeforeDamageChanged(Entity<WH40KTeamMemberComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || args.Origin == null || args.Damage.GetTotal() >= 0)
            return;

        if (!_attackerResolver.TryResolveAttacker(args.Origin.Value, out var sourceEntity, out _))
            sourceEntity = args.Origin.Value;

        if (ShouldBlockEnemyMedicalInteraction(sourceEntity, ent.Owner))
            args.Cancelled = true;
    }

    private bool IsRestrictedMedicalTool(EntityUid used)
    {
        return HasComp<DefibrillatorComponent>(used) ||
               HasComp<HealingComponent>(used) ||
               HasComp<InjectorComponent>(used);
    }

    private bool ShouldBlockEnemyMedicalInteraction(EntityUid user, EntityUid target)
    {
        if (user == target)
            return false;

        if (!_teamBattle.TryGetTeamIdFromEntity(user, out var sourceTeamId))
            return false;

        if (!_teamBattle.TryGetTeamIdFromEntity(target, out var targetTeamId))
            return false;

        return !string.Equals(sourceTeamId, targetTeamId, System.StringComparison.Ordinal);
    }
}
