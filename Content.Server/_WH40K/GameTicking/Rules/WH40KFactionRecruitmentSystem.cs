using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server.Popups;
using Content.Shared._WH40K.GameTicking.Rules;
using Content.Shared.DoAfter;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;

namespace Content.Server._WH40K.GameTicking.Rules;

public sealed partial class WH40KFactionRecruitmentSystem : EntitySystem
{
    private const string VerbLoc = "wh40k-recruitment-verb";
    private const string StartLoc = "wh40k-recruitment-start";
    private const string InvalidLoc = "wh40k-recruitment-invalid";
    private const string CompleteLoc = "wh40k-recruitment-complete";

    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private WH40KTeamBattleRuleSystem _teamBattle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KTeamMemberComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
        SubscribeLocalEvent<WH40KFactionRecruiterComponent, WH40KFactionRecruitmentDoAfterEvent>(OnRecruitmentDoAfter);
    }

    private void OnGetAlternativeVerbs(Entity<WH40KTeamMemberComponent> target, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.User == target.Owner)
            return;

        if (!TryComp<WH40KFactionRecruiterComponent>(args.User, out var recruiter))
            return;

        if (!CanRecruit(args.User, target.Owner, recruiter, popup: false))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(VerbLoc),
            Priority = 25,
            Act = () => TryStartRecruitment(user, target.Owner, recruiter),
        });
    }

    private void TryStartRecruitment(EntityUid user, EntityUid target, WH40KFactionRecruiterComponent recruiter)
    {
        if (!CanRecruit(user, target, recruiter, popup: true))
            return;

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            user,
            recruiter.DoAfter,
            new WH40KFactionRecruitmentDoAfterEvent(),
            user,
            target)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = false,
            DistanceThreshold = 1.5f,
            DuplicateCondition = DuplicateConditions.SameTarget | DuplicateConditions.SameEvent,
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs))
            _popup.PopupEntity(Loc.GetString(StartLoc), target, user, PopupType.Small);
    }

    private void OnRecruitmentDoAfter(Entity<WH40KFactionRecruiterComponent> recruiter, ref WH40KFactionRecruitmentDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;

        args.Handled = true;

        if (!CanRecruit(recruiter.Owner, target, recruiter.Comp, popup: true))
            return;

        if (!_teamBattle.TryGetTeamIdFromEntity(recruiter.Owner, out var recruiterTeamId) ||
            !_teamBattle.TrySetEntityTeam(target, recruiterTeamId))
        {
            _popup.PopupEntity(Loc.GetString(InvalidLoc), target, recruiter.Owner, PopupType.SmallCaution);
            return;
        }

        _teamBattle.TryGrantRecruitmentReward(recruiterTeamId, recruiter.Comp.RewardMultiplier);
        _popup.PopupEntity(Loc.GetString(CompleteLoc), target, recruiter.Owner, PopupType.Medium);
    }

    private bool CanRecruit(
        EntityUid user,
        EntityUid target,
        WH40KFactionRecruiterComponent? recruiter,
        bool popup)
    {
        if (!Resolve(user, ref recruiter, false) ||
            !TryComp<WH40KTeamMemberComponent>(user, out var userTeam) ||
            !TryComp<WH40KTeamMemberComponent>(target, out var targetTeam) ||
            string.IsNullOrWhiteSpace(userTeam.TeamId) ||
            string.IsNullOrWhiteSpace(targetTeam.TeamId) ||
            string.Equals(userTeam.TeamId, targetTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
            !_mobState.IsAlive(user) ||
            !_mobState.IsAlive(target) ||
            _mobState.IsCritical(target))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString(InvalidLoc), target, user, PopupType.SmallCaution);

            return false;
        }

        return true;
    }
}
