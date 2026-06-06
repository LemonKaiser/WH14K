using Content.Shared.Access.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wires;
using Content.Shared._WH40K.Combat;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared.Turrets;

public abstract partial class SharedDeployableTurretSystem : EntitySystem
{
    [Dependency] private  SharedPopupSystem _popup = default!;
    [Dependency] private  SharedAudioSystem _audio = default!;
    [Dependency] private  UseDelaySystem _useDelay = default!;
    [Dependency] private  AccessReaderSystem _accessReader = default!;
    [Dependency] private  DamageableSystem _damageable = default!;
    [Dependency] private  NpcFactionSystem _npcFaction = default!;
    [Dependency] private  SharedPhysicsSystem _physics = default!;
    [Dependency] private  SharedWiresSystem _wires = default!;
    [Dependency] private  IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DeployableTurretComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<DeployableTurretComponent, AttemptChangePanelEvent>(OnAttemptChangeWirePanelWire);
        SubscribeLocalEvent<DeployableTurretComponent, GetVerbsEvent<Verb>>(OnGetVerb);
        SubscribeLocalEvent<DeployableTurretComponent, PullStartedMessage>(OnPullStarted);
        SubscribeLocalEvent<DeployableTurretComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
    }

    private void OnGetVerb(Entity<DeployableTurretComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        if (!_accessReader.IsAllowed(args.User, ent))
            return;

        if (ent.Comp.Enabled && IsEnemyDeactivationBlocked(ent, args.User, false))
            return;

        if (IsGlobalActivationToggleBlocked(ent, args.User))
            return;

        var user = args.User;

        var verb = new Verb
        {
            Priority = 1,
            Text = ent.Comp.Enabled ? Loc.GetString("deployable-turret-component-deactivate") : Loc.GetString("deployable-turret-component-activate"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/Spare/poweronoff.svg.192dpi.png")),
            Disabled = !HasAmmo(ent),
            Impact = LogImpact.Low,
            Act = () => { TryToggleState(ent, user); }
        };

        args.Verbs.Add(verb);
    }

    private void OnActivate(Entity<DeployableTurretComponent> ent, ref ActivateInWorldEvent args)
    {
        if (TryComp(ent, out UseDelayComponent? useDelay) && !_useDelay.TryResetDelay((ent, useDelay), true))
            return;

        if (!_accessReader.IsAllowed(args.User, ent))
        {
            _popup.PopupClient(Loc.GetString("deployable-turret-component-access-denied"), ent, args.User);
            _audio.PlayPredicted(ent.Comp.AccessDeniedSound, ent, args.User);

            return;
        }

        TryToggleState(ent, args.User);
    }

    private void OnAttemptChangeWirePanelWire(Entity<DeployableTurretComponent> ent, ref AttemptChangePanelEvent args)
    {
        if (!ent.Comp.Enabled || args.Cancelled)
            return;

        _popup.PopupClient(Loc.GetString("deployable-turret-component-cannot-access-wires"), ent, args.User);

        args.Cancelled = true;
    }

    public bool TryToggleState(Entity<DeployableTurretComponent> ent, EntityUid? user = null)
    {
        return TrySetState(ent, !ent.Comp.Enabled, user);
    }

    public bool TrySetState(Entity<DeployableTurretComponent> ent, bool enabled, EntityUid? user = null)
    {
        if (user != null && IsEnemyDeactivationBlocked(ent, user.Value, enabled))
        {
            _popup.PopupClient(Loc.GetString("deployable-turret-component-access-denied"), ent, user.Value);
            _audio.PlayPredicted(ent.Comp.AccessDeniedSound, ent, user.Value);
            return false;
        }

        // For strategic points we also need server-side logic (tier gates / owner binding) to be able to
        // keep the turret enabled/disabled. So the global activation lock only blocks player-initiated toggles.
        // If called without a user (server systems), do not block.
        if (user != null && IsGlobalActivationToggleBlocked(ent, user.Value))
        {
            _popup.PopupClient(Loc.GetString("deployable-turret-component-access-denied"), ent, user.Value);
            _audio.PlayPredicted(ent.Comp.AccessDeniedSound, ent, user.Value);
            return false;
        }

        if (enabled && ent.Comp.CurrentState == DeployableTurretState.Broken)
        {
            if (user != null)
                _popup.PopupClient(Loc.GetString("deployable-turret-component-is-broken"), ent, user.Value);

            return false;
        }

        if (enabled && !HasAmmo(ent))
        {
            if (user != null)
                _popup.PopupClient(Loc.GetString("deployable-turret-component-no-ammo"), ent, user.Value);

            return false;
        }

        if (enabled &&
            ent.Comp.ReactivationCooldown > TimeSpan.Zero &&
            !_useDelay.TryResetDelay(ent.Owner, checkDelayed: true, component: CompOrNull<UseDelayComponent>(ent.Owner), id: ent.Comp.ReactivationDelayId))
        {
            if (user != null &&
                TryComp<UseDelayComponent>(ent, out var useDelay) &&
                _useDelay.TryGetDelayInfo((ent.Owner, useDelay), out var delayInfo, ent.Comp.ReactivationDelayId))
            {
                var seconds = Math.Max(1, (int) Math.Ceiling((delayInfo.EndTime - _timing.CurTime).TotalSeconds));
                _popup.PopupClient(
                    Loc.GetString("deployable-turret-component-reactivation-cooldown", ("seconds", seconds)),
                    ent,
                    user.Value);
            }

            return false;
        }

        SetState(ent, enabled, user);

        return true;
    }

    private void OnPullStarted(Entity<DeployableTurretComponent> ent, ref PullStartedMessage args)
    {
        if (args.PulledUid != ent.Owner || !ent.Comp.DisableWhenPulled)
            return;

        TriggerMobilityShutdown(ent, args.PullerUid);
    }

    private void OnAnchorStateChanged(Entity<DeployableTurretComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored || args.Detaching || !ent.Comp.DisableWhenUnanchored)
            return;

        TriggerMobilityShutdown(ent);
    }

    private void TriggerMobilityShutdown(Entity<DeployableTurretComponent> ent, EntityUid? user = null)
    {
        ApplyReactivationCooldown(ent);

        if (ent.Comp.Enabled)
            SetState(ent, false, user);
    }

    private void ApplyReactivationCooldown(Entity<DeployableTurretComponent> ent)
    {
        if (ent.Comp.ReactivationCooldown <= TimeSpan.Zero)
            return;

        _useDelay.SetLength((ent.Owner, CompOrNull<UseDelayComponent>(ent.Owner)), ent.Comp.ReactivationCooldown, ent.Comp.ReactivationDelayId);
        _useDelay.TryResetDelay(ent.Owner, component: CompOrNull<UseDelayComponent>(ent.Owner), id: ent.Comp.ReactivationDelayId);
    }

    protected virtual void SetState(Entity<DeployableTurretComponent> ent, bool enabled, EntityUid? user = null)
    {
        if (ent.Comp.Enabled == enabled)
            return;

        // Hide the wires panel UI on activation
        if (enabled && TryComp<WiresPanelComponent>(ent, out var wires) && wires.Open)
        {
            _wires.TogglePanel(ent, wires, false);
            _audio.PlayPredicted(wires.ScrewdriverCloseSound, ent, user);
        }

        // Determine how much time is remaining in the current animation and the one next in queue
        // We track this so that when a turret is toggled on/off, we can wait for all queued animations
        // to end before the turret's HTN is reactivated
        var animTimeRemaining = MathF.Max((float)(ent.Comp.AnimationCompletionTime - _timing.CurTime).TotalSeconds, 0f);
        var animTimeNext = enabled ? ent.Comp.DeploymentLength : ent.Comp.RetractionLength;

        ent.Comp.AnimationCompletionTime = _timing.CurTime + TimeSpan.FromSeconds(animTimeNext + animTimeRemaining);

        // Change the turret's damage modifiers
        if (TryComp<DamageableComponent>(ent, out var damageable))
        {
            var damageSetID = enabled ? ent.Comp.DeployedDamageModifierSetId : ent.Comp.RetractedDamageModifierSetId;
            _damageable.SetDamageModifierSetId((ent, damageable), damageSetID);
        }

        // Change the turret's fixtures
        if (ent.Comp.DeployedFixture != null &&
            TryComp(ent, out FixturesComponent? fixtures) &&
            fixtures.Fixtures.TryGetValue(ent.Comp.DeployedFixture, out var fixture))
        {
            _physics.SetHard(ent, fixture, enabled);
        }

        // Play pop up message
        var msg = enabled ? "deployable-turret-component-activating" : "deployable-turret-component-deactivating";
        _popup.PopupClient(Loc.GetString(msg), ent, user);

        // Update enabled state
        ent.Comp.Enabled = enabled;
        DirtyField(ent, ent.Comp, "Enabled");
    }

    public bool HasAmmo(Entity<DeployableTurretComponent> ent)
    {
        var ammoCountEv = new GetAmmoCountEvent();
        RaiseLocalEvent(ent, ref ammoCountEv);

        return ammoCountEv.Count > 0;
    }

    private bool IsGlobalActivationToggleBlocked(Entity<DeployableTurretComponent> ent, EntityUid user)
    {
        if (!TryComp<WH40KTurretGlobalActivationLockComponent>(ent, out var lockComp))
            return false;

        return lockComp.PreventAllActivationToggle;
    }

    private bool IsEnemyDeactivationBlocked(Entity<DeployableTurretComponent> ent, EntityUid user, bool targetEnabledState)
    {
        // WH40K rule: enemy users may not deactivate faction-locked turrets.
        if (targetEnabledState)
            return false;

        if (!TryComp<WH40KTurretFactionLockComponent>(ent, out var lockComp) || !lockComp.PreventEnemyDeactivation)
            return false;

        if (!TryComp<NpcFactionMemberComponent>(ent, out var turretFaction) || turretFaction.Factions.Count == 0)
            return false;

        if (!TryComp<NpcFactionMemberComponent>(user, out var userFaction))
            return lockComp.TreatNoFactionAsEnemy;

        return !_npcFaction.IsEntityFriendly((ent.Owner, turretFaction), (user, userFaction));
    }
}
