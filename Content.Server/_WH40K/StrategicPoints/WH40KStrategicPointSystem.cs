using Content.Server._WH40K.Combat;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server.Cargo.Systems;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared._WH40K.GameMode;
using Content.Shared._WH40K.Notifications;
using Content.Shared._WH40K.Overlays;
using Content.Shared._WH40K.StrategicPoints;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Repairable;
using Content.Shared.Repairable.Events;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.StrategicPoints;

public sealed partial class WH40KStrategicPointSystem : EntitySystem
{
    private static readonly TimeSpan IncomeInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TripleHoldDuration = TimeSpan.FromMinutes(10);
    private static readonly SoundSpecifier BuildSound = new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");
    private static readonly SoundSpecifier UpgradeSound = new SoundPathSpecifier("/Audio/Machines/twobeep.ogg");
    private static readonly SoundSpecifier DestroySound = new SoundPathSpecifier("/Audio/Effects/metal_break1.ogg");
    private static readonly ProtoId<TagPrototype> HideContextMenuTag = "HideContextMenu";
    private const int TripleHoldRequiredPoints = 3;
    private static readonly Dictionary<ProtoId<StackPrototype>, int> InitialBuildMaterials = new()
    {
        ["Steel"] = 5,
        ["MetalRod"] = 5
    };
    private static readonly string[] TacticalCallsignTokens =
    {
        "Alpha",
        "Bravo",
        "Charlie",
        "Delta",
        "Echo",
        "Foxtrot",
        "Golf",
        "Hotel",
        "India",
        "Juliett",
        "Kilo",
        "Lima",
        "Mike",
        "November",
        "Oscar",
        "Papa",
        "Quebec",
        "Romeo",
        "Sierra",
        "Tango",
        "Uniform",
        "Victor",
        "Whiskey",
        "Xray",
        "Yankee",
        "Zulu",
    };

    [Dependency] private  SharedAppearanceSystem _appearance = default!;
    [Dependency] private  SharedAudioSystem _audio = default!;
    [Dependency] private  CargoSystem _cargo = default!;
    [Dependency] private  DamageableSystem _damageable = default!;
    [Dependency] private  SharedDoAfterSystem _doAfter = default!;
    [Dependency] private  ILocalizationManager _loc = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  SharedPhysicsSystem _physics = default!;
    [Dependency] private  IPrototypeManager _prototype = default!;
    [Dependency] private  WH40KAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private  SharedPopupSystem _popup = default!;
    [Dependency] private  StationSystem _station = default!;
    [Dependency] private  StackSystem _stack = default!;
    [Dependency] private  TagSystem _tag = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;
    [Dependency] private  WH40KTeamRuleFacadeSystem _teamRule = default!;
    [Dependency] private  UserInterfaceSystem _ui = default!;

    private TimeSpan _nextFallbackIncomeTick = TimeSpan.Zero;
    private int _nextAutoCallsignIndex;
    private readonly Dictionary<string, TimeSpan> _tripleHoldStartedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tripleHoldCompletedTeams = new(StringComparer.OrdinalIgnoreCase);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KStrategicPointAnchorComponent, MapInitEvent>(OnAnchorMapInit);
        SubscribeLocalEvent<WH40KStrategicPointComponent, MapInitEvent>(OnPointMapInit);
        SubscribeLocalEvent<WH40KStrategicPointComponent, ComponentShutdown>(OnPointShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<WH40KStrategicPointComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<WH40KStrategicPointComponent, DamageDealtEvent>(OnPointDamageDealt);
        SubscribeLocalEvent<WH40KStrategicPointComponent, RepairAttemptEvent>(OnRepairAttempt);
        SubscribeLocalEvent<WH40KStrategicPointComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WH40KStrategicPointComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WH40KStrategicPointComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<WH40KStrategicPointComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WH40KStrategicPointComponent, ActivatableUIOpenAttemptEvent>(OnUiOpenAttempt);
        SubscribeLocalEvent<WH40KStrategicPointComponent, WH40KStrategicPointUpgradeDoAfterEvent>(OnUpgradeDoAfter);

        Subs.BuiEvents<WH40KStrategicPointComponent>(WH40KStrategicPointUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<WH40KStrategicPointStartUpgradeMessage>(OnStartUpgradeMessage);
            subs.Event<WH40KStrategicPointRefreshMessage>(OnRefreshMessage);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        UpdateFallbackIncome(now);
        UpdatePointIncome(now);
        UpdateTripleHoldMilestones(now);
    }

    public bool TryBindConstructedPoint(
        EntityUid pointUid,
        EntityUid? userUid,
        WH40KStrategicPointType pointType,
        WH40KStrategicPointTier tier,
        ProtoId<WH40KStrategicPointProfilePrototype> profileId,
        float maxDistance,
        EntityUid? preferredAnchorUid = null)
    {
        if (userUid is not { Valid: true } user ||
            !_teamRule.TryGetTeamIdFromEntity(user, out var teamId) ||
            !_teamRule.TryResolveTeamId(teamId, out var resolvedTeamId))
        {
            QueueDel(pointUid);
            return false;
        }

        EntityUid anchorUid;
        WH40KStrategicPointAnchorComponent anchor;

        // If construction started from a specific anchor, that anchor is the source of truth.
        if (preferredAnchorUid is { Valid: true } candidateUid)
        {
            if (!TryComp<WH40KStrategicPointAnchorComponent>(candidateUid, out var preferredAnchor) ||
                !IsAnchorCompatibleForPoint(pointUid, pointType, candidateUid, preferredAnchor))
            {
                QueueDel(pointUid);
                return false;
            }

            anchorUid = candidateUid;
            anchor = preferredAnchor;
        }
        else if (!TryFindFreeAnchor(pointUid, pointType, maxDistance, out anchorUid, out anchor))
        {
            QueueDel(pointUid);
            return false;
        }

        var point = EnsureComp<WH40KStrategicPointComponent>(pointUid);
        EnsureAnchorCallsign(anchorUid, anchor);
        point.PointType = pointType;
        point.Tier = tier;
        point.Profile = profileId;
        point.OwnerTeamId = resolvedTeamId;
        point.Anchor = anchorUid;
        point.NextIncomeTick = _timing.CurTime + GetIncomeInterval(point);
        point.IncomeRemainders.Clear();
        point.LoadedUpgradeMaterials.Clear();
        point.UpgradeInProgress = false;
        point.PendingUpgradeTier = WH40KStrategicPointTier.T0;

        anchor.BuiltPoint = pointUid;

        SnapBuiltPointToAnchor(pointUid, anchorUid, anchor);
        HealPointToFull(pointUid);
        UpdatePointAppearance(pointUid, point);
        SetAnchorVisualHidden(anchorUid, anchor, true);
        SetAnchorContextMenuHidden(anchorUid, true);
        UpdateUi((pointUid, point));
        _audio.PlayPvs(BuildSound, pointUid);
        AnnouncePointEvent(point, anchor, "wh40k-strategic-point-notification-built");
        RaiseLocalEvent(new WH40KStrategicPointBuiltEvent(
            pointUid,
            user,
            resolvedTeamId,
            point.PointType,
            point.Tier));

        return true;
    }

    private bool IsAnchorCompatibleForPoint(
        EntityUid pointUid,
        WH40KStrategicPointType pointType,
        EntityUid anchorUid,
        WH40KStrategicPointAnchorComponent anchor)
    {
        if (anchor.PointType != pointType)
            return false;

        if (anchor.BuiltPoint is { } built && Exists(built))
            return false;

        var target = _transform.GetMapCoordinates(pointUid);
        var anchorCoords = _transform.GetMapCoordinates(anchorUid);
        if (anchorCoords.MapId != target.MapId)
            return false;

        return true;
    }

    private bool IsAnchorValidForPoint(
        EntityUid pointUid,
        WH40KStrategicPointType pointType,
        float maxDistance,
        EntityUid anchorUid,
        WH40KStrategicPointAnchorComponent anchor)
    {
        if (!IsAnchorCompatibleForPoint(pointUid, pointType, anchorUid, anchor))
            return false;

        var target = _transform.GetMapCoordinates(pointUid);
        var anchorCoords = _transform.GetMapCoordinates(anchorUid);
        var effectiveAnchorPosition = anchorCoords.Position + anchor.BuiltOffset;
        var maxDistanceSquared = maxDistance * maxDistance;
        return (effectiveAnchorPosition - target.Position).LengthSquared() <= maxDistanceSquared;
    }

    private void SnapBuiltPointToAnchor(
        EntityUid pointUid,
        EntityUid anchorUid,
        WH40KStrategicPointAnchorComponent anchor)
    {
        var anchorCoordinates = Transform(anchorUid).Coordinates;
        EnsureStrategicEntityLocked(pointUid, anchorCoordinates.Offset(anchor.BuiltOffset));
    }

    private void EnsureStrategicPointLocked(EntityUid uid, WH40KStrategicPointComponent point)
    {
        if (point.Anchor is { } anchorUid &&
            TryComp<WH40KStrategicPointAnchorComponent>(anchorUid, out var anchor))
        {
            var anchorCoordinates = Transform(anchorUid).Coordinates;
            EnsureStrategicEntityLocked(uid, anchorCoordinates.Offset(anchor.BuiltOffset));
            return;
        }

        EnsureStrategicEntityLocked(uid);
    }

    private void EnsureStrategicEntityLocked(EntityUid uid, EntityCoordinates? desiredCoordinates = null)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var xform = Transform(uid);
        var targetCoordinates = desiredCoordinates ?? xform.Coordinates;

        if (!xform.Anchored && xform.GridUid != null)
            _transform.AnchorEntity(uid, xform);

        if (TryComp<PhysicsComponent>(uid, out var physics) && physics.BodyType != BodyType.Static)
            _physics.SetBodyType(uid, BodyType.Static, body: physics);

        if (TerminatingOrDeleted(uid))
            return;

        _transform.SetCoordinates(uid, xform, targetCoordinates);
    }

    private void OnAnchorMapInit(Entity<WH40KStrategicPointAnchorComponent> ent, ref MapInitEvent args)
    {
        EnsureStrategicEntityLocked(ent.Owner);
        EnsureAnchorCallsign(ent.Owner, ent.Comp);

        if (ent.Comp.BuiltPoint is { } built && !Exists(built))
            ent.Comp.BuiltPoint = null;

        var hasBuiltPoint = ent.Comp.BuiltPoint is { } existing && Exists(existing);
        SetAnchorVisualHidden(ent.Owner, ent.Comp, hasBuiltPoint);
        SetAnchorContextMenuHidden(ent.Owner, hasBuiltPoint);
    }

    private void OnPointMapInit(Entity<WH40KStrategicPointComponent> ent, ref MapInitEvent args)
    {
        EnsureStrategicPointLocked(ent.Owner, ent.Comp);
        ent.Comp.NextIncomeTick = _timing.CurTime + GetIncomeInterval(ent.Comp);
        HealPointToFull(ent.Owner);
        UpdatePointAppearance(ent.Owner, ent.Comp);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _nextAutoCallsignIndex = 0;
        _tripleHoldStartedAt.Clear();
        _tripleHoldCompletedTeams.Clear();
    }

    private void OnPointShutdown(Entity<WH40KStrategicPointComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Anchor is not { } anchorUid ||
            !TryComp<WH40KStrategicPointAnchorComponent>(anchorUid, out var anchor) ||
            anchor.BuiltPoint != ent.Owner)
        {
            return;
        }

        anchor.BuiltPoint = null;
        SetAnchorVisualHidden(anchorUid, anchor, false);
        SetAnchorContextMenuHidden(anchorUid, false);
    }

    private void OnBeforeDamageChanged(Entity<WH40KStrategicPointComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (string.IsNullOrWhiteSpace(ent.Comp.OwnerTeamId) ||
            args.Origin is not { } origin ||
            !args.Damage.AnyPositive())
            return;

        if (!_attackerResolver.TryResolveAttacker(origin, out var attacker, out _))
            attacker = origin;

        if (!_teamRule.TryGetTeamIdFromEntity(attacker, out var rawTeamId) ||
            !_teamRule.TryResolveTeamId(rawTeamId, out var teamId))
        {
            return;
        }

        if (string.Equals(teamId, ent.Comp.OwnerTeamId, StringComparison.OrdinalIgnoreCase))
            args.Cancelled = true;
    }

    private void OnPointDamageDealt(Entity<WH40KStrategicPointComponent> ent, ref DamageDealtEvent args)
    {
        if (!TryGetTierProfile(ent.Comp, out var tier))
            return;

        if (!TryComp<DamageableComponent>(ent.Owner, out var damageable))
            return;

#pragma warning disable CS0618
        if (_damageable.GetTotalDamage((ent.Owner, damageable)) >= tier.MaxHp)
#pragma warning restore CS0618
        {
            DestroyStrategicPoint(ent.Owner, ent.Comp, args.Origin);
            return;
        }

        UpdateUi(ent);
    }

    private void OnRepairAttempt(Entity<WH40KStrategicPointComponent> ent, ref RepairAttemptEvent args)
    {
        if (IsUserOnOwnerTeam(args.User, ent.Comp))
            return;

        args.Cancelled = true;
        _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-wrong-team"), ent.Owner, args.User);
    }

    private void OnExamined(Entity<WH40KStrategicPointComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !TryGetTierProfile(ent.Comp, out var tier))
            return;

        var hp = GetCurrentHp(ent.Owner, tier.MaxHp);
        args.PushMarkup(Loc.GetString(
            "wh40k-strategic-point-examine-status",
            ("type", Loc.GetString(GetPointTypeLocKey(ent.Comp.PointType))),
            ("tier", (int) ent.Comp.Tier),
            ("owner", GetOwnerDisplayName(ent.Comp.OwnerTeamId)),
            ("hp", hp),
            ("maxHp", tier.MaxHp)));
    }

    private void OnInteractUsing(Entity<WH40KStrategicPointComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<StackComponent>(args.Used, out var stack))
            return;

        args.Handled = TryLoadUpgradeMaterial(ent, args.User, (args.Used, stack));
    }

    private void OnInteractHand(Entity<WH40KStrategicPointComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || !CanUsePoint(args.User, ent.Comp, ent.Owner))
            return;

        args.Handled = _ui.TryOpenUi(ent.Owner, WH40KStrategicPointUiKey.Key, args.User);
        if (args.Handled)
            UpdateUi(ent);
    }

    private void OnGetVerbs(Entity<WH40KStrategicPointComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;

        if (CanUsePoint(user, ent.Comp, ent.Owner))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wh40k-strategic-point-verb-open-ui"),
                Priority = 20,
                Act = () =>
                {
                    if (_ui.TryOpenUi(ent.Owner, WH40KStrategicPointUiKey.Key, user))
                        UpdateUi(ent);
                }
            });
        }

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wh40k-strategic-point-verb-upgrade"),
            Priority = 10,
            Act = () => TryStartUpgrade(ent, user)
        });
    }

    private void OnUiOpenAttempt(Entity<WH40KStrategicPointComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (CanUsePoint(args.User, ent.Comp, ent.Owner))
            return;

        if (!args.Silent)
            _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-wrong-team"), ent.Owner, args.User);

        args.Cancel();
    }

    private void OnUiOpened(Entity<WH40KStrategicPointComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnStartUpgradeMessage(Entity<WH40KStrategicPointComponent> ent, ref WH40KStrategicPointStartUpgradeMessage args)
    {
        TryStartUpgrade(ent, args.Actor);
        UpdateUi(ent);
    }

    private void OnRefreshMessage(Entity<WH40KStrategicPointComponent> ent, ref WH40KStrategicPointRefreshMessage args)
    {
        UpdateUi(ent);
    }

    private void OnUpgradeDoAfter(Entity<WH40KStrategicPointComponent> ent, ref WH40KStrategicPointUpgradeDoAfterEvent args)
    {
        if (args.Cancelled)
        {
            CancelUpgrade(ent, args.User);
            return;
        }

        CompleteUpgrade(ent, args.User, args.TargetTier);
    }

    private void UpdatePointIncome(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WH40KStrategicPointComponent>();
        while (query.MoveNext(out var uid, out var point))
        {
            if (point.Tier <= WH40KStrategicPointTier.T0 ||
                string.IsNullOrWhiteSpace(point.OwnerTeamId) ||
                now < point.NextIncomeTick)
            {
                continue;
            }

            point.NextIncomeTick = now + GetIncomeInterval(point);

            if (!TryGetTierProfile(point, out var tier))
                continue;

            ApplyPointIncome(uid, point, tier);
        }
    }

    private void UpdateTripleHoldMilestones(TimeSpan now)
    {
        var ownedPointCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var query = EntityQueryEnumerator<WH40KStrategicPointComponent>();
        while (query.MoveNext(out _, out var point))
        {
            if (point.Tier <= WH40KStrategicPointTier.T0 ||
                string.IsNullOrWhiteSpace(point.OwnerTeamId))
            {
                continue;
            }

            ownedPointCounts[point.OwnerTeamId] = ownedPointCounts.GetValueOrDefault(point.OwnerTeamId) + 1;
        }

        var trackedTeams = new HashSet<string>(_teamRule.GetTeamIds(), StringComparer.OrdinalIgnoreCase);
        trackedTeams.UnionWith(_tripleHoldStartedAt.Keys);
        trackedTeams.UnionWith(_tripleHoldCompletedTeams);

        foreach (var teamId in trackedTeams)
        {
            ownedPointCounts.TryGetValue(teamId, out var ownedPointCount);

            if (ownedPointCount < TripleHoldRequiredPoints)
            {
                _tripleHoldStartedAt.Remove(teamId);
                continue;
            }

            if (_tripleHoldCompletedTeams.Contains(teamId))
                continue;

            if (!_tripleHoldStartedAt.TryGetValue(teamId, out var startedAt))
            {
                _tripleHoldStartedAt[teamId] = now;
                continue;
            }

            var heldDuration = now - startedAt;
            if (heldDuration < TripleHoldDuration)
                continue;

            _tripleHoldCompletedTeams.Add(teamId);
            RaiseLocalEvent(new WH40KStrategicPointTripleHoldCompletedEvent(teamId, ownedPointCount, heldDuration));
        }
    }

    private void ApplyPointIncome(EntityUid uid, WH40KStrategicPointComponent point, WH40KStrategicPointTierProfile tier)
    {
        var teamId = point.OwnerTeamId!;

        var teamXp = ApplyPhaseMultiplier(tier.TeamXpIncome, point, WH40KStrategicPointCurrency.TeamXp);
        if (teamXp > 0)
            _teamRule.TryAdjustTeamXp(teamId, teamXp, out _, out _, out _, "strategic-point");

        var influence = ApplyPhaseMultiplier(tier.InfluenceIncome, point, WH40KStrategicPointCurrency.Influence);
        if (influence > 0)
            _teamRule.TryAdjustTeamInfluence(teamId, influence, out _, out _, "strategic-point");

        var research = ApplyPhaseMultiplier(tier.ResearchIncome, point, WH40KStrategicPointCurrency.Research);
        if (research > 0)
            _teamRule.TryAdjustTeamResearchPoints(teamId, research, out _, out _, "strategic-point");

        // Artifact income stays fixed per 10-second cycle so research points follow their declared tier yield.
        var artifacts = Math.Max(0, tier.ArtifactIncome);
        if (artifacts > 0)
            _teamRule.TryAdjustTeamArtifacts(teamId, artifacts, out _, out _, "strategic-point");

        var funds = ApplyPhaseMultiplier(tier.FundsIncome, point, WH40KStrategicPointCurrency.Funds);
        if (funds > 0)
            TryAdjustTeamFunds(uid, teamId, funds);
    }

    private void UpdateFallbackIncome(TimeSpan now)
    {
        if (_nextFallbackIncomeTick == TimeSpan.Zero)
        {
            _nextFallbackIncomeTick = now + IncomeInterval;
            return;
        }

        if (now < _nextFallbackIncomeTick)
            return;

        _nextFallbackIncomeTick = now + IncomeInterval;

        foreach (var teamId in _teamRule.GetTeamIds())
        {
            _teamRule.TryAdjustTeamXp(teamId, 1, out _, out _, out _, "base-fallback");
            _teamRule.TryAdjustTeamInfluence(teamId, 1, out _, out _, "base-fallback");
            TryAdjustTeamFunds(null, teamId, 20);
        }
    }

    public bool TryGetTeamIncomeRates(
        string teamId,
        out float teamXpPerSecond,
        out float influencePerSecond,
        out float researchPerSecond,
        out float artifactPerSecond,
        out float fundsPerSecond)
    {
        teamXpPerSecond = 0f;
        influencePerSecond = 0f;
        researchPerSecond = 0f;
        artifactPerSecond = 0f;
        fundsPerSecond = 0f;

        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        if (!_teamRule.TryResolveTeamId(teamId, out var resolvedTeamId))
            resolvedTeamId = teamId;

        var fallbackIntervalSeconds = (float) IncomeInterval.TotalSeconds;
        if (fallbackIntervalSeconds > 0f)
        {
            teamXpPerSecond += 1f / fallbackIntervalSeconds;
            influencePerSecond += 1f / fallbackIntervalSeconds;
            fundsPerSecond += 20f / fallbackIntervalSeconds;
        }

        var phase = _teamRule.GetCurrentPhase();
        var (numerator, denominator) = WH40KStrategicPointIncomeCalculator.GetPhaseMultiplier(phase);

        var query = EntityQueryEnumerator<WH40KStrategicPointComponent>();
        while (query.MoveNext(out _, out var point))
        {
            if (point.Tier <= WH40KStrategicPointTier.T0 ||
                string.IsNullOrWhiteSpace(point.OwnerTeamId) ||
                !string.Equals(point.OwnerTeamId, resolvedTeamId, StringComparison.OrdinalIgnoreCase) ||
                !TryGetTierProfile(point, out var tier))
            {
                continue;
            }

            var intervalSeconds = Math.Max(1f, point.IncomeIntervalSeconds);
            teamXpPerSecond += GetAverageIncomePerSecond(tier.TeamXpIncome, numerator, denominator, intervalSeconds);
            influencePerSecond += GetAverageIncomePerSecond(tier.InfluenceIncome, numerator, denominator, intervalSeconds);
            researchPerSecond += GetAverageIncomePerSecond(tier.ResearchIncome, numerator, denominator, intervalSeconds);
            artifactPerSecond += Math.Max(0, tier.ArtifactIncome) / intervalSeconds / 10f;
            fundsPerSecond += GetAverageIncomePerSecond(tier.FundsIncome, numerator, denominator, intervalSeconds);
        }

        return true;
    }

    private int ApplyPhaseMultiplier(
        int baseAmount,
        WH40KStrategicPointComponent point,
        WH40KStrategicPointCurrency currency)
    {
        if (baseAmount <= 0)
            return 0;

        var remainder = point.IncomeRemainders.GetValueOrDefault(currency);
        var granted = WH40KStrategicPointIncomeCalculator.ApplyPhaseMultiplier(
            baseAmount,
            _teamRule.GetCurrentPhase(),
            ref remainder);
        point.IncomeRemainders[currency] = remainder;
        return granted;
    }

    private bool TryGetTierProfile(
        WH40KStrategicPointComponent point,
        out WH40KStrategicPointTierProfile tier)
    {
        tier = default!;

        if (!_prototype.TryIndex(point.Profile, out var profile) ||
            profile.PointType != point.PointType)
        {
            return false;
        }

        return profile.Tiers.TryGetValue((int) point.Tier, out tier!);
    }

    private bool TryGetUpgradeProfile(
        WH40KStrategicPointComponent point,
        WH40KStrategicPointTier targetTier,
        out WH40KStrategicPointUpgradeProfile upgrade)
    {
        upgrade = default!;

        if (!_prototype.TryIndex(point.Profile, out var profile) ||
            profile.PointType != point.PointType)
        {
            return false;
        }

        return profile.Upgrades.TryGetValue((int) targetTier, out upgrade!);
    }

    private bool TryGetProfile(WH40KStrategicPointComponent point, out WH40KStrategicPointProfilePrototype profile)
    {
        profile = default!;

        return _prototype.TryIndex(point.Profile, out profile!) &&
               profile.PointType == point.PointType;
    }

    private bool TryFindFreeAnchor(
        EntityUid pointUid,
        WH40KStrategicPointType pointType,
        float maxDistance,
        out EntityUid anchorUid,
        out WH40KStrategicPointAnchorComponent anchor)
    {
        anchorUid = EntityUid.Invalid;
        anchor = default!;

        var target = _transform.GetMapCoordinates(pointUid);
        var maxDistanceSquared = maxDistance * maxDistance;
        var query = EntityQueryEnumerator<WH40KStrategicPointAnchorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var candidate, out var xform))
        {
            if (candidate.PointType != pointType)
                continue;

            if (candidate.BuiltPoint is { } built && Exists(built))
                continue;

            var anchorCoords = _transform.GetMapCoordinates(uid, xform: xform);
            if (anchorCoords.MapId != target.MapId)
                continue;

            var effectiveAnchorPosition = anchorCoords.Position + candidate.BuiltOffset;
            if ((effectiveAnchorPosition - target.Position).LengthSquared() > maxDistanceSquared)
                continue;

            anchorUid = uid;
            anchor = candidate;
            return true;
        }

        return false;
    }

    private TimeSpan GetIncomeInterval(WH40KStrategicPointComponent point)
    {
        return TimeSpan.FromSeconds(Math.Max(1f, point.IncomeIntervalSeconds));
    }

    private bool TryLoadUpgradeMaterial(
        Entity<WH40KStrategicPointComponent> ent,
        EntityUid user,
        Entity<StackComponent> usedStack)
    {
        if (!CanUsePoint(user, ent.Comp, ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-wrong-team"), ent.Owner, user);
            return true;
        }

        if (ent.Comp.UpgradeInProgress)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-upgrade-in-progress"), ent.Owner, user);
            return true;
        }

        var nextTier = GetNextTier(ent.Comp.Tier);
        if (nextTier == ent.Comp.Tier || !TryGetUpgradeProfile(ent.Comp, nextTier, out var upgrade))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-max-tier"), ent.Owner, user);
            return true;
        }

        var stackId = usedStack.Comp.StackTypeId;
        if (!upgrade.Materials.TryGetValue(stackId, out var required))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-material-not-needed"), ent.Owner, user);
            return true;
        }

        var loaded = ent.Comp.LoadedUpgradeMaterials.GetValueOrDefault(stackId);
        var missing = required - loaded;
        if (missing <= 0)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-material-complete"), ent.Owner, user);
            return true;
        }

        var take = Math.Min(missing, usedStack.Comp.Count);
        if (take <= 0 || !_stack.TryUse(usedStack.AsNullable(), take))
            return true;

        ent.Comp.LoadedUpgradeMaterials[stackId] = loaded + take;

        _popup.PopupEntity(
            Loc.GetString(
                "wh40k-strategic-point-popup-material-loaded",
                ("amount", take),
                ("loaded", ent.Comp.LoadedUpgradeMaterials[stackId]),
                ("required", required)),
            ent.Owner,
            user);

        UpdateUi(ent);
        return true;
    }

    private bool TryStartUpgrade(Entity<WH40KStrategicPointComponent> ent, EntityUid user)
    {
        if (!CanUsePoint(user, ent.Comp, ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-wrong-team"), ent.Owner, user);
            return false;
        }

        if (!HasComp<WH40KStrategicPointUpgradeSkillComponent>(user))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-no-upgrade-skill"), ent.Owner, user);
            return false;
        }

        if (ent.Comp.UpgradeInProgress)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-upgrade-in-progress"), ent.Owner, user);
            return false;
        }

        var nextTier = GetNextTier(ent.Comp.Tier);
        if (nextTier == ent.Comp.Tier || !TryGetUpgradeProfile(ent.Comp, nextTier, out var upgrade))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-max-tier"), ent.Owner, user);
            return false;
        }

        if (!HasLoadedMaterials(ent.Comp, upgrade))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-missing-materials"), ent.Owner, user);
            return false;
        }

        ent.Comp.UpgradeInProgress = true;
        ent.Comp.PendingUpgradeTier = nextTier;

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            user,
            TimeSpan.FromSeconds(Math.Max(0.1f, upgrade.Seconds)),
            new WH40KStrategicPointUpgradeDoAfterEvent(nextTier),
            ent.Owner,
            target: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            DistanceThreshold = 1.75f,
            DuplicateCondition = DuplicateConditions.SameTarget | DuplicateConditions.SameEvent
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs))
        {
            ent.Comp.UpgradeInProgress = false;
            ent.Comp.PendingUpgradeTier = WH40KStrategicPointTier.T0;
            return false;
        }

        _popup.PopupEntity(
            Loc.GetString("wh40k-strategic-point-popup-upgrade-started", ("tier", (int) nextTier)),
            ent.Owner,
            user);
        UpdateUi(ent);
        return true;
    }

    private void CancelUpgrade(Entity<WH40KStrategicPointComponent> ent, EntityUid user)
    {
        ent.Comp.UpgradeInProgress = false;
        ent.Comp.PendingUpgradeTier = WH40KStrategicPointTier.T0;
        _popup.PopupEntity(Loc.GetString("wh40k-strategic-point-popup-upgrade-cancelled"), ent.Owner, user);
        UpdateUi(ent);
    }

    private void CompleteUpgrade(
        Entity<WH40KStrategicPointComponent> ent,
        EntityUid user,
        WH40KStrategicPointTier targetTier)
    {
        if (ent.Comp.PendingUpgradeTier != targetTier ||
            !TryGetUpgradeProfile(ent.Comp, targetTier, out var upgrade) ||
            !HasLoadedMaterials(ent.Comp, upgrade) ||
            !CanUsePoint(user, ent.Comp, ent.Owner))
        {
            CancelUpgrade(ent, user);
            return;
        }

        ConsumeLoadedMaterials(ent.Comp, upgrade);
        ent.Comp.Tier = targetTier;
        ent.Comp.UpgradeInProgress = false;
        ent.Comp.PendingUpgradeTier = WH40KStrategicPointTier.T0;

        HealPointToFull(ent.Owner);
        UpdatePointAppearance(ent.Owner, ent.Comp);
        UpdateUi(ent);

        _audio.PlayPvs(UpgradeSound, ent.Owner);
        if (ent.Comp.Anchor is { } anchorUid && TryComp<WH40KStrategicPointAnchorComponent>(anchorUid, out var anchor))
            AnnouncePointEvent(ent.Comp, anchor, "wh40k-strategic-point-notification-upgraded");

        if (!string.IsNullOrWhiteSpace(ent.Comp.OwnerTeamId))
        {
            RaiseLocalEvent(new WH40KStrategicPointUpgradedEvent(
                ent.Owner,
                user,
                ent.Comp.OwnerTeamId,
                ent.Comp.PointType,
                ent.Comp.Tier));
        }

        _popup.PopupEntity(
            Loc.GetString("wh40k-strategic-point-popup-upgrade-complete", ("tier", (int) targetTier)),
            ent.Owner,
            user);
    }

    public void DestroyStrategicPoint(
        EntityUid uid,
        WH40KStrategicPointComponent? point = null,
        EntityUid? cause = null)
    {
        if (!Resolve(uid, ref point, false))
            return;

        if (!TryGetTierProfile(point, out var tier))
            return;

        RewardDestroyer(point, tier, cause);
        TryRaiseDestroyProgressEvent(uid, point, cause);
        RefundDestroyedPointMaterials(uid, point);

        if (point.Anchor is { } anchorUid && TryComp<WH40KStrategicPointAnchorComponent>(anchorUid, out var anchor))
            AnnouncePointEvent(point, anchor, "wh40k-strategic-point-notification-destroyed");

        _audio.PlayPvs(DestroySound, uid);
        QueueDel(uid);
    }

    private void RewardDestroyer(
        WH40KStrategicPointComponent point,
        WH40KStrategicPointTierProfile tier,
        EntityUid? cause)
    {
        if (!TryResolveDestroyer(point, cause, out _, out var teamId))
        {
            return;
        }

        if (tier.DestroyTeamXpReward > 0)
            _teamRule.TryAdjustTeamXp(teamId, tier.DestroyTeamXpReward, out _, out _, out _, "strategic-point-destroy");

        if (tier.DestroyInfluenceReward > 0)
            _teamRule.TryAdjustTeamInfluence(teamId, tier.DestroyInfluenceReward, out _, out _, "strategic-point-destroy");
    }

    private bool TryResolveDestroyer(
        WH40KStrategicPointComponent point,
        EntityUid? cause,
        out EntityUid attackerUid,
        out string teamId)
    {
        attackerUid = EntityUid.Invalid;
        teamId = string.Empty;

        if (cause is not { Valid: true } causeUid)
            return false;

        if (!_attackerResolver.TryResolveAttacker(causeUid, out attackerUid, out _))
            attackerUid = causeUid;

        if (!_teamRule.TryGetTeamIdFromEntity(attackerUid, out var rawTeamId) ||
            !_teamRule.TryResolveTeamId(rawTeamId, out teamId) ||
            string.IsNullOrWhiteSpace(teamId) ||
            string.Equals(teamId, point.OwnerTeamId, StringComparison.OrdinalIgnoreCase))
        {
            attackerUid = EntityUid.Invalid;
            teamId = string.Empty;
            return false;
        }

        return true;
    }

    private void TryRaiseDestroyProgressEvent(
        EntityUid pointUid,
        WH40KStrategicPointComponent point,
        EntityUid? cause)
    {
        if (string.IsNullOrWhiteSpace(point.OwnerTeamId) ||
            !TryResolveDestroyer(point, cause, out var attackerUid, out var attackerTeamId))
        {
            return;
        }

        RaiseLocalEvent(new WH40KStrategicPointDestroyedEvent(
            pointUid,
            attackerUid,
            attackerTeamId,
            point.OwnerTeamId,
            point.PointType,
            point.Tier));
    }

    private void RefundDestroyedPointMaterials(EntityUid uid, WH40KStrategicPointComponent point)
    {
        if (!TryGetProfile(point, out var profile))
            return;

        var materials = new Dictionary<ProtoId<StackPrototype>, int>(InitialBuildMaterials);
        foreach (var (targetTier, upgrade) in profile.Upgrades)
        {
            if (targetTier > (int) point.Tier)
                continue;

            AddMaterials(materials, upgrade.Materials);
        }

        AddMaterials(materials, point.LoadedUpgradeMaterials);

        var coords = Transform(uid).Coordinates;
        foreach (var (stackId, amount) in materials)
        {
            var refund = amount / 2;
            if (refund <= 0 || !_prototype.HasIndex<StackPrototype>(stackId))
                continue;

            _stack.SpawnAtPosition(refund, stackId, coords);
        }
    }

    private void UpdatePointAppearance(EntityUid uid, WH40KStrategicPointComponent point)
    {
        UpdatePointHealthBar(uid, point);

        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        _appearance.SetData(uid, WH40KStrategicPointVisuals.PointType, point.PointType, appearance);
        _appearance.SetData(uid, WH40KStrategicPointVisuals.Tier, point.Tier, appearance);
        _appearance.SetData(uid, WH40KStrategicPointVisuals.OwnerTeamId, point.OwnerTeamId ?? string.Empty, appearance);
    }

    private void UpdatePointHealthBar(EntityUid uid, WH40KStrategicPointComponent point)
    {
        if (!TryGetTierProfile(point, out var tier) ||
            !TryComp<WH40KAlwaysShowHealthBarComponent>(uid, out var bar))
        {
            return;
        }

        var maxHealth = FixedPoint2.New(tier.MaxHp);
        if (bar.MaxHealth == maxHealth && !bar.UseMobThresholds)
            return;

        bar.MaxHealth = maxHealth;
        bar.UseMobThresholds = false;
        Dirty(uid, bar);
    }

    private void SetAnchorVisualHidden(
        EntityUid uid,
        WH40KStrategicPointAnchorComponent anchor,
        bool hasBuiltPoint)
    {
        if (!anchor.HideSpriteWhenBuilt || !TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        _appearance.SetData(uid, WH40KStrategicPointVisuals.AnchorHidden, hasBuiltPoint, appearance);
    }

    private void SetAnchorContextMenuHidden(EntityUid uid, bool hasBuiltPoint)
    {
        if (hasBuiltPoint)
        {
            if (!_tag.HasTag(uid, HideContextMenuTag))
                _tag.AddTag(uid, HideContextMenuTag);

            return;
        }

        if (_tag.HasTag(uid, HideContextMenuTag))
            _tag.RemoveTag(uid, HideContextMenuTag);
    }

    private void HealPointToFull(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return;

        _damageable.SetAllDamage((uid, damageable), FixedPoint2.Zero);
    }

    private int GetCurrentHp(EntityUid uid, int maxHp)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return maxHp;

#pragma warning disable CS0618
        var damage = (int) Math.Ceiling(_damageable.GetTotalDamage((uid, damageable)).Float());
#pragma warning restore CS0618
        return Math.Clamp(maxHp - damage, 0, maxHp);
    }

    private void UpdateUi(Entity<WH40KStrategicPointComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, WH40KStrategicPointUiKey.Key))
            return;

        _ui.SetUiState(ent.Owner, WH40KStrategicPointUiKey.Key, BuildUiState(ent));
    }

    private WH40KStrategicPointBuiState BuildUiState(Entity<WH40KStrategicPointComponent> ent)
    {
        var hasTier = TryGetTierProfile(ent.Comp, out var tier);
        var maxHp = hasTier ? tier.MaxHp : 0;
        var hp = hasTier ? GetCurrentHp(ent.Owner, tier.MaxHp) : 0;
        var nextTier = GetNextTier(ent.Comp.Tier);
        WH40KStrategicPointUpgradeProfile? upgrade = null;
        var hasUpgrade = nextTier != ent.Comp.Tier && TryGetUpgradeProfile(ent.Comp, nextTier, out upgrade);
        var materialsComplete = hasUpgrade && upgrade != null && HasLoadedMaterials(ent.Comp, upgrade);
        var status = ResolveUiStatus(ent.Comp, hasUpgrade, materialsComplete);

        return new WH40KStrategicPointBuiState(
            ent.Comp.OwnerTeamId ?? string.Empty,
            GetOwnerDisplayName(ent.Comp.OwnerTeamId),
            GetPointCallsign(ent.Comp),
            ent.Comp.PointType,
            ent.Comp.Tier,
            hp,
            maxHp,
            (int) Math.Round(GetIncomeInterval(ent.Comp).TotalSeconds),
            hasTier ? BuildIncomeEntries(tier) : Array.Empty<WH40KStrategicPointIncomeUiEntry>(),
            hasUpgrade && upgrade != null
                ? BuildMaterialEntries(upgrade, ent.Comp)
                : Array.Empty<WH40KStrategicPointMaterialUiEntry>(),
            status,
            nextTier,
            hasUpgrade && upgrade != null ? (int) Math.Ceiling(upgrade.Seconds) : 0,
            ent.Comp.UpgradeInProgress,
            hasUpgrade,
            materialsComplete);
    }

    private WH40KStrategicPointIncomeUiEntry[] BuildIncomeEntries(WH40KStrategicPointTierProfile tier)
    {
        var entries = new List<WH40KStrategicPointIncomeUiEntry>();
        AddIncomeEntry(entries, "wh40k-strategic-point-ui-income-team-xp", tier.TeamXpIncome);
        AddIncomeEntry(entries, "wh40k-strategic-point-ui-income-funds", tier.FundsIncome);
        AddIncomeEntry(entries, "wh40k-strategic-point-ui-income-research", tier.ResearchIncome);
        AddIncomeEntry(entries, "wh40k-strategic-point-ui-income-influence", tier.InfluenceIncome);
        AddIncomeEntry(entries, "wh40k-strategic-point-ui-income-artifact", tier.ArtifactIncome, applyPhaseMultiplier: false);
        return entries.ToArray();
    }

    private void AddIncomeEntry(
        List<WH40KStrategicPointIncomeUiEntry> entries,
        string locKey,
        int baseAmount,
        bool applyPhaseMultiplier = true)
    {
        if (baseAmount <= 0)
            return;

        entries.Add(new WH40KStrategicPointIncomeUiEntry(
            locKey,
            baseAmount,
            applyPhaseMultiplier
                ? WH40KStrategicPointIncomeCalculator.GetEffectiveIncome(baseAmount, _teamRule.GetCurrentPhase())
                : baseAmount));
    }

    private WH40KStrategicPointMaterialUiEntry[] BuildMaterialEntries(
        WH40KStrategicPointUpgradeProfile upgrade,
        WH40KStrategicPointComponent point)
    {
        var entries = new List<WH40KStrategicPointMaterialUiEntry>();
        foreach (var (stackId, required) in upgrade.Materials)
        {
            if (!_prototype.TryIndex(stackId, out var stack))
                continue;

            entries.Add(new WH40KStrategicPointMaterialUiEntry(
                stackId.ToString(),
                stack.Name.ToString(),
                required,
                point.LoadedUpgradeMaterials.GetValueOrDefault(stackId)));
        }

        return entries.ToArray();
    }

    private static WH40KStrategicPointUiStatus ResolveUiStatus(
        WH40KStrategicPointComponent point,
        bool hasUpgrade,
        bool materialsComplete)
    {
        if (point.UpgradeInProgress)
            return WH40KStrategicPointUiStatus.UpgradeInProgress;

        if (!hasUpgrade)
            return WH40KStrategicPointUiStatus.MaxTier;

        return materialsComplete
            ? WH40KStrategicPointUiStatus.Ready
            : WH40KStrategicPointUiStatus.MissingMaterials;
    }

    private string GetPointCallsign(WH40KStrategicPointComponent point)
    {
        if (point.Anchor is { } anchorUid &&
            TryComp<WH40KStrategicPointAnchorComponent>(anchorUid, out var anchor) &&
            !string.IsNullOrWhiteSpace(anchor.Callsign))
        {
            return anchor.Callsign;
        }

        return Loc.GetString("wh40k-strategic-point-callsign-unknown");
    }

    private void EnsureAnchorCallsign(EntityUid anchorUid, WH40KStrategicPointAnchorComponent anchor)
    {
        if (!string.IsNullOrWhiteSpace(anchor.Callsign))
            return;

        anchor.Callsign = FormatCallsign(_nextAutoCallsignIndex++);
    }

    private static string FormatCallsign(int index)
    {
        var safeIndex = Math.Max(0, index);
        var baseName = TacticalCallsignTokens[safeIndex % TacticalCallsignTokens.Length];
        var tier = safeIndex / TacticalCallsignTokens.Length;
        return tier == 0 ? baseName : $"{baseName}-{tier + 1}";
    }

    private void AnnouncePointEvent(
        WH40KStrategicPointComponent point,
        WH40KStrategicPointAnchorComponent anchor,
        string locKey)
    {
        var teamId = point.OwnerTeamId ?? string.Empty;
        var teamDisplay = GetOwnerDisplayName(point.OwnerTeamId);
        RaiseNetworkEvent(new WH40KLocalizedNotificationEvent
        {
            Title = "wh40k-notification-title-point",
            LocKey = locKey,
            LocArgs = new Dictionary<string, string>
            {
                ["team"] = teamDisplay,
                ["type"] = GetPointTypeLocKey(point.PointType),
                ["tier"] = ((int) point.Tier).ToString(),
                ["callsign"] = string.IsNullOrWhiteSpace(anchor.Callsign)
                    ? Loc.GetString("wh40k-strategic-point-callsign-unknown")
                    : anchor.Callsign!
            },
            ResolveArgValues = true,
            AccentColor = WH40KNotificationColors.ForTeam(teamId),
            Category = WH40KNotificationCategory.Point,
            Priority = WH40KNotificationPriority.Point,
            Icon = WH40KNotificationIcon.Point,
            StackKey = $"strategic-point-{anchor.Callsign}-{locKey}"
        });
    }

    private string GetOwnerDisplayName(string? teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return Loc.GetString("wh40k-strategic-point-owner-neutral");

        if (!_teamRule.TryGetTeamDisplayName(teamId, out var teamName))
            return teamId;

        return _loc.TryGetString(teamName, out var localized) && !string.IsNullOrWhiteSpace(localized)
            ? localized
            : teamName;
    }

    private bool CanUsePoint(EntityUid user, WH40KStrategicPointComponent point, EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) ||
            string.IsNullOrWhiteSpace(point.OwnerTeamId) ||
            !_teamRule.TryGetTeamIdFromEntity(user, out var teamId) ||
            !_teamRule.TryResolveTeamId(teamId, out var resolvedTeamId))
        {
            return false;
        }

        return string.Equals(resolvedTeamId, point.OwnerTeamId, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsUserOnOwnerTeam(EntityUid user, WH40KStrategicPointComponent point)
    {
        return CanUsePoint(user, point, EntityUid.Invalid) ||
               (!string.IsNullOrWhiteSpace(point.OwnerTeamId) &&
                _teamRule.TryGetTeamIdFromEntity(user, out var teamId) &&
                _teamRule.TryResolveTeamId(teamId, out var resolvedTeamId) &&
                string.Equals(resolvedTeamId, point.OwnerTeamId, StringComparison.OrdinalIgnoreCase));
    }

    private static WH40KStrategicPointTier GetNextTier(WH40KStrategicPointTier tier)
    {
        return tier switch
        {
            WH40KStrategicPointTier.T1 => WH40KStrategicPointTier.T2,
            WH40KStrategicPointTier.T2 => WH40KStrategicPointTier.T3,
            _ => tier
        };
    }

    private static float GetAverageIncomePerSecond(int baseAmount, int numerator, int denominator, float intervalSeconds)
    {
        if (baseAmount <= 0 || numerator <= 0 || denominator <= 0 || intervalSeconds <= 0f)
            return 0f;

        return baseAmount * numerator / (float) denominator / intervalSeconds;
    }

    private static bool HasLoadedMaterials(
        WH40KStrategicPointComponent point,
        WH40KStrategicPointUpgradeProfile upgrade)
    {
        foreach (var (stackId, required) in upgrade.Materials)
        {
            if (point.LoadedUpgradeMaterials.GetValueOrDefault(stackId) < required)
                return false;
        }

        return true;
    }

    private static void ConsumeLoadedMaterials(
        WH40KStrategicPointComponent point,
        WH40KStrategicPointUpgradeProfile upgrade)
    {
        foreach (var (stackId, required) in upgrade.Materials)
        {
            var loaded = point.LoadedUpgradeMaterials.GetValueOrDefault(stackId);
            var remaining = loaded - required;
            if (remaining > 0)
                point.LoadedUpgradeMaterials[stackId] = remaining;
            else
                point.LoadedUpgradeMaterials.Remove(stackId);
        }
    }

    private static void AddMaterials(
        Dictionary<ProtoId<StackPrototype>, int> destination,
        IReadOnlyDictionary<ProtoId<StackPrototype>, int> source)
    {
        foreach (var (stackId, amount) in source)
            destination[stackId] = destination.GetValueOrDefault(stackId) + amount;
    }

    private static string GetPointTypeLocKey(WH40KStrategicPointType pointType)
    {
        return pointType switch
        {
            WH40KStrategicPointType.Resource => "wh40k-strategic-point-type-resource",
            WH40KStrategicPointType.Research => "wh40k-strategic-point-type-research",
            WH40KStrategicPointType.Influence => "wh40k-strategic-point-type-influence",
            _ => "wh40k-strategic-point-type-unknown"
        };
    }

    public IReadOnlyList<WH40KStrategicPointAdminSnapshot> GetAdminSnapshots()
    {
        var snapshots = new List<WH40KStrategicPointAdminSnapshot>();
        var query = EntityQueryEnumerator<WH40KStrategicPointAnchorComponent>();
        while (query.MoveNext(out var anchorUid, out var anchor))
        {
            var targetUid = anchorUid;
            var builtPointNet = NetEntity.Invalid;
            var tier = WH40KStrategicPointTier.T0;
            var ownerTeamId = string.Empty;
            var teamXpIncome = 0;
            var influenceIncome = 0;
            var researchIncome = 0;
            var artifactIncome = 0;
            var fundsIncome = 0;

            if (anchor.BuiltPoint is { } builtPointUid &&
                Exists(builtPointUid) &&
                TryComp<WH40KStrategicPointComponent>(builtPointUid, out var point))
            {
                targetUid = builtPointUid;
                builtPointNet = GetNetEntity(builtPointUid);
                tier = point.Tier;
                ownerTeamId = point.OwnerTeamId ?? string.Empty;

                if (TryGetTierProfile(point, out var tierProfile))
                {
                    teamXpIncome = tierProfile.TeamXpIncome;
                    influenceIncome = tierProfile.InfluenceIncome;
                    researchIncome = tierProfile.ResearchIncome;
                    artifactIncome = tierProfile.ArtifactIncome;
                    fundsIncome = tierProfile.FundsIncome;
                }
            }

            snapshots.Add(new WH40KStrategicPointAdminSnapshot(
                GetNetEntity(targetUid),
                GetNetEntity(anchorUid),
                builtPointNet,
                anchor.Callsign ?? string.Empty,
                anchor.PointType,
                tier,
                ownerTeamId,
                teamXpIncome,
                influenceIncome,
                researchIncome,
                artifactIncome,
                fundsIncome));
        }

        snapshots.Sort(static (left, right) =>
        {
            var callsignCompare = string.Compare(left.Callsign, right.Callsign, StringComparison.OrdinalIgnoreCase);
            if (callsignCompare != 0)
                return callsignCompare;

            return left.PointType.CompareTo(right.PointType);
        });
        return snapshots;
    }

    public bool TryAdminResetPoint(EntityUid target, out string error)
    {
        error = string.Empty;

        if (!TryResolveAdminTarget(target, out _, out _, out var pointUid, out var point, out error))
            return false;

        if (pointUid == null || point == null)
            return true;

        DestroyStrategicPoint(pointUid.Value, point, null);
        return true;
    }

    public bool TryAdminSetPointOwner(EntityUid target, string teamId, out string error)
    {
        error = string.Empty;

        if (!TryResolveAdminTarget(target, out _, out _, out var pointUid, out var point, out error))
            return false;

        if (pointUid == null || point == null)
        {
            error = "Target point is still T0. Set an owner only on a built T1-T3 point.";
            return false;
        }

        if (!_teamRule.TryResolveTeamId(teamId, out var resolvedTeamId))
        {
            error = $"Unknown team '{teamId}'.";
            return false;
        }

        point.OwnerTeamId = resolvedTeamId;
        point.NextIncomeTick = _timing.CurTime + GetIncomeInterval(point);
        UpdatePointAppearance(pointUid.Value, point);
        UpdateUi((pointUid.Value, point));
        return true;
    }

    public bool TryAdminSetPointTier(EntityUid target, WH40KStrategicPointTier tier, out string error)
    {
        error = string.Empty;

        if (tier <= WH40KStrategicPointTier.T0)
            return TryAdminResetPoint(target, out error);

        if (!TryResolveAdminTarget(target, out _, out _, out var pointUid, out var point, out error))
            return false;

        if (pointUid == null || point == null)
        {
            error = "Target point is still T0. Build it first before forcing T1-T3.";
            return false;
        }

        point.Tier = tier;
        point.UpgradeInProgress = false;
        point.PendingUpgradeTier = WH40KStrategicPointTier.T0;
        point.LoadedUpgradeMaterials.Clear();
        point.NextIncomeTick = _timing.CurTime + GetIncomeInterval(point);
        HealPointToFull(pointUid.Value);
        UpdatePointAppearance(pointUid.Value, point);
        UpdateUi((pointUid.Value, point));
        return true;
    }

    private bool TryResolveAdminTarget(
        EntityUid target,
        out EntityUid? anchorUid,
        out WH40KStrategicPointAnchorComponent? anchor,
        out EntityUid? pointUid,
        out WH40KStrategicPointComponent? point,
        out string error)
    {
        anchorUid = null;
        anchor = null;
        pointUid = null;
        point = null;
        error = string.Empty;

        if (TryComp<WH40KStrategicPointComponent>(target, out var targetPoint))
        {
            pointUid = target;
            point = targetPoint;

            if (targetPoint.Anchor is { } existingAnchorUid &&
                TryComp<WH40KStrategicPointAnchorComponent>(existingAnchorUid, out var existingAnchor))
            {
                anchorUid = existingAnchorUid;
                anchor = existingAnchor;
            }

            return true;
        }

        if (!TryComp<WH40KStrategicPointAnchorComponent>(target, out var targetAnchor))
        {
            error = "Entity is neither a strategic point anchor nor a built strategic point.";
            return false;
        }

        anchorUid = target;
        anchor = targetAnchor;

        if (targetAnchor.BuiltPoint is { } builtPointUid &&
            Exists(builtPointUid) &&
            TryComp<WH40KStrategicPointComponent>(builtPointUid, out var builtPoint))
        {
            pointUid = builtPointUid;
            point = builtPoint;
        }

        return true;
    }

    private bool TryAdjustTeamFunds(EntityUid? sourceUid, string teamId, int amount)
    {
        if (amount <= 0 || !TryGetCargoAccount(teamId, out var account))
            return false;

        if (!TryGetTeamBank(sourceUid, out var bank))
            return false;

        if (_cargo.TryGetAccount(bank, account, out _))
            return _cargo.TryAdjustBankAccount(bank, account, amount);

        return _cargo.TrySetBankAccount(bank, account, amount, createAccount: true);
    }

    private bool TryGetTeamBank(EntityUid? sourceUid, out Entity<StationBankAccountComponent?> bank)
    {
        bank = default;

        if (sourceUid is { } source &&
            _station.GetOwningStation(source) is { } stationUid &&
            TryComp<StationBankAccountComponent>(stationUid, out var sourceBank))
        {
            bank = (stationUid, sourceBank);
            return true;
        }

        var query = EntityQueryEnumerator<StationBankAccountComponent>();
        while (query.MoveNext(out var uid, out var fallbackBank))
        {
            bank = (uid, fallbackBank);
            return true;
        }

        return false;
    }

    private static bool TryGetCargoAccount(string teamId, out ProtoId<CargoAccountPrototype> account)
    {
        if (string.Equals(teamId, "Imperium", StringComparison.OrdinalIgnoreCase))
        {
            account = "WH40KImperium";
            return true;
        }

        if (string.Equals(teamId, "Heretics", StringComparison.OrdinalIgnoreCase))
        {
            account = "WH40KHeretics";
            return true;
        }

        account = default;
        return false;
    }
}
