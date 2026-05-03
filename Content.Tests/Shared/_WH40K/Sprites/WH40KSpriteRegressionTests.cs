#nullable enable
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Sprites;

[TestFixture]
public sealed class WH40KSpriteRegressionTests
{
    [TestCase("Resources/Textures/_WH40K/Objects/Weapons/Guns/Autoguns/stub_rifle_inhands.rsi/inhand-right.png")]
    [TestCase("Resources/Textures/_WH40K/Objects/Weapons/Guns/Autoguns/stub_rifle_inhands.rsi/wielded-inhand-right.png")]
    public void StubRifleEastFacingRightHandFramesStayContiguous(string relativePath)
    {
        var repoRoot = FindRepoRoot();
        using var image = Image.Load<Rgba32>(Path.Combine(repoRoot, relativePath));
        using var eastFrame = ExtractDirectionFrame(image, directionIndex: 2);

        Assert.That(CountOpaqueComponents(eastFrame), Is.EqualTo(1),
            $"{relativePath} east-facing frame should remain a single silhouette so the autogun does not split onto the face when facing right.");
    }

    [Test]
    public void ImperialCombatShieldBackSideFramesStayOpaqueInsideTheirSilhouette()
    {
        var repoRoot = FindRepoRoot();
        using var image = Image.Load<Rgba32>(Path.Combine(
            repoRoot,
            "Resources/Textures/_WH40K/Objects/Weapons/Melee/imperial_combat_shield.rsi/equipped-BACK.png"));

        Assert.Multiple(() =>
        {
            Assert.That(HasInteriorTransparency(ExtractDirectionFrame(image, directionIndex: 2)), Is.False,
                "Shield east-facing back frame has an interior transparency hole.");
            Assert.That(HasInteriorTransparency(ExtractDirectionFrame(image, directionIndex: 3)), Is.False,
                "Shield west-facing back frame has an interior transparency hole.");
        });
    }

    private static Image<Rgba32> ExtractDirectionFrame(Image<Rgba32> sheet, int directionIndex)
    {
        const int frameSize = 32;
        const int columns = 2;

        var column = directionIndex % columns;
        var row = directionIndex / columns;
        return sheet.Clone(context => context.Crop(new SixLabors.ImageSharp.Rectangle(column * frameSize, row * frameSize, frameSize, frameSize)));
    }

    private static int CountOpaqueComponents(Image<Rgba32> image)
    {
        var visited = new bool[image.Width, image.Height];
        var components = 0;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (visited[x, y] || image[x, y].A == 0)
                    continue;

                components++;

                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue((x, y));
                visited[x, y] = true;

                while (queue.Count > 0)
                {
                    var (currentX, currentY) = queue.Dequeue();

                    foreach (var (nextX, nextY) in EnumerateCardinalNeighbors(currentX, currentY, image.Width, image.Height))
                    {
                        if (visited[nextX, nextY] || image[nextX, nextY].A == 0)
                            continue;

                        visited[nextX, nextY] = true;
                        queue.Enqueue((nextX, nextY));
                    }
                }
            }
        }

        return components;
    }

    private static bool HasInteriorTransparency(Image<Rgba32> image)
    {
        for (var y = 0; y < image.Height; y++)
        {
            var minX = -1;
            var maxX = -1;

            for (var x = 0; x < image.Width; x++)
            {
                if (image[x, y].A == 0)
                    continue;

                minX = minX == -1 ? x : minX;
                maxX = x;
            }

            if (minX == -1 || maxX <= minX + 1)
                continue;

            for (var x = minX + 1; x < maxX; x++)
            {
                if (image[x, y].A == 0)
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<(int X, int Y)> EnumerateCardinalNeighbors(int x, int y, int width, int height)
    {
        if (x > 0)
            yield return (x - 1, y);

        if (x + 1 < width)
            yield return (x + 1, y);

        if (y > 0)
            yield return (x, y - 1);

        if (y + 1 < height)
            yield return (x, y + 1);
    }

    private static string FindRepoRoot()
    {
        var cursor = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (cursor != null)
        {
            var candidate = Path.Combine(cursor.FullName, "Resources", "Prototypes");
            if (Directory.Exists(candidate))
                return cursor.FullName;

            cursor = cursor.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Resources/Prototypes.");
    }
}
