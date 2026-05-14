#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Equipment;

[TestFixture]
public sealed class WH40KPsykerForceStaffSpecTests
{
    [Test]
    public void ForceStaffsAreRestrictedToPsykersAndBuildInstability()
    {
        var prototypes = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Objects/Weapons/Guns/Psyker/psyker_staves.yml");
        var sharedComponent = ReadRepoFile("Content.Shared/_WH40K/Weapons/Ranged/WH40KPsykerForceStaffComponent.cs");
        var sharedSystem = ReadRepoFile("Content.Shared/_WH40K/Weapons/Ranged/SharedWH40KPsykerForceStaffSystem.cs");
        var serverSystem = ReadRepoFile("Content.Server/_WH40K/Weapons/Ranged/WH40KPsykerForceStaffSystem.cs");
        var english = ReadRepoFile("Resources/Locale/en-US/_wh40k/psyker.ftl");
        var russian = ReadRepoFile("Resources/Locale/ru-RU/_wh40k/psyker.ftl");

        Assert.Multiple(() =>
        {
            Assert.That(prototypes, Does.Contain("- type: WH40KPsykerForceStaff"));
            Assert.That(prototypes, Does.Contain("rechargeCooldown: 45"));

            Assert.That(sharedComponent, Does.Contain("public float ShotInstability = 15f;"));
            Assert.That(sharedComponent, Does.Contain("wh40k-psyker-force-staff-user-required"));

            Assert.That(sharedSystem, Does.Contain("SubscribeLocalEvent<WH40KPsykerForceStaffComponent, AttemptShootEvent>(OnAttemptShoot);"));
            Assert.That(sharedSystem, Does.Contain("HasComp<WH40KPsykerRoleComponent>(user)"));
            Assert.That(sharedSystem, Does.Contain("args.Message = Loc.GetString(ent.Comp.Popup);"));

            Assert.That(serverSystem, Does.Contain("SubscribeLocalEvent<WH40KPsykerForceStaffComponent, GunShotEvent>(OnGunShot);"));
            Assert.That(serverSystem, Does.Contain("new WH40KWarpInstabilityContributionEvent(args.User, ent.Comp.ShotInstability, StaffShotSourceKey)"));

            Assert.That(english, Does.Contain("wh40k-psyker-force-staff-user-required = Only a sanctioned psyker can channel a force staff."));
            Assert.That(russian, Does.Contain("wh40k-psyker-force-staff-user-required = Только санкционированный псайкер может проводить силу через такой посох."));
        });
    }

    private static string ReadRepoFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Resources")) &&
                File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Could not locate WH14K repository root.");
        return string.Empty;
    }
}
