using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Shared.CCVar;
using Content.Shared.Players;
using Moq;
using NUnit.Framework;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Tests.Server.Administration;

[TestFixture]
[NonParallelizable]
public sealed class AdminManagerTests
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
    public async Task AdminLoginOccursWhenInGameHappensAfterUserDbFinish()
    {
        var userDb = new UserDbDataManager();
        IoCManager.InjectDependencies(userDb);
        ((IPostInjectInit) userDb).PostInject();

        var session = CreateSession();
        userDb.ClientConnected(session);
        await userDb.WaitLoadComplete(session);

        var cfg = new Mock<IConfigurationManager>();
        cfg.Setup(c => c.GetCVar(CCVars.ConsoleLoginLocal)).Returns(false);
        cfg.Setup(c => c.GetCVar(CCVars.ConsoleLoginHostUser)).Returns(string.Empty);
        cfg.Setup(c => c.GetCVar(CCVars.AdminAnnounceLogin)).Returns(false);
        cfg.Setup(c => c.GetCVar(CCVars.AdminUseCustomNamesAdminRank)).Returns(false);

        var db = new Mock<IServerDbManager>();
        db.Setup(d => d.GetAdminDataForAsync(session.UserId, default))
            .ReturnsAsync(new Admin
            {
                UserId = session.UserId.UserId,
                Title = "Migrated Admin",
                Flags = new List<AdminFlag>
                {
                    new()
                    {
                        AdminId = session.UserId.UserId,
                        Flag = "ADMIN",
                        Negative = false
                    }
                }
            });

        var net = new Mock<IServerNetManager>();
        var chat = new Mock<IChatManager>();

        var manager = new AdminManager();
        SetField(manager, "_userDb", userDb);
        SetField(manager, "_cfg", cfg.Object);
        SetField(manager, "_dbManager", db.Object);
        SetField(manager, "_netMgr", net.Object);
        SetField(manager, "_chat", chat.Object);

        InvokePrivate(manager, "OnUserDbLoadFinished", session);
        await Task.Delay(50);
        Assert.That(manager.IsAdmin(session), Is.False);

        session.Status = SessionStatus.InGame;
        InvokePrivate(
            manager,
            "PlayerStatusChanged",
            null,
            new SessionStatusEventArgs(session, SessionStatus.Connected, SessionStatus.InGame));

        await WaitForConditionAsync(() => manager.IsAdmin(session));
        Assert.That(manager.GetAdminData(session), Is.Not.Null);
    }

    private static TestSession CreateSession()
    {
        var userId = new NetUserId(Guid.NewGuid());
        var data = new SessionData(userId, "MigratedAdmin")
        {
            ContentDataUncast = new ContentPlayerData(userId, "MigratedAdmin")
        };

        var channel = new Mock<INetChannel>();
        channel.SetupGet(c => c.AuthType).Returns(LoginType.LoggedIn);
        channel.SetupGet(c => c.UserId).Returns(userId);
        channel.SetupGet(c => c.UserName).Returns("MigratedAdmin");
        channel.SetupGet(c => c.RemoteEndPoint).Returns(new IPEndPoint(IPAddress.Parse("203.0.113.15"), 1212));

        return new TestSession
        {
            UserId = userId,
            Name = "MigratedAdmin",
            Status = SessionStatus.Connected,
            Channel = channel.Object,
            Data = data
        };
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(20);
        }

        Assert.Fail("Timed out waiting for condition.");
    }

    private static void InvokePrivate(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Could not find private method '{methodName}'.");
        method!.Invoke(instance, args);
    }

    private static void SetField(object instance, string fieldName, object value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}'.");
        field!.SetValue(instance, value);
    }

    private sealed class TestSession : ICommonSession
    {
        public SessionStatus Status { get; set; }
        public EntityUid? AttachedEntity { get; set; }
        public NetUserId UserId { get; init; }
        public string Name { get; init; } = string.Empty;
        public short Ping { get; set; }
        public INetChannel Channel { get; set; } = default!;
        public LoginType AuthType => Channel.AuthType;
        public HashSet<EntityUid> ViewSubscriptions { get; } = new();
        public DateTime ConnectedTime { get; set; }
        public SessionState State { get; } = new();
        public SessionData Data { get; init; } = default!;
        public bool ClientSide { get; set; }
    }
}
