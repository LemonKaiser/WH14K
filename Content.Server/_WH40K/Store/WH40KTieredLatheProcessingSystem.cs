using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._WH40K.Command;
using Content.Server._WH40K.Command.Components;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Store.Components;
using Content.Server.Lathe;
using Content.Server.Materials;
using Content.Shared._WH40K.Tiers;
using Content.Shared.Examine;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Store;

/// <summary>
/// Applies WH40K tier progression to generic lathes:
/// tiered cycle cap, material cap, and optional tier-pack profile remap.
/// </summary>
public sealed class WH40KTieredLatheProcessingSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly WH40KCommandTreeBonusSystem _treeBonuses = default!;
    [Dependency] private readonly LatheSystem _lathe = default!;
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KTieredLatheProcessingComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KTieredLatheProcessingComponent, LatheGetProductionTimeEvent>(OnLatheGetProductionTime);
        SubscribeLocalEvent<WH40KTieredLatheProcessingComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(EntityUid uid, WH40KTieredLatheProcessingComponent component, MapInitEvent args)
    {
        ApplyTierMachineProfile(component);
        component.NextUpdate = TimeSpan.Zero;
        UpdateLathe(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KTieredLatheProcessingComponent>();
        while (query.MoveNext(out var uid, out var processing))
        {
            if (processing.NextUpdate > now)
                continue;

            processing.NextUpdate = now + TimeSpan.FromSeconds(1);
            UpdateLathe(uid, processing);
        }
    }

    private void UpdateLathe(EntityUid uid, WH40KTieredLatheProcessingComponent processing)
    {
        if (!TryComp<LatheComponent>(uid, out var lathe))
            return;

        var effectiveLevel = GetEffectiveLevel(processing);
        var tier = SelectTier(effectiveLevel, processing);
        var teamBonuses = GetBestTrackedTeamBonuses(processing);
        var desiredTimeMultiplier = GetEffectiveGlobalTimeMultiplier(processing, teamBonuses);
        var desiredStorageLimit = GetEffectiveMaterialStorageLimit(tier, processing, teamBonuses);
        var desiredPack = SelectPackForTier(tier, processing);

        var changed = false;
        if (MathF.Abs(lathe.TimeMultiplier - desiredTimeMultiplier) > 0.001f)
        {
            lathe.TimeMultiplier = desiredTimeMultiplier;
            changed = true;
        }

        if (desiredPack is { } pack)
        {
            if (lathe.StaticPacks.Count != 1 || lathe.StaticPacks[0] != pack)
            {
                lathe.StaticPacks.Clear();
                lathe.StaticPacks.Add(pack);
                changed = true;
            }

            if (processing.RemapQueueToSelectedTierPack &&
                TryGetPrimaryRecipeFromPack(pack, out var desiredRecipe) &&
                RemapQueuedRecipes(lathe, processing, desiredRecipe))
            {
                changed = true;
            }
        }

        if (desiredStorageLimit != null &&
            TryComp<MaterialStorageComponent>(uid, out var materialStorage) &&
            _materialStorage.SetStorageLimit(uid, desiredStorageLimit, materialStorage))
        {
            changed = true;
        }

        if (changed)
            _lathe.UpdateUserInterfaceState(uid, lathe);
    }

    private void OnLatheGetProductionTime(
        EntityUid uid,
        WH40KTieredLatheProcessingComponent processing,
        ref LatheGetProductionTimeEvent args)
    {
        var effectiveLevel = GetEffectiveLevel(processing);
        var tier = SelectTier(effectiveLevel, processing);
        var minSeconds = GetEffectiveMinProcessSeconds(tier, processing, GetBestTrackedTeamBonuses(processing));
        if (minSeconds <= 0f)
            return;

        var minTime = TimeSpan.FromSeconds(minSeconds);
        if (args.Time < minTime)
            args.Time = minTime;
    }

    private void OnExamined(EntityUid uid, WH40KTieredLatheProcessingComponent processing, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var effectiveLevel = GetEffectiveLevel(processing);
        var tier = SelectTier(effectiveLevel, processing);
        var timeMultiplier = GetGlobalTimeMultiplier(processing);
        var minSeconds = GetMinProcessSeconds(tier, processing);
        var teamBonuses = GetBestTrackedTeamBonuses(processing);
        timeMultiplier = GetEffectiveGlobalTimeMultiplier(processing, teamBonuses);
        minSeconds = GetEffectiveMinProcessSeconds(tier, processing, teamBonuses);
        var storageLimit = GetEffectiveMaterialStorageLimit(tier, processing, teamBonuses);
        var storageText = storageLimit is > 0
            ? storageLimit.Value.ToString()
            : Loc.GetString("wh40k-tiered-machine-storage-unlimited");

        args.PushMarkup(Loc.GetString(
            "wh40k-tiered-machine-examine-tier",
            ("tier", tier),
            ("level", effectiveLevel)));
        args.PushMarkup(Loc.GetString(
            "wh40k-tiered-machine-examine-bonuses",
            ("multiplier", timeMultiplier.ToString("0.##")),
            ("min_seconds", minSeconds.ToString("0.##")),
            ("storage_limit", storageText)));

        if (HasTieredRecipePacks(processing))
        {
            var outputMultiplier = GetProfileOutputMultiplierForTier(tier, processing);
            args.PushMarkup(Loc.GetString(
                "wh40k-tiered-machine-examine-output-multiplier",
                ("output", outputMultiplier)));
        }
    }

    private int GetEffectiveLevel(WH40KTieredLatheProcessingComponent processing)
    {
        var best = 1;
        var teams = GetTrackedTeams(processing);
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

    private static List<string> GetTrackedTeams(WH40KTieredLatheProcessingComponent processing)
    {
        if (processing.TeamIds.Count > 0)
            return processing.TeamIds;

        if (!string.IsNullOrWhiteSpace(processing.TeamId))
            return new List<string> { processing.TeamId };

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

    private static int SelectTier(int level, WH40KTieredLatheProcessingComponent processing)
    {
        return WH40KTierMath.SelectTier(level, processing.Tier1MinBaseLevel, processing.Tier2MinBaseLevel, processing.Tier3MinBaseLevel);
    }

    private void ApplyTierMachineProfile(WH40KTieredLatheProcessingComponent processing)
    {
        if (processing.TierMachineProfile is { } profileId &&
            _proto.TryIndex(profileId, out WH40KTierMachineProfilePrototype? profile))
        {
            if (profile.ThresholdProfile is { } thresholdId &&
                _proto.TryIndex(thresholdId, out WH40KTierThresholdProfilePrototype? threshold))
            {
                processing.Tier1MinBaseLevel = threshold.Tier1MinBaseLevel;
                processing.Tier2MinBaseLevel = threshold.Tier2MinBaseLevel;
                processing.Tier3MinBaseLevel = threshold.Tier3MinBaseLevel;
            }

            processing.GlobalTimeMultiplier = profile.GlobalTimeMultiplier;
            processing.MinProcessSecondsTier0 = profile.MinProcessSecondsTier0;
            processing.MinProcessSecondsTier1 = profile.MinProcessSecondsTier1;
            processing.MinProcessSecondsTier2 = profile.MinProcessSecondsTier2;
            processing.MinProcessSecondsTier3 = profile.MinProcessSecondsTier3;
            processing.MaterialStorageLimitTier0 = profile.MaterialStorageLimitTier0;
            processing.MaterialStorageLimitTier1 = profile.MaterialStorageLimitTier1;
            processing.MaterialStorageLimitTier2 = profile.MaterialStorageLimitTier2;
            processing.MaterialStorageLimitTier3 = profile.MaterialStorageLimitTier3;
        }

        var (tier1, tier2, tier3) = WH40KTierMath.NormalizeThresholds(
            processing.Tier1MinBaseLevel,
            processing.Tier2MinBaseLevel,
            processing.Tier3MinBaseLevel);

        processing.Tier1MinBaseLevel = tier1;
        processing.Tier2MinBaseLevel = tier2;
        processing.Tier3MinBaseLevel = tier3;
    }

    private static float GetGlobalTimeMultiplier(WH40KTieredLatheProcessingComponent processing)
    {
        return Math.Max(0.01f, processing.GlobalTimeMultiplier);
    }

    private static float GetMinProcessSeconds(int tier, WH40KTieredLatheProcessingComponent processing)
    {
        return Math.Max(0f, tier switch
        {
            3 => processing.MinProcessSecondsTier3,
            2 => processing.MinProcessSecondsTier2,
            1 => processing.MinProcessSecondsTier1,
            _ => processing.MinProcessSecondsTier0
        });
    }

    private static int? GetMaterialStorageLimit(int tier, WH40KTieredLatheProcessingComponent processing)
    {
        var limit = tier switch
        {
            3 => processing.MaterialStorageLimitTier3,
            2 => processing.MaterialStorageLimitTier2,
            1 => processing.MaterialStorageLimitTier1,
            _ => processing.MaterialStorageLimitTier0
        };

        return limit is > 0 ? limit : null;
    }

    private WH40KCommandTreeTeamBonuses GetBestTrackedTeamBonuses(WH40KTieredLatheProcessingComponent processing)
    {
        var best = default(WH40KCommandTreeTeamBonuses);
        var bestScore = int.MinValue;

        foreach (var teamId in GetTrackedTeams(processing).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var current = _treeBonuses.GetTeamBonuses(teamId);
            var score = current.MachineSpeedBonusPercent * 1000 + current.MachineStorageBonus;
            if (score <= bestScore)
                continue;

            best = current;
            bestScore = score;
        }

        return best;
    }

    private static float GetEffectiveGlobalTimeMultiplier(
        WH40KTieredLatheProcessingComponent processing,
        WH40KCommandTreeTeamBonuses bonuses)
    {
        var baseMultiplier = GetGlobalTimeMultiplier(processing);
        if (bonuses.MachineSpeedBonusPercent <= 0)
            return baseMultiplier;

        var speedMultiplier = Math.Max(0.05f, 1f - bonuses.MachineSpeedBonusPercent / 100f);
        return MathF.Max(0.01f, baseMultiplier * speedMultiplier);
    }

    private static float GetEffectiveMinProcessSeconds(
        int tier,
        WH40KTieredLatheProcessingComponent processing,
        WH40KCommandTreeTeamBonuses bonuses)
    {
        var baseMinSeconds = GetMinProcessSeconds(tier, processing);
        if (bonuses.MachineSpeedBonusPercent <= 0 || baseMinSeconds <= 0.001f)
            return baseMinSeconds;

        var speedMultiplier = Math.Max(0.05f, 1f - bonuses.MachineSpeedBonusPercent / 100f);
        return MathF.Max(0.1f, baseMinSeconds * speedMultiplier);
    }

    private static int? GetEffectiveMaterialStorageLimit(
        int tier,
        WH40KTieredLatheProcessingComponent processing,
        WH40KCommandTreeTeamBonuses bonuses)
    {
        var baseLimit = GetMaterialStorageLimit(tier, processing);
        if (baseLimit == null)
            return null;

        return Math.Max(1, baseLimit.Value + Math.Max(0, bonuses.MachineStorageBonus));
    }

    private static ProtoId<LatheRecipePackPrototype>? SelectPackForTier(
        int tier,
        WH40KTieredLatheProcessingComponent processing)
    {
        var direct = GetPackForTier(tier, processing);
        if (direct is { })
            return direct;

        for (var fallback = tier - 1; fallback >= 0; fallback--)
        {
            var candidate = GetPackForTier(fallback, processing);
            if (candidate is { })
                return candidate;
        }

        for (var fallback = tier + 1; fallback <= 3; fallback++)
        {
            var candidate = GetPackForTier(fallback, processing);
            if (candidate is { })
                return candidate;
        }

        return null;
    }

    private static ProtoId<LatheRecipePackPrototype>? GetPackForTier(
        int tier,
        WH40KTieredLatheProcessingComponent processing)
    {
        return tier switch
        {
            3 => processing.Tier3Pack,
            2 => processing.Tier2Pack,
            1 => processing.Tier1Pack,
            _ => processing.Tier0Pack
        };
    }

    private static bool HasTieredRecipePacks(WH40KTieredLatheProcessingComponent processing)
    {
        return processing.Tier0Pack is { } ||
               processing.Tier1Pack is { } ||
               processing.Tier2Pack is { } ||
               processing.Tier3Pack is { };
    }

    private int GetProfileOutputMultiplierForTier(int tier, WH40KTieredLatheProcessingComponent processing)
    {
        var packId = SelectPackForTier(tier, processing);
        if (packId is not { } id)
            return 1;

        if (!TryGetPrimaryRecipeFromPack(id, out var recipeId))
            return 1;

        if (!_proto.TryIndex(recipeId, out LatheRecipePrototype? recipe) || recipe.Result is not { } resultProto)
            return 1;

        if (!_proto.TryIndex<EntityPrototype>(resultProto, out var resultEntity))
            return 1;

        if (!resultEntity.TryGetComponent<StackComponent>(out var stackComp, EntityManager.ComponentFactory))
            return 1;

        return Math.Max(1, stackComp.Count);
    }

    private bool TryGetPrimaryRecipeFromPack(
        ProtoId<LatheRecipePackPrototype> packId,
        out ProtoId<LatheRecipePrototype> recipeId)
    {
        recipeId = default;
        if (!_proto.TryIndex(packId, out var pack))
            return false;

        foreach (var recipe in pack.Recipes)
        {
            recipeId = recipe;
            return true;
        }

        return false;
    }

    private bool RemapQueuedRecipes(
        LatheComponent lathe,
        WH40KTieredLatheProcessingComponent processing,
        ProtoId<LatheRecipePrototype> desiredRecipe)
    {
        var knownRecipes = new HashSet<ProtoId<LatheRecipePrototype>>();
        AddPackRecipes(knownRecipes, processing.Tier0Pack);
        AddPackRecipes(knownRecipes, processing.Tier1Pack);
        AddPackRecipes(knownRecipes, processing.Tier2Pack);
        AddPackRecipes(knownRecipes, processing.Tier3Pack);

        if (knownRecipes.Count == 0)
            return false;

        var changed = false;
        var node = lathe.Queue.First;
        while (node != null)
        {
            var batch = node.Value;
            if (knownRecipes.Contains(batch.Recipe) && batch.Recipe != desiredRecipe)
            {
                batch.Recipe = desiredRecipe;
                changed = true;
            }

            node = node.Next;
        }

        return changed;
    }

    private void AddPackRecipes(
        HashSet<ProtoId<LatheRecipePrototype>> target,
        ProtoId<LatheRecipePackPrototype>? packId)
    {
        if (packId is not { } id)
            return;

        if (!_proto.TryIndex(id, out var pack))
            return;

        foreach (var recipe in pack.Recipes)
        {
            target.Add(recipe);
        }
    }
}
