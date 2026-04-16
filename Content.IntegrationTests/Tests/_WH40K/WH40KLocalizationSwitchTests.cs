using System.Globalization;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class WH40KLocalizationSwitchTests
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly CultureInfo EnCulture = CultureInfo.GetCultureInfo("en-US");

    // --- Simple keys (no parameters) ---

    private static readonly (string Key, string En, string Ru)[] SimpleKeys =
    {
        // Popup / action messages
        ("wh40k-tank-engine-started", "The engine rumbles to life.", "Двигатель с рокотом оживает."),
        ("wh40k-tank-repair-engine-running", "Shut the engine down before attempting repairs.", "Заглушите двигатель перед ремонтом."),

        // UI window titles
        ("wh40k-tank-ui-window-title", "Leman Russ Diagnostics", "Диагностика Леман Русса"),
        ("wh40k-chaos-selector-title", "Chaos Patron Selection", "Выбор покровителя Хаоса"),
        ("w40k-cmd-window-title", "Command Node", "Командный узел"),

        // Game-mode labels
        ("wh40k-team-battle-title", "WH40K Team Battle", "Битва команд WH40K"),
        ("wh40k-weather-name-WHAsh", "Ash Front", "Пепельный фронт"),

        // Phase label
        ("wh40k-phase-preparation-name", "Preparation", "Подготовка"),

        // Chaos descriptions
        ("wh40k-chaos-selector-khorne-desc",
            "Blood and wrath. Dominates close assault and relentless offense.",
            "Кровь и ярость. Упор на ближний бой и постоянное давление."),
    };

    // --- Parameterized keys ---

    private static readonly (string Key, (string, object)[] Args, string En, string Ru)[] ParamKeys =
    {
        ("wh40k-tank-entry-verb",
            new (string, object)[] { ("role", "Driver") },
            "Enter as Driver",
            "Занять место: Driver"),

        ("wh40k-tank-weapon-disabled",
            new (string, object)[] { ("weapon", "Battle Cannon") },
            "The Battle Cannon is offline.",
            "Battle Cannon выведено из строя."),

        ("w40k-cg-already-attuned",
            new (string, object)[] { ("patron", "Khorne") },
            "You are already attuned to Khorne.",
            "Вы уже настроены на Khorne."),

        ("w40k-cg-sacrifice-success",
            new (string, object)[] { ("xp", 50), ("seconds", 30) },
            "The ritual is accepted (+50 Gift XP, cooldown 30s).",
            "Ритуал принят (+50 опыта даров, перезарядка 30с)."),
    };

    // --- Entity prototype loc keys (ent-* and ent-*.desc via GetString) ---
    // NOTE: GetEntityData() uses an engine-level ConcurrentDictionary cache
    // that is NOT flushed on SetCulture, so we test entity keys via GetString/HasString instead.

    private static readonly (string LocId, string EnName, string RuName)[] EntityNameKeys =
    {
        ("ent-WH40KMegaphone", "command megaphone", "командный мегафон"),
        ("ent-WH40KHeavyBolter", "heavy bolter emplacement", "станковый тяжелый болтер"),
        ("ent-ClothingNeckImperialAquilaMedal", "aquila medal", "медаль Аквилы"),
    };

    [Test]
    public async Task CriticalLocKeysSwitchCulturesInSingleServerPair()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var (key, expectedEn, expectedRu) in SimpleKeys)
            {
                locMan.SetCulture(EnCulture);
                Assert.That(locMan.HasString(key), Is.True, $"Missing en-US key: {key}");
                var actualEn = locMan.GetString(key);
                Assert.That(actualEn, Is.EqualTo(expectedEn), $"en-US mismatch for '{key}'");

                locMan.SetCulture(RuCulture);
                Assert.That(locMan.HasString(key), Is.True, $"Missing ru-RU key: {key}");
                var actualRu = locMan.GetString(key);
                Assert.That(actualRu, Is.EqualTo(expectedRu), $"ru-RU mismatch for '{key}'");
                Assert.That(actualEn, Is.Not.EqualTo(actualRu), $"Cultures returned identical text for '{key}'");
            }

            foreach (var (key, args, expectedEn, expectedRu) in ParamKeys)
            {
                locMan.SetCulture(EnCulture);
                var actualEn = locMan.GetString(key, args);
                Assert.That(actualEn, Is.EqualTo(expectedEn), $"en-US mismatch for parameterized '{key}'");

                locMan.SetCulture(RuCulture);
                var actualRu = locMan.GetString(key, args);
                Assert.That(actualRu, Is.EqualTo(expectedRu), $"ru-RU mismatch for parameterized '{key}'");
                Assert.That(actualEn, Is.Not.EqualTo(actualRu), $"Cultures returned identical text for parameterized '{key}'");
            }

            foreach (var (locId, enName, ruName) in EntityNameKeys)
            {
                locMan.SetCulture(EnCulture);
                Assert.That(locMan.HasString(locId), Is.True, $"Missing en-US entity key: {locId}");
                var actualEn = locMan.GetString(locId);
                Assert.That(actualEn, Is.EqualTo(enName), $"en-US name mismatch for '{locId}'");

                locMan.SetCulture(RuCulture);
                Assert.That(locMan.HasString(locId), Is.True, $"Missing ru-RU entity key: {locId}");
                var actualRu = locMan.GetString(locId);
                Assert.That(actualRu, Is.EqualTo(ruName), $"ru-RU name mismatch for '{locId}'");
                Assert.That(actualEn, Is.Not.EqualTo(actualRu), $"Entity '{locId}' name is identical in both cultures");
            }
        });

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }
}
