using System;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared._WH40K.Psyker;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.GameMode;

namespace Content.Server._WH40K.Psyker;

public sealed partial class WH40KChaosLeaderAbilitySystem : EntitySystem
{
    private const float SacrificeWarpRestore = 100f;
    private const float SacrificeCultXpReward = 100f;
    private static readonly TimeSpan SacrificeCooldown = TimeSpan.FromMinutes(5);
    private const string LeaderSacrificeAction = "ActionWH40KChaosLeaderSacrifice";

    [Dependency] private  SharedActionsSystem _actions = default!;
    [Dependency] private  WH40KChaosCultSystem _cult = default!;
    [Dependency] private  WH40KTeamBattleRuleSystem _teamRule = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, WH40KChaosLeaderSacrificeActionEvent>(OnLeaderSacrifice);
    }

    private void OnLeaderSacrifice(Entity<WH40KChaosGiftRoleComponent> ent, ref WH40KChaosLeaderSacrificeActionEvent args)
    {
        if (!TryComp<WH40KChaosGiftProgressionComponent>(args.Performer, out var progression) ||
            progression.AttunedPatron == WH40KChaosPatron.None ||
            !_cult.IsEffectiveLeader(ent.Owner, progression) ||
            args.Target == args.Performer ||
            TerminatingOrDeleted(args.Target) ||
            !IsLeaderSacrificeAction(args.Action.Owner) ||
            !IsEligibleFollowerTarget(args.Target, progression.AttunedPatron))
        {
            return;
        }

        if (_teamRule.GetCurrentPhase() < WH40KBattlePhase.Assault)
            return;

        _actions.SetUseDelay((args.Action.Owner, args.Action.Comp), SacrificeCooldown);
        RestoreWarpCharge(args.Performer, SacrificeWarpRestore);
        _cult.AddCultXp(progression.AttunedPatron, SacrificeCultXpReward);

        var coords = Transform(args.Target).Coordinates;
        QueueDel(args.Target);
        Spawn("Ash", coords);
        args.Handled = true;
    }

    private bool IsEligibleFollowerTarget(EntityUid target, WH40KChaosPatron patron)
    {
        return HasComp<WH40KChaosGiftRoleComponent>(target) &&
               !HasComp<WH40KChaosLeaderRoleComponent>(target) &&
               TryComp<WH40KChaosGiftProgressionComponent>(target, out var targetProgression) &&
               targetProgression.AttunedPatron == patron;
    }

    private bool IsLeaderSacrificeAction(EntityUid actionUid)
    {
        return string.Equals(
            MetaData(actionUid).EntityPrototype?.ID,
            LeaderSacrificeAction,
            StringComparison.Ordinal);
    }

    private void RestoreWarpCharge(EntityUid uid, float amount)
    {
        if (!TryComp<WH40KWarpResourceComponent>(uid, out var warp) || amount <= 0f)
            return;

        var next = Math.Clamp(warp.CurrentCharge + amount, 0f, warp.MaxCharge);
        if (next <= warp.CurrentCharge)
            return;

        warp.CurrentCharge = next;
        Dirty(uid, warp);
    }
}
