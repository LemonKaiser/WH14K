#nullable enable
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class PsykerChaosLeaderFollowerQaIntegrationTests
{
    [Test]
    public void LeaderFollowerJobAssignmentsStayPartitioned()
    {
        Assert.Multiple(() =>
        {
            AssertChaosRoleContract(
                expectLeader: true,
                "Resources",
                "Prototypes",
                "_WH40K",
                "Roles",
                "Jobs",
                "Heretics",
                "Colonel.yml");

            AssertChaosRoleContract(
                expectLeader: true,
                "Resources",
                "Prototypes",
                "_WH40K",
                "Roles",
                "Jobs",
                "Heretics",
                "Lieutenant.yml");

            AssertChaosRoleContract(
                expectLeader: false,
                "Resources",
                "Prototypes",
                "_WH40K",
                "Roles",
                "Jobs",
                "Heretics",
                "Sergeant.yml");

            AssertChaosRoleContract(
                expectLeader: false,
                "Resources",
                "Prototypes",
                "_WH40K",
                "Roles",
                "Jobs",
                "Heretics",
                "Tank Commander.yml");

            AssertChaosRoleContract(
                expectLeader: false,
                "Resources",
                "Prototypes",
                "_WH40K",
                "Roles",
                "Jobs",
                "Heretics",
                "magos.yml");
        });
    }

    [Test]
    public void SharedCultProgressionRemainsWiredAcrossChaosRuntime()
    {
        var bootstrap = ReadRepoFile(
            "Content.Shared",
            "_WH40K",
            "Psyker",
            "SharedWH40KChaosRoleBootstrapSystem.cs");

        var cult = ReadRepoFile(
            "Content.Server",
            "_WH40K",
            "Psyker",
            "WH40KChaosCultSystem.cs");

        var runtimeRules = ReadRepoFile(
            "Content.Server",
            "_WH40K",
            "Psyker",
            "WH40KChaosLeaderRuntimeRules.cs");

        var skrizhalUi = ReadRepoFile(
            "Content.Shared",
            "_WH40K",
            "Psyker",
            "WH40KChaosSkrizhalUi.cs");

        var branchWindow = ReadRepoFile(
            "Content.Client",
            "_WH40K",
            "Psyker",
            "UI",
            "WH40KChaosSkrizhalLeaderWindow.xaml.cs");

        var loadout = ReadRepoFile(
            "Content.Server",
            "_WH40K",
            "Psyker",
            "WH40KChaosStarterActionLoadoutSystem.cs");

        var ui = ReadRepoFile(
            "Content.Client",
            "_WH40K",
            "Psyker",
            "UI",
            "WH40KWarpUiController.cs");

        var sharedProgression = ReadRepoFile(
            "Content.Shared",
            "_WH40K",
            "Psyker",
            "WH40KChaosGiftProgressionComponent.cs");

        var progression = ReadRepoFile(
            "Content.Server",
            "_WH40K",
            "Psyker",
            "WH40KChaosGiftProgressionSystem.cs");

        Assert.Multiple(() =>
        {
            Assert.That(bootstrap, Does.Contain("SubscribeLocalEvent<WH40KChaosLeaderRoleComponent, ComponentStartup>"));
            Assert.That(bootstrap, Does.Contain("EnsureChaosCultRuntime(uid);"));
            Assert.That(bootstrap, Does.Contain("EnsureComp<WH40KWarpResourceComponent>(uid);"));
            Assert.That(bootstrap, Does.Contain("EnsureComp<WH40KWarpInstabilityComponent>(uid);"));
            Assert.That(bootstrap, Does.Contain("EnsureComp<WH40KChaosGiftStarterActionLoadoutComponent>(uid);"));
            Assert.That(CountOccurrences(bootstrap, "RaiseLocalEvent(uid, new WH40KChaosRoleStartupEvent(uid));"), Is.GreaterThanOrEqualTo(2));

            Assert.That(cult, Does.Contain("public bool IsEffectiveLeader(EntityUid uid, WH40KChaosGiftProgressionComponent progression)"));
            Assert.That(cult, Does.Contain("public void CaptureSharedProgression(EntityUid uid, WH40KChaosGiftProgressionComponent progression)"));
            Assert.That(cult, Does.Contain("public void AddCultXp(WH40KChaosPatron patron, float amount)"));
            Assert.That(cult, Does.Contain("SubscribeLocalEvent<WH40KChaosRoleStartupEvent>(OnChaosRoleStartup);"));
            Assert.That(cult, Does.Contain("public override void Update(float frameTime)"));
            Assert.That(cult, Does.Not.Contain("KhorneGiftOneExUnlocked = source.KhorneGiftOneExUnlocked"));
            Assert.That(cult, Does.Not.Contain("KhorneGiftTwoExUnlocked = source.KhorneGiftTwoExUnlocked"));
            Assert.That(cult, Does.Not.Contain("KhorneGiftThreeExUnlocked = source.KhorneGiftThreeExUnlocked"));
            Assert.That(cult, Does.Not.Contain("KhornePassiveExUnlocked = source.KhornePassiveExUnlocked"));

            Assert.That(runtimeRules, Does.Contain("public static bool ShouldGrantGiftSlot("));
            Assert.That(runtimeRules, Does.Contain("public static bool IsGiftExUnlocked("));
            Assert.That(runtimeRules, Does.Contain("public static bool IsPassiveExUnlocked("));

            Assert.That(sharedProgression, Does.Contain("public bool EffectiveLeader;"));
            Assert.That(skrizhalUi, Does.Contain("public bool CanEdit { get; }"));
            Assert.That(skrizhalUi, Does.Contain("public bool HasActiveLeader { get; }"));
            Assert.That(skrizhalUi, Does.Contain("public string ActiveLeaderName { get; }"));

            Assert.That(loadout, Does.Contain("var isLeader = _cult.IsEffectiveLeader(uid, progression);"));
            Assert.That(loadout, Does.Contain("WH40KChaosLeaderRuntimeRules.ShouldGrantGiftSlot("));
            Assert.That(loadout, Does.Contain("var passiveExUnlocked = WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked("));
            Assert.That(loadout, Does.Contain("if (patron is (WH40KChaosPatron.Khorne or"));
            Assert.That(loadout, Does.Contain("loadout.AppliedLeaderState = isLeader;"));

            Assert.That(ui, Does.Contain("var hasChaosUiRole = hasChaosRole && chaosProgression?.EffectiveLeader == true;"));
            Assert.That(ui, Does.Contain("var hasChaosHudRole = hasChaosRole;"));
            Assert.That(ui, Does.Contain("if (!hasPsykerRole && !hasChaosHudRole)"));
            Assert.That(ui, Does.Contain("progression?.EffectiveLeader != true"));

            Assert.That(branchWindow, Does.Contain("if (!state.CanEdit)"));
            Assert.That(branchWindow, Does.Contain("GiftTreeControl.SetInteractionEnabled(state.CanEdit);"));
            Assert.That(branchWindow, Does.Contain("w40k-ch-command-status-following"));
            Assert.That(branchWindow, Does.Contain("w40k-ch-card-button-review"));

            Assert.That(progression, Does.Contain("_cult.AttachMemberToCult(actor, progression, previousPatron);"));
            Assert.That(progression, Does.Contain("_cult.RegisterLeadershipCandidate(actor, progression);"));
            Assert.That(progression, Does.Contain("_cult.AddCultXp(actorPatron, samePatronXp);"));
            Assert.That(progression, Does.Contain("WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked("));
            Assert.That(progression, Does.Contain("WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked("));
            Assert.That(CountOccurrences(progression, "_cult.IsEffectiveLeader(args.Actor, progression)"), Is.GreaterThanOrEqualTo(4));
            Assert.That(CountOccurrences(progression, "w40k-ch-popup-leader-only"), Is.GreaterThanOrEqualTo(4));
        });
    }

    [Test]
    public void OldChampionRoleComponentIsGoneFromChaosRuntimeSources()
    {
        var root = FindRepositoryRoot(AppContext.BaseDirectory);
        var runtimeDirectories = new[]
        {
            Path.Combine(root, "Content.Server", "_WH40K", "Psyker"),
            Path.Combine(root, "Content.Shared", "_WH40K", "Psyker"),
            Path.Combine(root, "Content.Client", "_WH40K", "Psyker"),
        };

        var offenders = runtimeDirectories
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path => File.ReadAllText(path).Contains("WH40KChaosChampionRoleComponent", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty,
            $"Old champion role component references remain in chaos runtime sources: {string.Join(", ", offenders)}");
    }

    private static void AssertChaosRoleContract(bool expectLeader, params string[] relativePath)
    {
        var text = ReadRepoFile(relativePath);
        var fileName = relativePath[^1];

        Assert.That(text, Does.Contain("WH40KChaosGiftRole"), fileName);

        if (expectLeader)
            Assert.That(text, Does.Contain("WH40KChaosLeaderRole"), fileName);
        else
            Assert.That(text, Does.Not.Contain("WH40KChaosLeaderRole"), fileName);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var root = FindRepositoryRoot(AppContext.BaseDirectory);
        var fullPath = relativePath.Aggregate(root, Path.Combine);
        Assert.That(File.Exists(fullPath), Is.True, $"Expected file was not found: {fullPath}");
        return File.ReadAllText(fullPath);
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            var probe = Path.Combine(directory.FullName, "Resources", "Prototypes", "_WH40K", "Actions", "psyker_chaos.yml");
            if (File.Exists(probe))
                return directory.FullName;

            directory = directory.Parent;
        }

        Assert.Fail("Could not locate repository root for chaos leader/follower QA test.");
        return startDirectory;
    }
}
