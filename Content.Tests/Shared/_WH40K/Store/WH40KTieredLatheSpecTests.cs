#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Store;

[TestFixture]
public sealed class WH40KTieredLatheSpecTests
{
    [Test]
    public void TieredLathePackChangesRefreshMaterialWhitelist()
    {
        var source = ReadRepoFile("Content.Server/_WH40K/Store/WH40KTieredLatheProcessingSystem.cs");
        var updateLathe = ExtractBlock(source, "private void UpdateLathe(", "private void OnLatheGetProductionTime");

        Assert.Multiple(() =>
        {
            Assert.That(updateLathe, Does.Contain("var packChanged = false;"));
            Assert.That(updateLathe, Does.Contain("packChanged = true;"));
            Assert.That(updateLathe, Does.Contain("_materialStorage.UpdateMaterialWhitelist(uid);"));
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

        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
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
