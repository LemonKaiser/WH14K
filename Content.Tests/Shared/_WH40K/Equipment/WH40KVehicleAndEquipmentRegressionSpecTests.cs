#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Equipment;

[TestFixture]
public sealed class WH40KVehicleAndEquipmentRegressionSpecTests
{
    [Test]
    public void MountedMeleeTargetingSkipsBikeSelfPassengerAndOverlayArtifacts()
    {
        var source = ReadRepoFile("Content.Client/Weapons/Melee/MeleeWeaponSystem.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("var target = ResolveAttackTarget(attacker, mousePos);"));
            Assert.That(source, Does.Contain("buckle.BuckledTo == vehicle"));
            Assert.That(source, Does.Contain("Transform(candidate).ParentUid == vehicle"));
            Assert.That(source, Does.Contain("candidate == attacker || candidate == vehicle"));
        });
    }

    [Test]
    public void TechPriestMapItemsUsePrototypeDefaultsAndAreRemovedFromWh40KMaps()
    {
        var hudSource = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Clothing/Eyes/hud.yml");
        var maskSource = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Clothing/Mask/masks.yml");
        var azovMap = ReadRepoFile("Resources/Maps/_WH40K/Azov.yml");
        var battlefieldMap = ReadRepoFile("Resources/Maps/_WH40K/battlefield40k.yml");
        var visor = ExtractBlock(hudSource, "id: ClothingEyesHudTechPriest", "- type: entity\n  parent: ClothingEyesHudTechPriest");
        var breather = ExtractBlock(maskSource, "id: ClothingMaskGasTechPriest", "- type: entity\n  parent: ClothingMaskPullableBase");
        var gasMask = ExtractBlock(maskSource, "id: ClothingGasTechPriest", "- type: entity\n  parent: ClothingMaskGas\n  id: ClothingMaskGasChaosCultist1");

        Assert.Multiple(() =>
        {
            Assert.That(visor, Does.Contain("- type: FlashImmunity"));
            Assert.That(visor, Does.Contain("- type: EyeProtection"));
            Assert.That(visor, Does.Contain("- type: Unremoveable"));
            Assert.That(visor, Does.Not.Contain("WH40KForceRemovable"));
            Assert.That(breather, Does.Not.Contain("WH40KForceRemovable"));
            Assert.That(gasMask, Does.Not.Contain("WH40KForceRemovable"));
            Assert.That(File.Exists(Path.Combine(FindRepoRoot(), "Content.Server", "_WH40K", "Clothing", "WH40KForceRemovableSystem.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(FindRepoRoot(), "Content.Shared", "_WH40K", "Clothing", "Components", "WH40KForceRemovableComponent.cs")), Is.False);
            Assert.That(azovMap, Does.Not.Contain("- proto: ClothingEyesHudTechPriest\n"));
            Assert.That(azovMap, Does.Not.Contain("- proto: ClothingEyesHudTechPriestAdm\n"));
            Assert.That(azovMap, Does.Not.Contain("- proto: ClothingEyesHudTechPriestMed\n"));
            Assert.That(azovMap, Does.Not.Contain("- proto: ClothingGasTechPriest\n"));
            Assert.That(azovMap, Does.Not.Contain("- proto: ClothingMaskGasTechPriest\n"));
            Assert.That(battlefieldMap, Does.Not.Contain("- proto: ClothingGasTechPriest\n"));
        });
    }

    [Test]
    public void VraksHandbladeBeltVisualIsShiftedOntoHipInsteadOfCenterline()
    {
        var source = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Objects/Weapons/Melee/vraks_handblade.yml");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("clothingVisuals:"));
            Assert.That(source, Does.Contain("belt:"));
            Assert.That(source, Does.Contain("state: equipped-BELT"));
            Assert.That(source, Does.Contain("offset: \"-0.18, 0.12\""));
        });
    }

    [Test]
    public void FlamethrowerDoesNotKeepPlaceholderMagazinePaletteLayer()
    {
        var source = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Objects/Weapons/Guns/flamethrowers.yml");
        var flamethrower = ExtractBlock(source, "id: FlameThrowerGun", "- type: entity\n  id: ChaosFlameThrowerGun");
        var chaosFlamethrower = ExtractBlock(source, "id: ChaosFlameThrowerGun", string.Empty);
        var noBakBack = ReadRepoBytes("Resources/Textures/_WH40K/Objects/Weapons/Flamethrower/flamethrowerbuck.rsi/ig flamethrower no Bak back.png");
        var loyalBack = ReadRepoBytes("Resources/Textures/_WH40K/Objects/Weapons/Flamethrower/flamethrower.rsi/equipped-BACKPACK.png");
        var loyalSuitStorage = ReadRepoBytes("Resources/Textures/_WH40K/Objects/Weapons/Flamethrower/flamethrower.rsi/equipped-SUITSTORAGE.png");
        var chaosBack = ReadRepoBytes("Resources/Textures/_WH40K/Objects/Weapons/Flamethrower/chaos_flamethrower.rsi/equipped-BACKPACK.png");
        var chaosSuitStorage = ReadRepoBytes("Resources/Textures/_WH40K/Objects/Weapons/Flamethrower/chaos_flamethrower.rsi/equipped-SUITSTORAGE.png");

        Assert.Multiple(() =>
        {
            Assert.That(flamethrower, Does.Not.Contain("- type: MagazineVisuals"));
            Assert.That(flamethrower, Does.Not.Contain("state: mag-0"));
            Assert.That(flamethrower, Does.Not.Contain("\"enum.GunVisualLayers.Mag\""));
            Assert.That(chaosFlamethrower, Does.Not.Contain("state: mag-0"));
            Assert.That(chaosFlamethrower, Does.Not.Contain("\"enum.GunVisualLayers.Mag\""));
            Assert.That(loyalBack, Is.EqualTo(noBakBack));
            Assert.That(loyalSuitStorage, Is.EqualTo(noBakBack));
            Assert.That(chaosBack, Is.EqualTo(noBakBack));
            Assert.That(chaosSuitStorage, Is.EqualTo(noBakBack));
        });
    }

    [Test]
    public void BoltPistolSupportsAimedFireWithStandardSidearmProfile()
    {
        var source = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Objects/Weapons/Guns/Bolters/bolters.yml");
        var boltPistol = ExtractBlock(source, "id: WeaponPistolBolter", "- type: entity\n  parent: WeaponLaserWH40KEnergy");

        Assert.Multiple(() =>
        {
            Assert.That(boltPistol, Does.Contain("- type: AimingCamera"));
            Assert.That(boltPistol, Does.Contain("maxOffset: 2"));
            Assert.That(boltPistol, Does.Contain("pvsIncrease: 0.2"));
            Assert.That(boltPistol, Does.Contain("requireWield: false"));
        });
    }

    private static string ExtractBlock(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start == -1)
        {
            Assert.Fail($"Could not find start marker '{startMarker}'.");
            return string.Empty;
        }

        var end = string.IsNullOrEmpty(endMarker)
            ? source.Length
            : source.IndexOf(endMarker, start, StringComparison.Ordinal);

        if (end == -1)
            end = source.Length;

        return source.Substring(start, end - start);
    }

    private static string ReadRepoFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static byte[] ReadRepoBytes(string relativePath)
    {
        return File.ReadAllBytes(Path.Combine(
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
