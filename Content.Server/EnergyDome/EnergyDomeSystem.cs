using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Popups;
using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.EnergyDome;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Timing;
using Content.Shared.Toggleable;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.EnergyDome;

/// <summary>
/// Server logic for energy dome generators and their spawned domes.
/// </summary>
public sealed partial class EnergyDomeSystem : EntitySystem
{
    private static readonly TimeSpan VisualSyncInterval = TimeSpan.FromSeconds(0.2);
    private const float VisualChargeEpsilon = 0.002f;
    private const float MinInteriorRadius = 0.15f;
    private const float MinObservedDrawSampleSeconds = 0.2f;
    private const float ObservedDrawBlendFactor = 0.35f;
    private const float ObservedDrawRecoveryBlendFactor = 0.2f;
    private const float LinkOverloadResistancePerPeer = 0.08f;
    private const float LinkOverloadResistanceMax = 0.30f;
    private const float AutoActivationRadius = 5.0f;
    private const float EconomyShutdownCharge = 0.10f;
    private static readonly TimeSpan EconomyAutoOffDelay = TimeSpan.FromSeconds(10);
    private const float BalancedShutdownCharge = 0.05f;
    private static readonly TimeSpan BalancedAutoOffDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DesiredActivationRetryInterval = TimeSpan.FromSeconds(0.5f);

    private const string TeamImperium = "Imperium";
    private const string TeamHeretics = "Heretics";
    private const string TeamChaos = "Chaos";
    private const string TeamNeutral = "Neutral";

    [Dependency] private  SharedAudioSystem _audio = default!;
    [Dependency] private  SharedAppearanceSystem _appearance = default!;
    [Dependency] private  SharedBatterySystem _battery = default!;
    [Dependency] private  SharedContainerSystem _container = default!;
    [Dependency] private  EntityLookupSystem _lookup = default!;
    [Dependency] private  UseDelaySystem _useDelay = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;
    [Dependency] private  PopupSystem _popup = default!;
    [Dependency] private  PowerCellSystem _powerCell = default!;
    [Dependency] private  DeviceLinkSystem _deviceLink = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  InventorySystem _inventory = default!;
    [Dependency] private  NpcFactionSystem _npcFaction = default!;
    [Dependency] private  IPrototypeManager _prototype = default!;
    [Dependency] private  WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private  UserInterfaceSystem _ui = default!;

    private readonly HashSet<EntityUid> _nearbyEntities = new();
    private readonly List<(EntityUid Uid, float DistanceSq)> _linkedDonors = new();
    private readonly List<EntityUid> _activationConflictLosers = new();
    private readonly List<EnergyDomeUiLinkedNode> _uiLinkedNodes = new();
    private readonly List<string> _uiRecommendations = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EnergyDomeGeneratorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, ToggleActionEvent>(OnToggleAction);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, PowerCellChangedEvent>(OnPowerCellChanged);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, PowerCellSlotEmptyEvent>(OnPowerCellSlotEmpty);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, ChargeChangedEvent>(OnChargeChanged);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, GotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, GotUnequippedEvent>(OnGotUnequipped);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, GetVerbsEvent<ActivationVerb>>(OnGetVerbs);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<EnergyDomeGeneratorComponent, ComponentShutdown>(OnGeneratorShutdown);

        SubscribeLocalEvent<EnergyDomeComponent, DamageChangedEvent>(OnDomeDamaged);
        SubscribeLocalEvent<EnergyDomeComponent, ComponentShutdown>(OnDomeShutdown);
        Subs.BuiEvents<EnergyDomeGeneratorComponent>(EnergyDomeUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<EnergyDomeUiToggleMessage>(OnUiToggle);
            subs.Event<EnergyDomeUiSetModeMessage>(OnUiSetMode);
            subs.Event<EnergyDomeUiSetSizeMessage>(OnUiSetSize);
            subs.Event<EnergyDomeUiSetColorMessage>(OnUiSetColor);
            subs.Event<EnergyDomeUiSetWallSideMessage>(OnUiSetWallSide);
            subs.Event<EnergyDomeUiSetAutoResponseProfileMessage>(OnUiSetAutoResponseProfile);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<EnergyDomeGeneratorComponent>();

        while (query.MoveNext(out var uid, out var generator))
        {
            if (!ValidateWearableGeneratorRuntime((uid, generator)))
                continue;

            if (generator.Enabled &&
                generator.DomeParentEntity != GetProtectedEntity(uid))
            {
                DisableDesiredActivation((uid, generator));
                TurnOff((uid, generator), startReloading: false, reason: EnergyDomeBreakReason.ParentChanged);
                continue;
            }

            TryRaiseRechargeReadyEvent((uid, generator));
            EnforceTeamBattleColor((uid, generator));
            EnforceLinkedSingleShield((uid, generator));
            EnforceWearableSingleShield((uid, generator));
            UpdateContestedState((uid, generator));
            UpdateStress((uid, generator), frameTime, generator.Enabled);
            DecayUiTelemetry((uid, generator), frameTime);
            HandlePowerProfileAutomation((uid, generator));
            ProcessDesiredActivation((uid, generator), now);

            if (_ui.IsUiOpen(uid, EnergyDomeUiKey.Key) &&
                now >= generator.NextUiUpdateAt)
            {
                var uiSource = ResolveLinkedUiSource((uid, generator));
                UpdateUi(uiSource, uid);
                generator.NextUiUpdateAt = now + GetUiUpdateInterval(generator);
            }

            if (!generator.Enabled)
                continue;

            if (!TryConsumePassiveCharge((uid, generator), frameTime))
            {
                TurnOff((uid, generator), startReloading: true, reason: EnergyDomeBreakReason.Depleted);
                continue;
            }

            if (generator.SpawnedDome == null ||
                now < generator.NextVisualUpdateAt)
            {
                continue;
            }

            UpdateDomeChargeVisuals((uid, generator));
            generator.NextVisualUpdateAt = now + VisualSyncInterval;
        }
    }

    private void OnMapInit(Entity<EnergyDomeGeneratorComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.CanDeviceNetworkUse)
            _deviceLink.EnsureSinkPorts(ent, ent.Comp.TogglePort, ent.Comp.OnPort, ent.Comp.OffPort);

        NormalizePowerCellDrawRuntime(ent.Owner);

        // Initialize runtime state deterministically to avoid accidental startup drain/sounds on spawn.
        var requestedEnabledOnSpawn = ent.Comp.EnabledOnSpawn || ent.Comp.Enabled || ent.Comp.GlobalEnabled;
        SetDisabledState(ent);
        ent.Comp.GlobalEnabled = requestedEnabledOnSpawn;
        ent.Comp.Enabled = false;
        ent.Comp.AutoProfileFriendlyNearby = HasFriendlyInShieldRange(ent);

        if (!requestedEnabledOnSpawn)
            return;

        ent.Comp.NextAutoEnableAttemptAt = TimeSpan.Zero;
    }

    private void OnSignalReceived(Entity<EnergyDomeGeneratorComponent> ent, ref SignalReceivedEvent args)
    {
        if (!ent.Comp.CanDeviceNetworkUse)
            return;

        if (args.Port == ent.Comp.OnPort)
        {
            TryToggle(ent, true);
            return;
        }

        if (args.Port == ent.Comp.OffPort)
        {
            TryToggle(ent, false);
            return;
        }

        if (args.Port == ent.Comp.TogglePort)
            TryToggle(ent, !GetEffectiveEnabledState(ent));
    }

    private void OnActivateInWorld(Entity<EnergyDomeGeneratorComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || !ent.Comp.CanInteractUse)
            return;

        if (TryToggle(ent, !GetEffectiveEnabledState(ent), args.User))
            args.Handled = true;
    }

    private void OnAfterInteract(Entity<EnergyDomeGeneratorComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !ent.Comp.CanInteractUse)
            return;

        if (TryToggle(ent, !GetEffectiveEnabledState(ent), args.User))
            args.Handled = true;
    }

    private void OnGetActions(Entity<EnergyDomeGeneratorComponent> ent, ref GetItemActionsEvent args)
    {
        if (!ent.Comp.CanInteractUse)
            return;

        args.AddAction(ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction);
    }

    private void OnToggleAction(Entity<EnergyDomeGeneratorComponent> ent, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        TryToggle(ent, !GetEffectiveEnabledState(ent), args.Performer);
        args.Handled = true;
    }

    private void OnPowerCellChanged(Entity<EnergyDomeGeneratorComponent> ent, ref PowerCellChangedEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        if (args.Ejected || !_powerCell.HasDrawCharge(ent.Owner))
        {
            TurnOff(ent, startReloading: true, reason: EnergyDomeBreakReason.Depleted);
            return;
        }

        UpdateDomeChargeVisuals(ent, force: true);
    }

    private void OnPowerCellSlotEmpty(Entity<EnergyDomeGeneratorComponent> ent, ref PowerCellSlotEmptyEvent args)
    {
        TurnOff(ent, startReloading: true, reason: EnergyDomeBreakReason.Depleted);
    }

    private void OnChargeChanged(Entity<EnergyDomeGeneratorComponent> ent, ref ChargeChangedEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        if (args.CurrentCharge <= 0f)
        {
            TurnOff(ent, startReloading: true, reason: EnergyDomeBreakReason.Depleted);
            return;
        }

        UpdateDomeChargeVisuals(ent, force: true);
    }

    private void OnParentChanged(Entity<EnergyDomeGeneratorComponent> ent, ref EntParentChangedMessage args)
    {
        if (!ent.Comp.Enabled)
            return;

        if (GetProtectedEntity(ent.Owner) != ent.Comp.DomeParentEntity)
        {
            DisableDesiredActivation(ent);
            TurnOff(ent, startReloading: false, reason: EnergyDomeBreakReason.ParentChanged);
        }
    }

    private void OnGotEquipped(Entity<EnergyDomeGeneratorComponent> ent, ref GotEquippedEvent args)
    {
        if (!IsWearableGenerator(ent))
            return;

        if (!TryGetEquippedWearer(ent.Owner, out _))
        {
            DisableWearableGenerator(ent, EnergyDomeBreakReason.ParentChanged);
            return;
        }

        EnforceWearableSingleShield(ent);
    }

    private void OnGotUnequipped(Entity<EnergyDomeGeneratorComponent> ent, ref GotUnequippedEvent args)
    {
        if (!IsWearableGenerator(ent))
            return;

        DisableWearableGenerator(ent, EnergyDomeBreakReason.ParentChanged);
    }

    private void OnGetVerbs(Entity<EnergyDomeGeneratorComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !ent.Comp.CanInteractUse)
            return;

        var user = args.User;
        var verb = new ActivationVerb
        {
            Text = Loc.GetString("energy-dome-verb-toggle"),
            Act = () => TryToggle(ent, !GetEffectiveEnabledState(ent), user),
        };

        args.Verbs.Add(verb);
    }

    private void OnGetAlternativeVerbs(Entity<EnergyDomeGeneratorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !ent.Comp.CanInteractUse)
            return;

        var user = args.User;
        var uiVerb = new AlternativeVerb
        {
            Text = Loc.GetString("energy-dome-verb-open-ui"),
            Act = () => _ui.TryOpenUi(ent.Owner, EnergyDomeUiKey.Key, user)
        };

        args.Verbs.Add(uiVerb);
    }

    private void OnUiOpened(Entity<EnergyDomeGeneratorComponent> ent, ref BoundUIOpenedEvent args)
    {
        ent.Comp.NextUiUpdateAt = TimeSpan.Zero;
        var uiSource = ResolveLinkedUiSource(ent);
        UpdateUi(uiSource, ent.Owner);
    }

    private void OnUiToggle(Entity<EnergyDomeGeneratorComponent> ent, ref EnergyDomeUiToggleMessage args)
    {
        var uiSource = ResolveLinkedUiSource(ent);
        TryToggle(uiSource, args.Enabled, args.Actor);
        UpdateUi(uiSource, ent.Owner);
    }

    private void OnUiSetMode(Entity<EnergyDomeGeneratorComponent> ent, ref EnergyDomeUiSetModeMessage args)
    {
        var uiSource = ResolveLinkedUiSource(ent);
        if (!uiSource.Comp.UseModeProfiles)
            return;

        TrySetMode(uiSource, args.Mode);
        UpdateUi(uiSource, ent.Owner);
    }

    private void OnUiSetSize(Entity<EnergyDomeGeneratorComponent> ent, ref EnergyDomeUiSetSizeMessage args)
    {
        var uiSource = ResolveLinkedUiSource(ent);
        if (!uiSource.Comp.UseSizeColorProfiles)
            return;

        TrySetSize(uiSource, args.Size);
        UpdateUi(uiSource, ent.Owner);
    }

    private void OnUiSetColor(Entity<EnergyDomeGeneratorComponent> ent, ref EnergyDomeUiSetColorMessage args)
    {
        var uiSource = ResolveLinkedUiSource(ent);
        if (!uiSource.Comp.UseSizeColorProfiles)
            return;

        TrySetColor(uiSource, args.Color);
        UpdateUi(uiSource, ent.Owner);
    }

    private void OnUiSetWallSide(Entity<EnergyDomeGeneratorComponent> ent, ref EnergyDomeUiSetWallSideMessage args)
    {
        var uiSource = ResolveLinkedUiSource(ent);
        TrySetWallSide(uiSource, args.Side);
        UpdateUi(uiSource, ent.Owner);
    }

    private void OnUiSetAutoResponseProfile(Entity<EnergyDomeGeneratorComponent> ent, ref EnergyDomeUiSetAutoResponseProfileMessage args)
    {
        var uiSource = ResolveLinkedUiSource(ent);
        if (!uiSource.Comp.UseAutoResponseProfiles)
            return;

        if (uiSource.Comp.AutoResponseProfile == args.Profile)
            return;

        uiSource.Comp.AutoResponseProfile = args.Profile;
        UpdateUi(uiSource, ent.Owner);
    }

    private void OnExamined(Entity<EnergyDomeGeneratorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var state = ent.Comp.Enabled
            ? "energy-dome-on-examine-is-on-message"
            : "energy-dome-on-examine-is-off-message";
        args.PushMarkup(Loc.GetString(state));
        args.PushMarkup(Loc.GetString(
            "energy-dome-on-examine-mode-message",
            ("mode", Loc.GetString(GetModeLocKey(ent.Comp.Mode)))));
        args.PushMarkup(Loc.GetString(
            "energy-dome-on-examine-size-message",
            ("size", Loc.GetString(GetSizeLocKey(ent.Comp.Size)))));
        args.PushMarkup(Loc.GetString(
            "energy-dome-on-examine-color-message",
            ("color", Loc.GetString(GetColorLocKey(ent.Comp.Color)))));
        args.PushMarkup(Loc.GetString(
            "energy-dome-on-examine-wall-side-message",
            ("side", Loc.GetString(GetWallSideLocKey(ent.Comp.WallSide)))));

        var stressPercent = Math.Clamp((int) MathF.Round(ent.Comp.Stress * 100f), 0, 100);
        args.PushMarkup(Loc.GetString("energy-dome-on-examine-stress-message", ("stress", stressPercent)));

        var status = GetStatusLocKey(ent.Comp);
        args.PushMarkup(Loc.GetString(
            "energy-dome-on-examine-status-message",
            ("status", Loc.GetString(status))));
        args.PushMarkup(Loc.GetString(
            "energy-dome-on-examine-contested-message",
            ("contested", Loc.GetString(ent.Comp.Contested
                ? "energy-dome-contested-yes"
                : "energy-dome-contested-no"))));
        args.PushMarkup(Loc.GetString(
            "energy-dome-on-examine-linked-peers-message",
            ("count", ent.Comp.LinkedPeerCount)));

        if (TryGetBattery(ent, out var battery))
        {
            var percent = Math.Clamp((int) MathF.Round(_battery.GetChargeLevel(battery.Value.AsNullable()) * 100f), 0, 100);
            args.PushMarkup(Loc.GetString("energy-dome-on-examine-charge-message", ("charge", percent)));
        }
    }

    private void OnGeneratorShutdown(Entity<EnergyDomeGeneratorComponent> ent, ref ComponentShutdown args)
    {
        TurnOff(ent, startReloading: false, reason: EnergyDomeBreakReason.Shutdown, playSound: false);
    }

    private void OnDomeShutdown(Entity<EnergyDomeComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Generator is not { } generatorUid)
            return;

        if (!TryComp<EnergyDomeGeneratorComponent>(generatorUid, out var generator))
            return;

        if (generator.SpawnedDome != ent.Owner)
            return;

        SetDisabledState((generatorUid, generator));
        RaiseBrokenEvent(generatorUid, EnergyDomeBreakReason.ExternalDeletion);
    }

    private void OnDomeDamaged(Entity<EnergyDomeComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null || ent.Comp.Generator is not { } generatorUid)
            return;

        if (!TryComp<EnergyDomeGeneratorComponent>(generatorUid, out var generator))
            return;

        if (!generator.Enabled)
            return;

        var damage = args.DamageDelta.GetTotal().Float();
        if (damage <= 0f)
            return;

        CaptureUiImpactTelemetry((generatorUid, generator), ent.Owner, args.DamageDelta, damage, args.Origin);

        if (CanPlayParrySound(generator))
            _audio.PlayPvs(generator.ParrySound, ent);

        var baseRequired = damage * generator.DamageEnergyDraw * generator.ImpactEnergyDrawMultiplier;
        var damageTypeMultiplier = GetDamageTypeImpactMultiplier(args.DamageDelta, generator);
        var burstMultiplier = GetBurstImpactMultiplier((generatorUid, generator));
        var stressMultiplier = 1.0f + Math.Clamp(generator.Stress, 0f, 1f) * Math.Max(generator.StressImpactEnergyMultiplier, 0f);
        var modeMultiplier = GetModeCostMultiplier(generator);

        var requiredCharge = baseRequired * damageTypeMultiplier * burstMultiplier * stressMultiplier * modeMultiplier;
        if (requiredCharge <= 0f)
            return;

        if (!TryConsumeCharge((generatorUid, generator), requiredCharge, "impact"))
        {
            TurnOff((generatorUid, generator), startReloading: true, reason: EnergyDomeBreakReason.Depleted);
            return;
        }

        if (ApplyStressOnImpact((generatorUid, generator), damage))
        {
            TurnOff((generatorUid, generator), startReloading: true, reason: EnergyDomeBreakReason.Overloaded);
            return;
        }

        UpdateDomeChargeVisuals((generatorUid, generator), force: true);
    }

    private bool TryToggle(
        Entity<EnergyDomeGeneratorComponent> generator,
        bool enabled,
        EntityUid? user = null,
        bool ignoreUseDelay = false,
        bool popupErrors = true)
    {
        var toggleTarget = ResolveLinkedUiSource(generator);
        if (toggleTarget.Owner != generator.Owner)
        {
            return TryToggle(toggleTarget, enabled, user, ignoreUseDelay, popupErrors);
        }

        if (!enabled)
        {
            generator.Comp.GlobalEnabled = false;

            if (generator.Comp.Enabled)
                TurnOff(generator, startReloading: false, reason: EnergyDomeBreakReason.Manual);

            return true;
        }

        if (!TryGetActivationProtectedEntity(generator, out var protectedEntity, user, popupErrors))
        {
            DisableDesiredActivation(generator);
            return true;
        }

        if (!generator.Comp.GlobalEnabled)
            generator.Comp.GlobalEnabled = true;

        if (generator.Comp.Enabled)
            return true;

        // Explicit enable requests (LMB / verb / UI / device signal) should try to activate immediately.
        // If activation fails (cooldown/power/conflict), keep global enabled and let controller retry later.
        if (TryEnableActiveDome(generator, user, ignoreUseDelay, popupErrors, protectedEntity))
            return true;

        generator.Comp.NextAutoEnableAttemptAt = _timing.CurTime + DesiredActivationRetryInterval;
        return true;
    }

    private bool TryEnableActiveDome(
        Entity<EnergyDomeGeneratorComponent> generator,
        EntityUid? user = null,
        bool ignoreUseDelay = false,
        bool popupErrors = true,
        EntityUid? preResolvedProtectedEntity = null)
    {
        if (generator.Comp.Enabled)
            return true;

        if (preResolvedProtectedEntity is not { } protectedEntity &&
            !TryGetActivationProtectedEntity(generator, out protectedEntity, user, popupErrors))
        {
            DisableDesiredActivation(generator);
            return false;
        }

        if (!ignoreUseDelay &&
            TryComp<UseDelayComponent>(generator, out var useDelay) &&
            _useDelay.IsDelayed((generator, useDelay)))
        {
            ShowAccessDeniedPopup(generator, "energy-dome-recharging", user, popupErrors);
            return false;
        }

        if (!TryGetBattery(generator, out var battery))
        {
            ShowAccessDeniedPopup(generator, "energy-dome-no-cell", user, popupErrors);
            return false;
        }

        if (_battery.GetCharge(battery.Value.AsNullable()) <= 0f)
        {
            ShowAccessDeniedPopup(generator, "energy-dome-no-power", user, popupErrors);
            return false;
        }

        if (!TryResolveActivationConflicts(generator, protectedEntity, user, popupErrors))
            return false;

        var activationCost = GetActivationCost(generator.Comp);
        if (activationCost > 0f &&
            !TryConsumeCharge(generator, activationCost, "activation"))
        {
            ShowAccessDeniedPopup(generator, "energy-dome-no-power", user, popupErrors);
            return false;
        }

        TurnOn(generator, protectedEntity);
        return true;
    }

    private void ShowAccessDeniedPopup(
        Entity<EnergyDomeGeneratorComponent> generator,
        string locKey,
        EntityUid? user,
        bool popupErrors)
    {
        if (!popupErrors)
            return;

        _audio.PlayPvs(generator.Comp.AccessDeniedSound, generator);
        if (user != null)
            _popup.PopupEntity(Loc.GetString(locKey), generator, user.Value, PopupType.Medium);
        else
            _popup.PopupEntity(Loc.GetString(locKey), generator, PopupType.Medium);
    }

    private void TurnOn(Entity<EnergyDomeGeneratorComponent> generator, EntityUid protectedEntity)
    {
        if (generator.Comp.Enabled)
            return;

        var dome = Spawn(GetModeDomePrototype(generator.Comp), Transform(protectedEntity).Coordinates);
        _transform.SetParent(dome, protectedEntity);
        ApplyDomePlacement(generator, dome);

        if (TryComp<EnergyDomeComponent>(dome, out var domeComp))
            domeComp.Generator = generator.Owner;

        generator.Comp.SpawnedDome = dome;
        generator.Comp.DomeParentEntity = protectedEntity;
        generator.Comp.NextParrySoundAt = TimeSpan.Zero;
        generator.Comp.NextVisualUpdateAt = TimeSpan.Zero;
        generator.Comp.LastVisualChargeFraction = float.NaN;
        generator.Comp.WaitingForRechargeReadyEvent = false;
        generator.Comp.BurstHitStreak = 0;
        generator.Comp.LastImpactAt = TimeSpan.Zero;
        generator.Comp.Contested = false;
        generator.Comp.NextContestedCheckAt = TimeSpan.Zero;
        generator.Comp.LinkedPeerCount = 0;
        generator.Comp.NextUiUpdateAt = TimeSpan.Zero;
        ResetUiTelemetry(generator.Comp);
        generator.Comp.LastFriendlyPresenceAt = _timing.CurTime;
        generator.Comp.AutoProfileFriendlyNearby = HasFriendlyInShieldRange(generator);
        generator.Comp.Enabled = true;
        UpdateDomeChargeVisuals(generator, force: true);
        _audio.PlayPvs(generator.Comp.TurnOnSound, generator);
        RaiseActivatedEvent(generator.Owner, dome);
    }

    private void TurnOff(
        Entity<EnergyDomeGeneratorComponent> generator,
        bool startReloading,
        EnergyDomeBreakReason reason,
        bool playSound = true)
    {
        if (!generator.Comp.Enabled && generator.Comp.SpawnedDome == null)
            return;

        var spawnedDome = generator.Comp.SpawnedDome;
        SetDisabledState(generator);

        if (spawnedDome != null && !Deleted(spawnedDome.Value))
            QueueDel(spawnedDome.Value);

        if (playSound)
        {
            _audio.PlayPvs(generator.Comp.TurnOffSound, generator);

            if (startReloading)
                _audio.PlayPvs(generator.Comp.EnergyOutSound, generator);
        }

        if (startReloading &&
            TryComp<UseDelayComponent>(generator, out var useDelay))
        {
            _useDelay.TryResetDelay((generator, useDelay));
            generator.Comp.WaitingForRechargeReadyEvent = true;
        }
        else
        {
            generator.Comp.WaitingForRechargeReadyEvent = false;
        }

        if (reason != EnergyDomeBreakReason.Manual)
            RaiseBrokenEvent(generator.Owner, reason);
    }

    private void SetDisabledState(Entity<EnergyDomeGeneratorComponent> generator)
    {
        generator.Comp.Enabled = false;
        generator.Comp.SpawnedDome = null;
        generator.Comp.DomeParentEntity = null;
        generator.Comp.NextParrySoundAt = TimeSpan.Zero;
        generator.Comp.NextVisualUpdateAt = TimeSpan.Zero;
        generator.Comp.LastVisualChargeFraction = float.NaN;
        generator.Comp.WaitingForRechargeReadyEvent = false;
        generator.Comp.BurstHitStreak = 0;
        generator.Comp.LastImpactAt = TimeSpan.Zero;
        generator.Comp.Contested = false;
        generator.Comp.NextContestedCheckAt = TimeSpan.Zero;
        generator.Comp.LinkedPeerCount = 0;
        generator.Comp.NextUiUpdateAt = TimeSpan.Zero;
        ResetUiTelemetry(generator.Comp);
        generator.Comp.LastFriendlyPresenceAt = TimeSpan.Zero;
        generator.Comp.AutoProfileFriendlyNearby = HasFriendlyInShieldRange(generator);
        generator.Comp.NextAutoEnableAttemptAt = TimeSpan.Zero;
        NormalizePowerCellDrawRuntime(generator.Owner);
    }

    private static void ResetUiTelemetry(EnergyDomeGeneratorComponent generator)
    {
        generator.UiThreatHeat = 0f;
        generator.UiThreatPiercing = 0f;
        generator.UiThreatOther = 0f;

        for (var i = 0; i < generator.UiIncomingCompass.Length; i++)
        {
            generator.UiIncomingCompass[i] = 0f;
        }

        for (var i = 0; i < generator.UiSectorIntegrity.Length; i++)
        {
            generator.UiSectorIntegrity[i] = 1f;
        }
    }

    private bool CanPlayParrySound(EnergyDomeGeneratorComponent generator)
    {
        if (generator.ParrySoundCooldown <= TimeSpan.Zero)
            return true;

        var now = _timing.CurTime;
        if (now < generator.NextParrySoundAt)
            return false;

        generator.NextParrySoundAt = now + generator.ParrySoundCooldown;
        return true;
    }

    private bool TryConsumeCharge(
        Entity<EnergyDomeGeneratorComponent> generator,
        float requiredCharge,
        string source = "unknown")
    {
        if (requiredCharge <= 0f)
            return true;

        var remaining = Math.Max(requiredCharge, 0f);

        if (TryGetBattery(generator, out var battery))
        {
            var available = _battery.GetCharge(battery.Value.AsNullable());
            if (available > 0f)
            {
                var consumed = Math.Min(available, remaining);
                _battery.UseCharge(battery.Value.AsNullable(), consumed);
                remaining -= consumed;

                if (remaining <= 0f)
                {
                    SetLinkedPeerCount(generator, CountLinkedNetworkPeers(generator, requireUsableBattery: true));
                    return true;
                }
            }
        }

        if (!CanParticipateInLinkedNetwork(generator))
        {
            SetLinkedPeerCount(generator, 0);
            return false;
        }

        return TryConsumeLinkedCharge(generator, remaining, source);
    }

    private bool TryGetBattery(
        Entity<EnergyDomeGeneratorComponent> generator,
        [NotNullWhen(true)] out Entity<BatteryComponent>? battery)
    {
        return _powerCell.TryGetBatteryFromSlotOrEntity(generator.Owner, out battery);
    }

    private bool TryConsumeLinkedCharge(
        Entity<EnergyDomeGeneratorComponent> generator,
        float requiredCharge,
        string source = "unknown")
    {
        if (requiredCharge <= 0f ||
            !CanParticipateInLinkedNetwork(generator))
        {
            SetLinkedPeerCount(generator, 0);
            return requiredCharge <= 0f;
        }

        var efficiency = Math.Clamp(generator.Comp.LinkTransferEfficiency, 0f, 1f);
        if (efficiency <= 0f)
        {
            SetLinkedPeerCount(generator, 0);
            return false;
        }

        _linkedDonors.Clear();

        var query = EntityQueryEnumerator<EnergyDomeGeneratorComponent>();
        while (query.MoveNext(out var otherUid, out var other))
        {
            if (!CanParticipateInLinkedNetwork((otherUid, other)) ||
                otherUid == generator.Owner)
            {
                continue;
            }

            if (!TryGetBattery((otherUid, other), out var donorBattery))
                continue;

            var donorCharge = _battery.GetCharge(donorBattery.Value.AsNullable());
            var donorAvailable = donorCharge - Math.Max(other.LinkReserveCharge, 0f);
            if (donorAvailable <= 0f)
                continue;

            if (!TryGetLinkDistanceSquared(generator, (otherUid, other), out var distSq))
                continue;

            _linkedDonors.Add((otherUid, distSq));
        }

        _linkedDonors.Sort((a, b) =>
        {
            var distCmp = a.DistanceSq.CompareTo(b.DistanceSq);
            if (distCmp != 0)
                return distCmp;

            return a.Uid.Id.CompareTo(b.Uid.Id);
        });

        var remaining = requiredCharge;
        var usedPeers = 0;

        foreach (var (donorUid, _) in _linkedDonors)
        {
            if (usedPeers >= generator.Comp.LinkMaxPeers ||
                remaining <= 0f)
            {
                break;
            }

            if (!TryComp<EnergyDomeGeneratorComponent>(donorUid, out var donor) ||
                !TryGetBattery((donorUid, donor), out var donorBattery))
            {
                continue;
            }

            if (!CanParticipateInLinkedNetwork((donorUid, donor)) ||
                !TryGetLinkDistanceSquared(generator, (donorUid, donor), out _))
            {
                continue;
            }

            var donorCharge = _battery.GetCharge(donorBattery.Value.AsNullable());
            var donorAvailable = donorCharge - Math.Max(donor.LinkReserveCharge, 0f);
            if (donorAvailable <= 0f)
                continue;

            var requiredRaw = remaining / efficiency;
            var rawTaken = Math.Min(requiredRaw, donorAvailable);
            if (rawTaken <= 0f)
                continue;

            _battery.UseCharge(donorBattery.Value.AsNullable(), rawTaken);
            remaining -= rawTaken * efficiency;
            usedPeers += 1;

            if (donor.Enabled && donor.SpawnedDome != null)
                UpdateDomeChargeVisuals((donorUid, donor), force: true);
        }

        SetLinkedPeerCount(generator, CountLinkedNetworkPeers(generator, requireUsableBattery: true));

        return remaining <= 0f;
    }

    private void SetLinkedPeerCount(Entity<EnergyDomeGeneratorComponent> generator, int count)
    {
        count = Math.Max(count, 0);
        if (generator.Comp.LinkedPeerCount == count)
            return;

        generator.Comp.LinkedPeerCount = count;
    }

    private static TimeSpan GetUiUpdateInterval(EnergyDomeGeneratorComponent generator)
    {
        return generator.UiUpdateInterval <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(0.25f)
            : generator.UiUpdateInterval;
    }

    private void DecayUiTelemetry(Entity<EnergyDomeGeneratorComponent> generator, float frameTime)
    {
        var dt = Math.Max(frameTime, 0f);
        if (dt <= 0f)
            return;

        var decayRate = Math.Max(generator.Comp.UiTelemetryDecayPerSecond, 0f);
        if (decayRate > 0f)
        {
            var factor = MathF.Exp(-decayRate * dt);
            generator.Comp.UiThreatHeat *= factor;
            generator.Comp.UiThreatPiercing *= factor;
            generator.Comp.UiThreatOther *= factor;

            for (var i = 0; i < generator.Comp.UiIncomingCompass.Length; i++)
            {
                generator.Comp.UiIncomingCompass[i] *= factor;
            }
        }

        var recovery = Math.Max(generator.Comp.UiSectorRecoveryPerSecond, 0f) * dt;
        if (recovery <= 0f)
            return;

        for (var i = 0; i < generator.Comp.UiSectorIntegrity.Length; i++)
        {
            generator.Comp.UiSectorIntegrity[i] = Math.Clamp(generator.Comp.UiSectorIntegrity[i] + recovery, 0f, 1f);
        }
    }

    private void CaptureUiImpactTelemetry(
        Entity<EnergyDomeGeneratorComponent> generator,
        EntityUid domeUid,
        DamageSpecifier damageDelta,
        float totalDamage,
        EntityUid? origin)
    {
        var impulseScale = Math.Max(generator.Comp.UiIncomingBinImpulseScale, 0f);
        var impulse = Math.Clamp(totalDamage * impulseScale, 0f, 1f);
        if (impulse > 0f && generator.Comp.UiIncomingCompass.Length > 0)
        {
            var bin = ResolveImpactCompassBin(generator.Owner, domeUid, origin, generator.Comp.UiIncomingCompass.Length);
            generator.Comp.UiIncomingCompass[bin] = Math.Clamp(generator.Comp.UiIncomingCompass[bin] + impulse, 0f, 1f);

            if (generator.Comp.UiSectorIntegrity.Length > 0)
            {
                var sector = ResolveSectorFromCompassBin(bin, generator.Comp.UiIncomingCompass.Length, generator.Comp.UiSectorIntegrity.Length);
                generator.Comp.UiSectorIntegrity[sector] = Math.Clamp(
                    generator.Comp.UiSectorIntegrity[sector] - impulse * 0.55f,
                    0f,
                    1f);
            }
        }

        foreach (var (damageType, amount) in damageDelta.DamageDict)
        {
            var value = amount.Float();
            if (value <= 0f)
                continue;

            if (damageType == "Heat")
                generator.Comp.UiThreatHeat += value;
            else if (damageType == "Piercing")
                generator.Comp.UiThreatPiercing += value;
            else
                generator.Comp.UiThreatOther += value;
        }
    }

    private int ResolveImpactCompassBin(EntityUid generatorUid, EntityUid domeUid, EntityUid? origin, int bins)
    {
        bins = Math.Max(1, bins);
        if (origin == null || Deleted(origin.Value))
            return 0;

        if (!TryComp(origin.Value, out TransformComponent? originXform))
            return 0;

        var originMap = _transform.GetMapCoordinates((origin.Value, originXform));
        var domeMap = _transform.GetMapCoordinates(domeUid);
        if (originMap.MapId != domeMap.MapId)
            return 0;

        var delta = originMap.Position - domeMap.Position;
        if (delta.LengthSquared() <= 0.0001f)
            return 0;

        var incomingAngle = MathF.Atan2(delta.Y, delta.X);
        var forward = _transform.GetWorldRotation(generatorUid).ToWorldVec();
        var forwardAngle = MathF.Atan2(forward.Y, forward.X);
        var relative = incomingAngle - forwardAngle;
        var tau = MathF.PI * 2f;
        while (relative < 0f)
        {
            relative += tau;
        }

        while (relative >= tau)
        {
            relative -= tau;
        }

        var normalized = relative / tau;
        return Math.Clamp((int) MathF.Floor(normalized * bins), 0, bins - 1);
    }

    private static int ResolveSectorFromCompassBin(int bin, int bins, int sectors)
    {
        bins = Math.Max(1, bins);
        sectors = Math.Max(1, sectors);
        var normalized = (bin + 0.5f) / bins;
        var sector = (int) MathF.Floor(normalized * sectors);
        return Math.Clamp(sector, 0, sectors - 1);
    }

    private void UpdateUi(Entity<EnergyDomeGeneratorComponent> generator, EntityUid? uiOwnerOverride = null)
    {
        var hasPowerCell = TryGetBattery(generator, out var battery);
        var chargeFraction = 0f;
        if (hasPowerCell)
            chargeFraction = Math.Clamp(_battery.GetChargeLevel(battery!.Value.AsNullable()), 0f, 1f);

        var overloadFraction = Math.Clamp(generator.Comp.Stress, 0f, 1f);
        var cooldownRemaining = GetCooldownRemainingSeconds(generator);
        var predictedUptime = PredictUptimeSeconds(generator, battery);
        var passiveDrawPerSecond = ResolvePassiveDrawPerSecond(generator.Comp);
        var colorSelectionLocked = IsColorSelectionLocked(generator);
        var (heatThreat, piercingThreat, otherThreat) = GetThreatFractions(generator.Comp);
        GetInsideEntityCounts(generator, out var friendlyInside, out var hostileInside);

        var compass = CopyAndNormalize(generator.Comp.UiIncomingCompass);
        var sectors = CopyAndClamp(generator.Comp.UiSectorIntegrity);
        var linkedNodes = BuildLinkedNodeSnapshot(generator);
        var recommendations = BuildUiRecommendations(
            generator.Comp,
            hasPowerCell,
            chargeFraction,
            overloadFraction,
            predictedUptime,
            cooldownRemaining,
            heatThreat,
            piercingThreat,
            hostileInside);

        var state = new EnergyDomeBuiState(
            generator.Comp.GlobalEnabled,
            generator.Comp.Enabled,
            generator.Comp.WaitingForRechargeReadyEvent,
            hasPowerCell,
            generator.Comp.UseModeProfiles,
            generator.Comp.UseSizeColorProfiles,
            generator.Comp.UseAutoResponseProfiles,
            colorSelectionLocked,
            generator.Comp.Contested,
            generator.Comp.LinkedPeerCount,
            cooldownRemaining,
            friendlyInside,
            hostileInside,
            generator.Comp.Mode,
            generator.Comp.Size,
            generator.Comp.Color,
            generator.Comp.WallSide,
            generator.Comp.AutoResponseProfile,
            chargeFraction,
            overloadFraction,
            passiveDrawPerSecond,
            predictedUptime,
            heatThreat,
            piercingThreat,
            otherThreat,
            compass,
            sectors,
            linkedNodes,
            recommendations);

        _ui.SetUiState(uiOwnerOverride ?? generator.Owner, EnergyDomeUiKey.Key, state);
    }

    private int GetCooldownRemainingSeconds(Entity<EnergyDomeGeneratorComponent> generator)
    {
        if (!TryComp<UseDelayComponent>(generator, out var useDelay) ||
            !_useDelay.IsDelayed((generator, useDelay)) ||
            !_useDelay.TryGetDelayInfo((generator, useDelay), out var delayInfo))
        {
            return 0;
        }

        return Math.Max(0, (int) Math.Ceiling((delayInfo.EndTime - _timing.CurTime).TotalSeconds));
    }

    private float PredictUptimeSeconds(Entity<EnergyDomeGeneratorComponent> generator, Entity<BatteryComponent>? battery)
    {
        var modeledDrawPerSecond = ResolvePassiveDrawPerSecond(generator.Comp);
        var drawPerSecond = modeledDrawPerSecond;

        var available = 0f;
        if (battery != null)
            available = Math.Max(0f, _battery.GetCharge(battery.Value.AsNullable()));

        if (!generator.Comp.Enabled)
        {
            ResetObservedUptime(generator.Comp);
            available = Math.Max(0f, available - GetActivationCost(generator.Comp));
        }
        else if (battery != null)
        {
            drawPerSecond = UpdateObservedDrawPerSecond(generator.Comp, available, modeledDrawPerSecond);
        }
        else
        {
            ResetObservedUptime(generator.Comp);
        }

        if (drawPerSecond <= 0f)
            return float.PositiveInfinity;

        return available / drawPerSecond;
    }

    private float UpdateObservedDrawPerSecond(
        EnergyDomeGeneratorComponent generator,
        float currentCharge,
        float modeledDrawPerSecond)
    {
        var now = _timing.CurTime;

        if (generator.UiUptimeSampleAt == TimeSpan.Zero)
        {
            generator.UiUptimeSampleAt = now;
            generator.UiUptimeSampleCharge = currentCharge;
            generator.UiObservedDrawPerSecond = Math.Max(modeledDrawPerSecond, 0f);
            return generator.UiObservedDrawPerSecond;
        }

        var elapsedSeconds = (float) (now - generator.UiUptimeSampleAt).TotalSeconds;
        if (elapsedSeconds < MinObservedDrawSampleSeconds)
            return Math.Max(generator.UiObservedDrawPerSecond, modeledDrawPerSecond);

        var observedDrain = generator.UiUptimeSampleCharge - currentCharge;
        if (observedDrain > 0.0001f)
        {
            var measured = observedDrain / Math.Max(elapsedSeconds, 0.001f);
            if (generator.UiObservedDrawPerSecond <= 0f)
            {
                generator.UiObservedDrawPerSecond = measured;
            }
            else
            {
                generator.UiObservedDrawPerSecond = Lerp(
                    generator.UiObservedDrawPerSecond,
                    measured,
                    ObservedDrawBlendFactor);
            }
        }
        else
        {
            generator.UiObservedDrawPerSecond = Lerp(
                generator.UiObservedDrawPerSecond,
                Math.Max(modeledDrawPerSecond, 0f),
                ObservedDrawRecoveryBlendFactor);
        }

        generator.UiUptimeSampleAt = now;
        generator.UiUptimeSampleCharge = currentCharge;

        if (generator.UiObservedDrawPerSecond <= 0f)
            return Math.Max(modeledDrawPerSecond, 0f);

        return generator.UiObservedDrawPerSecond;
    }

    private static float Lerp(float from, float to, float amount)
    {
        return from + (to - from) * Math.Clamp(amount, 0f, 1f);
    }

    private static void ResetObservedUptime(EnergyDomeGeneratorComponent generator)
    {
        generator.UiUptimeSampleAt = TimeSpan.Zero;
        generator.UiUptimeSampleCharge = 0f;
        generator.UiObservedDrawPerSecond = 0f;
    }

    private float ResolvePassiveDrawPerSecond(EnergyDomeGeneratorComponent generator)
    {
        var draw = Math.Max(generator.PassiveEnergyDraw, 0f) * GetModeCostMultiplier(generator);
        if (draw <= 0f)
            return 0f;

        var stressFactor = 1.0f + Math.Clamp(generator.Stress, 0f, 1f) * Math.Max(generator.StressPassiveEnergyMultiplier, 0f);
        return draw * stressFactor;
    }

    private static (float Heat, float Piercing, float Other) GetThreatFractions(EnergyDomeGeneratorComponent generator)
    {
        var heat = Math.Max(generator.UiThreatHeat, 0f);
        var piercing = Math.Max(generator.UiThreatPiercing, 0f);
        var other = Math.Max(generator.UiThreatOther, 0f);
        var total = heat + piercing + other;

        if (total <= 0.0001f)
            return (0f, 0f, 0f);

        return (heat / total, piercing / total, other / total);
    }

    private static float[] CopyAndNormalize(float[] source)
    {
        if (source.Length == 0)
            return Array.Empty<float>();

        var result = new float[source.Length];
        var peak = 0f;
        for (var i = 0; i < source.Length; i++)
        {
            var value = Math.Max(source[i], 0f);
            result[i] = value;
            peak = Math.Max(peak, value);
        }

        if (peak <= 0.0001f)
            return result;

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Math.Clamp(result[i] / peak, 0f, 1f);
        }

        return result;
    }

    private static float[] CopyAndClamp(float[] source)
    {
        if (source.Length == 0)
            return Array.Empty<float>();

        var result = new float[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            result[i] = Math.Clamp(source[i], 0f, 1f);
        }

        return result;
    }

    private EnergyDomeUiLinkedNode[] BuildLinkedNodeSnapshot(Entity<EnergyDomeGeneratorComponent> generator)
    {
        _uiLinkedNodes.Clear();

        var selfCharge = 0f;
        if (TryGetBattery(generator, out var selfBattery))
            selfCharge = Math.Clamp(_battery.GetChargeLevel(selfBattery!.Value.AsNullable()), 0f, 1f);

        _uiLinkedNodes.Add(new EnergyDomeUiLinkedNode(0f, 0f, generator.Comp.Enabled, true, selfCharge));

        if (!CanParticipateInLinkedNetwork(generator))
            return _uiLinkedNodes.ToArray();

        var sourceMap = _transform.GetMapCoordinates(generator.Owner);
        var range = Math.Max(generator.Comp.LinkRange, 0.01f);
        var query = EntityQueryEnumerator<EnergyDomeGeneratorComponent>();

        while (query.MoveNext(out var otherUid, out var other))
        {
            if (otherUid == generator.Owner ||
                !CanParticipateInLinkedNetwork((otherUid, other)))
                continue;

            if (!TryGetLinkDistanceSquared(generator, (otherUid, other), out var distanceSq))
                continue;

            var otherMap = _transform.GetMapCoordinates(otherUid);
            var delta = otherMap.Position - sourceMap.Position;
            var relative = delta / range;
            var charge = 0f;
            if (TryGetBattery((otherUid, other), out var otherBattery))
                charge = Math.Clamp(_battery.GetChargeLevel(otherBattery!.Value.AsNullable()), 0f, 1f);

            _uiLinkedNodes.Add(new EnergyDomeUiLinkedNode(
                Math.Clamp(relative.X, -1f, 1f),
                Math.Clamp(relative.Y, -1f, 1f),
                other.Enabled,
                false,
                charge));
        }

        return _uiLinkedNodes.ToArray();
    }

    private void GetInsideEntityCounts(Entity<EnergyDomeGeneratorComponent> generator, out int friendly, out int hostile)
    {
        friendly = 0;
        hostile = 0;

        if (generator.Comp.SpawnedDome is not { } domeUid ||
            Deleted(domeUid))
        {
            return;
        }

        var radius = GetInteriorRadius(generator);
        if (radius <= MinInteriorRadius)
            return;

        var protectedEntity = GetProtectedEntity(generator.Owner);
        var domeMap = _transform.GetMapCoordinates(domeUid);
        _nearbyEntities.Clear();
        _lookup.GetEntitiesInRange(
            domeMap.MapId,
            domeMap.Position,
            radius,
            _nearbyEntities,
            LookupFlags.Dynamic | LookupFlags.Approximate);

        foreach (var candidate in _nearbyEntities)
        {
            if (candidate == protectedEntity ||
                candidate == generator.Owner ||
                candidate == domeUid ||
                Deleted(candidate))
            {
                continue;
            }

            if (!TryComp<MobStateComponent>(candidate, out var mobState) ||
                mobState.CurrentState == MobState.Dead)
            {
                continue;
            }

            if (!IsEntityInsideDome(candidate, generator))
                continue;

            if (IsEntityHostileToProtected(candidate, protectedEntity, generator.Comp))
                hostile += 1;
            else
                friendly += 1;
        }
    }

    private string[] BuildUiRecommendations(
        EnergyDomeGeneratorComponent generator,
        bool hasPowerCell,
        float chargeFraction,
        float overloadFraction,
        float predictedUptime,
        int cooldownRemainingSeconds,
        float heatThreatFraction,
        float piercingThreatFraction,
        int hostileInside)
    {
        _uiRecommendations.Clear();

        if (!generator.Enabled && cooldownRemainingSeconds > 0)
            _uiRecommendations.Add("energy-dome-ui-rec-wait-cooldown");

        if (!hasPowerCell)
            _uiRecommendations.Add("energy-dome-ui-rec-insert-cell");

        if (generator.Enabled && chargeFraction <= 0.15f)
            _uiRecommendations.Add("energy-dome-ui-rec-low-charge");

        if (generator.Enabled && overloadFraction >= 0.80f)
            _uiRecommendations.Add("energy-dome-ui-rec-high-overload");

        if (generator.Enabled && !float.IsInfinity(predictedUptime) && predictedUptime <= 12f)
            _uiRecommendations.Add("energy-dome-ui-rec-collapse-risk");

        if (hostileInside > 0)
            _uiRecommendations.Add("energy-dome-ui-rec-hostiles-inside");

        if (heatThreatFraction >= 0.52f)
            _uiRecommendations.Add("energy-dome-ui-rec-heat-pressure");
        else if (piercingThreatFraction >= 0.52f)
            _uiRecommendations.Add("energy-dome-ui-rec-piercing-pressure");

        if (generator.LinkEnabled &&
            generator.LinkedPeerCount <= 0 &&
            generator.Enabled &&
            chargeFraction <= 0.35f)
        {
            _uiRecommendations.Add("energy-dome-ui-rec-link-network");
        }

        if (_uiRecommendations.Count == 0)
            _uiRecommendations.Add("energy-dome-ui-rec-stable");

        if (_uiRecommendations.Count > 4)
            _uiRecommendations.RemoveRange(4, _uiRecommendations.Count - 4);

        return _uiRecommendations.ToArray();
    }

    private void UpdateDomeChargeVisuals(Entity<EnergyDomeGeneratorComponent> generator, bool force = false)
    {
        if (generator.Comp.SpawnedDome is not { } domeUid ||
            Deleted(domeUid) ||
            !TryComp<AppearanceComponent>(domeUid, out var appearance))
        {
            return;
        }

        var chargeFraction = 0f;
        if (TryGetBattery(generator, out var battery))
            chargeFraction = Math.Clamp(_battery.GetChargeLevel(battery.Value.AsNullable()), 0f, 1f);

        if (!force &&
            !float.IsNaN(generator.Comp.LastVisualChargeFraction) &&
            Math.Abs(generator.Comp.LastVisualChargeFraction - chargeFraction) < VisualChargeEpsilon)
        {
            return;
        }

        generator.Comp.LastVisualChargeFraction = chargeFraction;
        _appearance.SetData(domeUid, EnergyDomeVisuals.ChargeFraction, chargeFraction, appearance);
    }

    private void HandlePowerProfileAutomation(Entity<EnergyDomeGeneratorComponent> generator)
    {
        if (!generator.Comp.GlobalEnabled)
        {
            SetPowerProfileSizeReduced(generator, false);
            generator.Comp.LastFriendlyPresenceAt = TimeSpan.Zero;
            return;
        }

        var profile = generator.Comp.AutoResponseProfile;
        var economyProfile = profile == EnergyDomeAutoResponseProfile.Sustain;
        var maximumProfile = profile == EnergyDomeAutoResponseProfile.HoldLine;

        var shutdownThreshold = economyProfile ? EconomyShutdownCharge : BalancedShutdownCharge;
        var autoOffDelay = economyProfile ? EconomyAutoOffDelay : BalancedAutoOffDelay;

        if (!TryGetChargeFraction(generator, out var chargeFraction))
        {
            SetPowerProfileSizeReduced(generator, false);
            generator.Comp.LastFriendlyPresenceAt = TimeSpan.Zero;
            return;
        }

        var hasFriendlyNearby = HasFriendlyInShieldRange(generator);
        generator.Comp.AutoProfileFriendlyNearby = hasFriendlyNearby;
        SetPowerProfileSizeReduced(generator, false);

        if (maximumProfile)
        {
            generator.Comp.LastFriendlyPresenceAt = TimeSpan.Zero;
            return;
        }

        if (chargeFraction <= shutdownThreshold)
        {
            if (generator.Comp.Enabled)
            {
                TurnOff(generator, startReloading: true, reason: EnergyDomeBreakReason.Depleted);
            }

            generator.Comp.LastFriendlyPresenceAt = TimeSpan.Zero;
            return;
        }

        var now = _timing.CurTime;

        if (hasFriendlyNearby)
        {
            generator.Comp.LastFriendlyPresenceAt = now;
            return;
        }

        if (!generator.Comp.Enabled)
        {
            generator.Comp.LastFriendlyPresenceAt = TimeSpan.Zero;
            return;
        }

        if (generator.Comp.LastFriendlyPresenceAt == TimeSpan.Zero)
        {
            generator.Comp.LastFriendlyPresenceAt = now;
            return;
        }

        if (now - generator.Comp.LastFriendlyPresenceAt >= autoOffDelay)
        {
            TurnOff(generator, startReloading: false, reason: EnergyDomeBreakReason.Manual);
            generator.Comp.LastFriendlyPresenceAt = TimeSpan.Zero;
        }
    }

    private void ProcessDesiredActivation(Entity<EnergyDomeGeneratorComponent> generator, TimeSpan now)
    {
        if (!generator.Comp.GlobalEnabled ||
            generator.Comp.Enabled ||
            generator.Comp.WaitingForRechargeReadyEvent ||
            now < generator.Comp.NextAutoEnableAttemptAt)
        {
            return;
        }

        if (!CanAutoActivateWithProfile(generator))
        {
            generator.Comp.NextAutoEnableAttemptAt = now + DesiredActivationRetryInterval;
            return;
        }

        if (!TryGetBattery(generator, out var battery))
        {
            generator.Comp.NextAutoEnableAttemptAt = now + DesiredActivationRetryInterval;
            return;
        }

        var charge = _battery.GetCharge(battery.Value.AsNullable());
        if (charge <= 0f)
        {
            generator.Comp.NextAutoEnableAttemptAt = now + DesiredActivationRetryInterval;
            return;
        }

        var activationCost = Math.Max(GetActivationCost(generator.Comp), 0f);
        if (activationCost > 0f &&
            charge + 0.001f < activationCost &&
            !CanParticipateInLinkedNetwork(generator))
        {
            generator.Comp.NextAutoEnableAttemptAt = now + DesiredActivationRetryInterval;
            return;
        }

        TryEnableActiveDome(generator, popupErrors: false);
        generator.Comp.NextAutoEnableAttemptAt = now + DesiredActivationRetryInterval;
    }

    private bool CanAutoActivateWithProfile(Entity<EnergyDomeGeneratorComponent> generator)
    {
        var profile = generator.Comp.AutoResponseProfile;
        if (profile == EnergyDomeAutoResponseProfile.HoldLine)
            return true;

        if (!generator.Comp.AutoProfileFriendlyNearby ||
            !TryGetChargeFraction(generator, out var chargeFraction))
        {
            return false;
        }

        var shutdownThreshold = profile == EnergyDomeAutoResponseProfile.Sustain
            ? EconomyShutdownCharge
            : BalancedShutdownCharge;

        return chargeFraction > shutdownThreshold;
    }

    private bool TryGetChargeFraction(Entity<EnergyDomeGeneratorComponent> generator, out float chargeFraction)
    {
        chargeFraction = 0f;
        if (!TryGetBattery(generator, out var battery))
            return false;

        chargeFraction = Math.Clamp(_battery.GetChargeLevel(battery.Value.AsNullable()), 0f, 1f);
        return true;
    }

    private void SetPowerProfileSizeReduced(Entity<EnergyDomeGeneratorComponent> generator, bool reduced)
    {
        if (generator.Comp.PowerProfileSizeReduced == reduced)
            return;

        var previousSize = GetEffectiveSizeForPowerProfile(generator.Comp.Size, generator.Comp.PowerProfileSizeReduced);
        var nextSize = GetEffectiveSizeForPowerProfile(generator.Comp.Size, reduced);

        generator.Comp.PowerProfileSizeReduced = reduced;

        if (previousSize == nextSize ||
            !generator.Comp.Enabled)
        {
            return;
        }

        RebuildActiveDomeForMode(generator);
    }

    private static EnergyDomeSizePreset GetEffectiveSizeForPowerProfile(EnergyDomeSizePreset configured, bool reduced)
    {
        if (!reduced)
            return configured;

        return configured switch
        {
            EnergyDomeSizePreset.Huge => EnergyDomeSizePreset.Medium,
            EnergyDomeSizePreset.Medium => EnergyDomeSizePreset.Small,
            _ => EnergyDomeSizePreset.Small
        };
    }

    private static float GetDefaultInteriorRadius(EnergyDomeSizePreset size, EnergyDomeOperationMode mode)
    {
        return (size, mode) switch
        {
            (EnergyDomeSizePreset.Small, EnergyDomeOperationMode.Bubble) => 0.85f,
            (EnergyDomeSizePreset.Medium, EnergyDomeOperationMode.Bubble) => 1.75f,
            (EnergyDomeSizePreset.Huge, EnergyDomeOperationMode.Bubble) => 3.525f,
            (EnergyDomeSizePreset.Small, EnergyDomeOperationMode.Wall) => 1.05f,
            (EnergyDomeSizePreset.Medium, EnergyDomeOperationMode.Wall) => 1.60f,
            (EnergyDomeSizePreset.Huge, EnergyDomeOperationMode.Wall) => 3.225f,
            _ => 0.85f
        };
    }

    private bool HasFriendlyInShieldRange(Entity<EnergyDomeGeneratorComponent> generator)
    {
        var protectedEntity = GetProtectedEntity(generator.Owner);
        var origin = _transform.GetMapCoordinates(protectedEntity);
        if (origin.MapId == MapId.Nullspace)
            return false;

        var radius = Math.Max(AutoActivationRadius, MinInteriorRadius);
        _nearbyEntities.Clear();
        _lookup.GetEntitiesInRange(
            origin.MapId,
            origin.Position,
            radius,
            _nearbyEntities,
            LookupFlags.Dynamic | LookupFlags.Approximate);

        foreach (var candidate in _nearbyEntities)
        {
            if (Deleted(candidate) ||
                !TryComp<MobStateComponent>(candidate, out var mobState) ||
                mobState.CurrentState == MobState.Dead)
            {
                continue;
            }

            if (IsEntityFriendlyToProtected(candidate, protectedEntity, generator.Comp))
                return true;
        }

        return false;
    }

    private EntityUid GetProtectedEntity(EntityUid uid)
    {
        return _container.TryGetOuterContainer(uid, Transform(uid), out var outerContainer)
            ? outerContainer.Owner
            : uid;
    }

    private bool TryGetActivationProtectedEntity(
        Entity<EnergyDomeGeneratorComponent> generator,
        out EntityUid protectedEntity,
        EntityUid? user = null,
        bool popupErrors = true)
    {
        if (!IsWearableGenerator(generator))
        {
            protectedEntity = GetProtectedEntity(generator.Owner);
            return true;
        }

        if (TryGetEquippedWearer(generator.Owner, out protectedEntity))
            return true;

        ShowAccessDeniedPopup(generator, "energy-dome-must-be-worn", user, popupErrors);
        protectedEntity = EntityUid.Invalid;
        return false;
    }

    private bool ValidateWearableGeneratorRuntime(Entity<EnergyDomeGeneratorComponent> generator)
    {
        if (!IsWearableGenerator(generator))
            return true;

        if (TryGetEquippedWearer(generator.Owner, out _))
            return true;

        DisableWearableGenerator(generator, EnergyDomeBreakReason.ParentChanged);
        return false;
    }

    private void DisableWearableGenerator(
        Entity<EnergyDomeGeneratorComponent> generator,
        EnergyDomeBreakReason reason,
        bool playSound = true)
    {
        DisableDesiredActivation(generator);

        if (generator.Comp.Enabled || generator.Comp.SpawnedDome != null)
            TurnOff(generator, startReloading: false, reason: reason, playSound: playSound);
    }

    private bool TryGetEquippedWearer(EntityUid uid, out EntityUid wearer)
    {
        wearer = EntityUid.Invalid;

        if (!TryComp<ClothingComponent>(uid, out var clothing) ||
            !_container.TryGetContainingContainer(uid, out var container) ||
            !_inventory.TryGetContainingSlot(uid, out var slot))
        {
            return false;
        }

        if ((slot.SlotFlags & SlotFlags.POCKET) != 0 ||
            (clothing.Slots & slot.SlotFlags) == SlotFlags.NONE)
        {
            return false;
        }

        wearer = container.Owner;
        return true;
    }

    private bool IsWearableGenerator(Entity<EnergyDomeGeneratorComponent> generator)
    {
        return HasComp<ClothingComponent>(generator.Owner);
    }

    private static void DisableDesiredActivation(Entity<EnergyDomeGeneratorComponent> generator)
    {
        generator.Comp.GlobalEnabled = false;
        generator.Comp.NextAutoEnableAttemptAt = TimeSpan.Zero;
    }

    private bool CanParticipateInLinkedNetwork(Entity<EnergyDomeGeneratorComponent> generator)
    {
        if (!generator.Comp.LinkEnabled ||
            generator.Comp.LinkRange <= 0f ||
            generator.Comp.LinkMaxPeers <= 0)
        {
            return false;
        }

        // Linked network is for deployable generator clusters, not personal/worn shields.
        return GetProtectedEntity(generator.Owner) == generator.Owner;
    }

    private bool TryGetLinkDistanceSquared(
        Entity<EnergyDomeGeneratorComponent> source,
        Entity<EnergyDomeGeneratorComponent> other,
        out float distanceSq)
    {
        distanceSq = 0f;
        if (source.Owner == other.Owner ||
            !CanParticipateInLinkedNetwork(source) ||
            !CanParticipateInLinkedNetwork(other))
        {
            return false;
        }

        var sourceMap = _transform.GetMapCoordinates(source.Owner);
        var otherMap = _transform.GetMapCoordinates(other.Owner);
        if (sourceMap.MapId != otherMap.MapId)
            return false;

        var maxRange = MathF.Min(
            Math.Max(source.Comp.LinkRange, 0f),
            Math.Max(other.Comp.LinkRange, 0f));
        if (maxRange <= 0f)
            return false;

        distanceSq = (otherMap.Position - sourceMap.Position).LengthSquared();
        return distanceSq <= maxRange * maxRange;
    }

    private int CountLinkedNetworkPeers(
        Entity<EnergyDomeGeneratorComponent> generator,
        bool requireUsableBattery)
    {
        if (!CanParticipateInLinkedNetwork(generator))
            return 0;

        var count = 0;
        var maxPeers = Math.Max(generator.Comp.LinkMaxPeers, 0);
        if (maxPeers <= 0)
            return 0;

        var query = EntityQueryEnumerator<EnergyDomeGeneratorComponent>();
        while (query.MoveNext(out var otherUid, out var other))
        {
            if (count >= maxPeers)
                break;

            if (otherUid == generator.Owner ||
                !TryGetLinkDistanceSquared(generator, (otherUid, other), out _))
            {
                continue;
            }

            if (requireUsableBattery)
            {
                if (!TryGetBattery((otherUid, other), out var donorBattery))
                    continue;

                var donorCharge = _battery.GetCharge(donorBattery.Value.AsNullable());
                var donorAvailable = donorCharge - Math.Max(other.LinkReserveCharge, 0f);
                if (donorAvailable <= 0f)
                    continue;
            }

            count += 1;
        }

        return count;
    }

    private Entity<EnergyDomeGeneratorComponent> ResolveLinkedUiSource(Entity<EnergyDomeGeneratorComponent> generator)
    {
        if (!CanParticipateInLinkedNetwork(generator))
            return generator;

        var found = false;
        Entity<EnergyDomeGeneratorComponent> best = default;

        if (generator.Comp.Enabled &&
            generator.Comp.SpawnedDome != null)
        {
            best = generator;
            found = true;
        }

        var query = EntityQueryEnumerator<EnergyDomeGeneratorComponent>();
        while (query.MoveNext(out var otherUid, out var other))
        {
            if (otherUid == generator.Owner ||
                !other.Enabled ||
                other.SpawnedDome == null ||
                !TryGetLinkDistanceSquared(generator, (otherUid, other), out _))
            {
                continue;
            }

            if (!found || CompareActivationPriority((otherUid, other), best) > 0)
            {
                best = (otherUid, other);
                found = true;
            }
        }

        return found ? best : generator;
    }

    private bool GetEffectiveEnabledState(Entity<EnergyDomeGeneratorComponent> generator)
    {
        return ResolveLinkedUiSource(generator).Comp.GlobalEnabled;
    }

    private void EnforceLinkedSingleShield(Entity<EnergyDomeGeneratorComponent> generator)
    {
        if (!generator.Comp.Enabled ||
            generator.Comp.SpawnedDome == null ||
            !CanParticipateInLinkedNetwork(generator))
        {
            return;
        }

        var uiSource = ResolveLinkedUiSource(generator);
        if (uiSource.Owner == generator.Owner)
            return;

        TurnOff(generator, startReloading: false, reason: EnergyDomeBreakReason.Conflict, playSound: false);
    }

    private void EnforceWearableSingleShield(Entity<EnergyDomeGeneratorComponent> generator)
    {
        if (!generator.Comp.Enabled ||
            generator.Comp.SpawnedDome == null ||
            !IsWearableGenerator(generator) ||
            !TryGetEquippedWearer(generator.Owner, out var wearer))
        {
            return;
        }

        _activationConflictLosers.Clear();
        var query = EntityQueryEnumerator<EnergyDomeGeneratorComponent>();

        while (query.MoveNext(out var otherUid, out var other))
        {
            if (otherUid == generator.Owner ||
                !other.Enabled ||
                other.SpawnedDome == null ||
                !IsWearableGenerator((otherUid, other)) ||
                !TryGetEquippedWearer(otherUid, out var otherWearer) ||
                otherWearer != wearer)
            {
                continue;
            }

            _activationConflictLosers.Add(otherUid);
        }

        if (_activationConflictLosers.Count == 0)
            return;

        DisableDesiredActivation(generator);
        TurnOff(generator, startReloading: false, reason: EnergyDomeBreakReason.Conflict, playSound: false);

        foreach (var loserUid in _activationConflictLosers)
        {
            if (!TryComp<EnergyDomeGeneratorComponent>(loserUid, out var loser))
                continue;

            DisableDesiredActivation((loserUid, loser));
            TurnOff((loserUid, loser), startReloading: false, reason: EnergyDomeBreakReason.Conflict, playSound: false);
        }
    }

    private void TryCycleMode(Entity<EnergyDomeGeneratorComponent> generator, EntityUid? user = null)
    {
        var next = generator.Comp.Mode switch
        {
            EnergyDomeOperationMode.Bubble => EnergyDomeOperationMode.Wall,
            _ => EnergyDomeOperationMode.Bubble
        };

        if (!TrySetMode(generator, next))
            return;

        if (user == null)
            return;

        _popup.PopupEntity(
            Loc.GetString("energy-dome-mode-switched", ("mode", Loc.GetString(GetModeLocKey(next)))),
            generator,
            user.Value,
            PopupType.Medium);
    }

    private void TryCycleSize(Entity<EnergyDomeGeneratorComponent> generator, EntityUid? user = null)
    {
        var next = generator.Comp.Size switch
        {
            EnergyDomeSizePreset.Small => EnergyDomeSizePreset.Medium,
            EnergyDomeSizePreset.Medium => EnergyDomeSizePreset.Huge,
            _ => EnergyDomeSizePreset.Small
        };

        if (!TrySetSize(generator, next))
            return;

        if (user == null)
            return;

        _popup.PopupEntity(
            Loc.GetString("energy-dome-size-switched", ("size", Loc.GetString(GetSizeLocKey(next)))),
            generator,
            user.Value,
            PopupType.Medium);
    }

    private void TryCycleColor(Entity<EnergyDomeGeneratorComponent> generator, EntityUid? user = null)
    {
        var next = generator.Comp.Color == EnergyDomeColorPreset.Red
            ? EnergyDomeColorPreset.Blue
            : EnergyDomeColorPreset.Red;

        if (!TrySetColor(generator, next))
            return;

        if (user == null)
            return;

        _popup.PopupEntity(
            Loc.GetString("energy-dome-color-switched", ("color", Loc.GetString(GetColorLocKey(next)))),
            generator,
            user.Value,
            PopupType.Medium);
    }

    private bool TrySetMode(Entity<EnergyDomeGeneratorComponent> generator, EnergyDomeOperationMode nextMode)
    {
        if (!generator.Comp.UseModeProfiles)
            return false;

        if (generator.Comp.Mode == nextMode)
            return true;

        generator.Comp.Mode = nextMode;

        if (!generator.Comp.Enabled)
            return true;

        return RebuildActiveDomeForMode(generator);
    }

    private bool TrySetWallSide(Entity<EnergyDomeGeneratorComponent> generator, EnergyDomeWallSide nextSide)
    {
        if (generator.Comp.WallSide == nextSide)
            return true;

        generator.Comp.WallSide = nextSide;

        if (!generator.Comp.Enabled ||
            generator.Comp.Mode != EnergyDomeOperationMode.Wall)
        {
            return true;
        }

        return RebuildActiveDomeForMode(generator);
    }

    private bool TrySetSize(Entity<EnergyDomeGeneratorComponent> generator, EnergyDomeSizePreset nextSize)
    {
        if (generator.Comp.Size == nextSize)
            return true;

        generator.Comp.Size = nextSize;

        if (!generator.Comp.Enabled)
            return true;

        return RebuildActiveDomeForMode(generator);
    }

    private bool TrySetColor(Entity<EnergyDomeGeneratorComponent> generator, EnergyDomeColorPreset nextColor)
    {
        if (TryGetForcedTeamBattleColor(generator, out var forcedColor))
            nextColor = forcedColor;

        if (generator.Comp.Color == nextColor)
            return true;

        generator.Comp.Color = nextColor;

        if (!generator.Comp.Enabled)
            return true;

        return RebuildActiveDomeForMode(generator);
    }

    private bool IsColorSelectionLocked(Entity<EnergyDomeGeneratorComponent> generator)
    {
        return TryGetForcedTeamBattleColor(generator, out _);
    }

    private void EnforceTeamBattleColor(Entity<EnergyDomeGeneratorComponent> generator)
    {
        if (!TryGetForcedTeamBattleColor(generator, out var forcedColor))
            return;

        if (generator.Comp.Color == forcedColor)
            return;

        TrySetColor(generator, forcedColor);
    }

    private bool TryGetForcedTeamBattleColor(
        Entity<EnergyDomeGeneratorComponent> generator,
        out EnergyDomeColorPreset color)
    {
        color = default;

        if (_teamRule.GetTeamIds().Count == 0)
            return false;

        var protectedEntity = GetProtectedEntity(generator.Owner);
        if (!TryResolveProtectedTeamId(protectedEntity, generator.Comp, out var teamId) ||
            string.IsNullOrWhiteSpace(teamId))
            return false;

        if (string.Equals(teamId, TeamImperium, StringComparison.OrdinalIgnoreCase))
        {
            color = EnergyDomeColorPreset.Blue;
            return true;
        }

        if (string.Equals(teamId, TeamHeretics, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(teamId, TeamChaos, StringComparison.OrdinalIgnoreCase))
        {
            color = EnergyDomeColorPreset.Red;
            return true;
        }

        return false;
    }

    private bool RebuildActiveDomeForMode(Entity<EnergyDomeGeneratorComponent> generator)
    {
        var protectedEntity = GetProtectedEntity(generator.Owner);
        if (generator.Comp.DomeParentEntity != protectedEntity)
            return false;

        var oldDome = generator.Comp.SpawnedDome;
        if (oldDome != null && !Deleted(oldDome.Value))
            QueueDel(oldDome.Value);

        var dome = Spawn(GetModeDomePrototype(generator.Comp), Transform(protectedEntity).Coordinates);
        _transform.SetParent(dome, protectedEntity);
        ApplyDomePlacement(generator, dome);

        if (TryComp<EnergyDomeComponent>(dome, out var domeComp))
            domeComp.Generator = generator.Owner;

        generator.Comp.SpawnedDome = dome;
        generator.Comp.DomeParentEntity = protectedEntity;
        generator.Comp.NextVisualUpdateAt = TimeSpan.Zero;
        generator.Comp.LastVisualChargeFraction = float.NaN;
        UpdateDomeChargeVisuals(generator, force: true);
        return true;
    }

    private void ApplyDomePlacement(Entity<EnergyDomeGeneratorComponent> generator, EntityUid domeUid)
    {
        var domeXform = Transform(domeUid);
        var localOffset = Vector2.Zero;
        var localRotation = Angle.Zero;
        if (generator.Comp.Mode == EnergyDomeOperationMode.Wall)
        {
            localRotation = GetWallSideAngle(generator.Comp.WallSide);
            var distance = MathF.Max(generator.Comp.WallForwardOffset, 0f);
            localOffset = localRotation.ToWorldVec() * distance;
        }

        _transform.SetLocalRotation(domeUid, localRotation, domeXform);
        _transform.SetLocalPosition(domeUid, localOffset, domeXform);
    }

    private static Angle GetWallSideAngle(EnergyDomeWallSide side)
    {
        return side switch
        {
            EnergyDomeWallSide.Front => Angle.Zero,
            EnergyDomeWallSide.Right => Angle.FromDegrees(90f),
            EnergyDomeWallSide.Back => Angle.FromDegrees(180f),
            EnergyDomeWallSide.Left => Angle.FromDegrees(270f),
            _ => Angle.Zero
        };
    }

    private EntProtoId GetModeDomePrototype(EnergyDomeGeneratorComponent generator)
    {
        if (generator.UseSizeColorProfiles)
            return GetSizeColorDomePrototype(generator);

        if (generator.UseModeProfiles)
            return GetModeProfileDomePrototype(generator);

        return generator.DomePrototype;
    }

    private EntProtoId GetModeProfileDomePrototype(EnergyDomeGeneratorComponent generator)
    {
        return generator.Mode switch
        {
            EnergyDomeOperationMode.Bubble => generator.BubbleDomePrototype,
            EnergyDomeOperationMode.Wall => generator.WallDomePrototype,
            _ => generator.DomePrototype
        };
    }

    private EntProtoId GetSizeColorDomePrototype(EnergyDomeGeneratorComponent generator)
    {
        var directed = generator.Mode == EnergyDomeOperationMode.Wall;
        var effectiveSize = GetEffectiveSizeForPowerProfile(generator.Size, generator.PowerProfileSizeReduced);
        EntProtoId prototype;
        switch ((effectiveSize, generator.Color, directed))
        {
            case (EnergyDomeSizePreset.Small, EnergyDomeColorPreset.Red, false):
                prototype = "EnergyDomeSmallRed";
                break;
            case (EnergyDomeSizePreset.Small, EnergyDomeColorPreset.Blue, false):
                prototype = "EnergyDomeSmallBlue";
                break;
            case (EnergyDomeSizePreset.Medium, EnergyDomeColorPreset.Red, false):
                prototype = "EnergyDomeMediumRed";
                break;
            case (EnergyDomeSizePreset.Medium, EnergyDomeColorPreset.Blue, false):
                prototype = "EnergyDomeMediumBlue";
                break;
            case (EnergyDomeSizePreset.Huge, EnergyDomeColorPreset.Red, false):
                prototype = "EnergyDomeHugeRed";
                break;
            case (EnergyDomeSizePreset.Huge, EnergyDomeColorPreset.Blue, false):
                prototype = "EnergyDomeHugeBlue";
                break;
            case (EnergyDomeSizePreset.Small, EnergyDomeColorPreset.Red, true):
                prototype = "EnergyDomeSmallRedDirected";
                break;
            case (EnergyDomeSizePreset.Small, EnergyDomeColorPreset.Blue, true):
                prototype = "EnergyDomeSmallBlueDirected";
                break;
            case (EnergyDomeSizePreset.Medium, EnergyDomeColorPreset.Red, true):
                prototype = "EnergyDomeMediumRedDirected";
                break;
            case (EnergyDomeSizePreset.Medium, EnergyDomeColorPreset.Blue, true):
                prototype = "EnergyDomeMediumBlueDirected";
                break;
            case (EnergyDomeSizePreset.Huge, EnergyDomeColorPreset.Red, true):
                prototype = "EnergyDomeHugeRedDirected";
                break;
            case (EnergyDomeSizePreset.Huge, EnergyDomeColorPreset.Blue, true):
                prototype = "EnergyDomeHugeBlueDirected";
                break;
            default:
                prototype = generator.DomePrototype;
                break;
        }

        if (_prototype.TryIndex<EntityPrototype>(prototype, out _))
            return prototype;

        if (generator.UseModeProfiles)
            return GetModeProfileDomePrototype(generator);

        return generator.DomePrototype;
    }

    private float GetModeCostMultiplier(EnergyDomeGeneratorComponent generator)
    {
        var size = GetEffectiveSizeForPowerProfile(generator.Size, generator.PowerProfileSizeReduced);
        var multiplier = (generator.Mode, size) switch
        {
            (EnergyDomeOperationMode.Bubble, EnergyDomeSizePreset.Small) => 0.5f,
            (EnergyDomeOperationMode.Bubble, EnergyDomeSizePreset.Medium) => 1.0f,
            (EnergyDomeOperationMode.Bubble, EnergyDomeSizePreset.Huge) => 1.5f,
            (EnergyDomeOperationMode.Wall, EnergyDomeSizePreset.Small) => 0.2f,
            (EnergyDomeOperationMode.Wall, EnergyDomeSizePreset.Medium) => 0.4f,
            (EnergyDomeOperationMode.Wall, EnergyDomeSizePreset.Huge) => 0.8f,
            _ => 1f
        };

        return Math.Max(multiplier, 0f);
    }

    private float GetActivationCost(EnergyDomeGeneratorComponent generator)
    {
        return Math.Max(generator.ActivationEnergyCost, 0f) * GetModeCostMultiplier(generator);
    }

    private bool TryConsumePassiveCharge(Entity<EnergyDomeGeneratorComponent> generator, float frameTime)
    {
        var perSecond = ResolvePassiveDrawPerSecond(generator.Comp);
        if (perSecond <= 0f)
            return true;

        var required = perSecond * Math.Max(frameTime, 0f);

        if (required <= 0f)
            return true;

        if (!TryConsumeCharge(generator, required, "passive"))
            return false;

        return true;
    }

    /// <summary>
    /// EnergyDome manages battery usage itself (activation/passive/impact/link),
    /// so PowerCellDraw must stay disabled to avoid hidden duplicate drain.
    /// </summary>
    private void NormalizePowerCellDrawRuntime(EntityUid uid)
    {
        if (!TryComp<PowerCellDrawComponent>(uid, out var draw))
            return;

        // Keep draw disabled even if old entities/prototypes had it enabled.
        _powerCell.SetDrawEnabled((uid, draw), false);
    }

    private float GetDamageTypeImpactMultiplier(DamageSpecifier damageDelta, EnergyDomeGeneratorComponent generator)
    {
        var weightedMultiplier = 0f;
        var totalDamage = 0f;

        foreach (var (damageType, amount) in damageDelta.DamageDict)
        {
            var damage = amount.Float();
            if (damage <= 0f)
                continue;

            var typeMultiplier = generator.OtherImpactMultiplier;
            if (damageType == "Heat")
                typeMultiplier = generator.HeatImpactMultiplier;
            else if (damageType == "Piercing")
                typeMultiplier = generator.PiercingImpactMultiplier;

            weightedMultiplier += damage * Math.Max(typeMultiplier, 0f);
            totalDamage += damage;
        }

        if (totalDamage <= 0f)
            return 1f;

        return weightedMultiplier / totalDamage;
    }

    private float GetBurstImpactMultiplier(Entity<EnergyDomeGeneratorComponent> generator)
    {
        var now = _timing.CurTime;

        if (generator.Comp.BurstWindow <= TimeSpan.Zero ||
            generator.Comp.BurstStepMultiplier <= 0f)
        {
            generator.Comp.BurstHitStreak = 1;
            generator.Comp.LastImpactAt = now;
            return 1f;
        }

        generator.Comp.BurstHitStreak = now - generator.Comp.LastImpactAt <= generator.Comp.BurstWindow
            ? generator.Comp.BurstHitStreak + 1
            : 1;
        generator.Comp.LastImpactAt = now;

        var maxMultiplier = Math.Max(generator.Comp.BurstMaxMultiplier, 1f);
        var added = Math.Max(generator.Comp.BurstHitStreak - 1, 0) * Math.Max(generator.Comp.BurstStepMultiplier, 0f);
        return Math.Min(1f + added, maxMultiplier);
    }

    private bool ApplyStressOnImpact(Entity<EnergyDomeGeneratorComponent> generator, float damage)
    {
        if (!generator.Comp.StressEnabled ||
            damage <= 0f ||
            generator.Comp.StressPerDamage <= 0f)
        {
            return false;
        }

        var resistance = GetLinkedOverloadResistance(generator.Comp);
        var stressGain = damage * generator.Comp.StressPerDamage * (1f - resistance);
        generator.Comp.Stress = Math.Clamp(generator.Comp.Stress + stressGain, 0f, 1f);
        return generator.Comp.StressBreakThreshold > 0f &&
               generator.Comp.Stress >= generator.Comp.StressBreakThreshold;
    }

    private void UpdateStress(Entity<EnergyDomeGeneratorComponent> generator, float frameTime, bool active)
    {
        if (!generator.Comp.StressEnabled ||
            generator.Comp.Stress <= 0f ||
            generator.Comp.StressDecayPerSecond <= 0f)
        {
            return;
        }

        var decay = generator.Comp.StressDecayPerSecond;
        if (!active)
            decay *= Math.Max(generator.Comp.StressDecayInactiveMultiplier, 0f);

        if (decay <= 0f)
            return;

        var previous = generator.Comp.Stress;
        generator.Comp.Stress = Math.Max(0f, previous - decay * Math.Max(frameTime, 0f));
    }

    private static float GetLinkedOverloadResistance(EnergyDomeGeneratorComponent generator)
    {
        if (generator.LinkedPeerCount <= 0)
            return 0f;

        return Math.Clamp(
            generator.LinkedPeerCount * LinkOverloadResistancePerPeer,
            0f,
            LinkOverloadResistanceMax);
    }

    private void UpdateContestedState(Entity<EnergyDomeGeneratorComponent> generator)
    {
        if (!generator.Comp.Enabled)
        {
            if (generator.Comp.Contested)
            {
                generator.Comp.Contested = false;
            }

            if (generator.Comp.NextContestedCheckAt != TimeSpan.Zero)
            {
                generator.Comp.NextContestedCheckAt = TimeSpan.Zero;
            }

            return;
        }

        var now = _timing.CurTime;
        if (now < generator.Comp.NextContestedCheckAt)
            return;

        var interval = generator.Comp.ContestedCheckInterval <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(0.25f)
            : generator.Comp.ContestedCheckInterval;
        generator.Comp.NextContestedCheckAt = now + interval;

        var contested = HasHostileInsideDome(generator);
        if (generator.Comp.Contested == contested)
            return;

        generator.Comp.Contested = contested;
    }

    private bool HasHostileInsideDome(Entity<EnergyDomeGeneratorComponent> generator)
    {
        if (generator.Comp.SpawnedDome is not { } domeUid ||
            Deleted(domeUid))
        {
            return false;
        }

        var radius = GetInteriorRadius(generator);
        if (radius <= MinInteriorRadius)
            return false;

        var protectedEntity = GetProtectedEntity(generator.Owner);
        var domeMap = _transform.GetMapCoordinates(domeUid);
        _nearbyEntities.Clear();
        _lookup.GetEntitiesInRange(
            domeMap.MapId,
            domeMap.Position,
            radius,
            _nearbyEntities,
            LookupFlags.Dynamic | LookupFlags.Approximate);

        foreach (var candidate in _nearbyEntities)
        {
            if (candidate == protectedEntity ||
                candidate == generator.Owner ||
                candidate == domeUid ||
                Deleted(candidate))
            {
                continue;
            }

            if (!TryComp<MobStateComponent>(candidate, out var mobState) ||
                mobState.CurrentState == MobState.Dead)
            {
                continue;
            }

            if (!IsEntityInsideDome(candidate, generator))
                continue;

            if (IsEntityHostileToProtected(candidate, protectedEntity, generator.Comp))
                return true;
        }

        return false;
    }

    private bool IsEntityInsideDome(EntityUid entity, Entity<EnergyDomeGeneratorComponent> generator)
    {
        if (!TryComp(entity, out TransformComponent? entityXform) ||
            generator.Comp.SpawnedDome is not { } domeUid ||
            Deleted(domeUid))
        {
            return false;
        }

        var entityMap = _transform.GetMapCoordinates((entity, entityXform));
        var domeMap = _transform.GetMapCoordinates(domeUid);
        if (entityMap.MapId != domeMap.MapId)
            return false;

        var radius = GetInteriorRadius(generator);
        if (radius <= MinInteriorRadius)
            return false;

        return (entityMap.Position - domeMap.Position).LengthSquared() <= radius * radius;
    }

    private float GetInteriorRadius(Entity<EnergyDomeGeneratorComponent> generator)
    {
        var effectiveSize = GetEffectiveSizeForPowerProfile(generator.Comp.Size, generator.Comp.PowerProfileSizeReduced);
        var baseRadius = GetDefaultInteriorRadius(effectiveSize, generator.Comp.Mode);

        if (generator.Comp.SpawnedDome is { } domeUid &&
            TryComp<EnergyDomeVisualsComponent>(domeUid, out var visuals))
        {
            baseRadius = Math.Max(visuals.InsideTransparencyRadius, MinInteriorRadius);
        }

        var scaled = baseRadius * Math.Max(generator.Comp.InteriorRadiusMultiplier, 0f);
        return Math.Max(scaled, MinInteriorRadius);
    }

    private bool IsEntityFriendlyToProtected(
        EntityUid candidate,
        EntityUid protectedEntity,
        EnergyDomeGeneratorComponent generator)
    {
        if (candidate == protectedEntity)
            return true;

        if (TryResolveProtectedTeamId(protectedEntity, generator, out var protectedTeam) &&
            TryResolveTeamId(candidate, out var candidateTeam))
        {
            if (IsNeutralTeamId(protectedTeam) || IsNeutralTeamId(candidateTeam))
                return true;

            return string.Equals(protectedTeam, candidateTeam, StringComparison.OrdinalIgnoreCase);
        }

        if (TryComp<NpcFactionMemberComponent>(protectedEntity, out var protectedFaction) &&
            TryComp<NpcFactionMemberComponent>(candidate, out var candidateFaction))
        {
            return _npcFaction.IsEntityFriendly((protectedEntity, protectedFaction), (candidate, candidateFaction)) ||
                   _npcFaction.IsEntityFriendly((candidate, candidateFaction), (protectedEntity, protectedFaction));
        }

        return false;
    }

    private bool IsEntityHostileToProtected(
        EntityUid candidate,
        EntityUid protectedEntity,
        EnergyDomeGeneratorComponent generator)
    {
        if (candidate == protectedEntity)
            return false;

        if (!generator.ContestedRequireDistinctTeams)
            return true;

        if (TryResolveProtectedTeamId(protectedEntity, generator, out var protectedTeam) &&
            TryResolveTeamId(candidate, out var candidateTeam))
        {
            if (IsNeutralTeamId(protectedTeam) || IsNeutralTeamId(candidateTeam))
                return false;

            return !string.Equals(protectedTeam, candidateTeam, StringComparison.OrdinalIgnoreCase);
        }

        if (TryComp<NpcFactionMemberComponent>(protectedEntity, out var protectedFaction) &&
            TryComp<NpcFactionMemberComponent>(candidate, out var candidateFaction))
        {
            var friendly = _npcFaction.IsEntityFriendly((protectedEntity, protectedFaction), (candidate, candidateFaction)) ||
                           _npcFaction.IsEntityFriendly((candidate, candidateFaction), (protectedEntity, protectedFaction));
            return !friendly;
        }

        return false;
    }

    private bool TryResolveProtectedTeamId(
        EntityUid protectedEntity,
        EnergyDomeGeneratorComponent generator,
        [NotNullWhen(true)] out string? teamId)
    {
        if (!string.IsNullOrWhiteSpace(generator.TeamId))
        {
            teamId = generator.TeamId;
            return true;
        }

        return TryResolveTeamId(protectedEntity, out teamId);
    }

    private bool TryResolveTeamId(EntityUid entity, [NotNullWhen(true)] out string? teamId)
    {
        if (_teamRule.TryGetTeamIdFromEntity(entity, out var resolved) &&
            !string.IsNullOrWhiteSpace(resolved))
        {
            teamId = resolved;
            return true;
        }

        if (TryComp<WH40KTeamMemberComponent>(entity, out var member) &&
            !string.IsNullOrWhiteSpace(member.TeamId))
        {
            teamId = member.TeamId;
            return true;
        }

        teamId = null;
        return false;
    }

    private static bool IsNeutralTeamId(string teamId)
    {
        return string.Equals(teamId, TeamNeutral, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveActivationConflicts(
        Entity<EnergyDomeGeneratorComponent> generator,
        EntityUid protectedEntity,
        EntityUid? user,
        bool popupErrors)
    {
        _activationConflictLosers.Clear();
        var wearableConflict = IsWearableGenerator(generator);
        var hasWearableConflict = false;
        var query = EntityQueryEnumerator<EnergyDomeGeneratorComponent>();

        while (query.MoveNext(out var otherUid, out var other))
        {
            if (otherUid == generator.Owner ||
                !other.Enabled ||
                other.SpawnedDome == null ||
                other.DomeParentEntity != protectedEntity)
            {
                continue;
            }

            if (wearableConflict && IsWearableGenerator((otherUid, other)))
            {
                _activationConflictLosers.Add(otherUid);
                hasWearableConflict = true;
                continue;
            }

            var compare = CompareActivationPriority(generator, (otherUid, other));
            if (compare < 0)
            {
                ShowAccessDeniedPopup(generator, "energy-dome-interference", user, popupErrors);
                return false;
            }

            _activationConflictLosers.Add(otherUid);
        }

        if (hasWearableConflict)
        {
            DisableDesiredActivation(generator);

            foreach (var loserUid in _activationConflictLosers)
            {
                if (!TryComp<EnergyDomeGeneratorComponent>(loserUid, out var loser))
                    continue;

                DisableDesiredActivation((loserUid, loser));
                TurnOff((loserUid, loser), startReloading: false, reason: EnergyDomeBreakReason.Conflict, playSound: false);
            }

            ShowAccessDeniedPopup(generator, "energy-dome-worn-conflict", user, popupErrors);
            return false;
        }

        foreach (var loserUid in _activationConflictLosers)
        {
            if (TryComp<EnergyDomeGeneratorComponent>(loserUid, out var loser))
                TurnOff((loserUid, loser), startReloading: false, reason: EnergyDomeBreakReason.Conflict, playSound: false);
        }

        return true;
    }

    private static int CompareActivationPriority(
        Entity<EnergyDomeGeneratorComponent> candidate,
        Entity<EnergyDomeGeneratorComponent> other)
    {
        var priorityCmp = candidate.Comp.Priority.CompareTo(other.Comp.Priority);
        if (priorityCmp != 0)
            return priorityCmp;

        if (candidate.Owner == other.Owner)
            return 0;

        // Lower uid wins ties for deterministic overlap resolution.
        return candidate.Owner.Id < other.Owner.Id ? 1 : -1;
    }

    private void TryRaiseRechargeReadyEvent(Entity<EnergyDomeGeneratorComponent> generator)
    {
        if (!generator.Comp.WaitingForRechargeReadyEvent)
            return;

        if (!TryComp<UseDelayComponent>(generator, out var useDelay) ||
            !_useDelay.IsDelayed((generator, useDelay)))
        {
            generator.Comp.WaitingForRechargeReadyEvent = false;
            RaiseRechargeReadyEvent(generator.Owner);
        }
    }

    private static string GetModeLocKey(EnergyDomeOperationMode mode)
    {
        return mode switch
        {
            EnergyDomeOperationMode.Bubble => "energy-dome-mode-bubble",
            EnergyDomeOperationMode.Wall => "energy-dome-mode-wall",
            _ => "energy-dome-mode-bubble"
        };
    }

    private static string GetSizeLocKey(EnergyDomeSizePreset size)
    {
        return size switch
        {
            EnergyDomeSizePreset.Small => "energy-dome-size-small",
            EnergyDomeSizePreset.Medium => "energy-dome-size-medium",
            EnergyDomeSizePreset.Huge => "energy-dome-size-huge",
            _ => "energy-dome-size-small"
        };
    }

    private static string GetColorLocKey(EnergyDomeColorPreset color)
    {
        return color switch
        {
            EnergyDomeColorPreset.Red => "energy-dome-color-red",
            EnergyDomeColorPreset.Blue => "energy-dome-color-blue",
            _ => "energy-dome-color-red"
        };
    }

    private static string GetWallSideLocKey(EnergyDomeWallSide side)
    {
        return side switch
        {
            EnergyDomeWallSide.Front => "energy-dome-wall-side-front",
            EnergyDomeWallSide.Right => "energy-dome-wall-side-right",
            EnergyDomeWallSide.Back => "energy-dome-wall-side-back",
            EnergyDomeWallSide.Left => "energy-dome-wall-side-left",
            _ => "energy-dome-wall-side-front"
        };
    }

    private static string GetStatusLocKey(EnergyDomeGeneratorComponent generator)
    {
        if (generator.WaitingForRechargeReadyEvent)
            return "energy-dome-status-cooldown";

        if (!generator.Enabled)
            return "energy-dome-status-idle";

        if (generator.Contested)
        {
            return "energy-dome-status-contested";
        }

        if (generator.StressEnabled &&
            generator.StressWarningThreshold > 0f &&
            generator.Stress >= generator.StressWarningThreshold)
        {
            return "energy-dome-status-stressed";
        }

        return "energy-dome-status-active";
    }

    private void RaiseActivatedEvent(EntityUid generatorUid, EntityUid domeUid)
    {
        var ev = new EnergyDomeActivatedEvent(generatorUid, domeUid);
        RaiseLocalEvent(generatorUid, ref ev);
    }

    private void RaiseBrokenEvent(EntityUid generatorUid, EnergyDomeBreakReason reason)
    {
        var ev = new EnergyDomeBrokenEvent(generatorUid, reason);
        RaiseLocalEvent(generatorUid, ref ev);
    }

    private void RaiseRechargeReadyEvent(EntityUid generatorUid)
    {
        var ev = new EnergyDomeRechargeReadyEvent(generatorUid);
        RaiseLocalEvent(generatorUid, ref ev);
    }
}
