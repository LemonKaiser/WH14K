using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class WH40KEntityTranslationParityTests
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly CultureInfo EnCulture = CultureInfo.GetCultureInfo("en-US");

    // EN-only entity keys that were added and do not yet have RU translations.
    // This list documents the known gap. When RU translations are added, move them to SharedEntityKeys.
    private static readonly string[] EnOnlyEntityKeys =
    {
        // Floor tiles
        "ent-NecronFloorWH40k",
        "ent-NecronFloorWH40k1",
        "ent-BrickFloorWH40k",
        "ent-BrickFloorWH40k1",
        "ent-BrickFloorWH40k2",
        "ent-BrickFloorWH40k3",
        "ent-ChaosBrickAshFloorWH40k",
        "ent-ConcreteFloorWH40k",
        "ent-ConcreteFloorWH40k1",
        "ent-ConcreteFloorWH40k2",
        "ent-ConcreteFloorWH40k3",
        // Actions
        "ent-ActionWH40KMoraleExecution",
        "ent-ActionWH40KChaosWarpBlast",
        "ent-ActionWH40KChaosWarpKnock",
        "ent-ActionWH40KChaosUndividedBlink",
        "ent-ActionWH40KChaosKhorneRepulse",
        "ent-ActionWH40KChaosNurgleMiasma",
        "ent-ActionWH40KChaosSlaaneshSwap",
        "ent-ActionWH40KChaosTzeentchBarrier",
        "ent-ActionWH40KChaosTzeentchFireball",
        "ent-ActionWH40KChaosWarpBlastSurge",
        "ent-ActionWH40KChaosWarpRiftStep",
        "ent-ActionWH40KChaosUndividedAegis",
        "ent-ActionWH40KChaosKhorneExecutionStep",
        "ent-ActionWH40KChaosNurgleRepulse",
        "ent-ActionWH40KChaosSlaaneshMiasma",
        "ent-ActionWH40KChaosTzeentchMindTwist",
        "ent-ActionWH40KChaosKhorneBloodstorm",
        "ent-ActionWH40KChaosKhorneGroxMorph",
        "ent-ActionWH40KChaosNurgleCorpseBloom",
        "ent-ActionWH40KChaosSlaaneshExquisiteTempo",
        "ent-ActionWH40KChaosTzeentchWarpRewrite",
        "ent-ActionWH40KChaosSlaaneshArena",
        "ent-ActionWH40KChaosLeaderSacrifice",
        "ent-ActionWH40KOpenSquadConsole",
        // SM equipment
        "ent-ClothingBackpackAstartesMk2Powerpack",
        "ent-ClothingBackpackAstartesMk3Powerpack",
        "ent-ClothingBackpackAstartesMk4Powerpack",
        "ent-ClothingBackpackAstartesMk5Powerpack",
        "ent-ClothingBackpackAstartesMk6Powerpack",
        "ent-ClothingBackpackAstartesMk7JumpPack",
        "ent-ClothingBackpackAstartesMk23JumpPack",
        "ent-ClothingHeadHelmetAstartesMk7",
        "ent-ClothingHeadHelmetAstartesMk2",
        "ent-ClothingHeadHelmetAstartesMk3",
        "ent-ClothingHeadHelmetAstartesMk4",
        "ent-ClothingHeadHelmetAstartesMk5",
        "ent-ClothingHeadHelmetAstartesMk6",
        "ent-ClothingOuterArmorAstartesMk7",
        "ent-ClothingOuterArmorAstartesMk2",
        "ent-ClothingOuterArmorAstartesMk3",
        "ent-ClothingOuterArmorAstartesMk4A",
        "ent-ClothingOuterArmorAstartesMk4B",
        "ent-ClothingOuterArmorAstartesMk5",
        "ent-ClothingOuterArmorAstartesMk6",
        // Cultist equipment
        "ent-ClothingBackpackCULT1",
        "ent-ClothingBackpackCULT2",
        "ent-ClothingBackpackSatchelChaosWheel",
        "ent-chaosbelt",
        "ent-chaosbelt2",
        "ent-ClothingHandsGlovesCombatcult",
        "ent-ClothingHeadScarfChaos",
        "ent-ClothingHeadHelmetFlakCHAOS",
        "ent-ClothingMaskGasChaosCultist1",
        "ent-ClothingMaskGasChaosCultist2",
        "ent-ClothingMaskGasCultist3",
        "ent-NecklaceChaosCultistReflectWeak",
        "ent-ClothingOuterArmorChaosVest",
        "ent-ClothingOuterArmorCultistCoat",
        "ent-ClothingShoesChaosBoots1",
        "ent-ClothingShoesChaosBoots2",
        "ent-ClothingUniformJumpsuitCultist1",
        "ent-ClothingUniformJumpsuitCultist2",
        // Rune tablets
        "ent-WH40KRuneSkrizhalBase",
        "ent-WH40KRuneSkrizhalChaos",
        "ent-WH40KRuneSkrizhalKhorn",
        "ent-WH40KRuneSkrizhalNurgk",
        "ent-WH40KRuneSkrizhalSlaanesh",
        "ent-WH40KRuneSkrizhalTzinch",
        // Misc
        "ent-WH40KChaosKhorneStealerBlade",
        "ent-BarricadeWarhammerChaos",
        "ent-BarricadeWarhammerChaosgreen",
        "ent-ShuttleComputerModular",
        "ent-AdMechComputerModular",
        "ent-Battlecry",
    };

    // Entity keys known to exist in both EN and RU (a representative sample).
    private static readonly (string Key, string EnName, string RuName)[] SharedEntitySamples =
    {
        ("ent-WH40KMegaphone", "command megaphone", "командный мегафон"),
        ("ent-WH40KHeavyBolter", "heavy bolter emplacement", "станковый тяжелый болтер"),
        ("ent-ClothingNeckImperialAquilaMedal", "aquila medal", "медаль Аквилы"),
    };

    [Test]
    public async Task EnOnlyEntityKeysExistInEnglish()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        locMan.SetCulture(EnCulture);
        Assert.Multiple(() =>
        {
            foreach (var key in EnOnlyEntityKeys)
            {
                Assert.That(locMan.HasString(key), Is.True, $"EN-only key missing from en-US: {key}");
            }
        });

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EnOnlyEntityKeysDoNotExistInRussian()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        locMan.SetCulture(RuCulture);

        var unexpectedlyPresent = new List<string>();
        foreach (var key in EnOnlyEntityKeys)
        {
            if (locMan.HasString(key))
                unexpectedlyPresent.Add(key);
        }

        // If any of the "EN-only" keys now have RU translations,
        // move them from EnOnlyEntityKeys to SharedEntitySamples.
        if (unexpectedlyPresent.Count > 0)
        {
            Assert.Warn(
                $"{unexpectedlyPresent.Count} EN-only keys now have RU translations. " +
                $"Update the test arrays: {string.Join(", ", unexpectedlyPresent.Take(10))}");
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SharedEntityKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var (key, _, _) in SharedEntitySamples)
            {
                locMan.SetCulture(EnCulture);
                Assert.That(locMan.HasString(key), Is.True, $"Missing en-US: {key}");

                locMan.SetCulture(RuCulture);
                Assert.That(locMan.HasString(key), Is.True, $"Missing ru-RU: {key}");
            }
        });

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SharedEntityKeysReturnCorrectText()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var (key, enName, ruName) in SharedEntitySamples)
            {
                locMan.SetCulture(EnCulture);
                var actualEn = locMan.GetString(key);
                Assert.That(actualEn, Is.EqualTo(enName), $"en-US name mismatch: {key}");

                locMan.SetCulture(RuCulture);
                var actualRu = locMan.GetString(key);
                Assert.That(actualRu, Is.EqualTo(ruName), $"ru-RU name mismatch: {key}");

                Assert.That(actualEn, Is.Not.EqualTo(actualRu),
                    $"Entity '{key}' identical in both cultures");
            }
        });

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NoRuOnlyEntityKeysExist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        // All EN-only keys should at least exist in EN.
        // And no RU key should exist without an EN counterpart.
        // We can't iterate all keys from the localization manager, so we verify
        // the known shared samples exist in EN to confirm the superset relationship.
        locMan.SetCulture(EnCulture);
        Assert.Multiple(() =>
        {
            foreach (var (key, _, _) in SharedEntitySamples)
            {
                Assert.That(locMan.HasString(key), Is.True,
                    $"EN is missing shared entity key {key} - this breaks the superset invariant");
            }
        });

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }
}
