using System.Globalization;
using System.Linq;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class WH40KLocalizationCoverageTests
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly CultureInfo EnCulture = CultureInfo.GetCultureInfo("en-US");

    // All locale keys used by WH40KTeamBattleRuleSystem chat event conversions.
    private static readonly string[] TeamBattleKeys =
    {
        "wh40k-team-time-limit-announce",
        "wh40k-team-level-up-announce",
        "wh40k-team-level-buff-announce",
        "wh40k-weather-start-announce",
        "wh40k-weather-warning-announce",
        "wh40k-round-event-logistics-start",
        "wh40k-round-event-logistics-end",
        "wh40k-round-event-orbital-start",
        "wh40k-round-event-orbital-end",
        "wh40k-round-event-blackfront-start",
        "wh40k-round-event-blackfront-end",
        "wh40k-round-event-warning",
    };

    // Weather display name keys referenced by GetWeatherDisplayNameKey helper.
    private static readonly string[] WeatherNameKeys =
    {
        "wh40k-weather-name-unknown",
        "wh40k-weather-name-WHAsh",
        "wh40k-weather-name-WHToxicAshFront",
        "wh40k-weather-name-WHAcidRain",
        "wh40k-weather-name-WHRadFront",
        "wh40k-weather-name-WHIonStorm",
        "wh40k-weather-name-WHBlackIce",
        "wh40k-weather-name-WHSandHurricane",
        "wh40k-weather-name-WHMetalHail",
        "wh40k-weather-name-WHSporeDrift",
        "wh40k-weather-name-WHGellarTremor",
        "wh40k-weather-name-WHMachineCorrosionStorm",
        "wh40k-weather-name-WHBlackFront",
    };

    // Weather summary keys referenced by GetWeatherSummaryKey helper.
    private static readonly string[] WeatherSummaryKeys =
    {
        "wh40k-weather-summary-unknown",
        "wh40k-weather-summary-WHAsh",
        "wh40k-weather-summary-WHToxicAshFront",
        "wh40k-weather-summary-WHAcidRain",
        "wh40k-weather-summary-WHRadFront",
        "wh40k-weather-summary-WHIonStorm",
        "wh40k-weather-summary-WHBlackIce",
        "wh40k-weather-summary-WHSandHurricane",
        "wh40k-weather-summary-WHMetalHail",
        "wh40k-weather-summary-WHSporeDrift",
        "wh40k-weather-summary-WHGellarTremor",
        "wh40k-weather-summary-WHMachineCorrosionStorm",
        "wh40k-weather-summary-WHBlackFront",
    };

    // Weather danger level keys.
    private static readonly string[] WeatherDangerKeys =
    {
        "wh40k-weather-danger-low",
        "wh40k-weather-danger-medium",
        "wh40k-weather-danger-high",
        "wh40k-weather-danger-extreme",
    };

    // Weather protection keys referenced by GetWeatherProtectionAdviceKey.
    private static readonly string[] WeatherProtectionKeys =
    {
        "wh40k-weather-protection-generic",
        "wh40k-weather-protection-gasmask",
        "wh40k-weather-protection-hardsuit",
        "wh40k-weather-protection-gasmask-or-hardsuit",
        "wh40k-weather-protection-emp",
        "wh40k-weather-protection-structures",
        "wh40k-weather-protection-cover",
        "wh40k-weather-protection-WHBlackIce",
        "wh40k-weather-protection-WHGellarTremor",
        "wh40k-weather-protection-WHBlackFront",
    };

    // Round event name keys.
    private static readonly string[] RoundEventNameKeys =
    {
        "wh40k-round-event-name-logistics",
        "wh40k-round-event-name-orbital",
        "wh40k-round-event-name-blackfront",
        "wh40k-round-event-name-unknown",
    };

    // Team level buff keys used in level-up announcements.
    private static readonly string[] LevelBuffKeys =
    {
        "wh40k-team-level-buff-name-none",
        "wh40k-team-level-buff-name-pulling",
        "wh40k-team-level-buff-name-medical",
        "wh40k-team-level-buff-name-construction",
        "wh40k-team-level-buff-effect-none",
        "wh40k-team-level-buff-effect-pulling",
        "wh40k-team-level-buff-effect-medical",
        "wh40k-team-level-buff-effect-construction",
    };

    // Command runtime mission keys.
    private static readonly string[] CommandRuntimeKeys =
    {
        "wh40k-command-runtime-event-periodic-bonus",
        "wh40k-command-runtime-event-started",
        "wh40k-command-runtime-event-ended",
        "wh40k-command-runtime-mission-global-started",
        "wh40k-command-runtime-mission-faction-started",
        "wh40k-command-runtime-mission-enemy-counter-cargo",
        "wh40k-command-runtime-mission-enemy-counter-control",
        "wh40k-command-runtime-mission-global-resolved-major",
        "wh40k-command-runtime-mission-global-resolved-minor",
        "wh40k-command-runtime-mission-global-timeout",
        "wh40k-command-runtime-mission-global-failed",
        "wh40k-command-runtime-mission-faction-resolved",
        "wh40k-command-runtime-mission-outcome-team",
        "wh40k-command-runtime-mission-token-applied",
        "wh40k-command-runtime-mission-token-applied-event-roll",
        "wh40k-command-runtime-mission-intel-jam-applied",
    };

    // Core team/phase locale keys.
    private static readonly string[] TeamCoreKeys =
    {
        "wh40k-team-imperium",
        "wh40k-team-heretics",
        "wh40k-team-service-message",
        "wh40k-team-service-message-Imperium",
        "wh40k-team-service-message-Heretics",
        "wh40k-team-winner-announce",
        "wh40k-team-draw-announce",
        "wh40k-phase-preparation-announce",
        "wh40k-phase-assault-announce",
        "wh40k-phase-apocalypse-announce",
    };

    // Megaphone locale keys used in WH40KMegaphoneSystem.
    private static readonly string[] MegaphoneKeys =
    {
        "wh40k-megaphone-popup-item-cooldown",
        "wh40k-megaphone-popup-user-cooldown",
        "wh40k-megaphone-popup-rate-limit",
        "wh40k-megaphone-popup-not-in-hand",
        "wh40k-megaphone-popup-empty",
        "wh40k-megaphone-popup-no-orders",
    };

    // Client wrapper key used by WH40KLocalizedChatSystem.
    private static readonly string[] ClientKeys =
    {
        "chat-manager-server-wrap-message",
    };

    [Test]
    public async Task TeamBattleKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in TeamBattleKeys)
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
    public async Task WeatherKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        var allWeather = WeatherNameKeys
            .Concat(WeatherSummaryKeys)
            .Concat(WeatherDangerKeys)
            .Concat(WeatherProtectionKeys);

        Assert.Multiple(() =>
        {
            foreach (var key in allWeather)
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
    public async Task WeatherKeysReturnDifferentTextPerCulture()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in WeatherNameKeys)
            {
                locMan.SetCulture(EnCulture);
                var en = locMan.GetString(key);

                locMan.SetCulture(RuCulture);
                var ru = locMan.GetString(key);

                Assert.That(en, Is.Not.EqualTo(ru),
                    $"Weather name '{key}' identical in both cultures — locale switch may not work");
            }
        });

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundEventKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in RoundEventNameKeys)
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
    public async Task LevelBuffKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in LevelBuffKeys)
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
    public async Task CommandRuntimeKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in CommandRuntimeKeys)
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
    public async Task TeamCoreKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in TeamCoreKeys)
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
    public async Task MegaphoneKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in MegaphoneKeys)
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
    public async Task ClientWrapperKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in ClientKeys)
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
    public async Task ParameterizedWeatherAnnouncementResolvesInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        var args = new (string, object)[]
        {
            ("weather", "Ash Front"),
            ("danger", "HIGH"),
            ("summary", "Superheated ash burns exposed personnel."),
            ("protection", "Stay under cover."),
        };

        locMan.SetCulture(EnCulture);
        var en = locMan.GetString("wh40k-weather-start-announce", args);
        Assert.That(en, Does.Contain("Ash Front"), "en-US weather announcement missing weather arg");

        locMan.SetCulture(RuCulture);
        var ru = locMan.GetString("wh40k-weather-start-announce", args);
        Assert.That(ru, Does.Contain("Ash Front"), "ru-RU weather announcement missing weather arg");

        Assert.That(en, Is.Not.EqualTo(ru),
            "Weather announcement is identical in both cultures");

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ParameterizedLevelUpAnnouncementResolvesInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        var args = new (string, object)[]
        {
            ("team", "Imperium"),
            ("level", 3),
        };

        locMan.SetCulture(EnCulture);
        var en = locMan.GetString("wh40k-team-level-up-announce", args);
        Assert.That(en, Does.Contain("3"), "en-US level-up announcement missing level");

        locMan.SetCulture(RuCulture);
        var ru = locMan.GetString("wh40k-team-level-up-announce", args);
        Assert.That(ru, Does.Contain("3"), "ru-RU level-up announcement missing level");

        Assert.That(en, Is.Not.EqualTo(ru),
            "Level-up announcement is identical in both cultures");

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ParameterizedRoundEventWarningResolvesInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        var args = new (string, object)[]
        {
            ("seconds", 30),
            ("event", "Logistics Surge"),
        };

        locMan.SetCulture(EnCulture);
        var en = locMan.GetString("wh40k-round-event-warning", args);
        Assert.That(en, Does.Contain("30"), "en-US round event warning missing seconds");
        Assert.That(en, Does.Contain("Logistics Surge"), "en-US round event warning missing event name");

        locMan.SetCulture(RuCulture);
        var ru = locMan.GetString("wh40k-round-event-warning", args);
        Assert.That(ru, Does.Contain("30"), "ru-RU round event warning missing seconds");

        Assert.That(en, Is.Not.EqualTo(ru),
            "Round event warning is identical in both cultures");

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ParameterizedMissionStartedResolvesInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        var args = new (string, object)[]
        {
            ("mission", "Supply Raid"),
            ("duration", 120),
            ("coords", "X:42 Y:17"),
        };

        locMan.SetCulture(EnCulture);
        var en = locMan.GetString("wh40k-command-runtime-mission-global-started", args);
        Assert.That(en, Does.Contain("Supply Raid"), "en-US mission started missing mission arg");
        Assert.That(en, Does.Contain("120"), "en-US mission started missing duration");

        locMan.SetCulture(RuCulture);
        var ru = locMan.GetString("wh40k-command-runtime-mission-global-started", args);
        Assert.That(ru, Does.Contain("Supply Raid"), "ru-RU mission started missing mission arg");

        Assert.That(en, Is.Not.EqualTo(ru),
            "Mission started announcement is identical in both cultures");

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AllConvertedKeysResolveWithoutPlaceholderLeaks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        // Only truly argument-free keys. Parameterized keys like weather-start-announce are excluded.
        var noArgTeamBattleKeys = new[]
        {
            "wh40k-team-time-limit-announce",
            "wh40k-round-event-logistics-start",
            "wh40k-round-event-logistics-end",
            "wh40k-round-event-orbital-start",
            "wh40k-round-event-orbital-end",
            "wh40k-round-event-blackfront-start",
            "wh40k-round-event-blackfront-end",
        };

        var simpleKeys = noArgTeamBattleKeys
            .Concat(RoundEventNameKeys)
            .Concat(WeatherNameKeys)
            .Concat(WeatherSummaryKeys)
            .Concat(WeatherDangerKeys)
            .Concat(WeatherProtectionKeys)
            .Concat(LevelBuffKeys);

        Assert.Multiple(() =>
        {
            foreach (var key in simpleKeys)
            {
                locMan.SetCulture(EnCulture);
                var en = locMan.GetString(key);
                Assert.That(en, Does.Not.Contain("{$"),
                    $"en-US key '{key}' has unresolved placeholder: {en}");

                locMan.SetCulture(RuCulture);
                var ru = locMan.GetString(key);
                Assert.That(ru, Does.Not.Contain("{$"),
                    $"ru-RU key '{key}' has unresolved placeholder: {ru}");
            }
        });

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }
}
