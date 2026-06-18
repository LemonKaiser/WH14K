using System;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared._WH40K.Overlays;
using Content.Shared._WH40K.WaveDefence;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Robust.Shared.Localization;

namespace Content.Server._WH40K.WaveDefence;

public sealed partial class WH40KWaveDefenceObjectiveDestroyedEvent : EntityEventArgs
{
    public readonly EntityUid Objective;
    public readonly string TeamId;

    public WH40KWaveDefenceObjectiveDestroyedEvent(EntityUid objective, string teamId)
    {
        Objective = objective;
        TeamId = teamId;
    }
}

public sealed partial class WH40KWaveDefenceObjectiveSystem : EntitySystem
{
    [Dependency] private  DamageableSystem _damageable = default!;
    [Dependency] private  WH40KWaveDefenceRuleSystem _rule = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KWaveDefenceObjectiveComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KWaveDefenceObjectiveComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<WH40KWaveDefenceObjectiveComponent, DamageDealtEvent>(OnDamageDealt);
        SubscribeLocalEvent<WH40KWaveDefenceObjectiveComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMapInit(EntityUid uid, WH40KWaveDefenceObjectiveComponent component, MapInitEvent args)
    {
        component.Destroyed = false;
        component.LowHealthAnnounced = false;

        if (component.MaxHealth <= FixedPoint2.Zero)
            component.MaxHealth = FixedPoint2.New(1);

        if (TryComp(uid, out WH40KAlwaysShowHealthBarComponent? bar))
        {
            bar.MaxHealth = component.MaxHealth;
            bar.UseMobThresholds = false;
            Dirty(uid, bar);
        }
    }

    private void OnBeforeDamageChanged(
        EntityUid uid,
        WH40KWaveDefenceObjectiveComponent component,
        ref BeforeDamageChangedEvent args)
    {
        if (component.Destroyed)
        {
            args.Cancelled = true;
            return;
        }

        if (args.Origin is not { } origin)
            return;

        if (_rule.TryGetEntityTeamId(origin, out var attackerTeamId) &&
            string.Equals(attackerTeamId, component.TeamId, StringComparison.OrdinalIgnoreCase))
        {
            args.Cancelled = true;
        }
    }

    private void OnDamageDealt(EntityUid uid, WH40KWaveDefenceObjectiveComponent component, DamageDealtEvent args)
    {
        if (component.Destroyed || !TryComp<DamageableComponent>(uid, out var damageable))
            return;

#pragma warning disable CS0618
        var totalDamage = _damageable.GetTotalDamage((uid, damageable));
#pragma warning restore CS0618
        if (component.MaxHealth > FixedPoint2.Zero)
        {
            var remainingRatio = (component.MaxHealth - totalDamage).Float() / component.MaxHealth.Float();
            if (!component.LowHealthAnnounced && remainingRatio <= component.WarnAtPercent)
            {
                component.LowHealthAnnounced = true;
                _rule.BroadcastWaveMessage(
                    Loc.GetString("wh40k-objective-low-health", ("target", Loc.GetString(component.NameLoc))));
            }
        }

        if (totalDamage < component.MaxHealth)
            return;

        DestroyObjective(uid, component);
    }

    private void OnMobStateChanged(EntityUid uid, WH40KWaveDefenceObjectiveComponent component, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            DestroyObjective(uid, component);
    }

    private void DestroyObjective(EntityUid uid, WH40KWaveDefenceObjectiveComponent component)
    {
        if (component.Destroyed)
            return;

        component.Destroyed = true;
        RaiseLocalEvent(new WH40KWaveDefenceObjectiveDestroyedEvent(uid, component.TeamId));
    }
}
