using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._WH40K.Command.Components;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.OreExtractor.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._WH40K.Tiers;
using Content.Shared.Examine;
using Content.Shared.Item;
using Content.Shared.Maps;
using Content.Shared.Mining;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Random;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.OreExtractor;

/// <summary>
/// Powered ore extractor that periodically spawns ore on its output tile.
/// </summary>
public sealed class WH40KOreExtractorSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private readonly HashSet<EntityUid> _tileEntities = new();
    private readonly CollisionGroup _outputCollisionMask =
        CollisionGroup.Impassable | CollisionGroup.MidImpassable | CollisionGroup.LowImpassable;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KOreExtractorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KOreExtractorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WH40KOreExtractorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    private void OnMapInit(Entity<WH40KOreExtractorComponent> ent, ref MapInitEvent args)
    {
        ApplyTierThresholdProfile(ent.Comp);
        EnsureConfiguredOres(ent.Comp);
        var tier = SelectTier(GetEffectiveLevel(ent.Comp), ent.Comp);
        var interval = GetSpawnIntervalSeconds(tier, ent.Comp);
        ent.Comp.NextSpawnAt = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(0f, interval));
    }

    private void OnExamined(Entity<WH40KOreExtractorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

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

        EnsureConfiguredOres(ent.Comp);
        var tier = SelectTier(GetEffectiveLevel(ent.Comp), ent.Comp);
        var availableByTier = GetAllowedOreIdsForTier(ent.Comp, tier);
        var user = args.User;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = ent.Comp.Enabled
                ? Loc.GetString("wh40k-ore-extractor-verb-disable")
                : Loc.GetString("wh40k-ore-extractor-verb-enable"),
            Category = VerbCategory.SelectType,
            Priority = 20,
            Act = () =>
            {
                SetEnabled(ent, !ent.Comp.Enabled, user);
            },
        });

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wh40k-ore-extractor-verb-select-random"),
            Category = VerbCategory.SelectType,
            Priority = 10,
            Disabled = ent.Comp.SelectedOre == null,
            Act = () =>
            {
                SetRandomOre(ent, user);
            },
        });

        var priority = 9;
        foreach (var oreId in availableByTier)
        {
            var oreName = GetOreDisplayName(oreId);
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wh40k-ore-extractor-verb-select-ore", ("ore", oreName)),
                Category = VerbCategory.SelectType,
                Priority = priority--,
                Disabled = string.Equals(ent.Comp.SelectedOre, oreId, StringComparison.Ordinal),
                Act = () =>
                {
                    SetSelectedOre(ent, oreId, user);
                },
            });
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KOreExtractorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var extractor, out var xform))
        {
            if (extractor.NextSpawnAt > now)
                continue;

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

            if (count >= maxItemsOnTile)
                return true;
        }

        return false;
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

    private void TryAddAllowedOres(HashSet<string> destination, List<string> source)
    {
        foreach (var oreId in source)
        {
            if (!string.IsNullOrWhiteSpace(oreId) && _prototype.HasIndex<OrePrototype>(oreId))
                destination.Add(oreId);
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
