using Content.Server._WH40K.GameTicking.Rules;
using Content.Server.Stunnable.Components;
using Content.Server.Stunnable.Systems;
using Content.Shared.CCVar;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Electrocution;
using Content.Shared.Ensnaring;
using Content.Shared.Ensnaring.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Configuration;

namespace Content.Server._WH40K.Combat;

/// <summary>
/// Extends WH40K TeamBattle friendly-fire protection to harmful side-effect channels
/// that do not use the normal damage pipeline.
/// </summary>
public sealed partial class WH40KFriendlyFireProtectionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private WH40KAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private WH40KTeamRuleFacadeSystem _teamRule = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StaminaComponent, BeforeStaminaDamageEvent>(OnBeforeStaminaDamage);
        SubscribeLocalEvent<DamageableComponent, ElectrocutionAttemptEvent>(OnElectrocutionAttempt);
        SubscribeLocalEvent<EnsnaringComponent, BeforeEnsnareAttemptEvent>(OnBeforeEnsnareAttempt);
        SubscribeLocalEvent<StunOnCollideComponent, BeforeStunOnCollideEvent>(OnBeforeStunOnCollide);
        SubscribeLocalEvent<FlammableComponent, BeforeIgniteOnCollideEvent>(OnBeforeIgniteOnCollide);
    }

    private void OnBeforeStaminaDamage(Entity<StaminaComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        if (args.Cancelled || args.Value <= 0f || args.Source == null)
            return;

        if (ShouldBlockFriendlyEffect(ent.Owner, args.Source.Value))
            args.Cancelled = true;
    }

    private void OnElectrocutionAttempt(Entity<DamageableComponent> ent, ref ElectrocutionAttemptEvent args)
    {
        if (args.Cancelled || args.SourceUid == null)
            return;

        if (ShouldBlockFriendlyEffect(ent.Owner, args.SourceUid.Value))
            args.Cancel();
    }

    private void OnBeforeStunOnCollide(Entity<StunOnCollideComponent> ent, ref BeforeStunOnCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (ShouldBlockFriendlyEffect(args.Target, args.Source))
            args.Cancelled = true;
    }

    private void OnBeforeEnsnareAttempt(Entity<EnsnaringComponent> ent, ref BeforeEnsnareAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (ShouldBlockFriendlyEffect(args.Target, args.Source))
            args.Cancelled = true;
    }

    private void OnBeforeIgniteOnCollide(Entity<FlammableComponent> ent, ref BeforeIgniteOnCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (ShouldBlockFriendlyEffect(ent.Owner, args.Source))
            args.Cancelled = true;
    }

    private bool ShouldBlockFriendlyEffect(EntityUid target, EntityUid source)
    {
        if (!_config.GetCVar(CCVars.WH40KFriendlyFireDisabled))
            return false;

        if (!_attackerResolver.TryResolveAttacker(source, out var attacker, out _))
            attacker = source;

        if (attacker == target)
            return false;

        if (!_teamRule.TryGetTeamIdFromEntity(attacker, out var attackerTeam) ||
            !_teamRule.TryGetTeamIdFromEntity(target, out var targetTeam))
        {
            return false;
        }

        return attackerTeam == targetTeam;
    }
}
