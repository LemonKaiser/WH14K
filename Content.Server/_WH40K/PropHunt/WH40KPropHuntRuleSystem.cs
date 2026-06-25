using System.Linq;
using Content.Server.Actions;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Managers;
using Content.Server.Combat;
using Content.Server.CombatMode;
using Content.Server.Damage.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.KillTracking;
using Content.Server.Mind;
using Content.Server.Polymorph.Systems;
using Content.Server.Popups;
using Content.Server.RoundEnd;
using Content.Server.Shuttles.Systems;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server._WH40K.Chaplain.Components;
using Content.Server._WH40K.MetaProgress;
using Content.Server._WH40K.Stats;
using Content.Shared.Actions;
using Content.Shared.Atmos;
using Content.Shared.Clothing.Components;
using Content.Shared.CombatMode;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Gravity;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Players;
using Content.Shared.Polymorph.Components;
using Content.Shared.Power.Components;
using Content.Shared.Preferences;
using Content.Shared.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Strip.Components;
using Content.Shared.Speech.Components;
using Content.Shared.Station.Components;
using Content.Shared.Storage.Components;
using Content.Shared.Trigger.Systems;
using Content.Shared.Wall;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Content.Shared._WH40K.Cinematic;
using Content.Shared._WH40K.GunGame;
using Content.Shared._WH40K.Interface;
using Content.Shared._WH40K.PropHunt;
using Robust.Shared.GameObjects;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WH40K.PropHunt;

public sealed partial class WH40KPropHuntRuleSystem : GameRuleSystem<WH40KPropHuntRuleComponent>
{
    private static readonly TimeSpan TimerSyncInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CountdownSyncInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WeaponMemoryDuration = TimeSpan.FromSeconds(8);

    private static readonly ProtoId<SpeciesPrototype>[] AllowedSpecies =
    [
        "Human",
        "Felinid",
        "Dwarf"
    ];

    [Dependency] private IChatManager _chat = default!;
    [Dependency] private CombatAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private CombatModeSystem _combatMode = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ChameleonProjectorSystem _chameleon = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private WH40KMetaProgressSystem _metaProgress = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private RoundEndSystem _roundEnd = default!;
    [Dependency] private GodmodeSystem _godmode = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private ShuttleSystem _shuttles = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private TriggerSystem _trigger = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private WH40KDamageProtectionSystem _damageProtection = default!;

    private readonly Dictionary<NetUserId, RecentWeaponUse> _recentWeaponUses = new();
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("wh40k.prophunt");

        SubscribeLocalEvent<RulePlayerSpawningEvent>(OnRulePlayerSpawning);
        SubscribeLocalEvent<RefreshLateJoinAllowedEvent>(OnRefreshLateJoinAllowed);
        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnBeforeSpawn);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged, before: new[] { typeof(KillTrackingSystem) });
        _damageProtection.RegisterHandler(OnBeforeDamageChanged);

        SubscribeLocalEvent<WH40KPropHuntPlayerComponent, ContainerGettingInsertedAttemptEvent>(OnContainerInsertAttempt);
        SubscribeLocalEvent<WH40KPropHuntPlayerComponent, WH40KPropHuntMorphActionEvent>(OnMorphAction);
        SubscribeLocalEvent<WH40KPropHuntPlayerComponent, WH40KPropHuntHonkActionEvent>(OnHonkAction);
        SubscribeLocalEvent<WH40KPropHuntPlayerComponent, WH40KPropHuntInvisibilityActionEvent>(OnInvisibilityAction);
        SubscribeLocalEvent<WH40KPropHuntPlayerComponent, WH40KPropHuntSmokeActionEvent>(OnSmokeAction);
        SubscribeLocalEvent<WH40KPropHuntPlayerComponent, WH40KPropHuntSeekerPulseActionEvent>(OnSeekerPulseAction);

        SubscribeLocalEvent<TransformComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<TransformComponent, MeleeHitEvent>(OnMeleeHit);
    }

    protected override void Started(EntityUid uid, WH40KPropHuntRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.PlayerRoles.Clear();
        component.PlayerKills.Clear();
        component.PlayerProfiles.Clear();
        component.WinnerRole = null;
        component.MvpSeeker = null;
        component.RewardsGranted = false;
        component.NextTimerSyncAt = TimeSpan.Zero;
        component.ActiveRoundElapsed = TimeSpan.Zero;
        component.LastRoundProgressUpdateAt = _timing.CurTime;
        component.WaitingForPlayers = true;
        component.LastTimerDurationSeconds = -1;
        component.LastTimerElapsedSeconds = -1;
        component.LastTimerStopped = false;
        component.NextCountdownSyncAt = TimeSpan.Zero;
        component.LastCountdownRemainingSeconds = -1;
        component.LastRoleHudSeekerCount = -1;
        component.LastRoleHudHiderCount = -1;
        component.NextPeriodicRevealAt = component.PeriodicRevealInterval;
        component.RevealActiveUntil = TimeSpan.Zero;
        _recentWeaponUses.Clear();
        ApplyMapStabilitySafeguards();

        PushRoundTimer(component, force: true);
        PushSeekerCountdown(component, force: true);
        PushRoleHud(component, force: true);
    }

    protected override void Ended(EntityUid uid, WH40KPropHuntRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        var query = EntityQueryEnumerator<WH40KPropHuntPlayerComponent>();
        while (query.MoveNext(out var playerUid, out var playerComp))
        {
            ClearPlayerState(playerUid, playerComp, resetAnchor: true, deleteWeapons: true);
            RemCompDeferred<WH40KPropHuntPlayerComponent>(playerUid);
        }

        _recentWeaponUses.Clear();
        component.PlayerProfiles.Clear();
        ClearRevealMarkers();
        ClearRoundTimerHud();
        ClearSeekerCountdownHud();
        ClearRoleHud();
    }

    protected override void AppendRoundEndText(EntityUid uid, WH40KPropHuntRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        if (component.WinnerRole is not { } winnerRole)
            return;

        args.AddLine(Loc.GetString("wh40k-prop-hunt-winner-team",
            ("team", Loc.GetString(GetTeamLocKey(winnerRole)))));

        if (component.MvpSeeker is { } seekerId &&
            _player.TryGetPlayerData(seekerId, out var seekerData))
        {
            args.AddLine(Loc.GetString("wh40k-prop-hunt-best-seeker",
                ("player", seekerData.UserName),
                ("kills", component.PlayerKills.GetValueOrDefault(seekerId))));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!TryGetActiveRule(out _, out var rule))
            return;

        UpdateRoundProgress(rule);
        SyncSeekerLocks(rule);
        UpdateInvisibility(rule);
        UpdatePeriodicReveal(rule);
        UpdateRevealMarkers(rule);
        PushRoundTimer(rule);
        PushSeekerCountdown(rule);
        PushRoleHud(rule);

        if (rule.WinnerRole != null || _roundEnd.IsRoundEndRequested() || rule.WaitingForPlayers)
            return;

        if (!HasLivingRole(rule, WH40KPropHuntRole.Hider))
        {
            FinishRound(rule, WH40KPropHuntRole.Seeker);
            return;
        }

        if (!HasLivingRole(rule, WH40KPropHuntRole.Seeker))
        {
            FinishRound(rule, WH40KPropHuntRole.Hider);
            return;
        }

        if (rule.RoundDuration > TimeSpan.Zero &&
            GetRoundElapsed(rule) >= rule.RoundDuration)
        {
            FinishRound(rule, WH40KPropHuntRole.Hider);
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

        var pool = ev.PlayerPool.ToList();
        _random.Shuffle(pool);
        var seekerCount = GetRequiredSeekerCount(pool.Count);

        for (var i = 0; i < pool.Count; i++)
        {
            var player = pool[i];
            var role = i < seekerCount ? WH40KPropHuntRole.Seeker : WH40KPropHuntRole.Hider;
            rule.PlayerRoles[player.UserId] = role;
            rule.PlayerKills.TryAdd(player.UserId, 0);

            if (ev.Profiles.TryGetValue(player.UserId, out var profile))
            {
                RememberPlayerProfile(player.UserId, profile, rule);
                SpawnPropHuntPlayer(player, profile, station, ruleEntity, rule, lateJoin: false);
                GameTicker.PlayerJoinGame(player);
            }
        }

        ev.PlayerPool.Clear();
    }

    private void OnRefreshLateJoinAllowed(RefreshLateJoinAllowedEvent ev)
    {
        if (!TryGetActiveRule(out _, out var rule))
            return;

        if (!IsLateJoinOpen(rule))
            ev.Disallow();
    }

    private void OnBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        if (!TryGetActiveRule(out var ruleEntity, out var rule))
            return;

        ev.Handled = SpawnPropHuntPlayer(ev.Player, ev.Profile, ev.Station, ruleEntity, rule, ev.LateJoin);
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!TryGetActiveRule(out _, out var rule) ||
            !TryComp<WH40KPropHuntPlayerComponent>(ev.Mob, out var playerComp))
        {
            return;
        }

        if (playerComp.Role == WH40KPropHuntRole.Hider)
        {
            RemoveCombatAndVoiceActions(ev.Mob, playerComp);
            AddHiderActions(ev.Mob, playerComp, rule);
            return;
        }

        ApplySeekerProtection(ev.Mob, playerComp);
        AddSeekerActions(ev.Mob, playerComp, rule);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Critical && args.NewMobState != MobState.Dead)
            return;

        if (!TryComp<WH40KPropHuntPlayerComponent>(args.Target, out var playerComp))
            return;

        if (!TryGetActiveRule(out _, out var rule))
            return;

        var role = playerComp.Role;
        if (!TryGetUserId(args.Target, out var victimId))
            return;

        if (role == WH40KPropHuntRole.Hider)
            ClearDisguise(args.Target, playerComp, resetAnchor: true);

        ClearPlayerState(args.Target, playerComp, resetAnchor: true, deleteWeapons: true);
        RemComp<WH40KPropHuntPlayerComponent>(args.Target);

        var killerId = ResolveLastHitKiller(args.Origin, args.Target);
        if (killerId is { } killer &&
            killer != victimId &&
            rule.PlayerRoles.ContainsKey(killer))
        {
            SendKillFeedEntry(rule, killer, victimId);

            if (rule.PlayerRoles.GetValueOrDefault(killer) == WH40KPropHuntRole.Seeker &&
                role == WH40KPropHuntRole.Hider)
            {
                rule.PlayerKills[killer] = rule.PlayerKills.GetValueOrDefault(killer) + 1;
            }
        }
        else
        {
            SendFallbackKillFeedEntry(victimId, killerId == victimId);
        }

        QueueDel(args.Target);
    }

    private void OnBeforeDamageChanged(EntityUid uid, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !HasHarmfulDamage(args.Damage))
            return;

        if (!TryGetActiveRule(out _, out _))
            return;

        if (!HasComp<WH40KPropHuntPlayerComponent>(uid) &&
            !HasComp<ChameleonDisguiseComponent>(uid))
        {
            args.Cancelled = true;
            return;
        }

        if (args.Origin == null)
            return;

        if (!_attackerResolver.TryResolveAttacker(args.Origin.Value, out var attacker))
            attacker = args.Origin.Value;

        if (attacker == uid)
            return;

        if (!TryComp<WH40KPropHuntPlayerComponent>(attacker, out var attackerComp) ||
            !TryComp<WH40KPropHuntPlayerComponent>(uid, out var targetComp))
        {
            return;
        }

        if (attackerComp.Role == WH40KPropHuntRole.Seeker &&
            targetComp.Role == WH40KPropHuntRole.Seeker)
        {
            args.Cancelled = true;
        }
    }

    private void OnContainerInsertAttempt(Entity<WH40KPropHuntPlayerComponent> ent, ref ContainerGettingInsertedAttemptEvent args)
    {
        if (ent.Comp.Disguise != null)
            args.Cancel();
    }

    private void OnMorphAction(Entity<WH40KPropHuntPlayerComponent> ent, ref WH40KPropHuntMorphActionEvent args)
    {
        if (args.Handled || ent.Comp.Role != WH40KPropHuntRole.Hider || args.Performer != ent.Owner)
            return;

        if (!TryGetActiveRule(out _, out var rule))
            return;

        if (!IsValidMorphTarget(ent.Owner, args.Target, rule))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-prop-hunt-morph-invalid"), ent.Owner, ent.Owner);
            return;
        }

        ReplaceDisguise(ent.Owner, ent.Comp, args.Target, rule);
        ent.Comp.HasMorphed = true;
        args.Handled = true;
    }

    private void OnHonkAction(Entity<WH40KPropHuntPlayerComponent> ent, ref WH40KPropHuntHonkActionEvent args)
    {
        if (args.Handled || ent.Comp.Role != WH40KPropHuntRole.Hider)
            return;

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Items/bikehorn.ogg"), ent.Owner);
        args.Handled = true;
    }

    private void OnInvisibilityAction(Entity<WH40KPropHuntPlayerComponent> ent, ref WH40KPropHuntInvisibilityActionEvent args)
    {
        if (args.Handled || ent.Comp.Role != WH40KPropHuntRole.Hider || ent.Comp.InvisibilityUsed)
            return;

        if (ent.Comp.Disguise == null)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-prop-hunt-invisibility-no-prop"), ent.Owner, ent.Owner);
            return;
        }

        if (!TryGetActiveRule(out _, out var rule))
            return;

        ent.Comp.InvisibilityUsed = true;
        ent.Comp.InvisibleUntil = _timing.CurTime + rule.InvisibilityDuration;

        var invisible = EnsureComp<WH40KPropHuntInvisibleComponent>(ent.Owner);
        invisible.Active = true;
        Dirty(ent.Owner, invisible);

        if (ent.Comp.InvisibilityActionEntity != null)
        {
            _actions.RemoveAction(ent.Owner, ent.Comp.InvisibilityActionEntity);
            ent.Comp.InvisibilityActionEntity = null;
        }

        args.Handled = true;
    }

    private void OnSmokeAction(Entity<WH40KPropHuntPlayerComponent> ent, ref WH40KPropHuntSmokeActionEvent args)
    {
        if (args.Handled || ent.Comp.Role != WH40KPropHuntRole.Hider || ent.Comp.SmokeUsed)
            return;

        if (!TryGetActiveRule(out _, out var rule))
            return;

        ent.Comp.SmokeUsed = true;

        var grenade = Spawn(rule.SmokeGrenadePrototype, _transform.GetMapCoordinates(ent.Owner));
        _trigger.Trigger(grenade, ent.Owner, "timer");

        if (ent.Comp.SmokeActionEntity != null)
        {
            _actions.RemoveAction(ent.Owner, ent.Comp.SmokeActionEntity);
            ent.Comp.SmokeActionEntity = null;
        }

        args.Handled = true;
    }

    private void OnSeekerPulseAction(Entity<WH40KPropHuntPlayerComponent> ent, ref WH40KPropHuntSeekerPulseActionEvent args)
    {
        if (args.Handled || ent.Comp.Role != WH40KPropHuntRole.Seeker)
            return;

        if (!TryGetActiveRule(out _, out var rule) ||
            !TryGetUserId(ent.Owner, out var userId) ||
            !_player.TryGetSessionById(userId, out var session))
        {
            return;
        }

        var count = CountNearbyLivingHiders(ent.Owner, rule.SeekerPulseRadius);
        _chat.DispatchServerMessage(session,
            Loc.GetString("wh40k-prop-hunt-pulse-result",
                ("count", count),
                ("radius", (int) MathF.Round(rule.SeekerPulseRadius))));
        args.Handled = true;
    }

    private void OnGunShot(EntityUid uid, TransformComponent component, ref GunShotEvent args)
    {
        if (!TryGetActiveRule(out _, out _))
            return;

        RecordRecentWeaponUse(args.User, uid);
    }

    private void OnMeleeHit(EntityUid uid, TransformComponent component, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        if (!TryGetActiveRule(out _, out _))
            return;

        RecordRecentWeaponUse(args.User, uid);
    }

    private bool SpawnPropHuntPlayer(
        ICommonSession player,
        HumanoidCharacterProfile originalProfile,
        EntityUid station,
        EntityUid ruleEntity,
        WH40KPropHuntRuleComponent rule,
        bool lateJoin)
    {
        if (lateJoin && !IsLateJoinOpen(rule))
        {
            GameTicker.JoinAsObserver(player);
            _chat.DispatchServerMessage(player, Loc.GetString("wh40k-prop-hunt-late-join-closed"));
            return true;
        }

        var profile = SanitizeProfile(player, originalProfile);
        RememberPlayerProfile(player.UserId, profile, rule);

        if (!rule.PlayerRoles.TryGetValue(player.UserId, out var role))
        {
            role = DetermineLateJoinRole(rule);
            rule.PlayerRoles[player.UserId] = role;
            rule.PlayerKills.TryAdd(player.UserId, 0);
        }

        var (mindId, mind) = _mind.GetOrCreateMind(player.UserId);
        mind.CharacterName = profile.Name;
        Dirty(mindId, mind);

        var spawn = FindFarthestSpawnPoint(station);
        if (spawn == EntityCoordinates.Invalid)
        {
            GameTicker.JoinAsObserver(player);
            _chat.DispatchServerMessage(player, Loc.GetString("wh40k-prop-hunt-no-spawn"));
            return true;
        }

        var mob = _stationSpawning.SpawnPlayerMob(spawn, rule.FallbackJob, profile, station);
        _mind.TransferTo(mindId, mob, mind: mind);

        var playerComp = EnsureComp<WH40KPropHuntPlayerComponent>(mob);
        playerComp.Role = role;

        EquipRoleLoadout(mob, playerComp, rule);

        if (role == WH40KPropHuntRole.Hider)
        {
            EnsureComp<PacifiedComponent>(mob);
            EnsureComp<WH40KPropHuntInvisibleComponent>(mob);
            AddHiderActions(mob, playerComp, rule);
            RemoveCombatAndVoiceActions(mob, playerComp);
        }
        else
        {
            EnsureSeekerWeapons(mob, playerComp, rule);
            AddSeekerActions(mob, playerComp, rule);
            ApplySeekerProtection(mob, playerComp);
        }

        SyncSeekerLock(mob, playerComp, rule);
        PushSeekerCountdown(rule, force: true);
        return true;
    }

    private HumanoidCharacterProfile SanitizeProfile(ICommonSession player, HumanoidCharacterProfile profile)
    {
        if (AllowedSpecies.Contains(profile.Species))
            return profile;

        var fallback = HumanoidCharacterProfile.DefaultWithSpecies();
        _sawmill.Warning(
            "Resetting Prop Hunt profile for {Player} ({UserId}) from unsupported species '{Species}' to default '{FallbackSpecies}'.",
            player.Name,
            player.UserId,
            profile.Species,
            fallback.Species);
        return fallback;
    }

    private static int GetRequiredSeekerCount(int totalPlayers)
    {
        if (totalPlayers <= 0)
            return 0;

        return Math.Max(1, (int) Math.Ceiling(totalPlayers / 5f));
    }

    private WH40KPropHuntRole DetermineLateJoinRole(WH40KPropHuntRuleComponent rule)
    {
        var totalPlayers = rule.PlayerRoles.Count + 1;
        var requiredSeekers = GetRequiredSeekerCount(totalPlayers);
        var currentSeekers = rule.PlayerRoles.Count(pair => pair.Value == WH40KPropHuntRole.Seeker);
        return currentSeekers < requiredSeekers
            ? WH40KPropHuntRole.Seeker
            : WH40KPropHuntRole.Hider;
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
        var playerQuery = EntityQueryEnumerator<WH40KPropHuntPlayerComponent, MobStateComponent, TransformComponent>();
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

    private void EquipRoleLoadout(EntityUid mob, WH40KPropHuntPlayerComponent playerComp, WH40KPropHuntRuleComponent rule)
    {
        if (playerComp.Role == WH40KPropHuntRole.Hider)
        {
            TryEquipPrototype(mob, rule.HiderJumpsuit, "jumpsuit");
            TryEquipPrototype(mob, rule.HiderShoes, "shoes");
            return;
        }

        TryEquipPrototype(mob, rule.SeekerJumpsuit, "jumpsuit");
        TryEquipPrototype(mob, rule.SeekerShoes, "shoes");
    }

    private void TryEquipPrototype(EntityUid mob, EntProtoId protoId, string slot)
    {
        if (_inventory.TryGetSlotEntity(mob, slot, out var existing) && existing != null)
        {
            _inventory.TryUnequip(mob, slot, force: true, silent: true);
            Del(existing.Value);
        }

        var item = Spawn(protoId, _transform.GetMapCoordinates(mob));
        if (!_inventory.TryEquip(mob, item, slot, force: true, silent: true))
            QueueDel(item);
    }

    private void EnsureSeekerWeapons(EntityUid mob, WH40KPropHuntPlayerComponent playerComp, WH40KPropHuntRuleComponent rule)
    {
        var gun = Spawn(rule.SeekerRangedWeapon, _transform.GetMapCoordinates(mob));
        EnsureComp<WH40KGunGameLockedComponent>(gun);
        _hands.TryPickupAnyHand(mob, gun);
        playerComp.PrimaryWeapon = gun;
    }

    private void AddHiderActions(EntityUid mob, WH40KPropHuntPlayerComponent playerComp, WH40KPropHuntRuleComponent rule)
    {
        _actions.AddAction(mob, ref playerComp.MorphActionEntity, rule.MorphAction, mob);

        if (!playerComp.InvisibilityUsed)
            _actions.AddAction(mob, ref playerComp.InvisibilityActionEntity, rule.InvisibilityAction, mob);

        if (!playerComp.SmokeUsed)
            _actions.AddAction(mob, ref playerComp.SmokeActionEntity, rule.SmokeAction, mob);
    }

    private void AddSeekerActions(EntityUid mob, WH40KPropHuntPlayerComponent playerComp, WH40KPropHuntRuleComponent rule)
    {
        _actions.AddAction(mob, ref playerComp.SeekerPulseActionEntity, rule.SeekerPulseAction, mob);

        var dash = EnsureComp<WH40KChaplainDashComponent>(mob);
        dash.ActionPrototype = rule.SeekerDashAction;
        dash.CooldownSeconds = 60f;
        dash.DashRange = Math.Max(1f, rule.SeekerDashRange);
        dash.ThrowSpeed = Math.Max(1f, rule.SeekerDashSpeed);
        dash.Damage = 0f;
        dash.KnockdownSeconds = 0f;
        dash.StunSeconds = 0f;
        dash.VoiceLine = null;
        Dirty(mob, dash);
    }

    private void RemoveRoleActions(EntityUid mob, WH40KPropHuntPlayerComponent playerComp)
    {
        RemoveAction(mob, playerComp.MorphActionEntity);
        RemoveAction(mob, playerComp.HonkActionEntity);
        RemoveAction(mob, playerComp.InvisibilityActionEntity);
        RemoveAction(mob, playerComp.SmokeActionEntity);
        RemoveAction(mob, playerComp.SeekerPulseActionEntity);

        playerComp.MorphActionEntity = null;
        playerComp.HonkActionEntity = null;
        playerComp.InvisibilityActionEntity = null;
        playerComp.SmokeActionEntity = null;
        playerComp.SeekerPulseActionEntity = null;
    }

    private void RemoveAction(EntityUid mob, EntityUid? action)
    {
        if (action != null)
            _actions.RemoveAction(mob, action);
    }

    private void DeleteSeekerWeapons(WH40KPropHuntPlayerComponent playerComp)
    {
        if (playerComp.PrimaryWeapon is { } gun && !TerminatingOrDeleted(gun))
            Del(gun);

        playerComp.PrimaryWeapon = null;
    }

    private bool IsValidMorphTarget(EntityUid user, EntityUid target, WH40KPropHuntRuleComponent rule)
    {
        if (user == target || TerminatingOrDeleted(target))
            return false;

        if (_container.IsEntityInContainer(target) || _container.IsEntityInContainer(user))
            return false;

        if (!TryComp(target, out MetaDataComponent? meta) || meta.EntityPrototype == null)
            return false;

        if (TryComp<MobStateComponent>(target, out _) ||
            TryComp<ActorComponent>(target, out _))
        {
            return false;
        }

        var prototypeId = meta.EntityPrototype.ID;
        var forceAllowed = IsForceAllowedMorphTarget(prototypeId, rule);

        if (!forceAllowed &&
            rule.MorphWhitelist != null &&
            _whitelist.IsWhitelistFail(rule.MorphWhitelist, target) &&
            !IsGeneralMorphTarget(target, meta.EntityPrototype))
        {
            return false;
        }

        if (!forceAllowed &&
            rule.MorphBlacklist != null &&
            _whitelist.IsWhitelistPass(rule.MorphBlacklist, target))
            return false;

        if (!forceAllowed && HasInvalidPrototypeHint(prototypeId))
            return false;

        if (forceAllowed)
            return true;

        return meta.EntityPrototype.Components.ContainsKey("Sprite") ||
               meta.EntityPrototype.Components.ContainsKey("Appearance");
    }

    private bool IsGeneralMorphTarget(EntityUid target, EntityPrototype prototype)
    {
        return HasComp<ItemComponent>(target) ||
               HasComp<WallMountComponent>(target) ||
               HasComp<FixturesComponent>(target) ||
               prototype.Components.ContainsKey("Item") ||
               prototype.Components.ContainsKey("WallMount") ||
               prototype.Components.ContainsKey("Clickable") ||
               prototype.Components.ContainsKey("InteractionOutline");
    }

    private static bool IsForceAllowedMorphTarget(string prototypeId, WH40KPropHuntRuleComponent rule)
    {
        foreach (var allowed in rule.MorphAllowedPrototypes)
        {
            if (string.Equals(prototypeId, allowed, StringComparison.Ordinal))
                return true;
        }

        foreach (var prefix in rule.MorphAllowedPrototypePrefixes)
        {
            if (prototypeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool HasInvalidPrototypeHint(string prototypeId)
    {
        return prototypeId.Contains("Grille", StringComparison.OrdinalIgnoreCase) ||
               prototypeId.Contains("Window", StringComparison.OrdinalIgnoreCase) ||
               prototypeId.Contains("Wall", StringComparison.OrdinalIgnoreCase);
    }

    private void ReplaceDisguise(EntityUid user, WH40KPropHuntPlayerComponent playerComp, EntityUid source, WH40KPropHuntRuleComponent rule)
    {
        if (!TryEnsureMorphProjector(user, playerComp, rule, out var projector))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-prop-hunt-morph-invalid"), user, user);
            return;
        }

        _chameleon.Disguise(projector, user, source);

        playerComp.Disguise = TryComp<ChameleonDisguisedComponent>(user, out var disguised)
            ? disguised.Disguise
            : null;
    }

    private void ClearDisguise(EntityUid user, WH40KPropHuntPlayerComponent playerComp, bool resetAnchor)
    {
        if (playerComp.Projector is { } projector &&
            TryComp<ChameleonProjectorComponent>(projector, out var projectorComp))
        {
            _chameleon.RevealProjector((projector, projectorComp));
        }
        else if (playerComp.Disguise is { } disguise && !TerminatingOrDeleted(disguise))
        {
            Del(disguise);
        }

        if (TryComp<ChameleonDisguisedComponent>(user, out _))
            RemComp<ChameleonDisguisedComponent>(user);

        playerComp.Disguise = null;

        if (!resetAnchor)
            return;

        var xform = Transform(user);
        if (xform.Anchored)
            _transform.Unanchor(user, xform);
    }

    private bool TryEnsureMorphProjector(
        EntityUid user,
        WH40KPropHuntPlayerComponent playerComp,
        WH40KPropHuntRuleComponent rule,
        out Entity<ChameleonProjectorComponent> projector)
    {
        if (playerComp.Projector is { } existing &&
            TryComp<ChameleonProjectorComponent>(existing, out var existingComp))
        {
            projector = (existing, existingComp);
            return true;
        }

        var projectorUid = Spawn(rule.MorphProjectorPrototype, _transform.GetMapCoordinates(user));
        if (!TryComp<ChameleonProjectorComponent>(projectorUid, out var projectorComp))
        {
            QueueDel(projectorUid);
            playerComp.Projector = null;
            projector = default;
            return false;
        }

        playerComp.Projector = projectorUid;
        projector = (projectorUid, projectorComp);
        return true;
    }

    private void DeleteMorphProjector(WH40KPropHuntPlayerComponent playerComp)
    {
        if (playerComp.Projector is { } projector && !TerminatingOrDeleted(projector))
            Del(projector);

        playerComp.Projector = null;
        playerComp.Disguise = null;
    }

    private void UpdateInvisibility(WH40KPropHuntRuleComponent rule)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KPropHuntPlayerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Role != WH40KPropHuntRole.Hider || comp.InvisibleUntil == TimeSpan.Zero || now < comp.InvisibleUntil)
                continue;

            StopInvisibility(uid, comp);
        }
    }

    private void StopInvisibility(EntityUid uid, WH40KPropHuntPlayerComponent playerComp)
    {
        playerComp.InvisibleUntil = TimeSpan.Zero;

        if (!TryComp<WH40KPropHuntInvisibleComponent>(uid, out var invisible))
            return;

        if (!invisible.Active)
            return;

        invisible.Active = false;
        Dirty(uid, invisible);
    }

    private void SyncSeekerLocks(WH40KPropHuntRuleComponent rule)
    {
        var query = EntityQueryEnumerator<WH40KPropHuntPlayerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            SyncSeekerLock(uid, comp, rule);
        }
    }

    private void SyncSeekerLock(EntityUid uid, WH40KPropHuntPlayerComponent playerComp, WH40KPropHuntRuleComponent rule)
    {
        if (playerComp.Role != WH40KPropHuntRole.Seeker)
        {
            RemComp<WH40KCinematicLockedComponent>(uid);
            return;
        }

        if (GetCountdownRemainingSeconds(rule) > 0)
            EnsureComp<WH40KCinematicLockedComponent>(uid);
        else
            RemComp<WH40KCinematicLockedComponent>(uid);
    }

    private bool HasLivingRole(WH40KPropHuntRuleComponent rule, WH40KPropHuntRole role)
    {
        var (seekerCount, hiderCount) = GetLivingRoleCounts(rule);
        return role == WH40KPropHuntRole.Seeker
            ? seekerCount > 0
            : hiderCount > 0;
    }

    private void FinishRound(WH40KPropHuntRuleComponent rule, WH40KPropHuntRole winnerRole)
    {
        if (rule.WinnerRole != null)
            return;

        rule.WinnerRole = winnerRole;
        rule.MvpSeeker = ResolveBestSeeker(rule);
        GrantWinnerRewards(rule, winnerRole);
        _roundEnd.EndRound(rule.RestartDelay);
    }

    private NetUserId? ResolveBestSeeker(WH40KPropHuntRuleComponent rule)
    {
        return rule.PlayerRoles
            .Where(pair => pair.Value == WH40KPropHuntRole.Seeker)
            .OrderByDescending(pair => rule.PlayerKills.GetValueOrDefault(pair.Key))
            .ThenBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => (NetUserId?) pair.Key)
            .FirstOrDefault();
    }

    private void GrantWinnerRewards(WH40KPropHuntRuleComponent rule, WH40KPropHuntRole winnerRole)
    {
        if (rule.RewardsGranted || rule.WinnerRewardXp <= 0)
            return;

        rule.RewardsGranted = true;

        foreach (var (userId, role) in rule.PlayerRoles)
        {
            if (role != winnerRole)
                continue;

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mode"] = "WH40KPropHunt",
                ["team"] = winnerRole.ToString(),
                ["roundId"] = GameTicker.RoundId.ToString()
            };

            _metaProgress.GrantLifetimeXp(userId, rule.WinnerRewardXp, WH40KPlayerStatKeys.MetaXpPropHuntWin, metadata);
        }
    }

    private void RecordRecentWeaponUse(EntityUid user, EntityUid weapon)
    {
        if (!TryComp<WH40KPropHuntPlayerComponent>(user, out _))
            return;

        if (!TryGetUserId(user, out var userId))
            return;

        var prototypeId = MetaData(weapon).EntityPrototype?.ID;
        if (string.IsNullOrWhiteSpace(prototypeId))
            return;

        _recentWeaponUses[userId] = new RecentWeaponUse(prototypeId, _timing.CurTime);
    }

    private void SendKillFeedEntry(WH40KPropHuntRuleComponent rule, NetUserId killerId, NetUserId victimId)
    {
        if (!_player.TryGetPlayerData(killerId, out var killerData) ||
            !_player.TryGetPlayerData(victimId, out var victimData))
        {
            return;
        }

        var weaponPrototypeId = ResolveKillFeedWeaponPrototype(killerId);
        foreach (var session in _player.Sessions)
        {
            var ev = new WH40KGunGameKillFeedEvent(
                killerData.UserName,
                victimData.UserName,
                weaponPrototypeId,
                false,
                session.UserId == killerId,
                session.UserId == victimId);

            RaiseNetworkEvent(ev, session);
        }
    }

    private void SendFallbackKillFeedEntry(NetUserId victimId, bool selfKill)
    {
        if (!_player.TryGetPlayerData(victimId, out var victimData))
            return;

        foreach (var session in _player.Sessions)
        {
            var isVictim = session.UserId == victimId;
            var ev = new WH40KGunGameKillFeedEvent(
                victimData.UserName,
                victimData.UserName,
                null,
                true,
                selfKill && isVictim,
                isVictim);

            RaiseNetworkEvent(ev, session);
        }
    }

    private string? ResolveKillFeedWeaponPrototype(NetUserId killerId)
    {
        if (_recentWeaponUses.TryGetValue(killerId, out var recent) &&
            _timing.CurTime - recent.RecordedAt <= WeaponMemoryDuration)
        {
            return recent.PrototypeId;
        }

        if (!TryGetOwnedEntity(killerId, out var mob) ||
            !TryComp<WH40KPropHuntPlayerComponent>(mob, out var playerComp))
        {
            return null;
        }

        if (playerComp.PrimaryWeapon is { } primary && !TerminatingOrDeleted(primary))
            return MetaData(primary).EntityPrototype?.ID;

        return null;
    }

    private void PushRoundTimer(WH40KPropHuntRuleComponent rule, bool force = false)
    {
        var stopped = rule.RoundDuration <= TimeSpan.Zero || rule.WaitingForPlayers;
        var durationSeconds = stopped
            ? 0
            : Math.Max(0, (int) Math.Ceiling(rule.RoundDuration.TotalSeconds));
        var elapsedSeconds = Math.Max(0, (int) Math.Floor(GetRoundElapsed(rule).TotalSeconds));

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

    private void PushSeekerCountdown(WH40KPropHuntRuleComponent rule, bool force = false)
    {
        var remaining = GetCountdownRemainingSeconds(rule);
        var active = remaining > 0;

        if (!active)
        {
            if (force || rule.LastCountdownRemainingSeconds != -1)
            {
                rule.LastCountdownRemainingSeconds = -1;
                ClearSeekerCountdownHud();
            }

            return;
        }

        if (!force &&
            remaining == rule.LastCountdownRemainingSeconds &&
            _timing.CurTime < rule.NextCountdownSyncAt)
        {
            return;
        }

        rule.LastCountdownRemainingSeconds = remaining;
        rule.NextCountdownSyncAt = _timing.CurTime + CountdownSyncInterval;

        foreach (var session in _player.Sessions)
        {
            if (session.Status != SessionStatus.InGame)
                continue;

            if (!rule.PlayerRoles.TryGetValue(session.UserId, out var role) || role != WH40KPropHuntRole.Seeker)
                continue;

            RaiseNetworkEvent(new WH40KPropHuntSeekerCountdownEvent(true, GameTicker.RoundId, remaining), session);
        }
    }

    private void ClearSeekerCountdownHud()
    {
        var ev = new WH40KPropHuntSeekerCountdownEvent(false, GameTicker.RoundId, 0);
        foreach (var session in _player.Sessions)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(ev, session);
        }
    }

    private void PushRoleHud(WH40KPropHuntRuleComponent rule, bool force = false)
    {
        var (seekerCount, hiderCount) = GetLivingRoleCounts(rule);
        var visible = seekerCount + hiderCount > 0;

        if (!force &&
            seekerCount == rule.LastRoleHudSeekerCount &&
            hiderCount == rule.LastRoleHudHiderCount)
        {
            return;
        }

        rule.LastRoleHudSeekerCount = seekerCount;
        rule.LastRoleHudHiderCount = hiderCount;

        var ev = new WH40KPropHuntRoleCountEvent(visible, GameTicker.RoundId, seekerCount, hiderCount);
        foreach (var session in _player.Sessions)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(ev, session);
        }
    }

    private void ClearRoleHud()
    {
        var ev = new WH40KPropHuntRoleCountEvent(false, GameTicker.RoundId, 0, 0);
        foreach (var session in _player.Sessions)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(ev, session);
        }
    }

    private int GetCountdownRemainingSeconds(WH40KPropHuntRuleComponent rule)
    {
        if (rule.SeekerFreezeDuration <= TimeSpan.Zero)
            return 0;

        var remaining = rule.SeekerFreezeDuration - GetRoundElapsed(rule);
        if (remaining <= TimeSpan.Zero)
            return 0;

        return Math.Max(0, (int) Math.Ceiling(remaining.TotalSeconds));
    }

    private bool IsLateJoinOpen(WH40KPropHuntRuleComponent rule)
    {
        return rule.LateJoinGraceDuration > TimeSpan.Zero &&
               GetRoundElapsed(rule) < rule.LateJoinGraceDuration;
    }

    private (int Seekers, int Hiders) GetLivingRoleCounts(WH40KPropHuntRuleComponent rule)
    {
        var seekers = 0;
        var hiders = 0;

        foreach (var (userId, assignedRole) in rule.PlayerRoles)
        {
            if (!TryGetOwnedEntity(userId, out var mob) ||
                !TryComp<MobStateComponent>(mob, out var mobState) ||
                mobState.CurrentState != MobState.Alive)
            {
                continue;
            }

            if (assignedRole == WH40KPropHuntRole.Seeker)
                seekers++;
            else
                hiders++;
        }

        return (seekers, hiders);
    }

    private bool TryGetActiveRule(out EntityUid ruleEntity, out WH40KPropHuntRuleComponent rule)
    {
        var query = EntityQueryEnumerator<WH40KPropHuntRuleComponent, GameRuleComponent>();
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

    private void ClearPlayerState(EntityUid uid, WH40KPropHuntPlayerComponent playerComp, bool resetAnchor, bool deleteWeapons)
    {
        StopInvisibility(uid, playerComp);
        ClearDisguise(uid, playerComp, resetAnchor);
        RemoveRoleActions(uid, playerComp);
        DeleteMorphProjector(playerComp);
        RemComp<PacifiedComponent>(uid);
        RemComp<WH40KCinematicLockedComponent>(uid);
        RemComp<WH40KPropHuntInvisibleComponent>(uid);
        RemComp<WH40KPropHuntRevealComponent>(uid);
        RemComp<WH40KChaplainDashComponent>(uid);
        RestoreCombatAndVoiceActions(uid, playerComp);
        RemoveSeekerProtection(uid, playerComp);

        if (deleteWeapons)
            DeleteSeekerWeapons(playerComp);
    }

    private void UpdateRoundProgress(WH40KPropHuntRuleComponent rule)
    {
        var now = _timing.CurTime;
        if (rule.LastRoundProgressUpdateAt == TimeSpan.Zero)
        {
            rule.LastRoundProgressUpdateAt = now;
            return;
        }

        if (!rule.WaitingForPlayers)
            rule.ActiveRoundElapsed += now - rule.LastRoundProgressUpdateAt;

        rule.LastRoundProgressUpdateAt = now;

        var shouldWait = CountActiveParticipants(rule) < rule.MinimumPlayersToRun;
        if (shouldWait == rule.WaitingForPlayers)
            return;

        rule.WaitingForPlayers = shouldWait;
        rule.NextTimerSyncAt = TimeSpan.Zero;
        rule.NextCountdownSyncAt = TimeSpan.Zero;

        if (shouldWait)
        {
            _chat.DispatchServerAnnouncement(Loc.GetString(
                "wh40k-prop-hunt-paused-waiting-players",
                ("players", rule.MinimumPlayersToRun)));
        }
        else
        {
            _chat.DispatchServerAnnouncement(Loc.GetString("wh40k-prop-hunt-paused-resumed"));
        }
    }

    private TimeSpan GetRoundElapsed(WH40KPropHuntRuleComponent rule)
    {
        return rule.ActiveRoundElapsed < TimeSpan.Zero
            ? TimeSpan.Zero
            : rule.ActiveRoundElapsed;
    }

    private int CountActiveParticipants(WH40KPropHuntRuleComponent rule)
    {
        var count = 0;
        foreach (var userId in rule.PlayerRoles.Keys)
        {
            if (_player.TryGetSessionById(userId, out var session) &&
                session.Status == SessionStatus.InGame &&
                session.AttachedEntity is { Valid: true })
            {
                count++;
            }
        }

        return count;
    }

    private void RemoveCombatAndVoiceActions(EntityUid mob, WH40KPropHuntPlayerComponent playerComp)
    {
        if (TryComp<CombatModeComponent>(mob, out var combat) &&
            combat.CombatToggleActionEntity != null)
        {
            _combatMode.SetCombatActionEnabled(mob, false, combat);
            playerComp.RemovedCombatModeAction = true;
        }

        if (TryComp<VocalComponent>(mob, out var vocal) &&
            vocal.ScreamActionEntity != null)
        {
            _actions.RemoveAction(mob, vocal.ScreamActionEntity);
            vocal.ScreamActionEntity = null;
            Dirty(mob, vocal);
            playerComp.RemovedScreamAction = true;
        }
    }

    private void RestoreCombatAndVoiceActions(EntityUid mob, WH40KPropHuntPlayerComponent playerComp)
    {
        if (playerComp.RemovedCombatModeAction &&
            TryComp<CombatModeComponent>(mob, out var combat))
        {
            _combatMode.SetCombatActionEnabled(mob, true, combat);
            playerComp.RemovedCombatModeAction = false;
        }

        if (playerComp.RemovedScreamAction &&
            TryComp<VocalComponent>(mob, out var vocal) &&
            vocal.ScreamAction != null)
        {
            _actions.AddAction(mob, ref vocal.ScreamActionEntity, vocal.ScreamAction);
            Dirty(mob, vocal);
            playerComp.RemovedScreamAction = false;
        }
    }

    private void ApplySeekerProtection(EntityUid mob, WH40KPropHuntPlayerComponent playerComp)
    {
        if (!playerComp.GrantedGodmode)
        {
            _godmode.EnableGodmode(mob);
            playerComp.GrantedGodmode = true;
        }

        EnsureComp<WH40KCinematicProtectedComponent>(mob);

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
    }

    private void RemoveSeekerProtection(EntityUid mob, WH40KPropHuntPlayerComponent playerComp)
    {
        if (playerComp.GrantedGodmode && HasComp<GodmodeComponent>(mob))
        {
            _godmode.DisableGodmode(mob);
            playerComp.GrantedGodmode = false;
        }

        if (TryComp<HandsComponent>(mob, out var hands))
        {
            _hands.SetCanBeStripped((mob, hands), playerComp.PreviousHandsCanBeStripped);
        }

        RemComp<WH40KCinematicProtectedComponent>(mob);

        if (playerComp.RemovedStrippable)
        {
            EnsureComp<StrippableComponent>(mob);
            playerComp.RemovedStrippable = false;
        }
    }

    private void UpdatePeriodicReveal(WH40KPropHuntRuleComponent rule)
    {
        if (rule.WaitingForPlayers || rule.PeriodicRevealInterval <= TimeSpan.Zero)
            return;

        var elapsed = GetRoundElapsed(rule);
        if (elapsed < rule.NextPeriodicRevealAt)
            return;

        rule.NextPeriodicRevealAt += rule.PeriodicRevealInterval;
        rule.RevealActiveUntil = elapsed + rule.PeriodicRevealDuration;

        var query = EntityQueryEnumerator<WH40KPropHuntPlayerComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var comp, out var mobState))
        {
            if (comp.Role != WH40KPropHuntRole.Hider || mobState.CurrentState != MobState.Alive)
                continue;

            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Items/bikehorn.ogg"), uid);
        }
    }

    private void UpdateRevealMarkers(WH40KPropHuntRuleComponent rule)
    {
        var active = rule.RevealActiveUntil > TimeSpan.Zero &&
                     GetRoundElapsed(rule) < rule.RevealActiveUntil;

        var query = EntityQueryEnumerator<WH40KPropHuntPlayerComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var comp, out var mobState))
        {
            if (comp.Role != WH40KPropHuntRole.Hider || mobState.CurrentState != MobState.Alive)
            {
                RemComp<WH40KPropHuntRevealComponent>(uid);
                continue;
            }

            if (active)
                EnsureComp<WH40KPropHuntRevealComponent>(uid);
            else
                RemComp<WH40KPropHuntRevealComponent>(uid);
        }
    }

    private void ClearRevealMarkers()
    {
        var query = EntityQueryEnumerator<WH40KPropHuntRevealComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemCompDeferred<WH40KPropHuntRevealComponent>(uid);
        }
    }

    private int CountNearbyLivingHiders(EntityUid seeker, float radius)
    {
        var seekerCoords = _transform.GetMapCoordinates(seeker, Transform(seeker));
        var maxDistance = radius * radius;
        var count = 0;

        var query = EntityQueryEnumerator<WH40KPropHuntPlayerComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var comp, out var mobState))
        {
            if (uid == seeker ||
                comp.Role != WH40KPropHuntRole.Hider ||
                mobState.CurrentState != MobState.Alive)
            {
                continue;
            }

            var coords = _transform.GetMapCoordinates(uid, Transform(uid));
            if (coords.MapId != seekerCoords.MapId)
                continue;

            if ((coords.Position - seekerCoords.Position).LengthSquared() <= maxDistance)
                count++;
        }

        return count;
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

    private void RememberPlayerProfile(NetUserId userId, HumanoidCharacterProfile profile, WH40KPropHuntRuleComponent rule)
    {
        rule.PlayerProfiles[userId] = profile;
    }

    private NetUserId? ResolveLastHitKiller(EntityUid? origin, EntityUid victim)
    {
        if (origin == null)
            return null;

        if (!_attackerResolver.TryResolveResponsibleEntity(origin.Value, out var responsible) || responsible == victim)
            return null;

        return TryGetUserId(responsible, out var killerId) ? killerId : null;
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

    private static bool HasHarmfulDamage(DamageSpecifier damage)
    {
        foreach (var value in damage.DamageDict.Values)
        {
            if (value > 0)
                return true;
        }

        return false;
    }

    private static string GetTeamLocKey(WH40KPropHuntRole role)
    {
        return role == WH40KPropHuntRole.Seeker
            ? "wh40k-prop-hunt-team-seekers"
            : "wh40k-prop-hunt-team-hiders";
    }

    private readonly record struct RecentWeaponUse(string PrototypeId, TimeSpan RecordedAt);
}
