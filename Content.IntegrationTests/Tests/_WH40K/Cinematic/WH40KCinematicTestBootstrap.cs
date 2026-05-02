using NUnit.Framework;

namespace Content.IntegrationTests.Tests._WH40K.Cinematic;

[SetUpFixture]
public sealed class WH40KCinematicTestBootstrap
{
    private static TimeSpan MaximumTotalTestingTimeLimit => TimeSpan.FromMinutes(45);
    private static TimeSpan HardStopTimeLimit => MaximumTotalTestingTimeLimit.Add(TimeSpan.FromMinutes(1));

    private bool _startedHere;

    [OneTimeSetUp]
    public void Setup()
    {
        try
        {
            PoolManager.Startup();
            _startedHere = true;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Already initialized", StringComparison.Ordinal))
        {
            _startedHere = false;
        }

        if (!_startedHere)
            return;

        _ = Task.Delay(MaximumTotalTestingTimeLimit).ContinueWith(_ =>
        {
            TestContext.Error.WriteLine(
                $"\n\n{nameof(WH40KCinematicTestBootstrap)}: ERROR: Cinematic tests are taking too long. Shutting down the local pool bootstrap.\n\n");
            PoolManager.Shutdown();
        });

        _ = Task.Delay(HardStopTimeLimit).ContinueWith(_ =>
        {
            var deathReport = PoolManager.DeathReport();
            Environment.FailFast($"WH40K cinematic tests took too long.\nDeath Report:\n{deathReport}");
        });
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (_startedHere)
            PoolManager.Shutdown();
    }
}
