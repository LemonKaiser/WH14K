using System;
using System.Collections.Generic;
using Content.Server._WH40K.Command.Components;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Store.Components;
using Content.Server.Lathe;
using Content.Server.Stack;
using Content.Shared.Examine;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Server._WH40K.Localizations;

namespace Content.Server._WH40K.Store;

/// <summary>
/// Applies WH40K team progression to chip converters:
/// recipe tier switching and active-job limit by tier.
/// </summary>
public sealed partial class WH40KChipConverterSystem : EntitySystem
{
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  IPrototypeManager _proto = default!;
    [Dependency] private  WH40KTeamRuleFacadeSystem _teamRule = default!;
    [Dependency] private  LatheSystem _lathe = default!;
    [Dependency] private  StackSystem _stack = default!;
    [Dependency] private  WH40KPlayerCultureTracker _culture = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KChipConverterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KChipConverterComponent, LatheFinishPrintingEvent>(OnLatheFinishedPrinting);
        SubscribeLocalEvent<WH40KChipConverterComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(EntityUid uid, WH40KChipConverterComponent component, MapInitEvent args)
    {
        component.NextUpdate = TimeSpan.Zero;
        UpdateConverter(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KChipConverterComponent>();
        while (query.MoveNext(out var uid, out var converter))
        {
            if (converter.NextUpdate > now)
                continue;

            converter.NextUpdate = now + TimeSpan.FromSeconds(1);
            UpdateConverter(uid, converter);
        }
    }

    private void UpdateConverter(EntityUid uid, WH40KChipConverterComponent converter)
    {
        if (!TryComp<LatheComponent>(uid, out var lathe))
            return;

        var effectiveLevel = GetEffectiveLevel(converter.TeamId);
        var tier = SelectTier(effectiveLevel, converter);
        var desiredPack = SelectPack(tier, converter);
        var desiredConcurrentLimit = GetConcurrentLimit(tier, converter);
        var changed = false;

        if (lathe.StaticPacks.Count != 1 || lathe.StaticPacks[0] != desiredPack)
        {
            lathe.StaticPacks.Clear();
            lathe.StaticPacks.Add(desiredPack);
            changed = true;
        }

        // Tiered recipe times are defined in recipe prototypes. Keep multiplier neutral.
        if (MathF.Abs(lathe.TimeMultiplier - 1f) > 0.001f)
        {
            lathe.TimeMultiplier = 1f;
            changed = true;
        }

        if (lathe.DefaultProductionAmount != desiredConcurrentLimit)
        {
            lathe.DefaultProductionAmount = desiredConcurrentLimit;
            changed = true;
        }

        if (TryGetPrimaryRecipe(desiredPack, out var desiredRecipe) &&
            RemapQueuedRecipes(lathe, converter, desiredRecipe))
        {
            changed = true;
        }

        if (changed)
            _lathe.UpdateUserInterfaceState(uid, lathe);
    }

    private void OnLatheFinishedPrinting(
        EntityUid uid,
        WH40KChipConverterComponent converter,
        ref LatheFinishPrintingEvent args)
    {
        if (!TryComp<LatheComponent>(uid, out var lathe))
            return;

        var effectiveLevel = GetEffectiveLevel(converter.TeamId);
        var tier = SelectTier(effectiveLevel, converter);
        var slots = GetConcurrentLimit(tier, converter);
        var extraCompletions = Math.Max(0, slots - 1);
        if (extraCompletions <= 0)
            return;

        if (CompleteExtraQueuedPrints(uid, lathe, args.Recipe, extraCompletions))
            _lathe.UpdateUserInterfaceState(uid, lathe);
    }

    private void OnExamined(EntityUid uid, WH40KChipConverterComponent converter, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using var scope = _culture.CreateScope(args.Examiner);

        var effectiveLevel = GetEffectiveLevel(converter.TeamId);
        var tier = SelectTier(effectiveLevel, converter);
        var jobs = GetConcurrentLimit(tier, converter);
        var storageText = Loc.GetString("wh40k-tiered-machine-storage-unlimited");

        args.PushMarkup(Loc.GetString(
            "wh40k-chip-converter-examine-tier",
            ("tier", tier),
            ("level", effectiveLevel)));
        args.PushMarkup(Loc.GetString(
            "wh40k-chip-converter-examine-bonuses",
            ("jobs", jobs),
            ("storage", storageText)));
    }

    private int GetEffectiveLevel(string teamId)
    {
        var level = 1;
        if (!string.IsNullOrWhiteSpace(teamId) &&
            _teamRule.TryGetTeamProgress(teamId, out var currentLevel, out _, out _))
        {
            level = Math.Max(1, currentLevel);
        }

        var nodeUpgrade = GetTeamNodeUpgrade(teamId);
        return level + nodeUpgrade;
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

    private static int SelectTier(int level, WH40KChipConverterComponent converter)
    {
        if (level >= converter.Tier3MinBaseLevel)
            return 3;

        if (level >= converter.Tier2MinBaseLevel)
            return 2;

        if (level >= converter.Tier1MinBaseLevel)
            return 1;

        return 0;
    }

    private static ProtoId<LatheRecipePackPrototype> SelectPack(int tier, WH40KChipConverterComponent converter)
    {
        return tier switch
        {
            3 => converter.Tier3Pack,
            2 => converter.Tier2Pack,
            1 => converter.Tier1Pack,
            _ => converter.Tier1Pack
        };
    }

    private static int GetConcurrentLimit(int tier, WH40KChipConverterComponent converter)
    {
        return Math.Max(1, tier switch
        {
            3 => converter.MaxConcurrentJobsTier3,
            2 => converter.MaxConcurrentJobsTier2,
            1 => converter.MaxConcurrentJobsTier1,
            _ => converter.MaxConcurrentJobsTier1
        });
    }

    private bool TryGetPrimaryRecipe(ProtoId<LatheRecipePackPrototype> packId, out ProtoId<LatheRecipePrototype> recipe)
    {
        recipe = default;

        if (!_proto.TryIndex(packId, out var pack))
            return false;

        foreach (var entry in pack.Recipes)
        {
            recipe = entry;
            return true;
        }

        return false;
    }

    private bool RemapQueuedRecipes(
        LatheComponent lathe,
        WH40KChipConverterComponent converter,
        ProtoId<LatheRecipePrototype> desiredRecipe)
    {
        var knownRecipes = new HashSet<ProtoId<LatheRecipePrototype>>();
        AddPackRecipes(knownRecipes, converter.Tier1Pack);
        AddPackRecipes(knownRecipes, converter.Tier2Pack);
        AddPackRecipes(knownRecipes, converter.Tier3Pack);

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

    private void AddPackRecipes(HashSet<ProtoId<LatheRecipePrototype>> dest, ProtoId<LatheRecipePackPrototype> packId)
    {
        if (!_proto.TryIndex(packId, out var pack))
            return;

        foreach (var recipe in pack.Recipes)
        {
            dest.Add(recipe);
        }
    }

    private bool CompleteExtraQueuedPrints(
        EntityUid uid,
        LatheComponent lathe,
        LatheRecipePrototype recipe,
        int extraCompletions)
    {
        if (extraCompletions <= 0 || recipe.Result is not {} resultProto)
            return false;

        var changed = false;
        while (extraCompletions > 0 && lathe.Queue.First is {} node)
        {
            var batch = node.Value;
            if (!_proto.TryIndex(batch.Recipe, out LatheRecipePrototype? queuedRecipe))
            {
                lathe.Queue.RemoveFirst();
                changed = true;
                continue;
            }

            if (queuedRecipe.ID != recipe.ID)
                break;

            var outstanding = Math.Max(0, batch.ItemsRequested - batch.ItemsPrinted);
            if (outstanding <= 0)
            {
                lathe.Queue.RemoveFirst();
                changed = true;
                continue;
            }

            batch.ItemsPrinted++;
            if (batch.ItemsPrinted >= batch.ItemsRequested)
                lathe.Queue.RemoveFirst();

            var result = Spawn(resultProto, Transform(uid).Coordinates);
            _stack.TryMergeToContacts(result);
            extraCompletions--;
            changed = true;
        }

        return changed;
    }
}
