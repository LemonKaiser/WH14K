#nullable enable
using System.Runtime.CompilerServices;
using System.Threading;
using Content.IntegrationTests.Fixtures;
using NUnit.Framework;

namespace Content.IntegrationTests.Tests._WH40K.Cinematic;

public abstract class WH40KCinematicGameTest : GameTest
{
    private int _stepIndex;

    protected virtual bool RequireConnectedPair => true;
    protected virtual bool RequireRealTicker => true;

    public override PoolSettings PoolSettings => new()
    {
        Connected = RequireConnectedPair,
        DummyTicker = !RequireRealTicker,
        Dirty = true,
    };

    [SetUp]
    public void ResetStepIndex()
    {
        _stepIndex = 0;
    }

    protected Task ServerStep(
        Action assertion,
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0)
    {
        return RunLoggedStep("server assertion", () => Server.WaitAssertion(assertion), member, line);
    }

    protected Task ClientStep(
        Action assertion,
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0)
    {
        return RunLoggedStep("client assertion", () => Client.WaitAssertion(assertion), member, line);
    }

    protected Task ServerPostStep(
        Action action,
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0)
    {
        return RunLoggedStep("server post", () => Server.WaitPost(action), member, line);
    }

    protected Task ClientPostStep(
        Action action,
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0)
    {
        return RunLoggedStep("client post", () => Client.WaitPost(action), member, line);
    }

    protected Task RunTicksStep(
        int ticks,
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0)
    {
        return RunLoggedStep($"run {ticks} synchronized tick(s)", () => Pair.RunTicksSync(ticks), member, line);
    }

    protected Task WaitForPairConditionStep(
        Func<bool> predicate,
        int maxTicks = 120,
        int tickStep = 1,
        string? label = null,
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0)
    {
        return RunLoggedStep(
            $"{label ?? "wait for pair condition"} (maxTicks={maxTicks}, tickStep={tickStep})",
            async () =>
            {
                if (predicate())
                    return;

                for (var i = 0; i < maxTicks; i += tickStep)
                {
                    await Pair.RunTicksSync(tickStep);
                    if (predicate())
                        return;
                }

                Assert.Fail(
                    $"{label ?? "Pair condition"} did not pass after {maxTicks} synchronized tick(s).");
            },
            member,
            line);
    }

    private async Task RunLoggedStep(
        string description,
        Func<Task> action,
        string member,
        int line)
    {
        var step = Interlocked.Increment(ref _stepIndex);
        TestContext.Progress.WriteLine(
            $"[{GetType().Name}.{member}] step {step} @ line {line}: {description}");

        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not AssertionException)
        {
            throw new AssertionException(
                $"Unhandled exception at {GetType().Name}.{member} step {step} (line {line}): {description}\n{ex}",
                ex);
        }
    }
}

public abstract class WH40KCinematicServerOnlyGameTest : WH40KCinematicGameTest
{
    protected override bool RequireConnectedPair => false;
}
