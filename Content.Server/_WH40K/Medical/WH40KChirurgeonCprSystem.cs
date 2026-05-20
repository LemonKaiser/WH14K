using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.Medical;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Database;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Traits.Assorted;
using Content.Shared.Verbs;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Medical;

public sealed class WH40KChirurgeonCprSystem : EntitySystem
{
    private const string VerbLoc = "wh40k-cpr-verb";
    private const string InvalidLoc = "wh40k-cpr-popup-invalid";
    private const string StartLoc = "wh40k-cpr-popup-start";
    private const string HelpedLoc = "wh40k-cpr-popup-helped";
    private const string RevivedLoc = "wh40k-cpr-popup-revived";

    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly RespiratorSystem _respirator = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedRottingSystem _rotting = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamBattle = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobStateComponent, GetVerbsEvent<InteractionVerb>>(OnGetInteractionVerbs);
        SubscribeLocalEvent<WH40KChirurgeonCprComponent, WH40KChirurgeonCprDoAfterEvent>(OnCprDoAfter);
        SubscribeLocalEvent<WH40KChirurgeonCprComponent, DoAfterAttemptEvent<WH40KChirurgeonCprDoAfterEvent>>(OnCprAttempt);
    }

    private void OnGetInteractionVerbs(Entity<MobStateComponent> target, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!TryComp<WH40KChirurgeonCprComponent>(args.User, out var cpr))
            return;

        if (!CanStartCpr(args.User, target, cpr, target.Comp))
            return;

        var user = args.User;
        var targetUid = target.Owner;

        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString(VerbLoc),
            Act = () => StartCpr(user, targetUid, cpr),
            Priority = 25,
            Impact = LogImpact.Medium,
        });
    }

    private void OnCprAttempt(Entity<WH40KChirurgeonCprComponent> user, ref DoAfterAttemptEvent<WH40KChirurgeonCprDoAfterEvent> args)
    {
        if (args.DoAfter.Args.Target is not { } target ||
            !CanStartCpr(user.Owner, target, user.Comp, popup: false))
        {
            args.Cancel();
        }
    }

    private void OnCprDoAfter(Entity<WH40KChirurgeonCprComponent> user, ref WH40KChirurgeonCprDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;

        if (!CanStartCpr(user.Owner, target, user.Comp, popup: false))
            return;

        args.Handled = true;
        var wasCritical = _mobState.IsCritical(target);

        RestoreBreathing(target);
        HealAsphyxiation(user.Owner, target, user.Comp, user.Comp.AsphyxiationHeal);

        if (wasCritical)
            TryReviveFromCpr(user.Owner, target, user.Comp);

        if (_mobState.IsCritical(target))
        {
            _popup.PopupEntity(Loc.GetString(HelpedLoc), target, user.Owner, PopupType.Small);
            args.Repeat = true;
            return;
        }

        _popup.PopupEntity(Loc.GetString(RevivedLoc), target, user.Owner, PopupType.Medium);
    }

    private void StartCpr(EntityUid user, EntityUid target, WH40KChirurgeonCprComponent cpr)
    {
        if (!CanStartCpr(user, target, cpr, popup: true))
            return;

        var doAfterArgs = new DoAfterArgs(EntityManager, user, cpr.DoAfter, new WH40KChirurgeonCprDoAfterEvent(), user, target)
        {
            NeedHand = true,
            BreakOnDamage = true,
            BreakOnMove = true,
            AttemptFrequency = AttemptFrequency.EveryTick,
            DistanceThreshold = 1.5f,
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs))
            _popup.PopupEntity(Loc.GetString(StartLoc), target, user, PopupType.Small);
    }

    private bool CanStartCpr(
        EntityUid user,
        EntityUid target,
        WH40KChirurgeonCprComponent? cpr = null,
        MobStateComponent? mobState = null,
        bool popup = false)
    {
        if (user == target ||
            !Resolve(user, ref cpr, false) ||
            !Resolve(target, ref mobState, false) ||
            !_mobState.IsCritical(target, mobState) ||
            !IsValidCprTarget(target) ||
            HasComp<UnrevivableComponent>(target) ||
            _rotting.IsRotten(target) ||
            IsEnemyMedicalTarget(user, target))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString(InvalidLoc), target, user, PopupType.SmallCaution);

            return false;
        }

        return true;
    }

    private bool IsValidCprTarget(EntityUid target)
    {
        // Catatonic / SSD bodies may temporarily have no active mind attached,
        // but they should still be treated like valid medical CPR targets.
        return HasComp<MindExaminableComponent>(target) ||
               (TryComp<MindContainerComponent>(target, out var mind) && mind.HasMind);
    }

    private void RestoreBreathing(EntityUid target)
    {
        if (TryComp<RespiratorComponent>(target, out var respirator))
            _respirator.RestoreSaturationBuffer((target, respirator));
    }

    private void HealAsphyxiation(
        EntityUid user,
        EntityUid target,
        WH40KChirurgeonCprComponent cpr,
        FixedPoint2 amount)
    {
        if (amount <= FixedPoint2.Zero ||
            !TryComp<DamageableComponent>(target, out var damageable))
        {
            return;
        }

        var healing = new DamageSpecifier();
        healing.DamageDict[cpr.AsphyxiationDamageType] = -amount;

        _damageable.TryChangeDamage(
            (target, damageable),
            healing,
            ignoreResistances: true,
            interruptsDoAfters: false,
            origin: user,
            ignoreGlobalModifiers: true);
    }

    private void TryReviveFromCpr(EntityUid user, EntityUid target, WH40KChirurgeonCprComponent cpr)
    {
        if (!_random.Prob(cpr.ReviveChance) ||
            !TryComp<DamageableComponent>(target, out var damageable) ||
            !TryComp<MobThresholdsComponent>(target, out var thresholds) ||
            !_mobThreshold.TryGetThresholdForState(target, MobState.Critical, out var criticalThreshold, thresholds))
        {
            return;
        }

#pragma warning disable CS0618
        var totalDamage = _damageable.GetTotalDamage((target, damageable));
#pragma warning restore CS0618
        var neededHeal = totalDamage - criticalThreshold.Value + cpr.ReviveBuffer;
        if (neededHeal <= FixedPoint2.Zero)
        {
            _mobThreshold.VerifyThresholds(target, thresholds, damageable: damageable);
            return;
        }

#pragma warning disable CS0618
        var asphyxiationDamage = _damageable.GetAllDamage((target, damageable)).DamageDict.GetValueOrDefault(cpr.AsphyxiationDamageType);
#pragma warning restore CS0618
        if (asphyxiationDamage < neededHeal)
            return;

        HealAsphyxiation(user, target, cpr, neededHeal);
    }

    private bool IsEnemyMedicalTarget(EntityUid user, EntityUid target)
    {
        if (!_teamBattle.TryGetTeamIdFromEntity(user, out var sourceTeamId) ||
            !_teamBattle.TryGetTeamIdFromEntity(target, out var targetTeamId))
        {
            return false;
        }

        return !string.Equals(sourceTeamId, targetTeamId, StringComparison.Ordinal);
    }
}
