#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Vehicle;

[TestFixture]
public sealed class WH40KMechPilotCatastrophicEjectSpecTests
{
    [Test]
    public void SentinelBreaksCausePilotConcussionAndExplosionLikeDamage()
    {
        var mechPrototype = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Objects/Specific/Mech/Mech.yml");
        var componentSource = ReadRepoFile("Content.Shared/_WH40K/Vehicle/Mech/WH40KMechPilotCatastrophicEjectComponent.cs");
        var systemSource = ReadRepoFile("Content.Server/_WH40K/Vehicle/Mech/WH40KMechPilotCatastrophicEjectSystem.cs");

        Assert.Multiple(() =>
        {
            Assert.That(mechPrototype, Does.Contain("- type: WH40KMechPilotCatastrophicEject"));

            Assert.That(componentSource, Does.Contain("public float StunSeconds = 10f;"));
            Assert.That(componentSource, Does.Contain("{ \"Blunt\", 24.0 }"));
            Assert.That(componentSource, Does.Contain("{ \"Heat\", 23.0 }"));
            Assert.That(componentSource, Does.Contain("{ \"Piercing\", 23.0 }"));

            Assert.That(systemSource, Does.Contain("before: [typeof(MechSystem)]"));
            Assert.That(systemSource, Does.Contain("var currentIntegrity = mech.MaxIntegrity - _damageable.GetTotalDamage((ent.Owner, args.Damageable));"));
            Assert.That(systemSource, Does.Contain("_stun.TryKnockdown(pilot, stunDuration, force: true);"));
            Assert.That(systemSource, Does.Contain("_stun.TryAddStunDuration(pilot, stunDuration);"));
            Assert.That(systemSource, Does.Contain("_damageable.TryChangeDamage(pilot, ent.Comp.Damage, origin: ent.Owner);"));
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
