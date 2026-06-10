using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Moq;
using NUnit.Framework;
using Robust.Shared.Asynchronous;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Tests.Server.Database;

[TestFixture]
[NonParallelizable]
public sealed class UserDbDataManagerTests
{
    private IDependencyCollection _deps = default!;

    [SetUp]
    public void SetUp()
    {
        _deps = IoCManager.InitThread();
        _deps.Clear();
        _deps.RegisterInstance<ILogManager>(new LogManager());
        var taskManager = new Mock<ITaskManager>();
        taskManager
            .Setup(manager => manager.RunOnMainThread(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        _deps.RegisterInstance<ITaskManager>(taskManager.Object);
        _deps.BuildGraph();
        IoCManager.InitThread(_deps, replaceExisting: true);
    }

    [TearDown]
    public void TearDown()
    {
        IoCManager.Clear();
    }

    [Test]
    public async Task PrepareCallbacksRunBeforeLoadCallbacks()
    {
        var manager = new UserDbDataManager();
        IoCManager.InjectDependencies(manager);
        ((IPostInjectInit) manager).PostInject();

        var session = CreateSession();
        var prepareStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrepare = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();

        manager.AddOnPreparePlayer(async (_, _) =>
        {
            events.Add("prepare-start");
            prepareStarted.SetResult();
            await releasePrepare.Task;
            events.Add("prepare-end");
        });

        manager.AddOnLoadPlayer((_, _) =>
        {
            events.Add("load");
            loadStarted.SetResult();
            return Task.CompletedTask;
        });

        manager.AddOnFinishLoad(_ => events.Add("finish"));

        manager.ClientConnected(session.Object);

        await prepareStarted.Task;
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(loadStarted.Task.IsCompleted, Is.False);
            Assert.That(manager.IsLoadComplete(session.Object), Is.False);
            Assert.That(events, Is.EqualTo(new[] { "prepare-start" }));
        });

        releasePrepare.SetResult();
        await manager.WaitLoadComplete(session.Object);

        Assert.That(events, Is.EqualTo(new[] { "prepare-start", "prepare-end", "load", "finish" }));
    }

    [Test]
    public void TryLoadHelpersReturnFalseBeforeClientConnected()
    {
        var manager = new UserDbDataManager();
        IoCManager.InjectDependencies(manager);
        ((IPostInjectInit) manager).PostInject();

        var session = CreateSession();

        Assert.Multiple(() =>
        {
            Assert.That(manager.TryGetLoadTask(session.Object, out var task), Is.False);
            Assert.That(task, Is.Null);
            Assert.That(manager.TryIsLoadComplete(session.Object), Is.False);
        });
    }

    [Test]
    public async Task LoadProgressTracksLifecycle()
    {
        var manager = new UserDbDataManager();
        IoCManager.InjectDependencies(manager);
        ((IPostInjectInit) manager).PostInject();

        var session = CreateSession();
        var prepareStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrepare = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progressSnapshots = new List<UserDbLoadProgress>();

        manager.AddOnLoadProgressChanged((_, progress) => progressSnapshots.Add(progress));
        manager.AddOnPreparePlayer(async (_, cancel) =>
        {
            prepareStarted.SetResult();
            await releasePrepare.Task.WaitAsync(cancel);
        });
        manager.AddOnLoadPlayer((_, _) => Task.CompletedTask);
        manager.AddOnFinishLoad(_ => { });

        manager.ClientConnected(session.Object);
        await prepareStarted.Task;

        Assert.That(manager.TryGetLoadProgress(session.Object, out var currentProgress), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(progressSnapshots.Select(progress => progress.Phase), Contains.Item(UserDbLoadPhase.Starting));
            Assert.That(progressSnapshots.Select(progress => progress.Phase), Contains.Item(UserDbLoadPhase.Preparing));
            Assert.That(currentProgress.Phase, Is.AnyOf(UserDbLoadPhase.Starting, UserDbLoadPhase.Preparing));
            Assert.That(currentProgress.Progress, Is.GreaterThan(0f));
        });

        releasePrepare.SetResult();
        await manager.WaitLoadComplete(session.Object);

        Assert.That(manager.TryGetLoadProgress(session.Object, out currentProgress), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(progressSnapshots.Last().Phase, Is.EqualTo(UserDbLoadPhase.Complete));
            Assert.That(currentProgress.Phase, Is.EqualTo(UserDbLoadPhase.Complete));
            Assert.That(currentProgress.Progress, Is.EqualTo(1f).Within(0.001f));
            Assert.That(currentProgress.CompletedSteps, Is.EqualTo(currentProgress.TotalSteps));
        });
    }

    [Test]
    public async Task DisconnectCancelsLoadAndClearsTrackedProgress()
    {
        var manager = new UserDbDataManager();
        IoCManager.InjectDependencies(manager);
        ((IPostInjectInit) manager).PostInject();

        var session = CreateSession();
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        manager.AddOnLoadPlayer(async (_, cancel) =>
        {
            loadStarted.SetResult();
            await releaseLoad.Task.WaitAsync(cancel);
        });

        manager.ClientConnected(session.Object);
        var loadTask = manager.GetLoadTask(session.Object);

        await loadStarted.Task;

        Assert.That(manager.TryGetLoadProgress(session.Object, out _), Is.True);

        manager.ClientDisconnected(session.Object);

        Assert.That(async () => await loadTask, Throws.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(manager.TryGetLoadTask(session.Object, out var task), Is.False);
            Assert.That(task, Is.Null);
            Assert.That(manager.TryGetLoadProgress(session.Object, out _), Is.False);
            Assert.That(manager.TryIsLoadComplete(session.Object), Is.False);
        });
    }

    private static Mock<ICommonSession> CreateSession()
    {
        var channel = new Mock<INetChannel>();
        channel.SetupGet(c => c.AuthType).Returns(LoginType.LoggedIn);

        var session = new Mock<ICommonSession>();
        session.SetupGet(s => s.UserId).Returns(new NetUserId(Guid.NewGuid()));
        session.SetupGet(s => s.Channel).Returns(channel.Object);

        return session;
    }
}
