#nullable enable
using System.Linq;
using Content.Server._WH40K.MetaProgress;
using Content.Shared._WH40K.MetaProgress;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.IntegrationTests.Tests._WH40K.MetaProgress;

[TestFixture]
public sealed class WH40KSecretAchievementIntegrationTests
{
    private const string SecretAchievementId = "wh40k-ach-whispers-in-void";

    [Test]
    public void HiddenAchievementMasksDetailsUntilCompleted()
    {
        var lockedEntry = new WH40KMetaAchievementSnapshotEntry(
            SecretAchievementId,
            WH40KMetaAchievementCategory.Hidden,
            "title",
            "description",
            "task",
            "reward",
            0,
            [],
            0,
            1,
            hidden: true,
            completed: false);

        var completedEntry = new WH40KMetaAchievementSnapshotEntry(
            SecretAchievementId,
            WH40KMetaAchievementCategory.Hidden,
            "title",
            "description",
            "task",
            "reward",
            0,
            [],
            1,
            1,
            hidden: true,
            completed: true);

        Assert.Multiple(() =>
        {
            Assert.That(WH40KMetaAchievementDisplayHelper.ShouldMaskSecretDetails(lockedEntry), Is.True);
            Assert.That(WH40KMetaAchievementDisplayHelper.ShouldMaskSecretDetails(completedEntry), Is.False);
        });
    }

    [Test]
    public async Task KaiserCommandUnlocksSecretAchievementForRegularPlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Dirty = true,
            DummyTicker = false,
        });

        var server = pair.Server;
        var consoleHost = server.ResolveDependency<IConsoleHost>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var session = playerMan.Sessions.Single();

        await server.WaitPost(() =>
        {
            _ = server.System<WH40KMetaProgressSystem>().GetSnapshot(session.UserId);
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var snapshot = server.System<WH40KMetaProgressSystem>().GetSnapshot(session.UserId);
            var entry = snapshot.Achievements.Single(a => a.Id == SecretAchievementId);

            Assert.Multiple(() =>
            {
                Assert.That(entry.Completed, Is.False);
                Assert.That(entry.Hidden, Is.True);
                Assert.That(WH40KMetaAchievementDisplayHelper.ShouldMaskSecretDetails(entry), Is.True);
            });
        });

        await server.WaitPost(() =>
        {
            consoleHost.GetSessionShell(session).ExecuteCommand("kaiser");
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var snapshot = server.System<WH40KMetaProgressSystem>().GetSnapshot(session.UserId);
            var entry = snapshot.Achievements.Single(a => a.Id == SecretAchievementId);

            Assert.Multiple(() =>
            {
                Assert.That(entry.Completed, Is.True);
                Assert.That(entry.Progress, Is.EqualTo(entry.Target));
                Assert.That(WH40KMetaAchievementDisplayHelper.ShouldMaskSecretDetails(entry), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }
}
