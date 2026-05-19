using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._WH40K.Command.Components;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.OreExtractor.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._WH40K.OreExtractor;
using Content.Shared._WH40K.Tiers;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Maps;
using Content.Shared.Mining;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Random;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server._WH40K.Localizations;

namespace Content.Server._WH40K.OreExtractor;

/// <summary>
/// Powered ore extractor that periodically spawns ore on its output tile.
/// </summary>
public sealed class WH40KOreExtractorSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly WH40KTeamRuleFacadeSystem _teamRule = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly WH40KPlayerCultureTracker _culture = default!;

    private readonly HashSet<EntityUid> _tileEntities = new();
    private readonly CollisionGroup _outputCollisionMask =
        CollisionGroup.Impassable | CollisionGroup.MidImpassable | CollisionGroup.LowImpassable;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KOreExtractorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KOreExtractorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WH40KOreExtractorComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<WH40KOreExtractorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);

        Subs.BuiEvents<WH40KOreExtractorComponent>(WH40KOreExtractorUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<WH40KOreExtractorSetEnabledMessage>(OnSetEnabledMessage);
            subs.Event<WH40KOreExtractorSetRandomModeMessage>(OnSetRandomModeMessage);
            subs.Event<WH40KOreExtractorSelectOreMessage>(OnSelectOreMessage);
        });
    }

    private void OnMapInit(Entity<WH40KOreExtractorComponent> ent, ref MapInitEvent args)
    {
        ApplyTierThresholdProfile(ent.Comp);
        EnsureConfiguredOres(ent.Comp);
        var tier = SelectTier(GetEffectiveLevel(ent.Comp), ent.Comp);
        var interval = GetSpawnIntervalSeconds(tier, ent.Comp);
        ent.Comp.NextSpawnAt = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(0f, interval));
        ent.Comp.NextUiRefreshAt = TimeSpan.Zero;
    }

    private void OnExamined(Entity<WH40KOreExtractorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using var scope = _culture.CreateScope(args.Examiner);
        EnsureConfiguredOres(ent.Comp);
        var enabledState = ent.Comp.Enabled
            ? Loc.GetString("wh40k-ore-extractor-state-enabled")
            : Loc.GetString("wh40k-ore-extractor-state-disabled");
        args.PushMarkup(Loc.GetString(
            "wh40k-ore-extractor-examine-enabled",
            ("state", enabledState)));

        var selectedOre = ent.Comp.SelectedOre != null
            ? GetOreDisplayName(ent.Comp.SelectedOre)
            : Loc.GetString("wh40k-ore-extractor-selection-random");

        args.PushMarkup(Loc.GetString(
            "wh40k-ore-extractor-examine-selected",
            ("ore", selectedOre)));

        var effectiveLevel = GetEffectiveLevel(ent.Comp);
        var tier = SelectTier(effectiveLevel, ent.Comp);
        var interval = GetSpawnIntervalSeconds(tier, ent.Comp);
        var spawnCount = GetSpawnCount(tier, ent.Comp);
        var allowedByTier = GetAllowedOreIdsForTier(ent.Comp, tier);
        var allowedOres = allowedByTier.Count == 0
            ? Loc.GetString("wh40k-ore-extractor-selection-random")
            : string.Join(", ", allowedByTier
                .Select(GetOreDisplayName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

        args.PushMarkup(Loc.GetString(
            "wh40k-ore-extractor-examine-tier",
            ("tier", tier),
            ("level", effectiveLevel)));
        args.PushMarkup(Loc.GetString(
            "wh40k-ore-extractor-examine-bonuses",
            ("interval", interval.ToString("0.##")),
            ("count", spawnCount)));
        args.PushMarkup(Loc.GetString(
            "wh40k-ore-extractor-examine-ores",
            ("ores", allowedOres)));

        if (!TryGetOutputTile(Transform(ent), out _, out _, out var outputDirection))
            return;

        args.PushMarkup(Loc.GetString(
            "wh40k-ore-extractor-examine-output",
            ("direction", Loc.GetString(GetDirectionLocKey(outputDirection)))));
    }

    private void OnGetVerbs(Entity<WH40KOreExtractorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wh40k-ore-extractor-verb-open-ui"),
            Priority = 20,
            Act = () =>
            {
                if (_ui.TryOpenUi(ent.Owner, WH40KOreExtractorUiKey.Key, user))
                    UpdateUi(ent);
            },
        });
    }

    private void OnInteractHand(Entity<WH40KOreExtractorComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        EnsureConfiguredOres(ent.Comp);
        args.Handled = _ui.TryOpenUi(ent.Owner, WH40KOreExtractorUiKey.Key, args.User);
    }

    private void OnUiOpened(Entity<WH40KOreExtractorComponent> ent, ref BoundUIOpenedEvent args)
    {
        EnsureConfiguredOres(ent.Comp);
        ent.Comp.NextUiRefreshAt = TimeSpan.Zero;
        UpdateUi(ent);
    }

    private void OnSetEnabledMessage(Entity<WH40KOreExtractorComponent> ent, ref WH40KOreExtractorSetEnabledMessage args)
    {
        EnsureConfiguredOres(ent.Comp);
        SetEnabled(ent, args.Enabled, args.Actor);
        ent.Comp.NextUiRefreshAt = TimeSpan.Zero;
        UpdateUi(ent);
    }

    private void OnSetRandomModeMessage(Entity<WH40KOreExtractorComponent> ent, ref WH40KOreExtractorSetRandomModeMessage args)
    {
        EnsureConfiguredOres(ent.Comp);
        SetRandomOre(ent, args.Actor);
        ent.Comp.NextUiRefreshAt = TimeSpan.Zero;
        UpdateUi(ent);
    }

    private void OnSelectOreMessage(Entity<WH40KOreExtractorComponent> ent, ref WH40KOreExtractorSelectOreMessage args)
    {
        EnsureConfiguredOres(ent.Comp);
        SetSelectedOre(ent, args.OreId, args.Actor);
        ent.Comp.NextUiRefreshAt = TimeSpan.Zero;
        UpdateUi(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KOreExtractorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var extractor, out var xform))
        {
            var shouldRefreshUi = _ui.IsUiOpen(uid, WH40KOreExtractorUiKey.Key) &&
                                  now >= extractor.NextUiRefreshAt;

            if (extractor.NextSpawnAt > now)
            {
                if (shouldRefreshUi)
                {
                    extractor.NextUiRefreshAt = now + TimeSpan.FromSeconds(1);
                    UpdateUi((uid, extractor));
                }

                continue;
            }

            EnsureConfiguredOres(extractor);
            var tier = SelectTier(GetEffectiveLevel(extractor), extractor);
            var interval = GetSpawnIntervalSeconds(tier, extractor);
            var spawnCount = GetSpawnCount(tier, extractor);
            var allowedByTier = GetAllowedOreIdsForTier(extractor, tier);
            EnsureSelectedOreAllowed(extractor, allowedByTier);

            extractor.NextSpawnAt = now + TimeSpan.FromSeconds(interval);

            if (!extractor.Enabled)
                continue;

            if (!xform.Anchored)
                continue;

            if (extractor.RequirePowered && !this.IsPowered(uid, EntityManager))
                continue;

            if (!TryGetOutputTile(xform, out var outputTile, out var outputCoords, out _))
                continue;

            if (_turf.IsTileBlocked(outputTile, _outputCollisionMask))
                continue;

            if (IsOutputTileSaturated(uid, outputTile, extractor.MaxItemsOnOutputTile))
                continue;

            for (var i = 0; i < spawnCount; i++)
            {
                if (IsOutputTileSaturated(uid, outputTile, extractor.MaxItemsOnOutputTile))
                    break;

                if (!TryResolveOreEntity(extractor, allowedByTier, out var oreEntity))
                    break;

                Spawn(oreEntity, outputCoords);
            }

            if (!shouldRefreshUi)
                continue;

            extractor.NextUiRefreshAt = now + TimeSpan.FromSeconds(1);
            UpdateUi((uid, extractor));
        }
    }

    private void SetEnabled(Entity<WH40KOreExtractorComponent> ent, bool enabled, EntityUid user)
    {
        if (ent.Comp.Enabled == enabled)
            return;

        ent.Comp.Enabled = enabled;
        _popup.PopupEntity(
            enabled
                ? Loc.GetString("wh40k-ore-extractor-popup-enabled")
                : Loc.GetString("wh40k-ore-extractor-popup-disabled"),
            ent.Owner,
            user);
    }

    private void SetRandomOre(Entity<WH40KOreExtractorComponent> ent, EntityUid user)
    {
        if (ent.Comp.SelectedOre == null)
            return;

        ent.Comp.SelectedOre = null;
        _popup.PopupEntity(
            Loc.GetString("wh40k-ore-extractor-popup-selected-random"),
            ent.Owner,
            user);
    }

    private void SetSelectedOre(Entity<WH40KOreExtractorComponent> ent, string oreId, EntityUid user)
    {
        if (!_prototype.HasIndex<OrePrototype>(oreId))
            return;

        var tier = SelectTier(GetEffectiveLevel(ent.Comp), ent.Comp);
        var allowedByTier = GetAllowedOreIdsForTier(ent.Comp, tier);
        if (!allowedByTier.Contains(oreId))
            return;

        ent.Comp.SelectedOre = oreId;
        _popup.PopupEntity(
            Loc.GetString("wh40k-ore-extractor-popup-selected-ore", ("ore", GetOreDisplayName(oreId))),
            ent.Owner,
            user);
    }

    private bool TryResolveOreEntity(
        WH40KOreExtractorComponent extractor,
        IReadOnlySet<string> allowedByTier,
        out EntProtoId oreEntity)
    {
        oreEntity = default;

        string? oreId = null;
        if (!string.IsNullOrWhiteSpace(extractor.SelectedOre) &&
            allowedByTier.Contains(extractor.SelectedOre))
        {
            oreId = extractor.SelectedOre;
        }

        if (oreId == null &&
            _prototype.TryIndex(extractor.RandomOrePool, out var randomPool) &&
            TryPickWeightedAllowedOre(randomPool, allowedByTier, out var weighted))
        {
            oreId = weighted;
        }

        if (oreId == null && TryPickFallbackAllowedOre(allowedByTier, out var fallback))
        {
            oreId = fallback;
        }

        if (oreId == null || !_prototype.TryIndex<OrePrototype>(oreId, out var ore))
            return false;

        if (ore.OreEntity is not { } oreProto)
            return false;

        oreEntity = oreProto;
        return true;
    }

    private bool TryGetOutputTile(
        TransformComponent xform,
        out TileRef outputTile,
        out EntityCoordinates outputCoords,
        out Direction outputDirection)
    {
        outputTile = default;
        outputCoords = default;
        outputDirection = Direction.Invalid;

        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        if (!_map.TryGetTileRef(gridUid, grid, xform.Coordinates, out var centerTile))
            return false;

        outputDirection = xform.LocalRotation.GetCardinalDir();
        var outputIndices = centerTile.GridIndices + outputDirection.ToIntVec();
        if (!_map.TryGetTileRef(gridUid, grid, outputIndices, out outputTile))
            return false;

        outputCoords = _turf.GetTileCenter(outputTile);
        return true;
    }

    private bool IsOutputTileSaturated(EntityUid extractorUid, TileRef outputTile, int maxItemsOnTile)
    {
        if (maxItemsOnTile <= 0)
            return false;

        return CountOutputOccupancy(extractorUid, outputTile) >= maxItemsOnTile;
    }

    private int CountOutputOccupancy(EntityUid extractorUid, TileRef outputTile)
    {
        _tileEntities.Clear();
        _lookup.GetLocalEntitiesIntersecting(
            outputTile.GridUid,
            outputTile.GridIndices,
            _tileEntities,
            0f,
            flags: LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Approximate);

        var count = 0;
        foreach (var entity in _tileEntities)
        {
            if (!IsOutputOccupant(extractorUid, entity))
                continue;

            count += TryComp<StackComponent>(entity, out var stack) && stack.Count > 0
                ? stack.Count
                : 1;
        }

        return count;
    }

    private bool IsOutputOccupant(EntityUid extractorUid, EntityUid entity)
    {
        if (!Exists(entity) || entity == extractorUid)
            return false;

        if (Transform(entity).Anchored)
            return false;

        if (!TryComp<PhysicsComponent>(entity, out var physics) || physics.BodyType == BodyType.Static)
            return false;

        if (_container.TryGetContainingContainer((entity, null, null), out _))
            return false;

        return HasComp<ItemComponent>(entity);
    }

    private int GetEffectiveLevel(WH40KOreExtractorComponent extractor)
    {
        var best = 1;
        var teams = GetTrackedTeams(extractor);
        foreach (var teamId in teams)
        {
            var teamLevel = 1;
            if (_teamRule.TryGetTeamProgress(teamId, out var currentLevel, out _, out _))
                teamLevel = Math.Max(1, currentLevel);

            var withNodeUpgrade = teamLevel + GetTeamNodeUpgrade(teamId);
            best = Math.Max(best, withNodeUpgrade);
        }

        return best;
    }

    private static List<string> GetTrackedTeams(WH40KOreExtractorComponent extractor)
    {
        if (extractor.TeamIds.Count > 0)
            return extractor.TeamIds;

        if (!string.IsNullOrWhiteSpace(extractor.TeamId))
            return new List<string> { extractor.TeamId };

        return new List<string>();
    }

    private int GetTeamNodeUpgrade(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return 0;

        var best = 0;
        var query = EntityQueryEnumerator<WH40KCommandNodeComponent>();
        while (query.MoveNext(out _, out var node))
        {
            if (!string.Equals(node.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            best = Math.Max(best, Math.Max(0, node.UpgradeLevel));
        }

        return best;
    }

    private static int SelectTier(int level, WH40KOreExtractorComponent extractor)
    {
        return WH40KTierMath.SelectTier(level, extractor.Tier1MinBaseLevel, extractor.Tier2MinBaseLevel, extractor.Tier3MinBaseLevel);
    }

    private void UpdateUi(Entity<WH40KOreExtractorComponent> ent)
    {
        var state = BuildUiState(ent);
        _ui.SetUiState(ent.Owner, WH40KOreExtractorUiKey.Key, state);
    }

    private WH40KOreExtractorBuiState BuildUiState(Entity<WH40KOreExtractorComponent> ent)
    {
        EnsureConfiguredOres(ent.Comp);

        var trackedTeams = GetTrackedTeams(ent.Comp);
        var effectiveLevel = GetEffectiveLevel(ent.Comp);
        var tier = SelectTier(effectiveLevel, ent.Comp);
        var allowedByTier = GetAllowedOreIdsForTier(ent.Comp, tier);
        var oreEntries = GetOreEntries(ent.Comp);
        EnsureSelectedOreAllowed(ent.Comp, allowedByTier);

        var orderedAllowedOres = GetOrderedAllowedOreIdsForTier(ent.Comp, tier, allowedByTier);
        var powered = !ent.Comp.RequirePowered || this.IsPowered(ent.Owner, EntityManager);
        var hasOutputTile = TryGetOutputTile(Transform(ent), out var outputTile, out _, out var outputDirection);
        var outputOccupancy = hasOutputTile ? CountOutputOccupancy(ent.Owner, outputTile) : 0;
        var outputBlocked = hasOutputTile && _turf.IsTileBlocked(outputTile, _outputCollisionMask);
        var outputSaturated = hasOutputTile &&
                              ent.Comp.MaxItemsOnOutputTile > 0 &&
                              outputOccupancy >= ent.Comp.MaxItemsOnOutputTile;
        var nextTierLevel = GetNextTierLevel(tier, ent.Comp);
        var status = ResolveUiStatus(ent.Comp, powered, hasOutputTile, outputBlocked, outputSaturated, orderedAllowedOres.Count > 0);
        var nextSpawnSeconds = status == WH40KOreExtractorUiStatus.Ready
            ? Math.Max(0, (int) Math.Ceiling((ent.Comp.NextSpawnAt - _timing.CurTime).TotalSeconds))
            : 0;

        return new WH40KOreExtractorBuiState(
            ResolveThemeTeamId(ent.Comp),
            trackedTeams.ToArray(),
            hasOutputTile
                ? GetDirectionLocKey(outputDirection)
                : "wh40k-ore-extractor-direction-unknown",
            oreEntries.ToArray(),
            orderedAllowedOres.ToArray(),
            ent.Comp.SelectedOre,
            status,
            ent.Comp.Enabled,
            powered,
            ent.Comp.RequirePowered,
            hasOutputTile,
            outputOccupancy,
            ent.Comp.MaxItemsOnOutputTile,
            tier,
            effectiveLevel,
            GetBestNodeUpgrade(trackedTeams),
            nextTierLevel,
            nextTierLevel <= 0 ? 0 : Math.Max(0, nextTierLevel - effectiveLevel),
            GetSpawnIntervalSeconds(tier, ent.Comp),
            GetSpawnCount(tier, ent.Comp),
            nextSpawnSeconds);
    }

    private void ApplyTierThresholdProfile(WH40KOreExtractorComponent extractor)
    {
        if (extractor.TierThresholdProfile is { } profileId &&
            _prototype.TryIndex(profileId, out WH40KTierThresholdProfilePrototype? profile))
        {
            extractor.Tier1MinBaseLevel = profile.Tier1MinBaseLevel;
            extractor.Tier2MinBaseLevel = profile.Tier2MinBaseLevel;
            extractor.Tier3MinBaseLevel = profile.Tier3MinBaseLevel;
        }

        var (tier1, tier2, tier3) = WH40KTierMath.NormalizeThresholds(
            extractor.Tier1MinBaseLevel,
            extractor.Tier2MinBaseLevel,
            extractor.Tier3MinBaseLevel);

        extractor.Tier1MinBaseLevel = tier1;
        extractor.Tier2MinBaseLevel = tier2;
        extractor.Tier3MinBaseLevel = tier3;
    }

    private static float GetSpawnIntervalSeconds(int tier, WH40KOreExtractorComponent extractor)
    {
        return MathF.Max(0.1f, tier switch
        {
            3 => extractor.SpawnIntervalTier3,
            2 => extractor.SpawnIntervalTier2,
            1 => extractor.SpawnIntervalTier1,
            _ => extractor.SpawnIntervalTier0
        });
    }

    private static int GetSpawnCount(int tier, WH40KOreExtractorComponent extractor)
    {
        return Math.Max(1, tier switch
        {
            3 => extractor.SpawnCountTier3,
            2 => extractor.SpawnCountTier2,
            1 => extractor.SpawnCountTier1,
            _ => extractor.SpawnCountTier0
        });
    }

    private HashSet<string> GetAllowedOreIdsForTier(WH40KOreExtractorComponent extractor, int tier)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        TryAddAllowedOres(allowed, extractor.Tier0Ores);
        if (tier >= 1)
            TryAddAllowedOres(allowed, extractor.Tier1Ores);
        if (tier >= 2)
            TryAddAllowedOres(allowed, extractor.Tier2Ores);
        if (tier >= 3)
            TryAddAllowedOres(allowed, extractor.Tier3Ores);

        if (allowed.Count == 0)
            TryAddAllowedOres(allowed, extractor.AvailableOres);

        return allowed;
    }

    private List<string> GetOrderedAllowedOreIdsForTier(
        WH40KOreExtractorComponent extractor,
        int tier,
        IReadOnlySet<string> allowedByTier)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        TryAddOrderedAllowedOres(ordered, seen, extractor.Tier0Ores, allowedByTier);
        if (tier >= 1)
            TryAddOrderedAllowedOres(ordered, seen, extractor.Tier1Ores, allowedByTier);
        if (tier >= 2)
            TryAddOrderedAllowedOres(ordered, seen, extractor.Tier2Ores, allowedByTier);
        if (tier >= 3)
            TryAddOrderedAllowedOres(ordered, seen, extractor.Tier3Ores, allowedByTier);

        if (ordered.Count == 0)
            TryAddOrderedAllowedOres(ordered, seen, extractor.AvailableOres, allowedByTier);

        return ordered;
    }

    private List<WH40KOreExtractorUiOreEntry> GetOreEntries(WH40KOreExtractorComponent extractor)
    {
        var ordered = new List<WH40KOreExtractorUiOreEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        TryAddOreEntries(ordered, seen, extractor.Tier0Ores, 0);
        TryAddOreEntries(ordered, seen, extractor.Tier1Ores, 1);
        TryAddOreEntries(ordered, seen, extractor.Tier2Ores, 2);
        TryAddOreEntries(ordered, seen, extractor.Tier3Ores, 3);

        if (ordered.Count == 0)
            TryAddOreEntries(ordered, seen, extractor.AvailableOres, 0);

        return ordered;
    }

    private void TryAddAllowedOres(HashSet<string> destination, List<string> source)
    {
        foreach (var oreId in source)
        {
            if (!string.IsNullOrWhiteSpace(oreId) && _prototype.HasIndex<OrePrototype>(oreId))
                destination.Add(oreId);
        }
    }

    private void TryAddOrderedAllowedOres(
        List<string> destination,
        HashSet<string> seen,
        List<string> source,
        IReadOnlySet<string> allowedByTier)
    {
        foreach (var oreId in source)
        {
            if (string.IsNullOrWhiteSpace(oreId) ||
                !allowedByTier.Contains(oreId) ||
                !_prototype.HasIndex<OrePrototype>(oreId) ||
                !seen.Add(oreId))
            {
                continue;
            }

            destination.Add(oreId);
        }
    }

    private void TryAddOreEntries(
        List<WH40KOreExtractorUiOreEntry> destination,
        HashSet<string> seen,
        List<string> source,
        int unlockTier)
    {
        foreach (var oreId in source)
        {
            if (string.IsNullOrWhiteSpace(oreId) ||
                !_prototype.HasIndex<OrePrototype>(oreId) ||
                !seen.Add(oreId))
            {
                continue;
            }

            destination.Add(new WH40KOreExtractorUiOreEntry(oreId, unlockTier));
        }
    }

    private static void EnsureSelectedOreAllowed(WH40KOreExtractorComponent extractor, IReadOnlySet<string> allowedByTier)
    {
        if (extractor.SelectedOre == null)
            return;

        if (!allowedByTier.Contains(extractor.SelectedOre))
            extractor.SelectedOre = null;
    }

    private bool TryPickWeightedAllowedOre(
        WeightedRandomOrePrototype randomPool,
        IReadOnlySet<string> allowedByTier,
        out string oreId)
    {
        oreId = string.Empty;
        var totalWeight = 0f;
        foreach (var (candidateId, weight) in randomPool.Weights)
        {
            var safeWeight = MathF.Max(0f, weight);
            if (safeWeight <= 0f ||
                !allowedByTier.Contains(candidateId) ||
                !_prototype.HasIndex<OrePrototype>(candidateId))
            {
                continue;
            }

            totalWeight += safeWeight;
        }

        if (totalWeight <= 0f)
            return false;

        var roll = _random.NextFloat(0f, totalWeight);
        string? lastCandidate = null;
        foreach (var (candidateId, weight) in randomPool.Weights)
        {
            var safeWeight = MathF.Max(0f, weight);
            if (safeWeight <= 0f ||
                !allowedByTier.Contains(candidateId) ||
                !_prototype.HasIndex<OrePrototype>(candidateId))
            {
                continue;
            }

            lastCandidate = candidateId;
            roll -= safeWeight;
            if (roll <= 0f)
            {
                oreId = candidateId;
                return true;
            }
        }

        if (lastCandidate == null)
            return false;

        oreId = lastCandidate;
        return true;
    }

    private bool TryPickFallbackAllowedOre(IReadOnlySet<string> allowedByTier, out string oreId)
    {
        oreId = string.Empty;
        var seen = 0;
        foreach (var candidateId in allowedByTier)
        {
            if (!_prototype.HasIndex<OrePrototype>(candidateId))
                continue;

            seen++;
            if (_random.Next(seen) == 0)
                oreId = candidateId;
        }

        return seen > 0;
    }

    private void EnsureConfiguredOres(WH40KOreExtractorComponent extractor)
    {
        SanitizeOreList(extractor.Tier0Ores);
        SanitizeOreList(extractor.Tier1Ores);
        SanitizeOreList(extractor.Tier2Ores);
        SanitizeOreList(extractor.Tier3Ores);
        SanitizeOreList(extractor.AvailableOres);

        if (!HasAnyTierOres(extractor) &&
            extractor.AvailableOres.Count == 0 &&
            _prototype.TryIndex(extractor.RandomOrePool, out var randomPool))
        {
            foreach (var oreId in randomPool.Weights.Keys)
            {
                if (_prototype.HasIndex<OrePrototype>(oreId))
                    extractor.AvailableOres.Add(oreId);
            }

            SanitizeOreList(extractor.AvailableOres);
        }

        if (!HasAnyTierOres(extractor) && extractor.AvailableOres.Count > 0)
        {
            extractor.Tier0Ores.AddRange(extractor.AvailableOres);
            SanitizeOreList(extractor.Tier0Ores);
        }

        if (string.IsNullOrWhiteSpace(extractor.SelectedOre))
        {
            extractor.SelectedOre = null;
        }
        else if (!_prototype.HasIndex<OrePrototype>(extractor.SelectedOre))
        {
            extractor.SelectedOre = null;
        }

    }

    private static bool HasAnyTierOres(WH40KOreExtractorComponent extractor)
    {
        return extractor.Tier0Ores.Count > 0 ||
               extractor.Tier1Ores.Count > 0 ||
               extractor.Tier2Ores.Count > 0 ||
               extractor.Tier3Ores.Count > 0;
    }

    private void SanitizeOreList(List<string> oreIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = oreIds.Count - 1; i >= 0; i--)
        {
            var oreId = oreIds[i];
            if (string.IsNullOrWhiteSpace(oreId) ||
                !_prototype.HasIndex<OrePrototype>(oreId) ||
                !seen.Add(oreId))
            {
                oreIds.RemoveAt(i);
            }
        }
    }

    private string GetOreDisplayName(string oreId)
    {
        if (!_prototype.TryIndex<OrePrototype>(oreId, out var ore) || ore.OreEntity is not { } oreEntity)
            return oreId;

        if (!_prototype.TryIndex<EntityPrototype>(oreEntity, out var oreEntityProto))
            return oreId;

        return oreEntityProto.Name;
    }

    private static WH40KOreExtractorUiStatus ResolveUiStatus(
        WH40KOreExtractorComponent extractor,
        bool powered,
        bool hasOutputTile,
        bool outputBlocked,
        bool outputSaturated,
        bool hasConfiguredOres)
    {
        if (!hasOutputTile)
            return WH40KOreExtractorUiStatus.NoOutput;

        if (!hasConfiguredOres)
            return WH40KOreExtractorUiStatus.NoConfiguredOres;

        if (!extractor.Enabled)
            return WH40KOreExtractorUiStatus.Disabled;

        if (extractor.RequirePowered && !powered)
            return WH40KOreExtractorUiStatus.Unpowered;

        if (outputBlocked)
            return WH40KOreExtractorUiStatus.OutputBlocked;

        if (outputSaturated)
            return WH40KOreExtractorUiStatus.OutputSaturated;

        return WH40KOreExtractorUiStatus.Ready;
    }

    private static int GetNextTierLevel(int tier, WH40KOreExtractorComponent extractor)
    {
        return tier switch
        {
            <= 0 => extractor.Tier1MinBaseLevel,
            1 => extractor.Tier2MinBaseLevel,
            2 => extractor.Tier3MinBaseLevel,
            _ => 0,
        };
    }

    private int GetBestNodeUpgrade(IReadOnlyCollection<string> teamIds)
    {
        var best = 0;
        foreach (var teamId in teamIds)
        {
            if (string.IsNullOrWhiteSpace(teamId))
                continue;

            best = Math.Max(best, GetTeamNodeUpgrade(teamId));
        }

        return best;
    }

    private static string ResolveThemeTeamId(WH40KOreExtractorComponent extractor)
    {
        if (!string.IsNullOrWhiteSpace(extractor.TeamId))
            return extractor.TeamId;

        return extractor.TeamIds.Count == 1
            ? extractor.TeamIds[0]
            : string.Empty;
    }

    private static string GetDirectionLocKey(Direction direction)
    {
        return direction switch
        {
            Direction.North => "wh40k-ore-extractor-direction-north",
            Direction.South => "wh40k-ore-extractor-direction-south",
            Direction.East => "wh40k-ore-extractor-direction-east",
            Direction.West => "wh40k-ore-extractor-direction-west",
            _ => "wh40k-ore-extractor-direction-unknown",
        };
    }
}
