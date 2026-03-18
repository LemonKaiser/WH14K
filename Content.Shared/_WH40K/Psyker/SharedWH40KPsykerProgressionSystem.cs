using Content.Shared.Bed.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Imperium psyker progression:
/// - passive meditation XP while sleeping;
/// - active cast XP with anti-spam diminishing return.
/// </summary>
public sealed class SharedWH40KPsykerProgressionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _netManager = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerProgressionComponent, ComponentStartup>(OnPsykerProgressionStartup);
        SubscribeLocalEvent<WH40KPsykerProgressionComponent, WH40KWarpActionCastEvent>(OnWarpActionCast);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_netManager.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KPsykerProgressionComponent, WH40KPsykerRoleComponent>();
        while (query.MoveNext(out var uid, out var progression, out _))
        {
            if (!TryComp<SleepingComponent>(uid, out _))
                continue;

            if (now < progression.NextMeditationAt)
                continue;

            var gained = progression.MeditationXpPerInterval;
            if (IsOnMeditationBed(uid))
                gained *= progression.MeditationBedBonusMultiplier;

            GainProgressionXp(uid, progression, gained);
            progression.NextMeditationAt = now + progression.MeditationInterval;
            Dirty(uid, progression);
        }
    }

    private void OnPsykerProgressionStartup(Entity<WH40KPsykerProgressionComponent> ent, ref ComponentStartup args)
    {
        if (!_netManager.IsServer)
            return;

        if (ent.Comp.NextMeditationAt != TimeSpan.Zero)
            return;

        ent.Comp.NextMeditationAt = _timing.CurTime + ent.Comp.MeditationInterval;
        Dirty(ent, ent.Comp);
    }

    private void OnWarpActionCast(Entity<WH40KPsykerProgressionComponent> ent, ref WH40KWarpActionCastEvent args)
    {
        if (!_netManager.IsServer)
            return;

        if (!HasComp<WH40KPsykerRoleComponent>(ent.Owner) ||
            HasComp<WH40KChaosGiftRoleComponent>(ent.Owner))
        {
            return;
        }

        var progression = ent.Comp;
        if (progression.CastXpBase <= 0f)
        {
            return;
        }

        var now = _timing.CurTime;
        if (progression.LastCastActionPrototype == args.ActionKey &&
            now - progression.LastCastAt <= progression.CastRepeatWindow)
        {
            progression.RepeatCastStreak++;
        }
        else
        {
            progression.RepeatCastStreak = 0;
        }

        progression.LastCastActionPrototype = args.ActionKey;
        progression.LastCastAt = now;

        var multiplier = MathF.Max(
            progression.CastMinMultiplier,
            MathF.Pow(progression.CastRepeatFalloff, progression.RepeatCastStreak));

        var gained = progression.CastXpBase * multiplier;
        GainProgressionXp(ent.Owner, progression, gained);
        Dirty(ent.Owner, progression);
    }

    private void GainProgressionXp(EntityUid uid, WH40KPsykerProgressionComponent progression, float amount)
    {
        if (amount <= 0f || progression.MaxLevel <= 0)
            return;

        progression.TotalXp += amount;

        if (progression.Level >= progression.MaxLevel)
            return;

        progression.LevelXp += amount;

        while (progression.Level < progression.MaxLevel)
        {
            var needed = GetXpRequiredForNextLevel(progression);
            if (progression.LevelXp + 0.0001f < needed)
                break;

            progression.LevelXp -= needed;
            progression.Level++;
        }

        if (progression.Level >= progression.MaxLevel)
            progression.LevelXp = 0f;

        Dirty(uid, progression);
    }

    private static float GetXpRequiredForNextLevel(WH40KPsykerProgressionComponent progression)
    {
        var levelIndex = Math.Max(0, progression.Level - 1);
        var xp = progression.BaseXpForNextLevel * MathF.Pow(progression.XpGrowthFactor, levelIndex);
        return MathF.Max(1f, xp);
    }

    private bool IsOnMeditationBed(EntityUid uid)
    {
        if (!TryComp<BuckleComponent>(uid, out var buckle) || buckle.BuckledTo is not { } strappedTo)
            return false;

        return HasComp<HealOnBuckleComponent>(strappedTo) || HasComp<StasisBedComponent>(strappedTo);
    }
}
