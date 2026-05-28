using Content.Shared._WH40K.Psyker;
using Content.Shared.Bed.Sleep;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Actions.Components;
using Content.Shared.Popups;
using Content.Server.Actions;
using Content.Server.Popups;
using Content.Server._WH40K.Localizations;
using System.Numerics;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Handles the Imperium psyker astral trance entry point.
/// The trance is built on SleepingComponent so it respects existing Robust/SS14 sleep restrictions.
/// </summary>
public sealed partial class WH40KPsykerAstralProjectionSystem : EntitySystem
{
    private const string AstralBarrierPrototype = "WH40KPsykerAstralBarrierVisual";
    private const float AstralDamageReductionFactor = 0.5f;

    [Dependency] private  ActionsSystem _actions = default!;
    [Dependency] private  SleepingSystem _sleeping = default!;
    [Dependency] private  MobStateSystem _mobState = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  PopupSystem _popup = default!;
    [Dependency] private  WH40KPlayerCultureTracker _culture = default!;
    [Dependency] private  WH40KGlobalWarpInstabilitySystem _globalWarp = default!;
    [Dependency] private  WH40KPsykerDisciplineModifierSystem _modifiers = default!;
    [Dependency] private  WH40KPsykerAstralRiskSystem _risks = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerRoleComponent, WH40KPsykerAstralProjectionActionEvent>(OnAstralProjection);
        SubscribeLocalEvent<WH40KPsykerAstralProjectionComponent, SleepStateChangedEvent>(OnSleepStateChanged);
        SubscribeLocalEvent<WH40KPsykerAstralProjectionComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WH40KPsykerAstralProjectionComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<WH40KPsykerAstralProjectionComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WH40KPsykerRoleShutdownEvent>(OnPsykerRoleShutdown);
        SubscribeLocalEvent<WH40KChaosRoleStartupEvent>(OnChaosRoleStartup);
        SubscribeNetworkEvent<WH40KPsykerAstralExitRequestEvent>(OnExitRequest);
    }

    public override void Update(float frameTime)
    {
        if (!_globalWarp.CatastropheTriggered)
            return;

        var query = EntityQueryEnumerator<WH40KPsykerAstralProjectionComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            EndAstralProjection(uid, wake: true, WH40KPsykerAstralExitReason.Catastrophe);
        }
    }

    private void OnAstralProjection(
        Entity<WH40KPsykerRoleComponent> ent,
        ref WH40KPsykerAstralProjectionActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        if (user != ent.Owner)
            return;

        if (!CanEnterAstralProjection(user, out var blockReason))
        {
            HandleAstralProjectionBlocked(user, blockReason);
            return;
        }

        if (!_sleeping.TrySleeping(user))
            return;

        var now = _timing.CurTime;
        var astral = EnsureComp<WH40KPsykerAstralProjectionComponent>(user);
        astral.StartedAt = now;
        astral.RevealStartsAt = now + WH40KPsykerAstralMath.AstralSleepIntroDuration;
        astral.FadeEndsAt = astral.RevealStartsAt + _modifiers.GetAstralFadeDuration(user);
        astral.CanExitAt = now + _modifiers.GetAstralMinimumDuration(user);
        astral.BarrierEntity = EnsureAstralBarrier(user);
        Dirty(user, astral);

        args.Handled = true;
    }

    private bool CanEnterAstralProjection(EntityUid uid, out AstralProjectionBlockReason blockReason)
    {
        if (_globalWarp.CatastropheTriggered)
        {
            blockReason = AstralProjectionBlockReason.GlobalCatastrophe;
            return false;
        }

        if (HasComp<WH40KChaosGiftRoleComponent>(uid))
        {
            blockReason = AstralProjectionBlockReason.ChaosRole;
            return false;
        }

        if (HasComp<WH40KPsykerAstralProjectionComponent>(uid))
        {
            blockReason = AstralProjectionBlockReason.AlreadyProjecting;
            return false;
        }

        if (HasComp<SleepingComponent>(uid))
        {
            blockReason = AstralProjectionBlockReason.Sleeping;
            return false;
        }

        if (_risks.IsAstralFatigued(uid))
        {
            blockReason = AstralProjectionBlockReason.Fatigue;
            return false;
        }

        if (!_mobState.IsAlive(uid))
        {
            blockReason = AstralProjectionBlockReason.Dead;
            return false;
        }

        blockReason = AstralProjectionBlockReason.None;
        return true;
    }

    private void HandleAstralProjectionBlocked(EntityUid uid, AstralProjectionBlockReason reason)
    {
        if (reason != AstralProjectionBlockReason.Fatigue)
            return;

        var remaining = _risks.GetAstralFatigueRemaining(uid);
        if (remaining <= TimeSpan.Zero)
            return;

        SyncAstralProjectionCooldown(uid, _timing.CurTime + remaining);
        PopupAstralCooldown(uid, "wh40k-psyker-astral-popup-fatigue-locked", remaining);
    }

    private void OnExitRequest(WH40KPsykerAstralExitRequestEvent ev, EntitySessionEventArgs args)
    {
        var uid = args.SenderSession.AttachedEntity;
        if (uid == null || !HasComp<WH40KPsykerAstralProjectionComponent>(uid.Value))
            return;

        if (TryComp<WH40KPsykerAstralProjectionComponent>(uid.Value, out var astral) &&
            _timing.CurTime < astral.CanExitAt)
        {
            return;
        }

        EndAstralProjection(uid.Value, wake: true, WH40KPsykerAstralExitReason.Voluntary);
    }

    private void OnSleepStateChanged(Entity<WH40KPsykerAstralProjectionComponent> ent, ref SleepStateChangedEvent args)
    {
        if (args.FellAsleep)
            return;

        EndAstralProjection(ent.Owner, wake: false, WH40KPsykerAstralExitReason.ForcedWake);
    }

    private void OnMobStateChanged(Entity<WH40KPsykerAstralProjectionComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        EndAstralProjection(ent.Owner, wake: false, WH40KPsykerAstralExitReason.Death);
    }

    private void OnDamageChanged(Entity<WH40KPsykerAstralProjectionComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null || args.DamageDelta.GetTotal() <= 0)
            return;

        EndAstralProjection(ent.Owner, wake: true, WH40KPsykerAstralExitReason.Damage);
    }

    private void OnBeforeDamageChanged(Entity<WH40KPsykerAstralProjectionComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive())
            return;

        var positive = DamageSpecifier.GetPositive(args.Damage);
        if (positive.Empty)
            return;

        var negative = DamageSpecifier.GetNegative(args.Damage);
        args.Damage = positive * AstralDamageReductionFactor + negative;
    }

    private void OnPsykerRoleShutdown(WH40KPsykerRoleShutdownEvent args)
    {
        EndAstralProjection(args.User, wake: true, WH40KPsykerAstralExitReason.RoleLost);
    }

    private void OnChaosRoleStartup(WH40KChaosRoleStartupEvent args)
    {
        EndAstralProjection(args.User, wake: true, WH40KPsykerAstralExitReason.ChaosCorruption);
    }

    private void EndAstralProjection(EntityUid uid, bool wake, WH40KPsykerAstralExitReason reason)
    {
        if (!TryComp<WH40KPsykerAstralProjectionComponent>(uid, out var astral))
            return;

        CleanupAstralBarrier(astral);
        _risks.HandleAstralExit(uid, astral, reason);
        RemComp<WH40KPsykerAstralProjectionComponent>(uid);
        SyncAstralProjectionCooldown(uid, reason);

        if (!wake || !TryComp<SleepingComponent>(uid, out var sleeping))
            return;

        _sleeping.TryWaking((uid, sleeping), force: true);
    }

    private void SyncAstralProjectionCooldown(EntityUid uid, WH40KPsykerAstralExitReason reason)
    {
        if (!TryGetAstralProjectionAction(uid, out var action))
            return;

        var now = _timing.CurTime;
        var cooldownEnd = action.Comp.Cooldown?.End ?? now;
        var fatigueRemaining = _risks.GetAstralFatigueRemaining(uid);

        if (fatigueRemaining > TimeSpan.Zero)
            cooldownEnd = Max(cooldownEnd, now + fatigueRemaining);

        if (cooldownEnd <= now)
            return;

        _actions.SetCooldown((action.Owner, action.Comp), now, cooldownEnd);

        if (reason == WH40KPsykerAstralExitReason.Voluntary)
            PopupAstralCooldown(uid, "wh40k-psyker-astral-popup-reentry-cooldown", cooldownEnd - now);
    }

    private void SyncAstralProjectionCooldown(EntityUid uid, TimeSpan cooldownEnd)
    {
        if (!TryGetAstralProjectionAction(uid, out var action))
            return;

        var now = _timing.CurTime;
        if (cooldownEnd <= now)
            return;

        _actions.SetCooldown((action.Owner, action.Comp), now, cooldownEnd);
    }

    private bool TryGetAstralProjectionAction(EntityUid uid, out Entity<ActionComponent> action)
    {
        foreach (var candidate in _actions.GetActions(uid))
        {
            if (!string.Equals(MetaData(candidate).EntityPrototype?.ID, WH40KPsykerAstralMath.AstralProjectionActionId))
                continue;

            action = candidate;
            return true;
        }

        action = default;
        return false;
    }

    private void PopupAstralCooldown(EntityUid uid, string key, TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return;

        var seconds = Math.Max(1, (int) MathF.Ceiling((float) remaining.TotalSeconds));
        _popup.PopupEntity(
            _culture.GetPlayerString(uid, key, ("seconds", seconds)),
            uid,
            uid,
            PopupType.SmallCaution);
    }

    private EntityUid? EnsureAstralBarrier(EntityUid uid)
    {
        var barrier = Spawn(AstralBarrierPrototype, Transform(uid).Coordinates);
        _transform.SetParent(barrier, uid);
        _transform.SetLocalPosition(barrier, Vector2.Zero);
        return barrier;
    }

    private void CleanupAstralBarrier(WH40KPsykerAstralProjectionComponent astral)
    {
        if (astral.BarrierEntity is not { Valid: true } barrier || Deleted(barrier))
            return;

        QueueDel(barrier);
        astral.BarrierEntity = null;
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right)
    {
        return left >= right ? left : right;
    }

    private enum AstralProjectionBlockReason : byte
    {
        None,
        GlobalCatastrophe,
        ChaosRole,
        AlreadyProjecting,
        Sleeping,
        Fatigue,
        Dead
    }
}
