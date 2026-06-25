using System.Linq;
using Content.Server.Actions;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Combat;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.KillTracking;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.RoundEnd;
using Content.Server.Shuttles.Systems;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server._WH40K.MetaProgress;
using Content.Server._WH40K.Stats;
using Content.Shared.Actions;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Flash;
using Content.Shared.Fluids.Components;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Gravity;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.PDA;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Shared.Projectiles;
using Content.Shared.Prototypes;
using Content.Shared.Pulling.Events;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Content.Shared.Storage;
using Content.Shared.Strip.Components;
using Content.Shared.StatusEffectNew;
using Content.Shared.Trigger.Systems;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared._WH40K.GunGame;
using Content.Shared._WH40K.Interface;
using Content.Shared._WH40K.MurderMystery;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.MurderMystery;

public sealed partial class WH40KMurderMysteryRuleSystem : GameRuleSystem<WH40KMurderMysteryRuleComponent>
{
    private static readonly TimeSpan TimerSyncInterval = TimeSpan.FromSeconds(5);
    private static readonly SoundSpecifier FlashSound = new SoundPathSpecifier("/Audio/Weapons/flash.ogg");
    private static readonly DamageSpecifier FatalBallisticDamage = new()
    {
        DamageDict = new()
        {
            ["Piercing"] = 200
        }
    };

    private static readonly ProtoId<SpeciesPrototype>[] AllowedSpecies =
    [
        "Human",
        "Felinid",
        "Dwarf"
    ];

    [Dependency] private IChatManager _chat = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private CombatAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _entityLookup = default!;
    [Dependency] private SharedFlashSystem _flash = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private WH40KMetaProgressSystem _metaProgress = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private RoundEndSystem _roundEnd = default!;
    [Dependency] private ShuttleSystem _shuttles = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private TriggerSystem _trigger = default!;
    [Dependency] private WH40KDamageProtectionSystem _damageProtection = default!;
    [Dependency] private WH40KMeleeProtectionSystem _meleeProtection = default!;

    private readonly HashSet<EntityUid> _flashTargets = [];
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("wh40k.murdermystery");

        SubscribeLocalEvent<RulePlayerSpawningEvent>(OnRulePlayerSpawning);
        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnBeforeSpawn);
        SubscribeLocalEvent<RefreshLateJoinAllowedEvent>(OnRefreshLateJoinAllowed);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged, before: new[] { typeof(KillTrackingSystem) });
        _damageProtection.RegisterHandler(OnBeforeDamageChanged);
        _meleeProtection.RegisterHandler(OnMeleeHit);

        SubscribeLocalEvent<WH40KMurderMysteryPlayerComponent, WH40KMurderMysterySmokeActionEvent>(OnSmokeAction);
        SubscribeLocalEvent<WH40KMurderMysteryPlayerComponent, WH40KMurderMysteryFlashActionEvent>(OnFlashAction);

        SubscribeLocalEvent<WH40KMurderMysteryKnifeComponent, GettingPickedUpAttemptEvent>(OnKnifePickupAttempt);
        SubscribeLocalEvent<WH40KMurderMysteryKnifeComponent, InteractHandEvent>(OnKnifeInteractHand);
        SubscribeLocalEvent<WH40KMurderMysteryKnifeComponent, GetVerbsEvent<InteractionVerb>>(OnKnifeInteractionVerbs);
        SubscribeLocalEvent<WH40KMurderMysteryKnifeComponent, GetVerbsEvent<AlternativeVerb>>(OnKnifeAlternativeVerbs);
        SubscribeLocalEvent<WH40KMurderMysteryKnifeComponent, GetVerbsEvent<ActivationVerb>>(OnKnifeActivationVerbs);
        SubscribeLocalEvent<WH40KMurderMysteryKnifeComponent, GetVerbsEvent<Verb>>(OnKnifeAnyVerbs);
        SubscribeLocalEvent<WH40KMurderMysteryKnifeComponent, BeingPulledAttemptEvent>(OnKnifeBeingPulledAttempt);

        SubscribeLocalEvent<WH40KMurderMysterySheriffRevolverComponent, GettingPickedUpAttemptEvent>(OnSheriffRevolverPickupAttempt);
        SubscribeLocalEvent<WH40KMurderMysterySheriffRevolverComponent, GotEquippedHandEvent>(OnSheriffRevolverEquipped);
        SubscribeLocalEvent<WH40KMurderMysterySheriffRevolverComponent, ShotAttemptedEvent>(OnSheriffRevolverShotAttempted);
        SubscribeLocalEvent<WH40KMurderMysterySheriffRevolverComponent, BeingPulledAttemptEvent>(OnSheriffRevolverBeingPulledAttempt);
        SubscribeLocalEvent<WH40KMurderMysterySheriffRevolverComponent, GetVerbsEvent<Verb>>(OnSheriffRevolverAnyVerbs);

        SubscribeLocalEvent<WH40KMurderMysterySheriffBulletComponent, ProjectileHitEvent>(OnSheriffBulletHit);
    }

    protected override void Started(EntityUid uid, WH40KMurderMysteryRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.PlayerRoles.Clear();
        component.PlayerProfiles.Clear();
        component.WinnerTeam = null;
        component.RewardsGranted = false;
        component.WaitingForPlayers = true;
        component.RolesAssigned = false;
        component.AssignmentElapsed = TimeSpan.Zero;
        component.ActiveRoundElapsed = TimeSpan.Zero;
        component.LastRoundProgressUpdateAt = _timing.CurTime;
        component.NextTimerSyncAt = TimeSpan.Zero;
        component.LastTimerDurationSeconds = -1;
        component.LastTimerElapsedSeconds = -1;
        component.LastTimerStopped = false;
        component.NextBloodCleanupAt = TimeSpan.Zero;

        ApplyMapStabilitySafeguards();
        PushRoundTimer(component, force: true);
    }

    protected override void Ended(EntityUid uid, WH40KMurderMysteryRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        var playerQuery = EntityQueryEnumerator<WH40KMurderMysteryPlayerComponent>();
        while (playerQuery.MoveNext(out var playerUid, out var playerComp))
        {
            RemoveRoleActions(playerUid, playerComp);
            RemComp<WH40KMurderMysteryMurderIconComponent>(playerUid);
            RemovePlayerProtection(playerUid, playerComp);
            RemCompDeferred<WH40KMurderMysteryPlayerComponent>(playerUid);
        }

        var knifeQuery = EntityQueryEnumerator<WH40KMurderMysteryKnifeComponent>();
        while (knifeQuery.MoveNext(out var knifeUid, out _))
        {
            if (!TerminatingOrDeleted(knifeUid))
                QueueDel(knifeUid);
        }

        var revolverQuery = EntityQueryEnumerator<WH40KMurderMysterySheriffRevolverComponent>();
        while (revolverQuery.MoveNext(out var revolverUid, out _))
        {
            if (!TerminatingOrDeleted(revolverUid))
                QueueDel(revolverUid);
        }

        component.PlayerRoles.Clear();
        component.PlayerProfiles.Clear();
        ClearRoundTimerHud();
    }

    protected override void AppendRoundEndText(EntityUid uid, WH40KMurderMysteryRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        if (component.WinnerTeam == null)
            return;

        args.AddLine(Loc.GetString(
            "wh40k-murder-mystery-winner-team",
            ("team", Loc.GetString(GetWinnerTeamLocKey(component.WinnerTeam.Value)))));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!TryGetActiveRule(out _, out var rule))
            return;

        UpdateRoundProgress(rule);
        RefreshSheriffRoles(rule);
        CleanupBlood(rule);
        PushRoundTimer(rule);

        if (rule.WinnerTeam != null || _roundEnd.IsRoundEndRequested())
            return;

        if (!rule.RolesAssigned)
        {
            if (!rule.WaitingForPlayers && rule.AssignmentElapsed >= rule.RoleAssignmentDelay)
                AssignRoles(rule);

            return;
        }

        if (rule.WaitingForPlayers)
            return;

        if (!HasLivingMurders(rule))
        {
            FinishRound(rule, WH40KMurderMysteryVictoryTeam.Innocents);
            return;
        }

        if (!HasLivingInnocents(rule))
        {
            FinishRound(rule, WH40KMurderMysteryVictoryTeam.Murders);
            return;
        }

        if (rule.RoundDuration > TimeSpan.Zero &&
            GetRoundElapsed(rule) >= rule.RoundDuration)
        {
            FinishRound(rule, WH40KMurderMysteryVictoryTeam.Innocents);
        }
    }

    private void OnRulePlayerSpawning(RulePlayerSpawningEvent ev)
    {
        if (!TryGetActiveRule(out var ruleEntity, out var rule))
            return;

        if (rule.PlayerRoles.Count > 0 || ev.PlayerPool.Count == 0)
            return;

        EntityUid station = EntityUid.Invalid;
        var stationQuery = EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent>();
        while (stationQuery.MoveNext(out var uid, out _, out _))
        {
            station = uid;
            break;
        }

        if (station == EntityUid.Invalid)
            return;

        foreach (var player in ev.PlayerPool.ToList())
        {
            if (!ev.Profiles.TryGetValue(player.UserId, out var profile))
                continue;

            SpawnMurderMysteryPlayer(player, profile, station, ruleEntity, rule, lateJoin: false);
            GameTicker.PlayerJoinGame(player);
        }

        ev.PlayerPool.Clear();
    }

    private void OnBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        if (!TryGetActiveRule(out var ruleEntity, out var rule))
            return;

        ev.Handled = SpawnMurderMysteryPlayer(ev.Player, ev.Profile, ev.Station, ruleEntity, rule, ev.LateJoin);
    }

    private void OnRefreshLateJoinAllowed(RefreshLateJoinAllowedEvent ev)
    {
        if (TryGetActiveRule(out _, out var rule) && rule.RolesAssigned)
            ev.Disallow();
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Critical && args.NewMobState != MobState.Dead)
            return;

        if (!TryComp<WH40KMurderMysteryPlayerComponent>(args.Target, out var playerComp) || playerComp.Eliminated)
            return;

        if (!TryGetActiveRule(out _, out var rule))
            return;

        playerComp.Eliminated = true;
        RemoveRoleActions(args.Target, playerComp);

        if (TryComp(args.Target, out BloodstreamComponent? bloodstream))
            _bloodstream.TryModifyBleedAmount((args.Target, bloodstream), -bloodstream.BleedAmount);

        if (!TryGetUserId(args.Target, out var victimId))
            return;

        if (playerComp.Role == WH40KMurderMysteryRole.Murder)
        {
            DeleteOwnedMurderKnives(victimId);
            RemComp<WH40KMurderMysteryMurderIconComponent>(args.Target);
        }
        else
        {
            DropOwnedSheriffRevolvers(args.Target);
        }

        RefreshSheriffRoles(rule);
    }

    private void OnBeforeDamageChanged(EntityUid uid, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !HasHarmfulDamage(args.Damage))
            return;

        if (!TryGetActiveRule(out _, out _))
            return;

        if (!HasComp<MobStateComponent>(uid))
        {
            args.Cancelled = true;
            return;
        }

        if (args.Origin == null)
            return;

        if (!_attackerResolver.TryResolveResponsibleEntity(args.Origin.Value, out var responsible))
            responsible = args.Origin.Value;

        if (responsible == uid)
            return;

        if (!TryComp<WH40KMurderMysteryPlayerComponent>(responsible, out var attackerComp) ||
            !TryComp<WH40KMurderMysteryPlayerComponent>(uid, out var targetComp))
        {
            return;
        }

        if (attackerComp.Role == WH40KMurderMysteryRole.Murder &&
            targetComp.Role == WH40KMurderMysteryRole.Murder)
        {
            args.Cancelled = true;
        }
    }

    private void OnMeleeHit(EntityUid uid, ref MeleeHitEvent args)
    {
        if (args.Handled || !args.IsHit)
            return;

        if (!TryGetActiveRule(out _, out _))
            return;

        if (HasComp<WH40KMurderMysteryKnifeComponent>(args.Weapon) ||
            HasComp<WH40KMurderMysterySheriffRevolverComponent>(args.Weapon))
        {
            return;
        }

        foreach (var target in args.HitEntities)
        {
            if (HasComp<WH40KMurderMysteryPlayerComponent>(target))
            {
                args.Handled = true;
                return;
            }
        }
    }

    private void OnSmokeAction(Entity<WH40KMurderMysteryPlayerComponent> ent, ref WH40KMurderMysterySmokeActionEvent args)
    {
        if (args.Handled ||
            args.Performer != ent.Owner ||
            ent.Comp.Role != WH40KMurderMysteryRole.Murder ||
            ent.Comp.SmokeUsesRemaining <= 0)
        {
            return;
        }

        if (!TryGetActiveRule(out _, out var rule))
            return;

        ent.Comp.SmokeUsesRemaining--;

        var grenade = Spawn(rule.SmokeGrenadePrototype, _transform.GetMapCoordinates(ent.Owner));
        _trigger.Trigger(grenade, ent.Owner, "timer");

        UpdateMurderActions(ent.Owner, ent.Comp, rule);
        args.Handled = true;
    }

    private void OnFlashAction(Entity<WH40KMurderMysteryPlayerComponent> ent, ref WH40KMurderMysteryFlashActionEvent args)
    {
        if (args.Handled ||
            args.Performer != ent.Owner ||
            ent.Comp.Role != WH40KMurderMysteryRole.Murder ||
            ent.Comp.FlashUsesRemaining <= 0)
        {
            return;
        }

        if (!TryGetActiveRule(out _, out var rule))
            return;

        ent.Comp.FlashUsesRemaining--;
        _audio.PlayPvs(FlashSound, ent.Owner);

        _flashTargets.Clear();
        _entityLookup.GetEntitiesInRange(Transform(ent.Owner).Coordinates, rule.FlashRadius, _flashTargets);

        foreach (var target in _flashTargets)
        {
            if (!TryComp<WH40KMurderMysteryPlayerComponent>(target, out var playerComp) ||
                playerComp.Role == WH40KMurderMysteryRole.Murder ||
                !TryComp<MobStateComponent>(target, out var mobState) ||
                mobState.CurrentState != MobState.Alive)
            {
                continue;
            }

            _flash.Flash(target, ent.Owner, ent.Owner, rule.FlashDuration, rule.FlashSlowTo);
            _statusEffects.TryAddStatusEffectDuration(target, BlindnessSystem.BlindingStatusEffect, rule.FlashDuration);
        }

        UpdateMurderActions(ent.Owner, ent.Comp, rule);
        args.Handled = true;
    }

    private void OnKnifePickupAttempt(Entity<WH40KMurderMysteryKnifeComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        if (args.Cancelled || CanUseKnife(args.User, ent.Comp))
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("wh40k-murder-mystery-knife-locked"), ent.Owner, args.User);
    }

    private void OnKnifeInteractHand(Entity<WH40KMurderMysteryKnifeComponent> ent, ref InteractHandEvent args)
    {
        if (CanUseKnife(args.User, ent.Comp))
            return;

        args.Handled = true;
    }

    private void OnKnifeInteractionVerbs(Entity<WH40KMurderMysteryKnifeComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        FilterKnifeVerbs(ent.Comp, ref args);
    }

    private void OnKnifeAlternativeVerbs(Entity<WH40KMurderMysteryKnifeComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        FilterKnifeVerbs(ent.Comp, ref args);
    }

    private void OnKnifeActivationVerbs(Entity<WH40KMurderMysteryKnifeComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        FilterKnifeVerbs(ent.Comp, ref args);
    }

    private void OnKnifeAnyVerbs(Entity<WH40KMurderMysteryKnifeComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        FilterKnifeVerbs(ent.Comp, ref args);
    }

    private void OnKnifeBeingPulledAttempt(Entity<WH40KMurderMysteryKnifeComponent> ent, ref BeingPulledAttemptEvent args)
    {
        if (args.Cancelled || CanUseKnife(args.Puller, ent.Comp))
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("wh40k-murder-mystery-knife-locked"), ent.Owner, args.Puller);
    }

    private void OnSheriffRevolverPickupAttempt(Entity<WH40KMurderMysterySheriffRevolverComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp<WH40KMurderMysteryPlayerComponent>(args.User, out var playerComp) ||
            playerComp.Role == WH40KMurderMysteryRole.Murder)
        {
            args.Cancel();
            _popup.PopupEntity(Loc.GetString("wh40k-murder-mystery-revolver-locked"), ent.Owner, args.User);
        }
    }

    private void OnSheriffRevolverEquipped(Entity<WH40KMurderMysterySheriffRevolverComponent> ent, ref GotEquippedHandEvent args)
    {
        if (!TryGetActiveRule(out _, out var rule) ||
            !TryComp<WH40KMurderMysteryPlayerComponent>(args.User, out var playerComp) ||
            !TryGetUserId(args.User, out var userId) ||
            playerComp.Role == WH40KMurderMysteryRole.Murder)
        {
            return;
        }

        if (playerComp.Role != WH40KMurderMysteryRole.Sheriff)
            SendRoleBriefing(userId, WH40KMurderMysteryRole.Sheriff, promotedSheriff: true);

        playerComp.Role = WH40KMurderMysteryRole.Sheriff;
        rule.PlayerRoles[userId] = WH40KMurderMysteryRole.Sheriff;
    }

    private void OnSheriffRevolverShotAttempted(Entity<WH40KMurderMysterySheriffRevolverComponent> ent, ref ShotAttemptedEvent args)
    {
        if (TryComp<WH40KMurderMysteryPlayerComponent>(args.User, out var playerComp) &&
            playerComp.Role == WH40KMurderMysteryRole.Sheriff)
        {
            return;
        }

        args.Cancel();
    }

    private void OnSheriffRevolverBeingPulledAttempt(Entity<WH40KMurderMysterySheriffRevolverComponent> ent, ref BeingPulledAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp<WH40KMurderMysteryPlayerComponent>(args.Puller, out var playerComp) ||
            playerComp.Role == WH40KMurderMysteryRole.Murder)
        {
            args.Cancel();
            _popup.PopupEntity(Loc.GetString("wh40k-murder-mystery-revolver-locked"), ent.Owner, args.Puller);
        }
    }

    private void OnSheriffRevolverAnyVerbs(Entity<WH40KMurderMysterySheriffRevolverComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (TryComp<WH40KMurderMysteryPlayerComponent>(args.User, out var playerComp) &&
            playerComp.Role != WH40KMurderMysteryRole.Murder)
        {
            return;
        }

        args.Verbs.Clear();
    }

    private void OnSheriffBulletHit(Entity<WH40KMurderMysterySheriffBulletComponent> ent, ref ProjectileHitEvent args)
    {
        if (!TryGetActiveRule(out _, out _) ||
            args.Shooter is not { } shooter ||
            !TryComp<WH40KMurderMysteryPlayerComponent>(shooter, out var shooterComp) ||
            shooterComp.Role != WH40KMurderMysteryRole.Sheriff)
        {
            return;
        }

        if (!TryComp<WH40KMurderMysteryPlayerComponent>(args.Target, out var targetComp))
            return;

        if (targetComp.Role == WH40KMurderMysteryRole.Murder)
        {
            ApplyFatalDamage(args.Target, shooter);

            if (TryGetUserId(args.Target, out var murderId))
                DeleteOwnedMurderKnives(murderId);

            return;
        }

        if (targetComp.Role == WH40KMurderMysteryRole.Unassigned)
            return;

        ApplyFatalDamage(args.Target, shooter);

        if (shooter != args.Target)
            ApplyFatalDamage(shooter, shooter);
    }

    private bool SpawnMurderMysteryPlayer(
        ICommonSession player,
        HumanoidCharacterProfile originalProfile,
        EntityUid station,
        EntityUid ruleEntity,
        WH40KMurderMysteryRuleComponent rule,
        bool lateJoin)
    {
        if (lateJoin && rule.RolesAssigned && !rule.PlayerRoles.ContainsKey(player.UserId))
        {
            GameTicker.JoinAsObserver(player);
            _chat.DispatchServerMessage(player, Loc.GetString("wh40k-murder-mystery-late-join-closed"));
            return true;
        }

        var profile = SanitizeProfile(player, originalProfile);
        RememberPlayerProfile(player.UserId, profile, rule);

        var (mindId, mind) = _mind.GetOrCreateMind(player.UserId);
        mind.CharacterName = profile.Name;
        Dirty(mindId, mind);

        var spawn = FindFarthestSpawnPoint(station);
        if (spawn == EntityCoordinates.Invalid)
        {
            GameTicker.JoinAsObserver(player);
            _chat.DispatchServerMessage(player, Loc.GetString("wh40k-murder-mystery-no-spawn"));
            return true;
        }

        var mob = _stationSpawning.SpawnPlayerMob(spawn, rule.FallbackJob, profile, station);
        _mind.TransferTo(mindId, mob, mind: mind);

        var playerComp = EnsureComp<WH40KMurderMysteryPlayerComponent>(mob);
        ApplyPlayerProtection(mob, playerComp);

        if (rule.RolesAssigned && rule.PlayerRoles.TryGetValue(player.UserId, out var assignedRole))
            ApplyRole(mob, player.UserId, playerComp, assignedRole, rule, announce: false, grantStartingWeapon: true);

        return true;
    }

    private HumanoidCharacterProfile SanitizeProfile(ICommonSession player, HumanoidCharacterProfile profile)
    {
        if (AllowedSpecies.Contains(profile.Species))
            return profile;

        var fallback = HumanoidCharacterProfile.DefaultWithSpecies();
        _sawmill.Warning(
            "Resetting Murder Mystery profile for {Player} ({UserId}) from unsupported species '{Species}' to default '{FallbackSpecies}'.",
            player.Name,
            player.UserId,
            profile.Species,
            fallback.Species);
        return fallback;
    }

    private EntityCoordinates FindFarthestSpawnPoint(EntityUid station)
    {
        var stationMaps = new HashSet<MapId>();
        var stationGrids = new HashSet<EntityUid>();

        if (TryComp<StationDataComponent>(station, out var stationData))
        {
            foreach (var grid in stationData.Grids)
            {
                if (!TryComp(grid, out TransformComponent? gridXform) || gridXform.MapID == MapId.Nullspace)
                    continue;

                stationGrids.Add(grid);
                stationMaps.Add(gridXform.MapID);
            }
        }

        if (stationMaps.Count == 0)
        {
            var stationMap = Transform(station).MapID;
            if (stationMap != MapId.Nullspace)
                stationMaps.Add(stationMap);
        }

        var spawnPoints = new List<(EntityUid Uid, EntityCoordinates Coords, int Priority)>();
        var customSpawns = EntityQueryEnumerator<WH40KGunGameSpawnPointComponent, TransformComponent>();
        while (customSpawns.MoveNext(out var uid, out var marker, out var xform))
        {
            if (!IsOnStation(xform, stationMaps, stationGrids))
                continue;

            spawnPoints.Add((uid, xform.Coordinates, marker.Priority));
        }

        if (spawnPoints.Count == 0)
        {
            var fallbackSpawns = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
            while (fallbackSpawns.MoveNext(out var uid, out var marker, out var xform))
            {
                if (!IsOnStation(xform, stationMaps, stationGrids))
                    continue;

                if (marker.SpawnType == SpawnPointType.LateJoin || marker.SpawnType == SpawnPointType.Job)
                    spawnPoints.Add((uid, xform.Coordinates, 0));
            }
        }

        if (spawnPoints.Count == 0)
        {
            foreach (var grid in stationGrids)
            {
                if (TryComp(grid, out TransformComponent? gridXform))
                    return gridXform.Coordinates;
            }

            return EntityCoordinates.Invalid;
        }

        var livingPlayers = new List<EntityUid>();
        var playerQuery = EntityQueryEnumerator<WH40KMurderMysteryPlayerComponent, MobStateComponent, TransformComponent>();
        while (playerQuery.MoveNext(out var uid, out _, out var mobState, out var xform))
        {
            if (mobState.CurrentState != MobState.Alive || !IsOnStation(xform, stationMaps, stationGrids))
                continue;

            livingPlayers.Add(uid);
        }

        if (livingPlayers.Count == 0)
            return _random.Pick(spawnPoints).Coords;

        var bestPoint = spawnPoints[0];
        var bestDistance = -1f;
        var bestPriority = int.MinValue;

        foreach (var point in spawnPoints)
        {
            var pointPos = _transform.GetMapCoordinates(point.Uid, Transform(point.Uid));
            var minDistance = float.MaxValue;

            foreach (var player in livingPlayers)
            {
                var playerPos = _transform.GetMapCoordinates(player, Transform(player));
                var distance = (pointPos.Position - playerPos.Position).LengthSquared();
                if (distance < minDistance)
                    minDistance = distance;
            }

            if (minDistance > bestDistance ||
                minDistance.Equals(bestDistance) && point.Priority > bestPriority)
            {
                bestDistance = minDistance;
                bestPriority = point.Priority;
                bestPoint = point;
            }
        }

        return bestPoint.Coords;
    }

    private static bool IsOnStation(TransformComponent xform, HashSet<MapId> stationMaps, HashSet<EntityUid> stationGrids)
    {
        if (xform.MapID == MapId.Nullspace || !stationMaps.Contains(xform.MapID))
            return false;

        return xform.GridUid == null || stationGrids.Count == 0 || stationGrids.Contains(xform.GridUid.Value);
    }

    private void AssignRoles(WH40KMurderMysteryRuleComponent rule)
    {
        if (rule.RolesAssigned)
            return;

        var participants = GetActiveParticipants();
        if (participants.Count < rule.MinimumPlayersToRun)
            return;

        _random.Shuffle(participants);
        var split = WH40KMurderMysteryMath.GetRoleSplit(participants.Count);
        var murders = Math.Min(split.Murders, participants.Count);
        var sheriffs = Math.Min(split.Sheriffs, participants.Count - murders);

        rule.PlayerRoles.Clear();
        rule.RolesAssigned = true;

        for (var i = 0; i < participants.Count; i++)
        {
            var participant = participants[i];
            var role = i < murders
                ? WH40KMurderMysteryRole.Murder
                : i < murders + sheriffs
                    ? WH40KMurderMysteryRole.Sheriff
                    : WH40KMurderMysteryRole.Civilian;

            ApplyRole(participant.Mob, participant.UserId, participant.PlayerComp, role, rule, announce: true, grantStartingWeapon: true);
        }

        rule.NextTimerSyncAt = TimeSpan.Zero;
        PushRoundTimer(rule, force: true);
    }

    private List<MurderMysteryParticipant> GetActiveParticipants()
    {
        var participants = new List<MurderMysteryParticipant>();
        foreach (var session in _player.Sessions)
        {
            if (session.Status != SessionStatus.InGame ||
                session.AttachedEntity is not { Valid: true } mob ||
                !TryComp<WH40KMurderMysteryPlayerComponent>(mob, out var playerComp))
            {
                continue;
            }

            participants.Add(new MurderMysteryParticipant(session.UserId, mob, playerComp));
        }

        return participants;
    }

    private void ApplyRole(
        EntityUid mob,
        NetUserId userId,
        WH40KMurderMysteryPlayerComponent playerComp,
        WH40KMurderMysteryRole role,
        WH40KMurderMysteryRuleComponent rule,
        bool announce,
        bool grantStartingWeapon)
    {
        playerComp.Role = role;
        rule.PlayerRoles[userId] = role;

        RemoveRoleActions(mob, playerComp);
        RemComp<WH40KMurderMysteryMurderIconComponent>(mob);

        if (role == WH40KMurderMysteryRole.Murder)
        {
            playerComp.SmokeUsesRemaining = rule.MurderAbilityUses;
            playerComp.FlashUsesRemaining = rule.MurderAbilityUses;
            EnsureComp<WH40KMurderMysteryMurderIconComponent>(mob);
            EnsureMurderKnife(mob, userId, rule, grantStartingWeapon);
            UpdateMurderActions(mob, playerComp, rule);
        }
        else if (role == WH40KMurderMysteryRole.Sheriff && grantStartingWeapon)
        {
            EnsureSheriffRevolver(mob, rule);
        }

        if (announce)
            SendRoleBriefing(userId, role);
    }

    private void SendRoleBriefing(NetUserId userId, WH40KMurderMysteryRole role, bool promotedSheriff = false)
    {
        if (!_player.TryGetSessionById(userId, out var session))
            return;

        var message = promotedSheriff
            ? Loc.GetString("wh40k-murder-mystery-role-promoted-sheriff")
            : role switch
            {
                WH40KMurderMysteryRole.Murder => Loc.GetString("wh40k-murder-mystery-role-murder"),
                WH40KMurderMysteryRole.Sheriff => Loc.GetString("wh40k-murder-mystery-role-sheriff"),
                _ => Loc.GetString("wh40k-murder-mystery-role-civilian")
            };

        _chat.DispatchServerMessage(session, message);
    }

    private void EnsureMurderKnife(EntityUid mob, NetUserId userId, WH40KMurderMysteryRuleComponent rule, bool grantStartingWeapon)
    {
        if (!grantStartingWeapon)
            return;

        if (FindOwnedKnife(userId) is { } knife && !TerminatingOrDeleted(knife))
            return;

        var spawned = Spawn(rule.MurderKnifePrototype, _transform.GetMapCoordinates(mob));
        var knifeComp = EnsureComp<WH40KMurderMysteryKnifeComponent>(spawned);
        knifeComp.OwnerUserId = userId;

        if (!TryInsertIntoBackpack(mob, spawned) && !_hands.TryPickupAnyHand(mob, spawned))
            _transform.DropNextTo(spawned, mob);
    }

    private void EnsureSheriffRevolver(EntityUid mob, WH40KMurderMysteryRuleComponent rule)
    {
        if (OwnsSheriffRevolver(mob))
            return;

        var spawned = Spawn(rule.SheriffRevolverPrototype, _transform.GetMapCoordinates(mob));
        if (!TryInsertIntoBackpack(mob, spawned) && !_hands.TryPickupAnyHand(mob, spawned))
            _transform.DropNextTo(spawned, mob);
    }

    private bool TryInsertIntoBackpack(EntityUid mob, EntityUid item)
    {
        if (!_inventory.TryGetSlotEntity(mob, "back", out var backItem) || backItem == null)
            return false;

        if (!_container.TryGetContainer(backItem.Value, StorageComponent.ContainerId, out var storage))
            return false;

        return _container.Insert((item, Transform(item), null, null), storage);
    }

    private void UpdateMurderActions(EntityUid mob, WH40KMurderMysteryPlayerComponent playerComp, WH40KMurderMysteryRuleComponent rule)
    {
        if (playerComp.Role != WH40KMurderMysteryRole.Murder)
        {
            RemoveRoleActions(mob, playerComp);
            return;
        }

        if (playerComp.SmokeUsesRemaining > 0)
            _actions.AddAction(mob, ref playerComp.SmokeActionEntity, rule.SmokeAction, mob);
        else
            RemoveAction(mob, ref playerComp.SmokeActionEntity);

        if (playerComp.FlashUsesRemaining > 0)
            _actions.AddAction(mob, ref playerComp.FlashActionEntity, rule.FlashAction, mob);
        else
            RemoveAction(mob, ref playerComp.FlashActionEntity);
    }

    private void RemoveRoleActions(EntityUid mob, WH40KMurderMysteryPlayerComponent playerComp)
    {
        RemoveAction(mob, ref playerComp.SmokeActionEntity);
        RemoveAction(mob, ref playerComp.FlashActionEntity);
    }

    private void RemoveAction(EntityUid mob, ref EntityUid? action)
    {
        if (action != null)
            _actions.RemoveAction(mob, action);

        action = null;
    }

    private void ApplyPlayerProtection(EntityUid mob, WH40KMurderMysteryPlayerComponent playerComp)
    {
        if (TryComp<HandsComponent>(mob, out var hands))
        {
            playerComp.PreviousHandsCanBeStripped = hands.CanBeStripped;
            _hands.SetCanBeStripped((mob, hands), false);
        }

        if (HasComp<StrippableComponent>(mob))
        {
            RemComp<StrippableComponent>(mob);
            playerComp.RemovedStrippable = true;
        }

        ProtectEquippedInventory(mob, playerComp);
    }

    private void RemovePlayerProtection(EntityUid mob, WH40KMurderMysteryPlayerComponent playerComp)
    {
        if (TryComp<HandsComponent>(mob, out var hands))
            _hands.SetCanBeStripped((mob, hands), playerComp.PreviousHandsCanBeStripped);

        if (playerComp.RemovedStrippable)
        {
            EnsureComp<StrippableComponent>(mob);
            playerComp.RemovedStrippable = false;
        }

        foreach (var item in playerComp.ProtectedItems.ToArray())
        {
            if (TerminatingOrDeleted(item))
                continue;

            RemComp<UnremoveableComponent>(item);
            RemComp<WH40KGunGameLockedComponent>(item);
        }

        playerComp.ProtectedItems.Clear();
    }

    private void ProtectEquippedInventory(EntityUid mob, WH40KMurderMysteryPlayerComponent playerComp)
    {
        if (!TryComp<InventoryComponent>(mob, out var inventory))
            return;

        foreach (var slot in inventory.Slots)
        {
            if (!_inventory.TryGetSlotEntity(mob, slot.Name, out var item, inventory) || item == null)
                continue;

            if (slot.Name is not ("id" or "ears") && !HasComp<Content.Shared.Clothing.Components.ClothingComponent>(item.Value))
                continue;

            if (slot.Name == "back")
            {
                ProtectBackpackItem(item.Value, playerComp);
                continue;
            }

            ProtectEquippedItem(item.Value, playerComp);
        }
    }

    private void ProtectEquippedItem(EntityUid item, WH40KMurderMysteryPlayerComponent playerComp)
    {
        var unremovable = EnsureComp<UnremoveableComponent>(item);
        unremovable.DeleteOnDrop = false;

        var locked = EnsureComp<WH40KGunGameLockedComponent>(item);
        locked.BlockInteractUsing = true;
        Dirty(item, locked);

        playerComp.ProtectedItems.Add(item);
        ProtectPdaContents(item, playerComp);
    }

    private void ProtectBackpackItem(EntityUid item, WH40KMurderMysteryPlayerComponent playerComp)
    {
        var unremovable = EnsureComp<UnremoveableComponent>(item);
        unremovable.DeleteOnDrop = false;

        playerComp.ProtectedItems.Add(item);
    }

    private void ProtectPdaContents(EntityUid item, WH40KMurderMysteryPlayerComponent playerComp)
    {
        if (!HasComp<PdaComponent>(item))
            return;

        ProtectPdaSlotItem(item, PdaComponent.PdaIdSlotId, playerComp);
        ProtectPdaSlotItem(item, PdaComponent.PdaPenSlotId, playerComp);
        ProtectPdaSlotItem(item, PdaComponent.PdaPaiSlotId, playerComp);
    }

    private void ProtectPdaSlotItem(EntityUid pda, string slotId, WH40KMurderMysteryPlayerComponent playerComp)
    {
        if (!_itemSlots.TryGetSlot(pda, slotId, out var slot) || slot.Item is not { } contained)
            return;

        var unremovable = EnsureComp<UnremoveableComponent>(contained);
        unremovable.DeleteOnDrop = false;
        playerComp.ProtectedItems.Add(contained);
    }

    private void RefreshSheriffRoles(WH40KMurderMysteryRuleComponent rule)
    {
        if (!rule.RolesAssigned)
            return;

        var query = EntityQueryEnumerator<WH40KMurderMysteryPlayerComponent>();
        while (query.MoveNext(out var uid, out var playerComp))
        {
            if (!TryGetUserId(uid, out var userId))
                continue;

            if (playerComp.Role == WH40KMurderMysteryRole.Murder || rule.PlayerRoles.GetValueOrDefault(userId) == WH40KMurderMysteryRole.Murder)
            {
                rule.PlayerRoles[userId] = WH40KMurderMysteryRole.Murder;
                continue;
            }

            var shouldBeSheriff = OwnsSheriffRevolver(uid);
            var newRole = shouldBeSheriff ? WH40KMurderMysteryRole.Sheriff : WH40KMurderMysteryRole.Civilian;
            if (playerComp.Role != newRole)
            {
                if (newRole == WH40KMurderMysteryRole.Sheriff)
                    SendRoleBriefing(userId, WH40KMurderMysteryRole.Sheriff, promotedSheriff: true);

                playerComp.Role = newRole;
            }

            rule.PlayerRoles[userId] = newRole;
        }
    }

    private bool OwnsSheriffRevolver(EntityUid mob)
    {
        var query = EntityQueryEnumerator<WH40KMurderMysterySheriffRevolverComponent>();
        while (query.MoveNext(out var revolverUid, out _))
        {
            if (IsNestedUnderOwner(revolverUid, mob))
                return true;
        }

        return false;
    }

    private EntityUid? FindOwnedKnife(NetUserId ownerUserId)
    {
        var query = EntityQueryEnumerator<WH40KMurderMysteryKnifeComponent>();
        while (query.MoveNext(out var knifeUid, out var knifeComp))
        {
            if (knifeComp.OwnerUserId == ownerUserId)
                return knifeUid;
        }

        return null;
    }

    private void DeleteOwnedMurderKnives(NetUserId ownerUserId)
    {
        var query = EntityQueryEnumerator<WH40KMurderMysteryKnifeComponent>();
        while (query.MoveNext(out var knifeUid, out var knifeComp))
        {
            if (knifeComp.OwnerUserId != ownerUserId || TerminatingOrDeleted(knifeUid))
                continue;

            QueueDel(knifeUid);
        }
    }

    private void DropOwnedSheriffRevolvers(EntityUid mob)
    {
        var query = EntityQueryEnumerator<WH40KMurderMysterySheriffRevolverComponent>();
        while (query.MoveNext(out var revolverUid, out _))
        {
            if (!IsNestedUnderOwner(revolverUid, mob))
                continue;

            _container.TryRemoveFromContainer(revolverUid, force: true);
            _transform.DropNextTo(revolverUid, mob);
        }
    }

    private bool IsNestedUnderOwner(EntityUid entity, EntityUid owner)
    {
        var current = entity;
        while (Exists(current))
        {
            var xform = Transform(current);
            if (xform.ParentUid == owner)
                return true;

            if (xform.ParentUid == EntityUid.Invalid || xform.ParentUid == current)
                return false;

            current = xform.ParentUid;
        }

        return false;
    }

    private void UpdateRoundProgress(WH40KMurderMysteryRuleComponent rule)
    {
        var now = _timing.CurTime;
        if (rule.LastRoundProgressUpdateAt == TimeSpan.Zero)
        {
            rule.LastRoundProgressUpdateAt = now;
            return;
        }

        if (!rule.WaitingForPlayers)
        {
            var delta = now - rule.LastRoundProgressUpdateAt;
            if (rule.RolesAssigned)
                rule.ActiveRoundElapsed += delta;
            else
                rule.AssignmentElapsed += delta;
        }

        rule.LastRoundProgressUpdateAt = now;

        var shouldWait = CountActiveParticipants() < rule.MinimumPlayersToRun;
        if (shouldWait == rule.WaitingForPlayers)
            return;

        rule.WaitingForPlayers = shouldWait;
        rule.NextTimerSyncAt = TimeSpan.Zero;

        if (shouldWait)
        {
            _chat.DispatchServerAnnouncement(Loc.GetString(
                "wh40k-murder-mystery-paused-waiting-players",
                ("players", rule.MinimumPlayersToRun)));
        }
        else
        {
            _chat.DispatchServerAnnouncement(Loc.GetString("wh40k-murder-mystery-paused-resumed"));
        }
    }

    private int CountActiveParticipants()
    {
        var count = 0;
        foreach (var session in _player.Sessions)
        {
            if (session.Status == SessionStatus.InGame &&
                session.AttachedEntity is { Valid: true } mob &&
                HasComp<WH40KMurderMysteryPlayerComponent>(mob))
            {
                count++;
            }
        }

        return count;
    }

    private bool HasLivingMurders(WH40KMurderMysteryRuleComponent rule)
    {
        foreach (var (userId, role) in rule.PlayerRoles)
        {
            if (role != WH40KMurderMysteryRole.Murder)
                continue;

            if (!TryGetOwnedEntity(userId, out var mob) ||
                !TryComp<MobStateComponent>(mob, out var mobState) ||
                mobState.CurrentState != MobState.Alive)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool HasLivingInnocents(WH40KMurderMysteryRuleComponent rule)
    {
        foreach (var (userId, role) in rule.PlayerRoles)
        {
            if (role == WH40KMurderMysteryRole.Murder || role == WH40KMurderMysteryRole.Unassigned)
                continue;

            if (!TryGetOwnedEntity(userId, out var mob) ||
                !TryComp<MobStateComponent>(mob, out var mobState) ||
                mobState.CurrentState != MobState.Alive)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void FinishRound(WH40KMurderMysteryRuleComponent rule, WH40KMurderMysteryVictoryTeam winnerTeam)
    {
        if (rule.WinnerTeam != null)
            return;

        rule.WinnerTeam = winnerTeam;
        GrantWinnerRewards(rule, winnerTeam);
        _roundEnd.EndRound(rule.RestartDelay);
    }

    private void GrantWinnerRewards(WH40KMurderMysteryRuleComponent rule, WH40KMurderMysteryVictoryTeam winnerTeam)
    {
        if (rule.RewardsGranted || rule.WinnerRewardXp <= 0)
            return;

        rule.RewardsGranted = true;

        foreach (var (userId, role) in rule.PlayerRoles)
        {
            if (winnerTeam == WH40KMurderMysteryVictoryTeam.Murders && role != WH40KMurderMysteryRole.Murder)
                continue;

            if (winnerTeam == WH40KMurderMysteryVictoryTeam.Innocents && role == WH40KMurderMysteryRole.Murder)
                continue;

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mode"] = "WH40KMurderMystery",
                ["team"] = winnerTeam.ToString(),
                ["roundId"] = GameTicker.RoundId.ToString()
            };

            _metaProgress.GrantLifetimeXp(userId, rule.WinnerRewardXp, WH40KPlayerStatKeys.MetaXpMurderMysteryWin, metadata);
        }
    }

    private void PushRoundTimer(WH40KMurderMysteryRuleComponent rule, bool force = false)
    {
        var stopped = rule.RoundDuration <= TimeSpan.Zero || rule.WaitingForPlayers || !rule.RolesAssigned;
        var durationSeconds = stopped
            ? 0
            : Math.Max(0, (int) Math.Ceiling(rule.RoundDuration.TotalSeconds));
        var elapsedSeconds = rule.RolesAssigned
            ? Math.Max(0, (int) Math.Floor(GetRoundElapsed(rule).TotalSeconds))
            : 0;

        var changed = rule.LastTimerStopped != stopped ||
                      rule.LastTimerDurationSeconds != durationSeconds;

        if (!force &&
            !changed &&
            _timing.CurTime < rule.NextTimerSyncAt)
        {
            return;
        }

        rule.LastTimerStopped = stopped;
        rule.LastTimerDurationSeconds = durationSeconds;
        rule.LastTimerElapsedSeconds = elapsedSeconds;
        rule.NextTimerSyncAt = _timing.CurTime + TimerSyncInterval;

        var ev = new WH40KRoundTimerEvent(true, GameTicker.RoundId, durationSeconds, elapsedSeconds, stopped);
        foreach (var session in _player.Sessions)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(ev, session);
        }
    }

    private void ClearRoundTimerHud()
    {
        var ev = new WH40KRoundTimerEvent(false, GameTicker.RoundId, 0, 0, false);
        foreach (var session in _player.Sessions)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(ev, session);
        }
    }

    private void CleanupBlood(WH40KMurderMysteryRuleComponent rule)
    {
        if (rule.BloodCleanupInterval <= TimeSpan.Zero || _timing.CurTime < rule.NextBloodCleanupAt)
            return;

        rule.NextBloodCleanupAt = _timing.CurTime + rule.BloodCleanupInterval;

        var puddleQuery = EntityQueryEnumerator<PuddleComponent>();
        while (puddleQuery.MoveNext(out var puddleUid, out var puddleComp))
        {
            if (!_solutions.ResolveSolution(puddleUid, puddleComp.SolutionName, ref puddleComp.Solution, out var solution))
                continue;

            if (solution.GetTotalPrototypeQuantity("Blood") > FixedPoint2.Zero && !TerminatingOrDeleted(puddleUid))
                QueueDel(puddleUid);
        }
    }

    private TimeSpan GetRoundElapsed(WH40KMurderMysteryRuleComponent rule)
    {
        return rule.ActiveRoundElapsed < TimeSpan.Zero
            ? TimeSpan.Zero
            : rule.ActiveRoundElapsed;
    }

    private void ApplyMapStabilitySafeguards()
    {
        var mapId = GameTicker.DefaultMap;
        if (mapId == MapId.Nullspace)
            return;

        foreach (var grid in _mapManager.GetAllGrids(mapId))
        {
            _shuttles.Disable(grid.Owner);
            EnsureInherentGravity(grid.Owner, raiseGravityChangedEvent: true);
        }

        if (_map.TryGetMap(mapId, out var mapUid))
        {
            EnsureInherentGravity(mapUid.Value, raiseGravityChangedEvent: false);
            EnsureAmbientAir(mapUid.Value);
        }
    }

    private void EnsureInherentGravity(EntityUid uid, bool raiseGravityChangedEvent)
    {
        var gravity = EnsureComp<GravityComponent>(uid);
        var wasEnabled = gravity.Enabled;

        if (!gravity.Enabled || !gravity.Inherent)
        {
            gravity.Enabled = true;
            gravity.Inherent = true;
            Dirty(uid, gravity);
        }

        if (raiseGravityChangedEvent && !wasEnabled)
        {
            var ev = new GravityChangedEvent(uid, true);
            RaiseLocalEvent(uid, ref ev, true);
        }
    }

    private void EnsureAmbientAir(EntityUid mapUid)
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;

        _atmos.SetMapAtmosphere(mapUid, false, new GasMixture(moles, Atmospherics.T20C));
    }

    private void ApplyFatalDamage(EntityUid target, EntityUid? origin)
    {
        _damageable.TryChangeDamage(target, FatalBallisticDamage, ignoreResistances: true, origin: origin);
    }

    private void RememberPlayerProfile(NetUserId userId, HumanoidCharacterProfile profile, WH40KMurderMysteryRuleComponent rule)
    {
        rule.PlayerProfiles[userId] = profile;
    }

    private bool TryGetActiveRule(out EntityUid ruleEntity, out WH40KMurderMysteryRuleComponent rule)
    {
        var query = EntityQueryEnumerator<WH40KMurderMysteryRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var comp, out var gameRule))
        {
            if (!GameTicker.IsGameRuleActive(uid, gameRule))
                continue;

            ruleEntity = uid;
            rule = comp;
            return true;
        }

        ruleEntity = EntityUid.Invalid;
        rule = default!;
        return false;
    }

    private bool TryGetOwnedEntity(NetUserId userId, out EntityUid mob)
    {
        if (_player.TryGetSessionById(userId, out var session) && session.AttachedEntity is { } attached)
        {
            mob = attached;
            return true;
        }

        if (_mind.TryGetMind(userId, out _, out var mind) && mind.OwnedEntity is { } owned)
        {
            mob = owned;
            return true;
        }

        mob = EntityUid.Invalid;
        return false;
    }

    private bool TryGetUserId(EntityUid entity, out NetUserId userId)
    {
        if (TryComp<ActorComponent>(entity, out var actor))
        {
            userId = actor.PlayerSession.UserId;
            return true;
        }

        if (_mind.TryGetMind(entity, out _, out var mind) && mind.UserId is { } mindUserId)
        {
            userId = mindUserId;
            return true;
        }

        userId = default;
        return false;
    }

    private bool CanUseKnife(EntityUid user, WH40KMurderMysteryKnifeComponent knifeComp)
    {
        return TryGetUserId(user, out var userId) &&
               userId == knifeComp.OwnerUserId &&
               TryComp<WH40KMurderMysteryPlayerComponent>(user, out var playerComp) &&
               playerComp.Role == WH40KMurderMysteryRole.Murder;
    }

    private void FilterKnifeVerbs<TVerb>(WH40KMurderMysteryKnifeComponent knifeComp, ref GetVerbsEvent<TVerb> args) where TVerb : Verb
    {
        if (CanUseKnife(args.User, knifeComp))
            return;

        args.Verbs.Clear();
    }

    private static bool HasHarmfulDamage(DamageSpecifier damage)
    {
        foreach (var value in damage.DamageDict.Values)
        {
            if (value > 0)
                return true;
        }

        return false;
    }

    private static string GetWinnerTeamLocKey(WH40KMurderMysteryVictoryTeam winnerTeam)
    {
        return winnerTeam == WH40KMurderMysteryVictoryTeam.Murders
            ? "wh40k-murder-mystery-team-murders"
            : "wh40k-murder-mystery-team-innocents";
    }

    private readonly record struct MurderMysteryParticipant(
        NetUserId UserId,
        EntityUid Mob,
        WH40KMurderMysteryPlayerComponent PlayerComp);
}
