using System;
using Content.Server.Popups;
using Content.Server._WH40K.Localizations;
using Content.Shared.Popups;
using Content.Shared.Mobs.Systems;
using Content.Shared._WH40K.Psyker;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Psyker;

public sealed class WH40KPsykerAstralRiskSystem : EntitySystem
{
    private static readonly TimeSpan StrainGracePeriod = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StrainTickInterval = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan StrainDecayInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CatastropheFatigueBonus = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan RoleLossFatigueBonus = TimeSpan.FromSeconds(4);
    private const float StrainPerTick = 1f;
    private const float ExitStrainInstabilityScale = 0.15f;
    private const float ExitStrainFatigueScale = 0.8f;
    private const float MaxExitInstabilityBonus = 3f;
    private const float MaxExitFatigueBonusSeconds = 12f;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly WH40KPlayerCultureTracker _culture = default!;
    [Dependency] private readonly WH40KGlobalWarpInstabilitySystem _globalWarp = default!;
    [Dependency] private readonly WH40KPsykerDisciplineModifierSystem _modifiers = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WH40KPsykerAstralProgressionComponent, WH40KPsykerRoleComponent>();
        while (query.MoveNext(out var uid, out var progression, out _))
        {
            var changed = TryComp<WH40KPsykerAstralProjectionComponent>(uid, out var astral)
                ? UpdateAstralStrain(progression, astral)
                : DecayAstralStrain(progression);

            if (changed)
                Dirty(uid, progression);
        }
    }

    public void HandleAstralEntry(EntityUid uid, WH40KPsykerAstralProjectionComponent astral)
    {
        var progression = EnsureComp<WH40KPsykerAstralProgressionComponent>(uid);
        progression.LastAstralSessionAt = _timing.CurTime;
        progression.NextStrainDecayAt = TimeSpan.Zero;
        astral.NextStrainTickAt = _timing.CurTime + StrainGracePeriod;
        Dirty(uid, progression);

        var instability = _modifiers.GetAstralEntryInstabilityContribution(uid);
        if (instability > 0f && !_globalWarp.CatastropheTriggered)
            RaiseLocalEvent(new WH40KWarpInstabilityContributionEvent(uid, instability, "psyker.astral.enter"));
    }

    public void HandleRiskyNodePurchase(EntityUid uid, WH40KPsykerDisciplineNodePrototype node)
    {
        var risk = MathF.Max(0f, node.InstabilityRisk);
        if (risk <= 0f || _globalWarp.CatastropheTriggered)
            return;

        RaiseLocalEvent(new WH40KWarpInstabilityContributionEvent(uid, risk, $"psyker.astral.node.{node.ID}"));
    }

    public bool IsAstralFatigued(EntityUid uid)
    {
        return TryComp<WH40KPsykerAstralProgressionComponent>(uid, out var progression) &&
               _timing.CurTime < progression.AstralFatigueUntil;
    }

    public TimeSpan GetAstralFatigueRemaining(EntityUid uid)
    {
        if (!TryComp<WH40KPsykerAstralProgressionComponent>(uid, out var progression))
            return TimeSpan.Zero;

        var remaining = progression.AstralFatigueUntil - _timing.CurTime;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public void HandleAstralExit(EntityUid uid, WH40KPsykerAstralProjectionComponent astral, WH40KPsykerAstralExitReason reason)
    {
        if (!TryComp<WH40KPsykerAstralProgressionComponent>(uid, out var progression))
            return;

        progression.NextStrainDecayAt = progression.AstralStrain > 0f
            ? _timing.CurTime + StrainDecayInterval
            : TimeSpan.Zero;

        if (reason == WH40KPsykerAstralExitReason.Voluntary || !_mobState.IsAlive(uid))
            return;

        var sessionDuration = _timing.CurTime - astral.StartedAt;
        var overtime = sessionDuration > StrainGracePeriod
            ? sessionDuration - StrainGracePeriod
            : TimeSpan.Zero;
        var sessionFatigueBonus = TimeSpan.FromSeconds(MathF.Min(4f, (float) overtime.TotalSeconds / 30f));
        var extraFatigue = TimeSpan.FromSeconds(MathF.Min(MaxExitFatigueBonusSeconds, progression.AstralStrain * ExitStrainFatigueScale));
        var fatigue = _modifiers.GetAstralFatigueDuration(uid) + extraFatigue + sessionFatigueBonus;

        if (reason == WH40KPsykerAstralExitReason.Catastrophe)
            fatigue += CatastropheFatigueBonus;
        else if (reason is WH40KPsykerAstralExitReason.RoleLost or WH40KPsykerAstralExitReason.ChaosCorruption)
            fatigue += RoleLossFatigueBonus;

        var fatigueUntil = _timing.CurTime + fatigue;
        if (fatigueUntil > progression.AstralFatigueUntil)
        {
            progression.AstralFatigueUntil = fatigueUntil;
            Dirty(uid, progression);
        }

        if (reason != WH40KPsykerAstralExitReason.Catastrophe && !_globalWarp.CatastropheTriggered)
        {
            var bonus = MathF.Min(MaxExitInstabilityBonus, progression.AstralStrain * ExitStrainInstabilityScale);
            var instability = _modifiers.GetAstralForcedWakeInstabilityContribution(uid) + bonus;
            if (instability > 0f)
                RaiseLocalEvent(new WH40KWarpInstabilityContributionEvent(uid, instability, "psyker.astral.forced_exit"));
        }

        var seconds = Math.Max(1, (int) MathF.Ceiling((float) fatigue.TotalSeconds));
        var popupKey = reason == WH40KPsykerAstralExitReason.Catastrophe
            ? "wh40k-psyker-astral-popup-catastrophe-exit"
            : "wh40k-psyker-astral-popup-violent-exit";

        _popup.PopupEntity(
            _culture.GetPlayerString(uid, popupKey, ("seconds", seconds)),
            uid,
            uid,
            PopupType.SmallCaution);
    }

    private bool UpdateAstralStrain(WH40KPsykerAstralProgressionComponent progression, WH40KPsykerAstralProjectionComponent astral)
    {
        if (progression.AstralStrain >= WH40KPsykerAstralMath.MaxAstralStrain && astral.NextStrainTickAt > _timing.CurTime)
            return false;

        if (astral.NextStrainTickAt == TimeSpan.Zero)
            astral.NextStrainTickAt = astral.StartedAt + StrainGracePeriod;

        var changed = false;
        while (_timing.CurTime >= astral.NextStrainTickAt)
        {
            var next = MathF.Min(WH40KPsykerAstralMath.MaxAstralStrain, progression.AstralStrain + StrainPerTick);
            if (MathF.Abs(next - progression.AstralStrain) > 0.001f)
            {
                progression.AstralStrain = next;
                changed = true;
            }

            astral.NextStrainTickAt += StrainTickInterval;
        }

        return changed;
    }

    private bool DecayAstralStrain(WH40KPsykerAstralProgressionComponent progression)
    {
        if (progression.AstralStrain <= 0f)
        {
            progression.AstralStrain = 0f;
            progression.NextStrainDecayAt = TimeSpan.Zero;
            return false;
        }

        if (progression.NextStrainDecayAt == TimeSpan.Zero)
            progression.NextStrainDecayAt = _timing.CurTime + StrainDecayInterval;

        var changed = false;
        while (_timing.CurTime >= progression.NextStrainDecayAt && progression.AstralStrain > 0f)
        {
            progression.AstralStrain = MathF.Max(0f, progression.AstralStrain - StrainPerTick);
            progression.NextStrainDecayAt += StrainDecayInterval;
            changed = true;
        }

        if (progression.AstralStrain <= 0f)
            progression.NextStrainDecayAt = TimeSpan.Zero;

        return changed;
    }
}

public enum WH40KPsykerAstralExitReason : byte
{
    Voluntary,
    Damage,
    ForcedWake,
    Death,
    RoleLost,
    ChaosCorruption,
    Catastrophe
}
