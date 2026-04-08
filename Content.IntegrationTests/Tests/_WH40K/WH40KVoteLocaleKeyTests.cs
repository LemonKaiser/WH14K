using System.Globalization;
using System.Linq;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class WH40KVoteLocaleKeyTests
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly CultureInfo EnCulture = CultureInfo.GetCultureInfo("en-US");

    // Restart vote keys.
    private static readonly string[] RestartVoteKeys =
    {
        "ui-vote-restart-title",
        "ui-vote-restart-yes",
        "ui-vote-restart-no",
        "ui-vote-restart-abstain",
    };

    // Gamemode/preset vote keys.
    private static readonly string[] GamemodeVoteKeys =
    {
        "ui-vote-gamemode-title",
    };

    // Map vote keys.
    private static readonly string[] MapVoteKeys =
    {
        "ui-vote-map-title",
    };

    // Votekick option keys.
    private static readonly string[] VotekickOptionKeys =
    {
        "ui-vote-votekick-yes",
        "ui-vote-votekick-no",
        "ui-vote-votekick-abstain",
    };

    // Vote UI keys used globally.
    private static readonly string[] VoteUiKeys =
    {
        "ui-vote-created",
        "ui-vote-button",
        "ui-vote-button-no-votes",
        "ui-vote-type-restart",
        "ui-vote-type-gamemode",
        "ui-vote-type-map",
        "ui-vote-type-votekick",
        "ui-vote-create-title",
        "ui-vote-create-button",
        "ui-vote-initiator-server",
    };

    [Test]
    public async Task RestartVoteKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in RestartVoteKeys)
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
    public async Task GamemodeVoteKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in GamemodeVoteKeys)
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
    public async Task MapVoteKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in MapVoteKeys)
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
    public async Task VotekickOptionKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in VotekickOptionKeys)
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
    public async Task VoteUiKeysExistInBothCultures()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var key in VoteUiKeys)
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
    public async Task VoteKeysReturnDifferentTextPerCulture()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        var titlesAndOptions = RestartVoteKeys
            .Concat(GamemodeVoteKeys)
            .Concat(MapVoteKeys)
            .Concat(VotekickOptionKeys);

        Assert.Multiple(() =>
        {
            foreach (var key in titlesAndOptions)
            {
                locMan.SetCulture(EnCulture);
                var en = locMan.GetString(key);

                locMan.SetCulture(RuCulture);
                var ru = locMan.GetString(key);

                Assert.That(en, Is.Not.EqualTo(ru),
                    $"Vote key '{key}' returned identical text in both cultures");
            }
        });

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VoteKeysFallbackBehaviorWhenKeyMissing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var locMan = pair.Server.ResolveDependency<ILocalizationManager>();

        // A fabricated key that should not exist — GetString returns the key itself as fallback.
        const string fakeKey = "ui-vote-nonexistent-test-key-12345";

        locMan.SetCulture(EnCulture);
        Assert.That(locMan.HasString(fakeKey), Is.False, "Fake key should not exist");
        var result = locMan.GetString(fakeKey);
        Assert.That(result, Is.EqualTo(fakeKey),
            "Missing key should return the key string itself as fallback text");

        locMan.SetCulture(RuCulture);
        await pair.CleanReturnAsync();
    }
}
