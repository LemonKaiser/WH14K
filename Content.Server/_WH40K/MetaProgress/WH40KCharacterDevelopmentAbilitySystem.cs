using System;
using Content.Shared.Alert;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition;
using Content.Shared._WH40K.MetaProgress;
using Content.Server.Body.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.MetaProgress;

public sealed class WH40KCharacterDevelopmentAbilitySystem : EntitySystem
{
    private const string ToxinGroup = "Toxins";
    private static readonly ProtoId<AlertCategoryPrototype> StomachImpulseAlertCategory = "WH40KCharacterDevelopmentStomachImpulse";
    private static readonly ProtoId<AlertCategoryPrototype> KidneyPurgeAlertCategory = "WH40KCharacterDevelopmentKidneyPurge";
    private static readonly ProtoId<AlertCategoryPrototype> WarFurnaceAlertCategory = "WH40KCharacterDevelopmentWarFurnace";
    private static readonly ProtoId<AlertPrototype> StomachImpulseActiveAlert = "WH40KCharacterDevelopmentStomachImpulseActive";
    private static readonly ProtoId<AlertPrototype> StomachImpulseCooldownAlert = "WH40KCharacterDevelopmentStomachImpulseCooldown";
    private static readonly ProtoId<AlertPrototype> KidneyPurgeReadyAlert = "WH40KCharacterDevelopmentKidneyPurgeReady";
    private static readonly ProtoId<AlertPrototype> KidneyPurgeCooldownAlert = "WH40KCharacterDevelopmentKidneyPurgeCooldown";
    private static readonly ProtoId<AlertPrototype> WarFurnaceReadyAlert = "WH40KCharacterDevelopmentWarFurnaceReady";
    private static readonly ProtoId<AlertPrototype> WarFurnaceActiveAlert = "WH40KCharacterDevelopmentWarFurnaceActive";
    private static readonly ProtoId<AlertPrototype> WarFurnaceCooldownAlert = "WH40KCharacterDevelopmentWarFurnaceCooldown";

    private static readonly TimeSpan StomachImpulseDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StomachImpulseCooldown = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan KidneyPurgeCooldown = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WarFurnaceCooldown = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WarFurnaceDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WarFurnaceTickInterval = TimeSpan.FromSeconds(1);
    private static readonly FixedPoint2 KidneyPurgeAmount = FixedPoint2.New(5f);
    private static readonly FixedPoint2 WarFurnaceHealPerTick = FixedPoint2.New(-2.5f);

    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, WH40KCharacterDevelopmentKidneyPurgeActionEvent>(OnKidneyPurgeAction);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, WH40KCharacterDevelopmentKidneyPurgeAlertEvent>(OnKidneyPurgeAlert);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, WH40KCharacterDevelopmentWarFurnaceActionEvent>(OnWarFurnaceAction);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, WH40KCharacterDevelopmentWarFurnaceAlertEvent>(OnWarFurnaceAlert);
        SubscribeLocalEvent<WH40KCharacterDevelopmentModifiersComponent, IngestingEvent>(OnFoodIngested);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var speedBoostQuery = EntityQueryEnumerator<WH40KCharacterDevelopmentSpeedBoostComponent>();
        while (speedBoostQuery.MoveNext(out var speedBoostUid, out var speedBoost))
        {
            if (speedBoost.ExpiresAt != TimeSpan.Zero && now >= speedBoost.ExpiresAt)
            {
                RemComp<WH40KCharacterDevelopmentSpeedBoostComponent>(speedBoostUid);
                SyncStomachImpulseAlert(speedBoostUid);
            }
        }

        var furnaceQuery = EntityQueryEnumerator<WH40KCharacterDevelopmentWarFurnaceActiveComponent>();
        while (furnaceQuery.MoveNext(out var uid, out var active))
        {
            while (active.NextTickAt != TimeSpan.Zero &&
                   active.NextTickAt <= active.ExpiresAt &&
                   now >= active.NextTickAt)
            {
                _damageable.HealEvenly(uid, WarFurnaceHealPerTick, origin: uid, ignoreGlobalModifiers: true);
                active.NextTickAt += WarFurnaceTickInterval;
            }

            if (active.ExpiresAt != TimeSpan.Zero &&
                now >= active.ExpiresAt &&
                (active.NextTickAt == TimeSpan.Zero || active.NextTickAt > active.ExpiresAt))
            {
                RemComp<WH40KCharacterDevelopmentWarFurnaceActiveComponent>(uid);
                SyncWarFurnaceAlert(uid);
            }
        }
    }

    public void SyncAbilities(EntityUid uid, WH40KCharacterDevelopmentModifiersComponent modifiers)
    {
        if (!modifiers.StomachImpulseUnlocked &&
            !modifiers.WarFurnaceUnlocked &&
            !modifiers.KidneyPurgeUnlocked)
        {
            ClearAbilities(uid);
            return;
        }

        EnsureComp<WH40KCharacterDevelopmentAbilityStateComponent>(uid);
        SyncStomachImpulseAlert(uid);
        SyncKidneyPurgeAlert(uid);
        SyncWarFurnaceAlert(uid);
    }

    public void ClearAbilities(EntityUid uid)
    {
        RemComp<WH40KCharacterDevelopmentAbilityStateComponent>(uid);
        RemComp<WH40KCharacterDevelopmentSpeedBoostComponent>(uid);
        RemComp<WH40KCharacterDevelopmentWarFurnaceActiveComponent>(uid);
        _alerts.ClearAlertCategory(uid, StomachImpulseAlertCategory);
        _alerts.ClearAlertCategory(uid, KidneyPurgeAlertCategory);
        _alerts.ClearAlertCategory(uid, WarFurnaceAlertCategory);
    }

    private void OnFoodIngested(Entity<WH40KCharacterDevelopmentModifiersComponent> ent, ref IngestingEvent args)
    {
        if (!ent.Comp.StomachImpulseUnlocked ||
            !TryComp(args.Food, out EdibleComponent? edible) ||
            edible.Edible != IngestionSystem.Food)
        {
            return;
        }

        var state = EnsureComp<WH40KCharacterDevelopmentAbilityStateComponent>(ent.Owner);
        var now = _timing.CurTime;
        if (now < state.NextStomachImpulseTime)
            return;

        ApplyStomachImpulse(ent.Owner);
        state.NextStomachImpulseTime = now + StomachImpulseCooldown;
        SyncStomachImpulseAlert(ent.Owner, ent.Comp, state);
    }

    private void OnKidneyPurgeAction(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref WH40KCharacterDevelopmentKidneyPurgeActionEvent args)
    {
        if (args.Handled ||
            args.Performer != ent.Owner ||
            !ent.Comp.KidneyPurgeUnlocked)
        {
            return;
        }

        args.Handled = TryActivateKidneyPurge(ent.Owner, ent.Comp);
    }

    private void OnKidneyPurgeAlert(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref WH40KCharacterDevelopmentKidneyPurgeAlertEvent args)
    {
        if (args.Handled || args.User != ent.Owner)
            return;

        args.Handled = TryActivateKidneyPurge(ent.Owner, ent.Comp);
    }

    private void OnWarFurnaceAction(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref WH40KCharacterDevelopmentWarFurnaceActionEvent args)
    {
        if (args.Handled ||
            args.Performer != ent.Owner ||
            !ent.Comp.WarFurnaceUnlocked)
        {
            return;
        }

        args.Handled = TryActivateWarFurnace(ent.Owner, ent.Comp);
    }

    private void OnWarFurnaceAlert(
        Entity<WH40KCharacterDevelopmentModifiersComponent> ent,
        ref WH40KCharacterDevelopmentWarFurnaceAlertEvent args)
    {
        if (args.Handled || args.User != ent.Owner)
            return;

        args.Handled = TryActivateWarFurnace(ent.Owner, ent.Comp);
    }

    private void ApplyStomachImpulse(EntityUid uid)
    {
        var boost = EnsureComp<WH40KCharacterDevelopmentSpeedBoostComponent>(uid);
        boost.ExpiresAt = _timing.CurTime + StomachImpulseDuration;
        boost.SpeedMultiplier = 1.10f;
        Dirty(uid, boost);
    }

    private bool TryPurgeToxins(EntityUid uid, FixedPoint2 totalAmount)
    {
        return _bloodstream.FlushChemicalsByGroup(uid, totalAmount, ToxinGroup) is not null;
    }

    private bool TryActivateKidneyPurge(
        EntityUid uid,
        WH40KCharacterDevelopmentModifiersComponent modifiers,
        WH40KCharacterDevelopmentAbilityStateComponent? state = null)
    {
        if (!modifiers.KidneyPurgeUnlocked)
            return false;

        state ??= EnsureComp<WH40KCharacterDevelopmentAbilityStateComponent>(uid);
        if (state.NextKidneyPurgeReadyTime > _timing.CurTime)
            return false;

        if (!TryPurgeToxins(uid, KidneyPurgeAmount))
            return false;

        state.NextKidneyPurgeReadyTime = _timing.CurTime + KidneyPurgeCooldown;
        SyncKidneyPurgeAlert(uid, modifiers, state);
        return true;
    }

    private bool TryActivateWarFurnace(
        EntityUid uid,
        WH40KCharacterDevelopmentModifiersComponent modifiers,
        WH40KCharacterDevelopmentAbilityStateComponent? state = null)
    {
        if (!modifiers.WarFurnaceUnlocked)
            return false;

        state ??= EnsureComp<WH40KCharacterDevelopmentAbilityStateComponent>(uid);
        var now = _timing.CurTime;
        if (state.NextWarFurnaceReadyTime > now)
            return false;

        if (TryComp(uid, out WH40KCharacterDevelopmentWarFurnaceActiveComponent? existingActive) &&
            existingActive.ExpiresAt > now)
        {
            return false;
        }

        var active = EnsureComp<WH40KCharacterDevelopmentWarFurnaceActiveComponent>(uid);
        // Apply the first pulse immediately so the full 5-second window reliably yields five heals.
        _damageable.HealEvenly(uid, WarFurnaceHealPerTick, origin: uid, ignoreGlobalModifiers: true);
        active.ExpiresAt = now + WarFurnaceDuration;
        active.NextTickAt = now + WarFurnaceTickInterval;
        state.NextWarFurnaceReadyTime = now + WarFurnaceCooldown;
        SyncWarFurnaceAlert(uid, modifiers, state, active);
        return true;
    }

    private void SyncStomachImpulseAlert(
        EntityUid uid,
        WH40KCharacterDevelopmentModifiersComponent? modifiers = null,
        WH40KCharacterDevelopmentAbilityStateComponent? state = null)
    {
        if (!Resolve(uid, ref modifiers, false) ||
            !modifiers.StomachImpulseUnlocked ||
            !Resolve(uid, ref state, false))
        {
            _alerts.ClearAlertCategory(uid, StomachImpulseAlertCategory);
            return;
        }

        var now = _timing.CurTime;
        if (TryComp(uid, out WH40KCharacterDevelopmentSpeedBoostComponent? boost) &&
            boost.ExpiresAt > now)
        {
            _alerts.ShowAlert(
                uid,
                StomachImpulseActiveAlert,
                cooldown: (now, boost.ExpiresAt),
                autoRemove: true,
                showCooldown: true);
            return;
        }

        if (state.NextStomachImpulseTime > now)
        {
            _alerts.ShowAlert(
                uid,
                StomachImpulseCooldownAlert,
                cooldown: (now, state.NextStomachImpulseTime),
                autoRemove: true,
                showCooldown: true);
            return;
        }

        _alerts.ClearAlertCategory(uid, StomachImpulseAlertCategory);
    }

    private void SyncKidneyPurgeAlert(
        EntityUid uid,
        WH40KCharacterDevelopmentModifiersComponent? modifiers = null,
        WH40KCharacterDevelopmentAbilityStateComponent? state = null)
    {
        if (!Resolve(uid, ref modifiers, false) ||
            !modifiers.KidneyPurgeUnlocked ||
            !Resolve(uid, ref state, false))
        {
            _alerts.ClearAlertCategory(uid, KidneyPurgeAlertCategory);
            return;
        }

        var now = _timing.CurTime;
        if (state.NextKidneyPurgeReadyTime > now)
        {
            _alerts.ShowAlert(
                uid,
                KidneyPurgeCooldownAlert,
                cooldown: (now, state.NextKidneyPurgeReadyTime),
                autoRemove: true,
                showCooldown: true);
            return;
        }

        _alerts.ShowAlert(uid, KidneyPurgeReadyAlert, autoRemove: false, showCooldown: false);
    }

    private void SyncWarFurnaceAlert(
        EntityUid uid,
        WH40KCharacterDevelopmentModifiersComponent? modifiers = null,
        WH40KCharacterDevelopmentAbilityStateComponent? state = null,
        WH40KCharacterDevelopmentWarFurnaceActiveComponent? active = null)
    {
        if (!Resolve(uid, ref modifiers, false) ||
            !modifiers.WarFurnaceUnlocked ||
            !Resolve(uid, ref state, false))
        {
            _alerts.ClearAlertCategory(uid, WarFurnaceAlertCategory);
            return;
        }

        var now = _timing.CurTime;
        if (Resolve(uid, ref active, false) &&
            active.ExpiresAt > now)
        {
            _alerts.ShowAlert(
                uid,
                WarFurnaceActiveAlert,
                cooldown: (now, active.ExpiresAt),
                autoRemove: true,
                showCooldown: true);
            return;
        }

        if (state.NextWarFurnaceReadyTime > now)
        {
            _alerts.ShowAlert(
                uid,
                WarFurnaceCooldownAlert,
                cooldown: (now, state.NextWarFurnaceReadyTime),
                autoRemove: true,
                showCooldown: true);
            return;
        }

        _alerts.ShowAlert(uid, WarFurnaceReadyAlert, autoRemove: false, showCooldown: false);
    }
}
