using Content.Shared.Examine;
using Robust.Shared.Timing;

namespace Content.Shared._WH40K.Combat.PhantomStep;

public sealed partial class SharedWH40KPhantomStepActionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KPhantomStepActionComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<WH40KPhantomStepActionComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using var _ = args.PushGroup(nameof(WH40KPhantomStepActionComponent));
        args.PushMarkup(Loc.GetString(
            "wh40k-phantom-step-action-tooltip-charges",
            ("current", ent.Comp.Charges),
            ("max", ent.Comp.MaxCharges)));

        args.PushMarkup(Loc.GetString(
            "wh40k-phantom-step-action-tooltip-recharge",
            ("seconds", Math.Max(1, (int) MathF.Ceiling((float) ent.Comp.RechargeDuration.TotalSeconds)))));

        if (ent.Comp.Charges >= ent.Comp.MaxCharges || ent.Comp.NextRecharge == TimeSpan.Zero)
            return;

        var remaining = ent.Comp.NextRecharge - _timing.CurTime;
        if (remaining <= TimeSpan.Zero)
            return;

        args.PushMarkup(Loc.GetString(
            "wh40k-phantom-step-action-tooltip-next-charge",
            ("seconds", Math.Max(1, (int) MathF.Ceiling((float) remaining.TotalSeconds)))));
    }
}
